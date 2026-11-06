using System.Reflection;

namespace Deckloom.ArchitectureTests;

// T-160 (RED, R-49): as exclusoes compensatorias do AccessThrottle (SEC-16 / ADR-0005 E4) nao dependem do cancelamento da
// requisicao; um parametro CancellationToken nelas seria ignorado e enganoso. A assinatura trava a ausencia do ct.
public class AccessThrottleSignatureTests
{
    private static Type ThrottleType() =>
        Assembly.Load("Deckloom.Accounts.Application").GetType("Deckloom.Accounts.Application.AccessThrottle", throwOnError: true)!;

    [Theory]
    [InlineData("ReleaseAsync")]
    [InlineData("ClearAccountFailuresAsync")]
    public void R49_compensating_deletions_of_the_AccessThrottle_take_no_CancellationToken(string methodName)
    {
        var methods = ThrottleType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == methodName)
            .ToList();

        Assert.NotEmpty(methods);
        var withToken = methods
            .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)))
            .Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})")
            .ToList();
        Assert.Empty(withToken);
    }
}
