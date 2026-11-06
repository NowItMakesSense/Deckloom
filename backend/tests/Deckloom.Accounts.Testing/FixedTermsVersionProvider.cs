using Deckloom.Accounts.Application;

namespace Deckloom.Accounts.Testing;

public sealed class FixedTermsVersionProvider(string version, Uri termsUrl, Uri privacyPolicyUrl) : ITermsVersionProvider
{
    public string CurrentVersion { get; } = version;

    public Uri TermsUrl { get; } = termsUrl;

    public Uri PrivacyPolicyUrl { get; } = privacyPolicyUrl;
}
