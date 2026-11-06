using Microsoft.EntityFrameworkCore;

namespace Deckloom.Accounts.Infrastructure;

// Save so vale para entidade rastreada (carregada pelo repositorio): desanexada nao grava nada e falha de forma explicita.
internal static class TrackedEntityGuard
{
    public static void EnsureTracked<TEntity>(this DbContext context, TEntity entity)
        where TEntity : class
    {
        if (context.Entry(entity).State == EntityState.Detached)
        {
            throw new InvalidOperationException(
                "Save requires an entity tracked by the context; load it through the repository first.");
        }
    }
}
