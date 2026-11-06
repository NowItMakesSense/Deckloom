using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Deckloom.Accounts.Testing;
using Deckloom.SharedKernel;
using Microsoft.Extensions.Time.Testing;
using static Deckloom.Accounts.Application.Tests.PlayerFixtures;

namespace Deckloom.Accounts.Application.Tests;

// T-042 (REQ-039): expiracao, uso unico e corrida na conclusao do cadastro Google. RED.
// Contrato fixado pela spec (design.md linhas 328-331, 1534, 1583):
//   - pendencia expirada/inexistente/ja consumida -> Result.Failure(DomainError("pending_not_found")) (404), sem Field;
//   - email que passou a existir OU violacao de unicidade (ux_player_email, ux_player_external_identity_provider_subject)
//     no commit -> Result.Failure(DomainError("google_not_completed")) (409), sem Field; nunca excecao (nunca 500);
//   - a pendencia e consumida na mesma unidade de trabalho que cria Jogador e Sessao; so o vencedor da corrida cria conta.
// Pre-requisitos de GREEN que estes testes exigem (a spec nao os fixa; decisao do usuario: o store entra no UoW na T-042):
//   1. O caso de uso passa a usar o resultado de ConsumeAsync: nulo (outro envio consumiu, ou expirou entre Find e Consume)
//      -> pending_not_found, sem criar nada.
//   2. O caso de uso captura UniqueConstraintViolationException em unitOfWork.ExecuteAsync -> google_not_completed.
//   3. FAKE: InMemoryPendingGoogleAuthStore passa a participar do InMemoryUnitOfWork (ICommitParticipant). Hoje ConsumeAsync
//      remove na hora e o rollback NAO restaura; o teste REQ039_a_failed_commit_keeps_the_pending_available so fica verde
//      quando o descarte do UoW restaura a pendencia. O construtor muda de (TimeProvider) para (TimeProvider, InMemoryUnitOfWork);
//      este arquivo e os outros testes que constroem o store (CompleteGoogleSignupTests e demais) serao ajustados no GREEN.
//      Atencao: o ConsumeAsync dentro do bloco deve continuar atomico entre contendores (so um recebe a pendencia).
public class CompleteGoogleSignupConcurrencyTests
{
    private const string TermsVersion = "2026-10-01";
    private const string PendingToken = "pending-raw-token";
    private const string Subject = "google-sub-1";
    private const string GoogleEmail = "bia@example.com";
    private const string EmailConstraint = "ux_player_email";
    private const string SubjectConstraint = "ux_player_external_identity_provider_subject";

    private static readonly DomainError PendingNotFound = new("pending_not_found");
    private static readonly DomainError GoogleNotCompleted = new("google_not_completed");

    private readonly InMemoryUnitOfWork _uow = new();
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemorySessionRepository _sessions;
    private readonly InMemoryPendingGoogleAuthStore _store;
    private readonly SequentialTokenGenerator _tokens = new();
    private readonly Sha256TokenHasher _tokenHasher = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryAvatarPresetCatalog _catalog = new(
    [
        new AvatarPresetEntry("preset-01", "Dragao", "/avatars/preset-01.png", 1, false, true),
        new AvatarPresetEntry("preset-02", "Planeswalker", "/avatars/preset-02.png", 2, true, true),
    ]);

    private readonly FixedTermsVersionProvider _terms = new(
        TermsVersion, new Uri("https://deckloom.test/termos"), new Uri("https://deckloom.test/privacidade"));

    public CompleteGoogleSignupConcurrencyTests()
    {
        _players = new InMemoryPlayerRepository(_uow);
        _sessions = new InMemorySessionRepository(_uow);
        _store = new InMemoryPendingGoogleAuthStore(_clock, _uow);
    }

    private CompleteGoogleSignup NewUseCase(IPendingGoogleAuthStore? store = null, IUnitOfWork? uow = null) =>
        new(_players, _sessions, uow ?? _uow, _tokens, _tokenHasher, _terms, _catalog, store ?? _store, _clock);

    private static CompleteGoogleSignupCommand ValidCommand() =>
        new(PendingToken, "Beatriz", true, TermsVersion, true);

