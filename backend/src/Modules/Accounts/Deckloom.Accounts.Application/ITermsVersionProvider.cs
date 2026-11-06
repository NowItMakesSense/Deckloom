namespace Deckloom.Accounts.Application;

public interface ITermsVersionProvider
{
    string CurrentVersion { get; }

    Uri TermsUrl { get; }

    Uri PrivacyPolicyUrl { get; }
}
