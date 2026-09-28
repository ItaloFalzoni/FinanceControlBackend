using System.Security.Cryptography;
using System.Text;
using FinanceControl.API.Application;
using Microsoft.Extensions.Options;

namespace FinanceControl.API.Infrastructure.Authentication;

/// <summary>
/// Enforces the shared API key on every /api/* request. Health and the OpenAPI
/// document do not live under /api, so they stay public (container healthcheck
/// and docs). The configured key is resolved via IOptions.Value, so a missing
/// key fails the host at startup (fail-closed) instead of opening the API.
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, IOptions<ApiKeyOptions> options)
{
    private readonly RequestDelegate _next = next;
    private readonly string _configuredKey = options.Value.ApiKey;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        if (!ApiKeyMatches(context.Request.Headers[ApiKeyOptions.HeaderName], _configuredKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            context.Response.Headers.WWWAuthenticate = ApiKeySecuritySchemeTransformer.SchemeName;
            await context.Response.WriteAsJsonAsync(
                new ErrorResponse("Unauthorized", "Missing or invalid API key."));
            return;
        }

        await _next(context);
    }

    // Constant-time comparison over SHA-512 digests: the digest has a fixed length,
    // so neither the configured key nor a prefix of it leaks through timing.
    internal static bool ApiKeyMatches(string? provided, string configured)
    {
        if (string.IsNullOrWhiteSpace(provided))
            return false;

        var providedHash = SHA512.HashData(Encoding.UTF8.GetBytes(provided));
        var configuredHash = SHA512.HashData(Encoding.UTF8.GetBytes(configured));
        return CryptographicOperations.FixedTimeEquals(providedHash, configuredHash);
    }
}

/// <summary>
/// Registers <see cref="ApiKeyMiddleware"/> in the pipeline.
/// </summary>
public static class ApiKeyMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyAuthentication(this IApplicationBuilder app) =>
        app.UseMiddleware<ApiKeyMiddleware>();
}
