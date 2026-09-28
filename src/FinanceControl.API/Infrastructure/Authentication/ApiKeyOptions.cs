namespace FinanceControl.API.Infrastructure.Authentication;

/// <summary>
/// API key settings. Bound from the "Authentication" section and validated at
/// startup, so the application never starts without a key (fail-closed: no
/// silent "everything is open" mode). The front sends the key on every
/// <c>/api/*</c> request in the <c>X-Api-Key</c> header; health and the
/// OpenAPI/Scalar docs stay public.
/// </summary>
public sealed class ApiKeyOptions
{
    /// <summary>Configuration section name. Environment variable: Authentication__ApiKey.</summary>
    public const string SectionName = "Authentication";

    /// <summary>Key inside the section. Full configuration key: Authentication:ApiKey.</summary>
    public const string KeyName = "ApiKey";

    /// <summary>Header the front must send (matched case-insensitively by ASP.NET Core).</summary>
    public const string HeaderName = "X-Api-Key";

    public string ApiKey { get; set; } = string.Empty;
}
