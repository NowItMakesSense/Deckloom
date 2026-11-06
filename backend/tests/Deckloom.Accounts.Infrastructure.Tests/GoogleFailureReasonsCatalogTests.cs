using System.Reflection;
using Deckloom.Accounts.Application;
using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Infrastructure.Tests;

// T-162 (RED, R-47): a Application nao referencia a Infrastructure; uma unica fonte de verdade e verificada por teste.
// Os motivos do 1010 emitidos pela Application (classe GoogleAuthFailureReasons, nome sugerido na tarefa) tem de ser
// valores do catalogo: os de GoogleAuthFailureLogNames.For(reason) mais session_invalid (emenda do T-151, Session).
public class GoogleFailureReasonsCatalogTests
{
    private const string ReasonsTypeName = "Deckloom.Accounts.Application.GoogleAuthFailureReasons";

    private static List<string> ApplicationReasonValues()
    {
        var type = typeof(HandleGoogleCallback).Assembly.GetType(ReasonsTypeName, throwOnError: false);
        Assert.True(type is not null, $"{ReasonsTypeName} (constantes dos motivos do 1010) nao existe na Application.");
        return type!
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();
    }

    private static HashSet<string> InfrastructureCatalog() =>
        Enum.GetValues<GoogleAuthFailureReason>()
            .Select(GoogleAuthFailureLogNames.For)
            .Append(Session.SessionInvalidCode)
            .ToHashSet();

    [Fact]
    public void R47_every_application_reason_equals_a_value_of_the_infrastructure_catalog()
    {
        var values = ApplicationReasonValues();

        Assert.NotEmpty(values);
        var catalog = InfrastructureCatalog();
        foreach (var value in values)
        {
            Assert.Contains(value, catalog);
        }
    }

    [Fact]
    public void R47_application_reasons_include_the_ones_emitted_by_HandleGoogleCallback()
    {
        var values = ApplicationReasonValues();

        foreach (var emitted in new[]
                 {
                     "session_invalid", "subject_mismatch", "provider_error",
                     "unverified_email", "cancelled", "state_mismatch",
                 })
        {
            Assert.Contains(emitted, values);
        }
    }
}
