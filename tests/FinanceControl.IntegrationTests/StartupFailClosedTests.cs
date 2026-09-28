using FinanceControl.API.Infrastructure.Authentication;
using FinanceControl.API.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FinanceControl.IntegrationTests;

/// <summary>
/// Fail-closed startup contract (mirrors Program.cs): an empty connection
/// string or API key must prevent the host from starting via
/// OptionsValidationException. Pure options tests — no database, no HTTP.
/// </summary>
public sealed class StartupFailClosedTests
{
    [Fact]
    public void DatabaseOptions_EmptyPostgres_ThrowsOnStart()
    {
        var services = new ServiceCollection();
        services.AddOptions<DatabaseOptions>()
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Postgres),
                $"{DatabaseOptions.SectionName}:{DatabaseOptions.KeyName} must be configured.")
            .ValidateOnStart();

        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);
    }

    [Fact]
    public void DatabaseOptions_WithPostgres_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddOptions<DatabaseOptions>()
            .Configure(options => options.Postgres = "Host=localhost;Database=x")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Postgres),
                $"{DatabaseOptions.SectionName}:{DatabaseOptions.KeyName} must be configured.")
            .ValidateOnStart();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        Assert.Equal("Host=localhost;Database=x", options.Postgres);
    }

    [Fact]
    public void ApiKeyOptions_EmptyKey_ThrowsOnStart()
    {
        var services = new ServiceCollection();
        services.AddOptions<ApiKeyOptions>()
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey),
                $"{ApiKeyOptions.SectionName}:{ApiKeyOptions.KeyName} must be configured.")
            .ValidateOnStart();

        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ApiKeyOptions>>().Value);
    }

    [Fact]
    public void ApiKeyOptions_WithKey_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddOptions<ApiKeyOptions>()
            .Configure(options => options.ApiKey = "test-key")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApiKey),
                $"{ApiKeyOptions.SectionName}:{ApiKeyOptions.KeyName} must be configured.")
            .ValidateOnStart();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ApiKeyOptions>>().Value;
        Assert.Equal("test-key", options.ApiKey);
    }
}
