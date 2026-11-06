using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-160 (QA-18) reescrito pela T-166 (R-50): REQ-032, REQ-041, SEC-16 / ADR-0005 E4. Com a requisicao cancelada DEPOIS do
// trabalho da unidade de trabalho e ANTES do commit (login: ao gerar o token de sessao; exclusao: ao purgar os decks), o
// commit observa o ct como o EfUnitOfWork (SaveChangesAsync(ct)): o OperationCanceledException propaga ao chamador e
// NADA fica gravado: nenhuma sessao, conta e Jogador intactos, nenhuma revogacao, e as falhas da conta e o evento
// provisorio MANTIDOS (a limpeza das falhas e participante do commit e e descartada no rollback). O que a limpeza
// compensatoria garante (T-160) e que o token passado ao store em DeleteByAccountAsync nao e o da requisicao.
// O store do teste (CancellationAwareStore) imita o EF: lanca OperationCanceledException em DeleteByAccountAsync recebido
// com token cancelado e registra o token. Os testes usam so a API publica dos casos de uso.
public sealed class AccountFailuresLateCancellationTests : IDisposable
{
    private const string Ip = "203.0.113.7";
    private const string RawPassword = "correct horse battery";
    private const string PasswordEmail = "ana@example.com";
    private const string GoogleEmail = "gui@example.com";
    private const string Subject = "google-sub-1";
    private const string OtherIp = "198.51.100.1";

