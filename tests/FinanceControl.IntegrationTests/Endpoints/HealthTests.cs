using System.Net;

namespace FinanceControl.IntegrationTests.Endpoints;

/// <summary>
/// Liveness (/health, no dependencies) vs readiness (/health/ready, includes
/// PostgreSQL). The 503 path (database down) is verified manually — the test
/// factory always provides a database.
/// </summary>
public sealed class HealthTests : IDisposable
{
    private readonly FinanceControlWebAppFactory _factory = new();
    private readonly HttpClient _client;

    public HealthTests() => _client = _factory.CreateAuthenticatedClient();

    [Fact]
    public async Task Live_WithoutApiKey_Returns200()
    {
        using var anonymous = _factory.CreateUnauthenticatedClient();

        var response = await anonymous.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_WithDatabase_Returns200()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
