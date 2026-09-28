using System.Net;
using System.Net.Http.Json;
using FinanceControl.API.Application;
using FinanceControl.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceControl.IntegrationTests.Endpoints;

/// <summary>
/// Rollback on rejected writes plus the first-run 404 when no account has been
/// created yet (or the row is removed by hand). Each test owns its factory
/// (isolated throwaway database).
/// </summary>
public sealed class PersistenceTests
{
    [Fact]
    public async Task WithdrawRejected_LeavesNoPartialRow()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateAuthenticatedClient();

        var created = await client.PostAsync("/api/accounts", content: null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var seed = await client.PostAsJsonAsync(
            "/api/deposit", new DepositRequest(5000, "Sales"));
        Assert.Equal(HttpStatusCode.Created, seed.StatusCode);

        var rejected = await client.PostAsJsonAsync(
            "/api/withdraw", new WithdrawRequest(6000, "Too much"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);

        var history = await client.GetFromJsonAsync<TransactionHistoryResponse>("/api/transactions");
        Assert.NotNull(history);
        Assert.Equal(5000L, history.CurrentBalance);
        Assert.Single(history.Transactions);
    }

    [Fact]
    public async Task MissingAccount_Returns404()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateAuthenticatedClient();

        // First-run: nothing created yet.
        var freshBalance = await client.GetAsync("/api/balance");
        Assert.Equal(HttpStatusCode.NotFound, freshBalance.StatusCode);

        var created = await client.PostAsync("/api/accounts", content: null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceControlDbContext>();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM transactions");
            await db.Database.ExecuteSqlRawAsync("DELETE FROM accounts");
        }

        var balance = await client.GetAsync("/api/balance");
        Assert.Equal(HttpStatusCode.NotFound, balance.StatusCode);

        var deposit = await client.PostAsJsonAsync(
            "/api/deposit", new DepositRequest(1000, "No account"));
        Assert.Equal(HttpStatusCode.NotFound, deposit.StatusCode);

        var withdraw = await client.PostAsJsonAsync(
            "/api/withdraw", new WithdrawRequest(1000, "No account"));
        Assert.Equal(HttpStatusCode.NotFound, withdraw.StatusCode);

        var history = await client.GetAsync("/api/transactions");
        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
    }
}
