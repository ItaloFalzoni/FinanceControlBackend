using System.Net;
using System.Net.Http.Json;
using System.Text;
using FinanceControl.API.Application;

namespace FinanceControl.IntegrationTests.Endpoints;

/// <summary>
/// Wire-contract guards: fractional amounts, pagination boundaries and auth on
/// every /api/* route. Each test owns its factory (isolated throwaway database).
/// </summary>
public sealed class ContractTests
{
    private static async Task<HttpClient> CreateClientWithAccountAsync(FinanceControlWebAppFactory factory)
    {
        var client = factory.CreateAuthenticatedClient();
        var created = await client.PostAsync("/api/accounts", content: null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return client;
    }

    [Theory]
    [InlineData("/api/deposit")]
    [InlineData("/api/withdraw")]
    public async Task FractionalAmount_Returns400(string route)
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = await CreateClientWithAccountAsync(factory);

        using var content = new StringContent(
            """{"amount":10.5,"description":"Fraction"}""",
            Encoding.UTF8,
            "application/json");
        var response = await client.PostAsync(route, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task History_PageBeyondTotal_Returns200EmptyWithMetadata()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = await CreateClientWithAccountAsync(factory);

        var deposit = await client.PostAsJsonAsync("/api/deposit", new DepositRequest(1000, "A"));
        Assert.Equal(HttpStatusCode.Created, deposit.StatusCode);

        var history = await client.GetFromJsonAsync<TransactionHistoryResponse>("/api/transactions?page=99&pageSize=50");
        Assert.NotNull(history);
        Assert.Equal(99, history.Page);
        Assert.Equal(1, history.TotalCount);
        Assert.Empty(history.Transactions);
        Assert.Equal(1000L, history.CurrentBalance);
    }

    [Fact]
    public async Task History_PageSizeAtMax_Returns200()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = await CreateClientWithAccountAsync(factory);

        var history = await client.GetFromJsonAsync<TransactionHistoryResponse>("/api/transactions?page=1&pageSize=200");
        Assert.NotNull(history);
        Assert.Equal(200, history.PageSize);
        Assert.Equal(0, history.TotalCount);
    }

    [Theory]
    [InlineData("/api/transactions")]
    [InlineData("/api/accounts")]
    public async Task ProtectedRoutes_WithoutApiKey_Returns401(string route)
    {
        using var factory = new FinanceControlWebAppFactory();
        using var anonymous = factory.CreateUnauthenticatedClient();

        var response = route == "/api/accounts"
            ? await anonymous.PostAsync(route, content: null)
            : await anonymous.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Withdraw_WithWrongApiKey_Returns401()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Add(FinanceControlWebAppFactory.ApiKeyHeaderName, "not-the-real-key");

        var response = await client.PostAsJsonAsync("/api/withdraw", new WithdrawRequest(1000, "Nope"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/deposit")]
    [InlineData("/api/withdraw")]
    public async Task EmptyBody_Returns400(string route)
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = await CreateClientWithAccountAsync(factory);

        var response = await client.PostAsync(route, content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("Validation failed", error.Error);
    }
}
