using Deckloom.Accounts.Domain;

namespace Deckloom.Accounts.Application;

// Validacao de nome e termos identica nos cadastros por senha e por Google; erros vao para o coletor na ordem displayName, termos.
internal static class RegistrationInputValidation
{
    public static DisplayName? CollectDisplayNameAndTerms(
        this ValidationErrorCollector validation,
        string? displayName,
        ITermsVersionProvider termsVersionProvider,
        string? submittedTermsVersion,
        bool adultDeclaration,
        bool acceptTerms,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(termsVersionProvider);

        var name = validation.Collect(DisplayName.Create(displayName), "displayName");
        validation.Collect(TermsAcceptance.Create(
            termsVersionProvider.CurrentVersion, submittedTermsVersion, adultDeclaration, acceptTerms, now));
        return name;
    }
}
