using System.Net;
using System.Net.Http.Json;
using FinanceControl.API.Application;

namespace FinanceControl.IntegrationTests.Endpoints;

/// <summary>
/// Happy path and validation contract of the four ledger routes against a real
/// PostgreSQL database. Each test owns its factory (isolated throwaway database),
/// so no test depends on execution order.
/// </summary>
public sealed class LedgerFlowTests
{
    private static async Task CreateAccountAsync(HttpClient client)
    {
        var created = await client.PostAsync("/api/accounts", content: null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Fact]
    public async Task FullFlow_DepositAndWithdraw_UpdatesBalanceAndHistory()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateAuthenticatedClient();
        await CreateAccountAsync(client);

        var initial = await client.GetFromJsonAsync<BalanceResponse>("/api/balance");
        Assert.NotNull(initial);
        Assert.Equal(0L, initial.Balance);

        var deposit = await client.PostAsJsonAsync(
            "/api/deposit", new DepositRequest(100000, "Sales revenue"));
        Assert.Equal(HttpStatusCode.Created, deposit.StatusCode);

        var afterDeposit = await client.GetFromJsonAsync<BalanceResponse>("/api/balance");
        Assert.NotNull(afterDeposit);
        Assert.Equal(100000L, afterDeposit.Balance);

        var withdraw = await client.PostAsJsonAsync(
            "/api/withdraw", new WithdrawRequest(30000, "Supplier payment"));
        Assert.Equal(HttpStatusCode.Created, withdraw.StatusCode);

        var history = await client.GetFromJsonAsync<TransactionHistoryResponse>("/api/transactions");
        Assert.NotNull(history);
        Assert.Equal(70000L, history.CurrentBalance);
        Assert.Equal(2, history.Transactions.Count);
        // Most recent first.
        Assert.True(history.Transactions[0].CreatedAt >= history.Transactions[1].CreatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Deposit_InvalidAmount_Returns400AndKeepsBalance(long amount)
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateAuthenticatedClient();
        await CreateAccountAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/deposit", new DepositRequest(amount, "Sales"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("Validation failed", error.Error);

        var balance = await client.GetFromJsonAsync<BalanceResponse>("/api/balance");
        Assert.NotNull(balance);
        Assert.Equal(0L, balance.Balance);
    }

    [Fact]
    public async Task Deposit_MissingDescription_Returns400()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateAuthenticatedClient();
        await CreateAccountAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/deposit", new DepositRequest(10000, ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Withdraw_WithInsufficientFunds_Returns422AndKeepsBalance()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateAuthenticatedClient();
        await CreateAccountAsync(client);

        await client.PostAsJsonAsync("/api/deposit", new DepositRequest(5000, "Sales"));

        var response = await client.PostAsJsonAsync(
            "/api/withdraw", new WithdrawRequest(6000, "Too much"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("Insufficient funds", error.Error);

        var balance = await client.GetFromJsonAsync<BalanceResponse>("/api/balance");
        Assert.NotNull(balance);
        Assert.Equal(5000L, balance.Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Withdraw_InvalidAmount_Returns400(long amount)
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateAuthenticatedClient();
        await CreateAccountAsync(client);

        var response = await client.PostAsJsonAsync(
            "/api/withdraw", new WithdrawRequest(amount, "Supplier"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task History_Balance_MatchesSignedSumAndBalanceEndpoint()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateAuthenticatedClient();
        await CreateAccountAsync(client);

        await client.PostAsJsonAsync("/api/deposit", new DepositRequest(100000, "Sales A"));
        await client.PostAsJsonAsync("/api/deposit", new DepositRequest(50000, "Sales B"));
        await client.PostAsJsonAsync("/api/withdraw", new WithdrawRequest(30000, "Supplier"));

        var history = await client.GetFromJsonAsync<TransactionHistoryResponse>("/api/transactions");
        Assert.NotNull(history);
        Assert.Equal(3, history.TotalCount);

        // Same SignedAmount rule as the domain: Credit adds, Debit subtracts.
        var signedSum = 0L;
        foreach (var t in history.Transactions)
            checked
            {
                signedSum += t.Type == FinanceControl.API.Domain.Enums.TransactionType.Credit
                    ? t.Amount
                    : -t.Amount;
            }

        Assert.Equal(120000L, signedSum);
        Assert.Equal(signedSum, history.CurrentBalance);

        var balance = await client.GetFromJsonAsync<BalanceResponse>("/api/balance");
        Assert.NotNull(balance);
        Assert.Equal(history.CurrentBalance, balance.Balance);
        Assert.Equal(history.AccountId, balance.AccountId);
    }
}
