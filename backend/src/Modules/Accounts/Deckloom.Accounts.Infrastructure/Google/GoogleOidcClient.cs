using System.Globalization;
using System.Text.Json;
using System.Web;
using Deckloom.Accounts.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deckloom.Accounts.Infrastructure;

// Cliente OIDC do Google. O endpoint de autorizacao vem do discovery, carregado de forma assincrona por
// LoadDiscoveryAsync e mantido em cache por 24 h; BuildAuthorizationUrl e sincrono e nunca faz I/O.
public sealed class GoogleOidcClient
{
    public static readonly TimeSpan DiscoveryLifetime = TimeSpan.FromHours(24);

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private const string CallbackPath = "/api/v1/auth/google/callback";
    private const int ReauthMaxAgeSeconds = 300;

    private static readonly Action<ILogger, int, Exception?> LogTokenEndpointStatus =
        LoggerMessage.Define<int>(LogLevel.Warning, new EventId(1, nameof(LogTokenEndpointStatus)),
            "Google token endpoint respondeu {StatusCode}.");

    private static readonly Action<ILogger, Exception?> LogMissingIdToken =
        LoggerMessage.Define(LogLevel.Warning, new EventId(2, nameof(LogMissingIdToken)),
            "Resposta do token endpoint do Google sem id_token.");

    private readonly HttpClient _http;
    private readonly GoogleOidcOptions _options;
    private readonly TimeProvider _time;
    private readonly object _sync = new();
    private readonly ILogger<GoogleOidcClient> _logger;
    private Uri? _authorizationEndpoint;
    private Uri? _tokenEndpoint;
    private Uri? _jwksUri;
    private DateTimeOffset _loadedAt;

    public GoogleOidcClient(HttpClient http, GoogleOidcOptions options)
        : this(http, options, TimeProvider.System)
    {
    }

    public GoogleOidcClient(HttpClient http, GoogleOidcOptions options, TimeProvider time)
        : this(http, options, time, NullLogger<GoogleOidcClient>.Instance)
    {
    }

    public GoogleOidcClient(
        HttpClient http, GoogleOidcOptions options, TimeProvider time, ILogger<GoogleOidcClient> logger)
    {
        _http = http;
        _options = options;
        _time = time;
        _logger = logger;
        if (_http.Timeout != RequestTimeout)
        {
            // HttpClient so aceita mudar o Timeout antes do primeiro uso; clientes compartilhados ja vem ajustados.
            _http.Timeout = RequestTimeout;
        }
    }

    // Compatibilidade com a porta IGoogleIdentityVerifier: falhas lancam GoogleVerificationFailedException.
    public async Task<GoogleVerifiedIdentity?> CompleteAsync(
        string code, string codeVerifier, string nonce, CancellationToken ct = default)
    {
        var outcome = await TryCompleteAsync(code, codeVerifier, nonce, GoogleAuthorizationIntent.SignIn, ct)
            .ConfigureAwait(false);
        return outcome.Failure is null ? outcome.Identity : throw new GoogleVerificationFailedException();
    }

    // Nunca lanca, exceto cancelamento do chamador. Falhas viram GoogleAuthFailed(Stage, Reason).
    public async Task<GoogleOidcOutcome> TryCompleteAsync(
        string code,
        string codeVerifier,
        string nonce,
        GoogleAuthorizationIntent intent = GoogleAuthorizationIntent.SignIn,
        CancellationToken ct = default)
    {
        var outcome = await RunAsync(code, codeVerifier, nonce, intent, ct).ConfigureAwait(false);
        if (outcome.Failure is { } failure)
        {
            AccountsLog.GoogleAuthFailed(
                _logger,
                LogLevel.Warning,
                GoogleAuthFailureLogNames.For(failure.Stage),
                GoogleAuthFailureLogNames.For(failure.Reason));
        }

        return outcome;
    }

    private async Task<GoogleOidcOutcome> RunAsync(
        string code, string codeVerifier, string nonce, GoogleAuthorizationIntent intent, CancellationToken ct)
    {
        string idToken;
        try
        {
            await LoadDiscoveryAsync(ct).ConfigureAwait(false);
            var exchange = await ExchangeCodeAsync(code, codeVerifier, ct).ConfigureAwait(false);
            if (exchange.Failure is not null)
            {
                return new GoogleOidcOutcome(null, exchange.Failure);
            }

            idToken = exchange.IdToken!;
        }
        catch (Exception ex) when (GoogleFailureClassifier.ClassifyTransport(ex, ct) is not null)
        {
            return new GoogleOidcOutcome(null, GoogleFailureClassifier.ClassifyTransport(ex, ct));
        }

        var jwks = await FetchJwksWithRetryAsync(ct).ConfigureAwait(false);
        if (jwks is null)
        {
            return Fail(GoogleAuthFailureStage.IdToken, GoogleAuthFailureReason.ProviderError);
        }

        using (jwks)
        {
            return ValidateIdToken(idToken, jwks.RootElement, nonce, intent);
        }
    }

