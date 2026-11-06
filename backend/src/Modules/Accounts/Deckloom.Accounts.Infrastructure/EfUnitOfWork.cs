using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;
using Microsoft.EntityFrameworkCore;

namespace Deckloom.Accounts.Infrastructure;

// Traduz as falhas do EF Core/provedor em excecoes do dominio, e ExecuteAsync delimita a transacao so em volta da persistencia
// e grava ao fim o que o bloco deixou enfileirado no contexto.
public sealed class EfUnitOfWork(AccountsDbContext context) : IUnitOfWork
{
    // SQLite nomeia a coluna ("UNIQUE constraint failed: player.email"); PostgreSQL nomeia o indice.
    private static readonly (string Constraint, string[] Markers)[] UniqueConstraints =
    [
        (
            PlayerEntityConfiguration.EmailIndexName,
            [PlayerEntityConfiguration.EmailIndexName, $"{PlayerEntityConfiguration.TableName}.email"]),
        (
            PlayerEntityConfiguration.ExternalIdentityIndexName,
            [
                PlayerEntityConfiguration.ExternalIdentityIndexName,
                $"{PlayerEntityConfiguration.ExternalIdentityTableName}.subject",
            ]),
        (
            SessionEntityConfiguration.TokenHashIndexName,
            [
                SessionEntityConfiguration.TokenHashIndexName,
                $"{SessionEntityConfiguration.TableName}.token_hash",
            ]),
        (
            PendingGoogleAuthEntityConfiguration.TokenHashIndexName,
            [
                PendingGoogleAuthEntityConfiguration.TokenHashIndexName,
                $"{PendingGoogleAuthEntityConfiguration.TableName}.token_hash",
            ]),
        (
            PlayerEntityConfiguration.ResetTokenHashIndexName,
            [
                PlayerEntityConfiguration.ResetTokenHashIndexName,
                $"{PlayerEntityConfiguration.ResetTokenTableName}.token_hash",
            ]),
        (
            AvatarImageEntityConfiguration.PlayerIndexName,
            [
                AvatarImageEntityConfiguration.PlayerIndexName,
                $"{AvatarImageEntityConfiguration.TableName}.player_id",
            ]),
        (
            AvatarImageEntityConfiguration.PrimaryKeyName,
            [
                AvatarImageEntityConfiguration.PrimaryKeyName,
                $"{AvatarImageEntityConfiguration.TableName}.storage_key",
            ]),
    ];

    public async Task CommitAsync(CancellationToken ct = default)
    {
        try
        {
            await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
        catch (DbUpdateException exception) when (ViolatedConstraint(exception) is not null)
        {
            throw new UniqueConstraintViolationException(ViolatedConstraint(exception)!);
        }
    }

    public Task ExecuteAsync(Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        return ExecuteAsync<object?>(
            async token =>
            {
                await work(token);
                return null;
            },
            ct);
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        try
        {
            var result = await work(ct);
            await CommitAsync(ct); // grava o que o bloco so enfileirou (no-op se ja gravado)
            await transaction.CommitAsync(ct);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear(); // descarta o que o bloco enfileirou para nao vazar na proxima execucao
            throw;
        }
    }

    private static string? ViolatedConstraint(DbUpdateException exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (!inner.Message.Contains("unique", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var (constraint, markers) in UniqueConstraints)
            {
                if (markers.Any(marker => inner.Message.Contains(marker, StringComparison.Ordinal)))
                {
                    return constraint;
                }
            }
        }

        return null;
    }
}
