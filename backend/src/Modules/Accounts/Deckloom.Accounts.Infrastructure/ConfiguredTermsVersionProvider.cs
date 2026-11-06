using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Infrastructure;

// Adapter de ITermsVersionProvider: versao vigente e URLs vindas da configuracao ("Legal:*"), lidas pela composicao.
public sealed class ConfiguredTermsVersionProvider : ITermsVersionProvider
{
    public ConfiguredTermsVersionProvider(string currentVersion, Uri termsUrl, Uri privacyPolicyUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);
        ArgumentNullException.ThrowIfNull(termsUrl);
        ArgumentNullException.ThrowIfNull(privacyPolicyUrl);
        CurrentVersion = currentVersion;
        TermsUrl = termsUrl;
        PrivacyPolicyUrl = privacyPolicyUrl;
    }

    public string CurrentVersion { get; }

    public Uri TermsUrl { get; }

    public Uri PrivacyPolicyUrl { get; }
}
