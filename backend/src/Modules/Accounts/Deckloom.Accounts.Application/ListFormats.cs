namespace Deckloom.Accounts.Application;

public sealed record FormatOption(string Id, string Name);

public sealed class ListFormats(IFormatCatalog formats)
{
    public async Task<IReadOnlyList<FormatOption>> ExecuteAsync(CancellationToken ct = default)
    {
        var active = await formats.ListActiveAsync(ct);
        return active.Select(f => new FormatOption(f.Id.Value, f.Name)).ToList();
    }
}
