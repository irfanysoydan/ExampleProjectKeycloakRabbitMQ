namespace Accounting.Api.Configuration;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    public required string Authority { get; init; }

    public required string PublicIssuer { get; init; }

    public required string Audience { get; init; }

    public required string ClientId { get; init; }

    public bool RequireHttpsMetadata { get; init; } = true;
}
