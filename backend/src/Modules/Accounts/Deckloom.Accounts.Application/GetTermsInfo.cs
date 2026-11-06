namespace Deckloom.Accounts.Application;

public sealed record TermsInfo(string Version, Uri TermsUrl, Uri PrivacyUrl);

public sealed class GetTermsInfo(ITermsVersionProvider terms)
{
    public Task<TermsInfo> ExecuteAsync(CancellationToken ct = default) =>
        Task.FromResult(new TermsInfo(terms.CurrentVersion, terms.TermsUrl, terms.PrivacyPolicyUrl));
}
