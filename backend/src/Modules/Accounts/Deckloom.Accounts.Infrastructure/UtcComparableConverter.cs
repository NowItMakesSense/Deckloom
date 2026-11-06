using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Deckloom.Accounts.Infrastructure;

// Para colunas usadas em predicados de data (limpeza de retencao, R-18): grava o instante em UTC como DateTime. A coluna
// continua timestamptz no PostgreSQL (mesmo mapeamento de access_attempt.attempted_at) e o EF traduz a comparacao
// tambem no SQLite dos testes, que nao compara DateTimeOffset.
internal static class UtcComparableConverter
{
    public static readonly ValueConverter<DateTimeOffset, DateTime> Instance =
        new(value => value.UtcDateTime, value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
}
