using System.Net;
using System.Net.Http.Json;
using FinanceControl.API.Application;

namespace FinanceControl.IntegrationTests.Endpoints;

/// <summary>
/// Proves the SELECT ... FOR UPDATE serialization in PostgresAccountRepository:
/// concurrent writers on the same account cannot overdraw it. Each test owns
/// its factory (isolated throwaway database).
/// </summary>
public sealed class ConcurrencyTests
{
    [Fact]
    public async Task TwoWithdrawals60_OnBalance70_OneSucceedsAndOneFails422()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateAuthenticatedClient();

        var created = await client.PostAsync("/api/accounts", content: null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var seed = await client.PostAsJsonAsync(
            "/api/deposit", new DepositRequest(7000, "Seed"));
        Assert.Equal(HttpStatusCode.Created, seed.StatusCode);

        using var clientA = factory.CreateAuthenticatedClient();
        using var clientB = factory.CreateAuthenticatedClient();

        var results = await Task.WhenAll(
            clientA.PostAsJsonAsync("/api/withdraw", new WithdrawRequest(6000, "Race A")),
            clientB.PostAsJsonAsync("/api/withdraw", new WithdrawRequest(6000, "Race B")));

        var statuses = results.Select(r => r.StatusCode).OrderBy(s => s).ToList();
        Assert.Equal(
            new[] { HttpStatusCode.Created, HttpStatusCode.UnprocessableEntity },
            statuses);

        var balance = await client.GetFromJsonAsync<BalanceResponse>("/api/balance");
        Assert.NotNull(balance);
        Assert.Equal(1000L, balance.Balance);
    }

    [Fact]
    public async Task TwoDeposits50_OnBalance70_BothSucceed()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateAuthenticatedClient();

        var created = await client.PostAsync("/api/accounts", content: null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var seed = await client.PostAsJsonAsync(
            "/api/deposit", new DepositRequest(7000, "Seed"));
        Assert.Equal(HttpStatusCode.Created, seed.StatusCode);

        using var clientA = factory.CreateAuthenticatedClient();
        using var clientB = factory.CreateAuthenticatedClient();

        var results = await Task.WhenAll(
            clientA.PostAsJsonAsync("/api/deposit", new DepositRequest(5000, "Race A")),
            clientB.PostAsJsonAsync("/api/deposit", new DepositRequest(5000, "Race B")));

        Assert.All(results, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        var balance = await client.GetFromJsonAsync<BalanceResponse>("/api/balance");
        Assert.NotNull(balance);
        Assert.Equal(17000L, balance.Balance);
    }
}