    private GoogleOidcOutcome ValidateIdToken(
        string idToken, JsonElement jwks, string nonce, GoogleAuthorizationIntent intent)
    {
        try
        {
            var now = _time.GetUtcNow();
            var claims = GoogleIdTokenValidator.Validate(
                idToken, jwks, GoogleIdTokenValidator.AcceptedIssuers(_options.Authority), _options.ClientId, nonce, now);
            var identity = GoogleIdentityMapper.Map(claims);
            var failure = GoogleFailureClassifier.ClassifyIdentity(identity, intent, now, ReauthMaxAgeSeconds);
            return failure is null ? new GoogleOidcOutcome(identity, null) : new GoogleOidcOutcome(null, failure);
        }
        catch (GoogleVerificationFailedException)
        {
            return Fail(GoogleAuthFailureStage.IdToken, GoogleAuthFailureReason.ProviderError);
        }
    }

    // Uma nova tentativa em falha transitoria; cancelamento do chamador sempre propaga.
    private async Task<JsonDocument?> FetchJwksWithRetryAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var body = await _http.GetStringAsync(_jwksUri, ct).ConfigureAwait(false);
                return JsonDocument.Parse(body);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException
                || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                // transitorio: tenta de novo uma vez
            }
        }

        return null;
    }

    private static GoogleOidcOutcome Fail(GoogleAuthFailureStage stage, GoogleAuthFailureReason reason) =>
        new(null, new GoogleAuthFailed(stage, reason));

    private async Task<(string? IdToken, GoogleAuthFailed? Failure)> ExchangeCodeAsync(
        string code, string codeVerifier, CancellationToken ct)
    {
        using var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["redirect_uri"] = _options.PublicOrigin.TrimEnd('/') + CallbackPath,
        });
        using var response = await _http.PostAsync(_tokenEndpoint, request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            LogTokenEndpointStatus(_logger, (int)response.StatusCode, null);
            return (null, GoogleFailureClassifier.ClassifyTokenError(body));
        }

        if (GoogleFailureClassifier.ExtractIdToken(body) is not { } idToken)
        {
            LogMissingIdToken(_logger, null);
            return (null, new GoogleAuthFailed(GoogleAuthFailureStage.Token, GoogleAuthFailureReason.ProviderError));
        }

        return (idToken, null);
    }

    public async Task LoadDiscoveryAsync(CancellationToken ct = default)
    {
        if (CachedAuthorizationEndpoint() is not null)
        {
            return;
        }

        var discoveryUrl = new Uri(_options.Authority.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration");
        await using var body = await _http.GetStreamAsync(discoveryUrl, ct).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: ct).ConfigureAwait(false);
        var endpoint = document.RootElement.GetProperty("authorization_endpoint").GetString()
            ?? throw new InvalidOperationException("Discovery sem authorization_endpoint.");

        var root = document.RootElement;
        var token = new Uri(root.GetProperty("token_endpoint").GetString()!);
        var jwks = new Uri(root.GetProperty("jwks_uri").GetString()!);

        lock (_sync)
        {
            _tokenEndpoint = token;
            _jwksUri = jwks;
            _authorizationEndpoint = new Uri(endpoint);
            _loadedAt = _time.GetUtcNow();
        }
    }

    public Uri BuildAuthorizationUrl(
        string state,
        string nonce,
        string codeChallenge,
        GoogleAuthorizationIntent intent = GoogleAuthorizationIntent.SignIn)
    {
        var endpoint = CachedAuthorizationEndpoint()
            ?? throw new InvalidOperationException("Discovery do Google nao carregado; chame LoadDiscoveryAsync antes.");

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["response_type"] = "code";
        query["client_id"] = _options.ClientId;
        query["redirect_uri"] = _options.PublicOrigin.TrimEnd('/') + CallbackPath;
        query["scope"] = "openid email profile";
        query["state"] = state;
        query["nonce"] = nonce;
        query["code_challenge"] = codeChallenge;
        query["code_challenge_method"] = "S256";
        if (intent == GoogleAuthorizationIntent.Reauth)
        {
            query["prompt"] = "login";
            query["max_age"] = ReauthMaxAgeSeconds.ToString(CultureInfo.InvariantCulture);
        }

        return new UriBuilder(endpoint) { Query = query.ToString() }.Uri;
    }

    private Uri? CachedAuthorizationEndpoint()
    {
        lock (_sync)
        {
            return _authorizationEndpoint is not null && _time.GetUtcNow() - _loadedAt < DiscoveryLifetime
                ? _authorizationEndpoint
                : null;
        }
    }
}
