namespace Deckloom.Api.Composition;

// Opcoes tipadas da composicao. Segredos nunca aparecem em ToString (nem em mensagens de validacao).
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string PublicOrigin { get; init; } = "";

    public string RateLimitHmacKey { get; init; } = "";

    public GoogleAuthOptions Google { get; init; } = new();

    public override string ToString() => nameof(AuthOptions);
}

public sealed class GoogleAuthOptions
{
    public string Authority { get; init; } = "https://accounts.google.com";

    public string ClientId { get; init; } = "";

    public string ClientSecret { get; init; } = "";

    public override string ToString() => nameof(GoogleAuthOptions);
}

public sealed class LegalOptions
{
    public const string SectionName = "Legal";

    public string TermsVersion { get; init; } = "";

    public string TermsUrl { get; init; } = "";

    public string PrivacyUrl { get; init; } = "";
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string From { get; init; } = "";

    public SmtpOptions Smtp { get; init; } = new();

    public override string ToString() => nameof(EmailOptions);
}

public sealed class SmtpOptions
{
    public string Host { get; init; } = "";

    public int Port { get; init; } = 587;

    public bool UseTls { get; init; } = true;

    public string? User { get; init; }

    public string? Password { get; init; }

    public override string ToString() => nameof(SmtpOptions);
}

public sealed class AvatarOptions
{
    public const string SectionName = "Avatar";

    public int MaxBytes { get; init; } = 262_144;

    public int MaxDimension { get; init; } = 4096;

    public long MaxPixels { get; init; } = 16_777_216;
}

// Secao "ConnectionStrings", propriedade Deckloom (papel deckloom_app).
// A API nao tem chave de configuracao para credenciais de postgres/dono/migrator.
public sealed class DatabaseOptions
{
    public string Deckloom { get; init; } = "";

    public override string ToString() => nameof(DatabaseOptions);
}
