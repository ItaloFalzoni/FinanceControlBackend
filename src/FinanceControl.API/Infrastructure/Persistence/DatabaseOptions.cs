namespace FinanceControl.API.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL connection settings. Bound from the "ConnectionStrings" section and
/// validated at startup, so the application never starts without a database
/// (fail-closed: no in-memory fallback, no silent degradation).
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>Configuration section name. Environment variable: ConnectionStrings__Postgres.</summary>
    public const string SectionName = "ConnectionStrings";

    /// <summary>Key inside the section. Full configuration key: ConnectionStrings:Postgres.</summary>
    public const string KeyName = "Postgres";

    public string Postgres { get; set; } = string.Empty;
}
