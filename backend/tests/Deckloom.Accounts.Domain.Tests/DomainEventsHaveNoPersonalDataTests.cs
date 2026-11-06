using System.Collections;
using System.Reflection;
using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain.Tests;

// T-023 (habilitadora): regras de design.md, "Regras dos eventos (emenda 2026-10-23)".
// Lista fechada de tipos de propriedade, strings so em propriedades nomeadas, nomes proibidos.
public class DomainEventsHaveNoPersonalDataTests
{
    private static readonly Type[] AllowedTypes =
    [
        typeof(PlayerId),
        typeof(SessionId),
        typeof(DateTimeOffset),
        typeof(DateTimeOffset?),
        typeof(bool),
    ];

    private static readonly string[] ForbiddenNameParts = ["email", "password", "hash", "token", "name", "bio", "ip"];

    private static readonly HashSet<string> AllowedStringProperties =
    [
        "PlayerRegistered.TermsVersion",
        "ProfileUpdated.ChangedFields",
        "AvatarChanged.PreviousStorageKey",
    ];

    private static readonly string[] KnownEvents =
    [
        "AvatarChanged",
        "GoogleIdentityLinked",
        "PasswordChanged",
        "PasswordResetIssued",
        "PlayerDeleted",
        "PlayerRegistered",
        "ProfileUpdated",
        "SessionRevoked",
        "SessionStarted",
    ];

    private sealed record LeakyEvent(PlayerId PlayerId, string Email) : DomainEvent;

    private sealed record LeakyTypeEvent(PlayerId PlayerId, Guid Raw) : DomainEvent;

    private sealed record SneakyStringEvent(PlayerId PlayerId, string Note) : DomainEvent;

    private sealed record NamedBadEvent(PlayerId PlayerId, DateTimeOffset LastIp) : DomainEvent;

    private static List<Type> ScanEventTypes() =>
        typeof(PlayerRegistered).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(DomainEvent)))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    private static PropertyInfo[] DeclaredProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private static bool IsAllowedStringShape(Type type) =>
        type == typeof(string) || type == typeof(IReadOnlyList<string>);

    private static bool IsStringLike(Type type) =>
        type == typeof(string) || (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type));

    private static List<string> Violations(Type eventType)
    {
        var violations = new List<string>();
        foreach (var property in DeclaredProperties(eventType))
        {
            var key = $"{eventType.Name}.{property.Name}";
            var lowered = property.Name.ToLowerInvariant();

            foreach (var forbidden in ForbiddenNameParts.Where(lowered.Contains))
            {
                violations.Add($"{key}: nome contem '{forbidden}'");
            }

            var type = property.PropertyType;
            var isAllowedType = AllowedTypes.Contains(type) || type.IsEnum;
            var isAllowedString = AllowedStringProperties.Contains(key) && IsAllowedStringShape(type);

            if (!isAllowedType && !isAllowedString)
            {
                violations.Add($"{key}: tipo '{type.Name}' fora da lista fechada");
            }
        }

        return violations;
    }

    [Fact]
    public void The_scan_finds_every_known_domain_event_so_it_cannot_pass_empty()
    {
        var found = ScanEventTypes().Select(t => t.Name).ToArray();

        Assert.Equal(KnownEvents, found);
    }

    [Fact]
    public void No_domain_event_breaks_the_closed_type_list_or_the_forbidden_names()
    {
        var violations = ScanEventTypes().SelectMany(Violations).ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void String_properties_exist_only_where_the_design_names_them()
    {
        var stringProperties = ScanEventTypes()
            .SelectMany(t => DeclaredProperties(t).Select(p => (Key: $"{t.Name}.{p.Name}", p.PropertyType)))
            .Where(x => IsStringLike(x.PropertyType))
            .Select(x => x.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AllowedStringProperties.Order(StringComparer.Ordinal).ToArray(), stringProperties);
    }

    [Fact]
    public void The_rule_rejects_a_string_property_named_email()
    {
        var violations = Violations(typeof(LeakyEvent));

        Assert.Contains(violations, v => v.Contains("'email'", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("fora da lista fechada", StringComparison.Ordinal));
    }

    [Fact]
    public void The_rule_rejects_a_type_outside_the_closed_list()
    {
        Assert.NotEmpty(Violations(typeof(LeakyTypeEvent)));
    }

    [Fact]
    public void The_rule_rejects_a_string_outside_the_named_properties()
    {
        Assert.NotEmpty(Violations(typeof(SneakyStringEvent)));
    }

    [Fact]
    public void The_rule_rejects_a_forbidden_name_even_with_an_allowed_type()
    {
        var violations = Violations(typeof(NamedBadEvent));

        Assert.Contains(violations, v => v.Contains("'ip'", StringComparison.Ordinal));
    }

    [Fact]
    public void The_rule_accepts_the_known_events()
    {
        Assert.Empty(Violations(typeof(SessionStarted)));
        Assert.Empty(Violations(typeof(ProfileUpdated)));
    }
}
