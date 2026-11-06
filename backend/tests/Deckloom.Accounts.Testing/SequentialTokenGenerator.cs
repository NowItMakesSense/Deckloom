using System.Globalization;
using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Testing;

public sealed class SequentialTokenGenerator : ISecureTokenGenerator
{
    private int _last;

    public string Generate() => string.Create(CultureInfo.InvariantCulture, $"tok-{++_last:D4}");
}
