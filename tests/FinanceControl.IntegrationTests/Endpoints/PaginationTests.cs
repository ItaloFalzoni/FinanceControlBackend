using System.Net;
using System.Net.Http.Json;
using FinanceControl.API.Application;

namespace FinanceControl.IntegrationTests.Endpoints;

/// <summary>
/// Paginated history contract: ?page & ?pageSize with defaults and 400 on
/// invalid values. Each test owns its factory (isolated throwaway database).
/// </summary>
public sealed class PaginationTests
{
    private static async Task<HttpClient> CreateClientWithAccountAsync(FinanceControlWebAppFactory factory)
    {
        var client = factory.CreateAuthenticatedClient();
        var created = await client.PostAsync("/api/accounts", content: null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return client;
    }

    [Fact]
    public async Task History_DefaultPagination_ReturnsFirstPageWithMetadata()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = await CreateClientWithAccountAsync(factory);

        var deposit = await client.PostAsJsonAsync("/api/deposit", new DepositRequest(1000, "A"));
        Assert.Equal(HttpStatusCode.Created, deposit.StatusCode);

        var history = await client.GetFromJsonAsync<TransactionHistoryResponse>("/api/transactions");
        Assert.NotNull(history);
        Assert.Equal(1, history.Page);
        Assert.Equal(50, history.PageSize);
        Assert.Equal(1, history.TotalCount);
        Assert.Single(history.Transactions);
    }

    [Fact]
    public async Task History_SecondPage_ReturnsRemainingItems()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = await CreateClientWithAccountAsync(factory);

        for (var i = 0; i < 3; i++)
        {
            var deposit = await client.PostAsJsonAsync("/api/deposit", new DepositRequest(1000, $"Op {i}"));
            Assert.Equal(HttpStatusCode.Created, deposit.StatusCode);
        }

        var page1 = await client.GetFromJsonAsync<TransactionHistoryResponse>("/api/transactions?page=1&pageSize=2");
        Assert.NotNull(page1);
        Assert.Equal(1, page1.Page);
        Assert.Equal(2, page1.PageSize);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Transactions.Count);

        var page2 = await client.GetFromJsonAsync<TransactionHistoryResponse>("/api/transactions?page=2&pageSize=2");
        Assert.NotNull(page2);
        Assert.Equal(2, page2.Page);
        Assert.Equal(3, page2.TotalCount);
        Assert.Single(page2.Transactions);
    }

    [Fact]
    public async Task History_Pages_ReturnBalanceAndDeterministicOrder()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = await CreateClientWithAccountAsync(factory);

        for (var i = 0; i < 3; i++)
        {
            var deposit = await client.PostAsJsonAsync("/api/deposit", new DepositRequest(1000, $"Op {i}"));
            Assert.Equal(HttpStatusCode.Created, deposit.StatusCode);
        }

        var page1 = await client.GetFromJsonAsync<TransactionHistoryResponse>("/api/transactions?page=1&pageSize=2");
        Assert.NotNull(page1);
        Assert.Equal(3000L, page1.CurrentBalance);
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.Transactions.Count);
        Assert.Equal(["Op 2", "Op 1"], page1.Transactions.Select(t => t.Description).ToList());

        var page2 = await client.GetFromJsonAsync<TransactionHistoryResponse>("/api/transactions?page=2&pageSize=2");
        Assert.NotNull(page2);
        Assert.Equal(3000L, page2.CurrentBalance);
        Assert.Equal(3, page2.TotalCount);
        Assert.Single(page2.Transactions);
        Assert.Equal("Op 0", page2.Transactions[0].Description);
    }

    [Fact]
    public async Task History_EmptyAccount_ReturnsZeroTotalAndEmptyList()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = await CreateClientWithAccountAsync(factory);

        var history = await client.GetFromJsonAsync<TransactionHistoryResponse>("/api/transactions");
        Assert.NotNull(history);
        Assert.Equal(0L, history.CurrentBalance);
        Assert.Equal(0, history.TotalCount);
        Assert.Empty(history.Transactions);
    }

    [Theory]
    [InlineData("?page=0&pageSize=10")]
    [InlineData("?page=1&pageSize=0")]
    [InlineData("?page=1&pageSize=201")]
    public async Task History_InvalidPagination_Returns400(string query)
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = await CreateClientWithAccountAsync(factory);

        var response = await client.GetAsync($"/api/transactions{query}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("Validation failed", error.Error);
        Assert.NotNull(error.Errors);
        Assert.NotEmpty(error.Errors);
    }

    [Fact]
    public async Task History_OverflowBalance_Returns422()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = await CreateClientWithAccountAsync(factory);

        var first = await client.PostAsJsonAsync("/api/deposit", new DepositRequest(long.MaxValue, "Max"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Second deposit would overflow the computed balance: the repository
        // rejects it with 422 (checked arithmetic) and rolls back.
        var second = await client.PostAsJsonAsync("/api/deposit", new DepositRequest(1, "Overflow"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);

        var error = await second.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("Domain error", error.Error);

        // Stored state is unchanged: balance still fits in long.
        var balance = await client.GetFromJsonAsync<BalanceResponse>("/api/balance");
        Assert.NotNull(balance);
        Assert.Equal(long.MaxValue, balance.Balance);
    }
}
