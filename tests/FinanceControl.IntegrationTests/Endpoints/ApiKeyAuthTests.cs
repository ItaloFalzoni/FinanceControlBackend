using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FinanceControl.API.Application;

namespace FinanceControl.IntegrationTests.Endpoints;

/// <summary>
/// API key contract: every /api/* route answers 401 without a key (or with a
/// wrong one) before touching the service, and runs normally with the key.
/// /health and the OpenAPI document stay public so the container healthcheck
/// and the Scalar reference UI keep working.
/// </summary>
public sealed class ApiKeyAuthTests : IDisposable
{
    private readonly FinanceControlWebAppFactory _factory;

    /// <summary>Baseline client: sends the valid X-Api-Key header.</summary>
    private readonly HttpClient _client;

    public ApiKeyAuthTests()
    {
        _factory = new FinanceControlWebAppFactory();
        _client = _factory.CreateAuthenticatedClient();
    }

    [Fact]
    public async Task Balance_WithoutApiKey_Returns401()
    {
        using var anonymous = _factory.CreateUnauthenticatedClient();

        var response = await anonymous.GetAsync("/api/balance");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("WWW-Authenticate"));

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("Unauthorized", error.Error);
    }

    [Fact]
    public async Task Balance_WithWrongApiKey_Returns401()
    {
        using var client = _factory.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Add(FinanceControlWebAppFactory.ApiKeyHeaderName, "not-the-real-key");

        var response = await client.GetAsync("/api/balance");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Balance_WithValidApiKey_Returns200()
    {
        var created = await _client.PostAsync("/api/accounts", content: null);
        Assert.True(
            created.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"setup: {created.StatusCode}");

        var response = await _client.GetAsync("/api/balance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var balance = await response.Content.ReadFromJsonAsync<BalanceResponse>();
        Assert.NotNull(balance);
        Assert.Equal(0L, balance.Balance);
    }

    [Fact]
    public async Task Deposit_WithoutApiKey_Returns401AndDoesNotPersist()
    {
        // Account must exist for the balance assertion below (shared factory).
        var setup = await _client.PostAsync("/api/accounts", content: null);
        Assert.True(
            setup.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
            $"setup: {setup.StatusCode}");

        using var anonymous = _factory.CreateUnauthenticatedClient();

        var response = await anonymous.PostAsJsonAsync(
            "/api/deposit",
            new DepositRequest(25050, "Never stored — no API key"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // The rejection happens in the pipeline: the service was never reached.
        var balance = await _client.GetFromJsonAsync<BalanceResponse>("/api/balance");
        Assert.NotNull(balance);
        Assert.Equal(0L, balance.Balance);
    }

    [Fact]
    public async Task Health_WithoutApiKey_Returns200()
    {
        using var anonymous = _factory.CreateUnauthenticatedClient();

        var response = await anonymous.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_AdvertisesTheApiKeyScheme()
    {
        var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var scheme = document["components"]!["securitySchemes"]!["ApiKey"]!;

        Assert.Equal("apiKey", scheme["type"]!.GetValue<string>());
        Assert.Equal("header", scheme["in"]!.GetValue<string>());
        Assert.Equal(FinanceControlWebAppFactory.ApiKeyHeaderName, scheme["name"]!.GetValue<string>());

        var security = document["security"]!.AsArray();
        Assert.Contains(security, requirement => requirement?["ApiKey"] is not null);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