    private async Task SeedSignupAsync() =>
        await _store.AddAsync(PendingGoogleSignup.Create(
            _tokenHasher.Hash(PendingToken), Subject, AnEmail(GoogleEmail), "Beatriz Souza", Now));

    private async Task<bool> PendingStillStoredAsync() =>
        await _store.FindAsync(_tokenHasher.Hash(PendingToken)) is not null;

    private async Task AssertNoAccountCreatedAsync()
    {
        Assert.Null(await _players.FindByEmail(AnEmail(GoogleEmail)));
        Assert.Null(await _players.FindByGoogleSubject(Subject));
    }

    [Fact]
    public async Task REQ039_a_pending_older_than_ten_minutes_creates_no_account_and_asks_to_redo_the_login()
    {
        await SeedSignupAsync();
        _clock.Advance(TimeSpan.FromMinutes(11));

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsFailure);
        Assert.Equal(PendingNotFound, Assert.Single(result.Errors));
        await AssertNoAccountCreatedAsync();
        Assert.Null(await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")));
    }

    [Fact]
    public async Task REQ039_a_pending_that_expires_between_reading_and_consuming_creates_no_account()
    {
        await SeedSignupAsync();
        var store = new ExpireAfterFindStore(_store, _clock, TimeSpan.FromMinutes(11));

        var result = await NewUseCase(store).ExecuteAsync(ValidCommand());

        Assert.True(result.IsFailure);
        Assert.Equal(PendingNotFound, Assert.Single(result.Errors));
        await AssertNoAccountCreatedAsync();
        Assert.Null(await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")));
    }

    [Fact]
    public async Task REQ039_resubmitting_the_same_pending_after_success_creates_no_second_account()
    {
        await SeedSignupAsync();
        var first = await NewUseCase().ExecuteAsync(ValidCommand());

        var second = await NewUseCase().ExecuteAsync(ValidCommand() with { DisplayName = "Outra" });

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal(PendingNotFound, Assert.Single(second.Errors));
        Assert.Equal("Beatriz", (await _players.FindByEmail(AnEmail(GoogleEmail)))!.DisplayName.Value);
        Assert.Null(await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0002")));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(20)]
    public async Task REQ039_concurrent_submissions_of_the_same_pending_have_exactly_one_winner(int contenders)
    {
        await SeedSignupAsync();
        var gate = new GateStore(_store, contenders);

        var results = await Task.WhenAll(Enumerable.Range(0, contenders).Select(_ =>
            NewUseCase(gate).ExecuteAsync(ValidCommand())));

        var winner = Assert.Single(results, r => r.IsSuccess);
        Assert.All(results.Where(r => r.IsFailure), r => Assert.Equal(PendingNotFound, Assert.Single(r.Errors)));
        var stored = (await _players.FindByEmail(AnEmail(GoogleEmail)))!;
        Assert.NotNull(stored);
        Assert.Same(stored, await _players.FindByGoogleSubject(Subject));
        Assert.NotNull(await _sessions.FindByTokenHash(_tokenHasher.Hash(winner.Value.SessionToken)));
        Assert.False(await PendingStillStoredAsync());
    }

    [Fact]
    public async Task REQ039_an_email_that_became_taken_after_the_callback_is_google_not_completed()
    {
        await SeedSignupAsync();
        await _players.Add(APasswordPlayer(GoogleEmail));
        await _uow.CommitAsync();

        var result = await NewUseCase().ExecuteAsync(ValidCommand());

        Assert.True(result.IsFailure);
        Assert.Equal(GoogleNotCompleted, Assert.Single(result.Errors));
        Assert.Null(await _players.FindByGoogleSubject(Subject));
        Assert.Null(await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")));
    }

    [Theory]
    [InlineData(EmailConstraint)]
    [InlineData(SubjectConstraint)]
    public async Task REQ039_a_unique_violation_at_commit_is_translated_to_google_not_completed_and_does_not_leak(
        string constraint)
    {
        await SeedSignupAsync();
        var failing = new ViolatingUnitOfWork(_uow, constraint);

        var result = await NewUseCase(uow: failing).ExecuteAsync(ValidCommand());

        Assert.True(result.IsFailure);
        Assert.Equal(GoogleNotCompleted, Assert.Single(result.Errors));
        await AssertNoAccountCreatedAsync();
        Assert.Null(await _sessions.FindByTokenHash(_tokenHasher.Hash("tok-0001")));
    }

    // Exige o store no InMemoryUnitOfWork (ver cabecalho, item 3): hoje o rollback nao restaura a pendencia consumida.
    [Fact]
    public async Task REQ039_a_failed_commit_keeps_the_pending_available()
    {
        await SeedSignupAsync();
        var failing = new ViolatingUnitOfWork(_uow, SubjectConstraint);

        var result = await NewUseCase(uow: failing).ExecuteAsync(ValidCommand());

        Assert.True(result.IsFailure);
        Assert.True(await PendingStillStoredAsync());
    }

    // Decorator do store: depois de ler a pendencia ainda valida, segura cada contendor ate todos terem lido
    // (ou ate o limite de tempo), forcando a disputa real pelo consumo. Continuacoes rodam em linha, uma por vez.
    private sealed class GateStore(IPendingGoogleAuthStore inner, int contenders) : IPendingGoogleAuthStore
    {
        private readonly TaskCompletionSource _released = new();
        private int _arrived;

        public Task AddAsync(PendingGoogleAuth pending, CancellationToken ct = default) => inner.AddAsync(pending, ct);

        public async Task<PendingGoogleAuth?> FindAsync(TokenHash tokenHash, CancellationToken ct = default)
        {
            var found = await inner.FindAsync(tokenHash, ct);
            if (Interlocked.Increment(ref _arrived) >= contenders)
            {
                _released.TrySetResult();
            }
            else
            {
                await Task.WhenAny(_released.Task, Task.Delay(TimeSpan.FromSeconds(2), ct));
            }

            return found;
        }

        public Task<PendingGoogleAuth?> ConsumeAsync(TokenHash tokenHash, CancellationToken ct = default) =>
            inner.ConsumeAsync(tokenHash, ct);

        public Task<int> DeleteExpiredAsync(int batchSize, CancellationToken ct = default) => inner.DeleteExpiredAsync(batchSize, ct);
    }

    // Decorator do store: a pendencia e lida valida e o relogio avanca logo depois (expira antes do consumo).
    private sealed class ExpireAfterFindStore(
        IPendingGoogleAuthStore inner, FakeTimeProvider clock, TimeSpan advance) : IPendingGoogleAuthStore
    {
        public Task AddAsync(PendingGoogleAuth pending, CancellationToken ct = default) => inner.AddAsync(pending, ct);

        public async Task<PendingGoogleAuth?> FindAsync(TokenHash tokenHash, CancellationToken ct = default)
        {
            var found = await inner.FindAsync(tokenHash, ct);
            clock.Advance(advance);
            return found;
        }

        public Task<PendingGoogleAuth?> ConsumeAsync(TokenHash tokenHash, CancellationToken ct = default) =>
            inner.ConsumeAsync(tokenHash, ct);

        public Task<int> DeleteExpiredAsync(int batchSize, CancellationToken ct = default) => inner.DeleteExpiredAsync(batchSize, ct);
    }

    // Decorator do UoW: executa o bloco e, ao confirmar, a violacao de unicidade estoura; o UoW real descarta e relanca
    // (design.md linha 331), reproduzindo uma corrida perdida no banco sem alterar os fakes de repositorio.
    private sealed class ViolatingUnitOfWork(InMemoryUnitOfWork inner, string constraint) : IUnitOfWork
    {
        public Task CommitAsync(CancellationToken ct = default) => inner.CommitAsync(ct);

        public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct = default) =>
            inner.ExecuteAsync(async token =>
            {
                await work(token);
                throw new UniqueConstraintViolationException(constraint);
            }, ct);

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default) =>
            inner.ExecuteAsync<T>(async token =>
            {
                await work(token);
                throw new UniqueConstraintViolationException(constraint);
            }, ct);
    }
}
