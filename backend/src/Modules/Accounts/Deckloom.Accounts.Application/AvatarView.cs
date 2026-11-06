namespace Deckloom.Accounts.Application;

public sealed record AvatarView(string Kind, string? PresetId, string ImageUrl, string Alt)
{
    public const string PresetKind = "preset";

    public const string CustomKind = "custom";
}