    private static readonly ExternalIdentity Linked = ExternalIdentity.Create("google", Subject).Value;

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly InMemoryAccessAttemptStore _inner;
    private readonly CancellationAwareStore _store;
    private readonly RecordingPlayerDecksPurger _purger;
    private readonly InMemoryAvatarStorage _storage;
    private readonly FakePasswordHasher _hasher = new(600_000);
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeKeyedHasher _keyedHasher = new();
    private readonly RecordingDelayer _delayer = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly CancellationTokenSource _request = new();
    private readonly InMemoryAvatarPresetCatalog _catalog = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, false, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, true, true),
    ]);

    public AccountFailuresLateCancellationTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _inner = new InMemoryAccessAttemptStore(_uow);
        _store = new CancellationAwareStore(_inner);
        _purger = new RecordingPlayerDecksPurger(_uow);
        _storage = new InMemoryAvatarStorage(_uow);
    }

    public void Dispose() => _request.Dispose();

    private AccessThrottle NewThrottle() =>
        new(_store, _keyedHasher, _delayer, new ProgressiveDelayPolicy(new ProgressiveDelayOptions()), _clock);

    private Task<int> AccountFailures(string email) =>
        _inner.CountByAccountAsync(_keyedHasher.Hash(email), Now.AddHours(-1));

    private async Task SeedFailuresAsync(string email, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await _inner.RecordAsync(
                new AccessAttempt(AccessAction.Login, _keyedHasher.Hash(OtherIp), _keyedHasher.Hash(email), Now.AddMinutes(-1)));
        }
    }

    private async Task<Player> SeedPasswordPlayerAsync()
    {
        var player = Player.RegisterWithPassword(
            AnEmail(PasswordEmail),
            await _hasher.HashAsync(Password.Create(RawPassword).Value),
            DisplayName.Create("Ana").Value,
            "2026-10-01",
            "2026-10-01",
            true,
            true,
            Now,
            "preset-01").Value;
        player.ClearDomainEvents();
        await _players.Add(player);
        await _uow.CommitAsync();
        return player;
    }

    private void AssertTheStoreGotAnIndependentToken()
    {
        var seen = Assert.Single(_store.DeleteByAccountTokens);
        Assert.NotEqual(_request.Token, seen);
        Assert.False(seen.IsCancellationRequested);
    }

    [Fact]
    public async Task QA18_R50_login_cancelled_before_the_commit_throws_and_rolls_everything_back()
    {
        var player = await SeedPasswordPlayerAsync();
        await SeedFailuresAsync(PasswordEmail, 3);
        var useCase = new LoginWithPassword(
            _players, _sessions, _uow, _hasher, new CancellingTokenGenerator(_request), _tokenHasher, _catalog,
            NewThrottle(), _clock);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useCase.ExecuteAsync(
            new LoginWithPasswordCommand(PasswordEmail, RawPassword, false, Ip), _request.Token));

        Assert.True(_request.IsCancellationRequested);
        Assert.Null(await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")));
        Assert.NotNull(await _players.GetById(player.Id));
        Assert.Equal(3 + 1, await AccountFailures(PasswordEmail)); // 3 falhas + o evento provisorio
        AssertTheStoreGotAnIndependentToken();
    }

    [Fact]
    public async Task QA18_R50_password_account_deletion_cancelled_before_the_commit_throws_and_rolls_everything_back()
    {
        var player = await SeedPasswordPlayerAsync();
        await SeedFailuresAsync(PasswordEmail, 3);
        var session = Session.Start(player.Id, _tokenHasher.Hash("ana-token"), false, Now);
        await _sessions.Add(session);
        await _uow.CommitAsync();
        var caller = (await new AuthenticateSession(_sessions, _players, _uow, _tokenHasher, _catalog, _clock)
            .ExecuteAsync(new AuthenticateSessionCommand("ana-token"))).Value;
        var useCase = new DeleteMyAccount(
            _players, _sessions, new CancellingPurger(_purger, _request), _storage, _uow, _hasher, NewThrottle(), _clock);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync(new DeleteMyAccountCommand(caller, RawPassword, true, Ip), _request.Token));

        Assert.True(_request.IsCancellationRequested);
        Assert.NotNull(await _players.GetById(player.Id));
        Assert.Null(session.RevokedAt);
        Assert.Equal(3 + 1, await AccountFailures(PasswordEmail)); // 3 falhas + o evento provisorio
        AssertTheStoreGotAnIndependentToken();
    }

    [Fact]
    public async Task QA18_R50_google_account_deletion_cancelled_before_the_commit_throws_and_rolls_everything_back()
    {
        var player = AGooglePlayer(GoogleEmail, Subject);
        player.ClearDomainEvents();
        await _players.Add(player);
        var session = Session.Start(player.Id, _tokenHasher.Hash("gui-token"), false, Now);
        await _sessions.Add(session);
        await _uow.CommitAsync();
        Assert.True(session.MarkReauthenticated(Linked, Linked, _clock.GetUtcNow()).IsSuccess);
        await SeedFailuresAsync(GoogleEmail, 3);
        var caller = (await new AuthenticateSession(_sessions, _players, _uow, _tokenHasher, _catalog, _clock)
            .ExecuteAsync(new AuthenticateSessionCommand("gui-token"))).Value;
        var useCase = new DeleteMyAccount(
            _players, _sessions, new CancellingPurger(_purger, _request), _storage, _uow, _hasher, NewThrottle(), _clock);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync(new DeleteMyAccountCommand(caller, null, true, Ip), _request.Token));

        Assert.True(_request.IsCancellationRequested);
        Assert.NotNull(await _players.GetById(player.Id));
        Assert.Null(session.RevokedAt);
        Assert.Equal(3, await AccountFailures(GoogleEmail)); // sem evento provisorio no fluxo Google
        AssertTheStoreGotAnIndependentToken();
    }

    // Cancela a requisicao dentro da unidade de trabalho do login, antes da limpeza.
    private sealed class CancellingTokenGenerator(CancellationTokenSource request) : ISecureTokenGenerator
    {
        private readonly SequentialTokenGenerator _inner = new();

        public string Generate()
        {
            request.Cancel();
            return _inner.Generate();
        }
    }

    // Cancela a requisicao dentro da unidade de trabalho da exclusao, antes da limpeza.
    private sealed class CancellingPurger(IPlayerDecksPurger inner, CancellationTokenSource request) : IPlayerDecksPurger
    {
        public async Task PurgeAsync(PlayerId playerId, CancellationToken ct = default)
        {
            await inner.PurgeAsync(playerId, CancellationToken.None);
            await request.CancelAsync();
        }
    }

    // Imita o EF: DeleteByAccountAsync com token cancelado lanca OperationCanceledException; registra o token recebido.
    private sealed class CancellationAwareStore(InMemoryAccessAttemptStore inner) : IAccessAttemptStore
    {
        public List<CancellationToken> DeleteByAccountTokens { get; } = [];

        public Task<long> RecordAsync(AccessAttempt attempt, CancellationToken ct = default) =>
            inner.RecordAsync(attempt, ct);

        public Task<int> DeleteByIdAsync(long id, CancellationToken ct = default) => inner.DeleteByIdAsync(id, ct);

        public Task<int> CountByIpAsync(byte[] ipKeyHash, DateTimeOffset since, CancellationToken ct = default) =>
            inner.CountByIpAsync(ipKeyHash, since, ct);

        public Task<int> CountByAccountAsync(byte[] accountKeyHash, DateTimeOffset since, CancellationToken ct = default) =>
            inner.CountByAccountAsync(accountKeyHash, since, ct);

        public Task<int> DeleteByAccountAsync(byte[] accountKeyHash, CancellationToken ct = default)
        {
            DeleteByAccountTokens.Add(ct);
            ct.ThrowIfCancellationRequested();
            return inner.DeleteByAccountAsync(accountKeyHash, ct);
        }

        public Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken ct = default) =>
            inner.DeleteOlderThanAsync(cutoff, batchSize, ct);
    }
}
