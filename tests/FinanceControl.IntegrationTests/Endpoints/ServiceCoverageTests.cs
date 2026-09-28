using System.Net;
using System.Net.Http.Json;
using FinanceControl.API.Application;
using FinanceControl.API.Application.Services;
using FinanceControl.API.Domain.Entities;
using FinanceControl.API.Domain.Enums;
using FinanceControl.API.Domain.Exceptions;
using FinanceControl.API.Domain.Repositories;
using FinanceControl.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceControl.IntegrationTests.Endpoints;

/// <summary>
/// Direct service/repository paths the HTTP endpoints never exercise: the
/// parameterless history overload, the service-level paging guards, the
/// persistence-only <c>Transaction.Reconstitute</c> failure mode, the
/// <c>TransactionRow.Account</c> navigation, and the no-op
/// <c>UpdateAsync</c> early return. Each test owns its factory (isolated
/// throwaway database).
/// </summary>
public sealed class ServiceCoverageTests
{
    [Fact]
    public async Task GetHistoryAsync_InvalidPage_ThrowsArgumentOutOfRangeException()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountService>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetHistoryAsync(0, 50));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task GetHistoryAsync_InvalidPageSize_ThrowsArgumentOutOfRangeException(int pageSize)
    {
        using var factory = new FinanceControlWebAppFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountService>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetHistoryAsync(1, pageSize));
    }

    [Fact]
    public async Task GetHistoryAsync_Parameterless_UsesDefaultPaging()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateAuthenticatedClient();

        var created = await client.PostAsync("/api/accounts", content: null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var seed = await client.PostAsJsonAsync(
            "/api/deposit", new DepositRequest(5000, "Sales"));
        Assert.Equal(HttpStatusCode.Created, seed.StatusCode);

        TransactionHistoryResponse? history;
        using (var scope = factory.Services.CreateScope())
            history = await scope.ServiceProvider
                .GetRequiredService<AccountService>()
                .GetHistoryAsync();

        Assert.NotNull(history);
        Assert.Equal(1, history.Page);
        Assert.Equal(50, history.PageSize);
        Assert.Equal(1, history.TotalCount);
        Assert.Single(history.Transactions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reconstitute_InvalidAmount_ThrowsInvalidAmountException(long amount)
    {
        Assert.Throws<InvalidAmountException>(() => Transaction.Reconstitute(
            Guid.NewGuid(), amount, TransactionType.Credit, "Sales", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Reconstitute_NullDescription_DefaultsToEmpty()
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var transaction = Transaction.Reconstitute(
            id, 2500, TransactionType.Debit, null!, createdAt);

        Assert.Equal(id, transaction.Id);
        Assert.Equal(2500L, transaction.Amount);
        Assert.Equal(TransactionType.Debit, transaction.Type);
        Assert.Equal(string.Empty, transaction.Description);
        Assert.Equal(createdAt, transaction.CreatedAt);
    }

    [Fact]
    public async Task TransactionRow_AccountNavigation_ResolvesParent()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateAuthenticatedClient();

        var created = await client.PostAsync("/api/accounts", content: null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var seed = await client.PostAsJsonAsync(
            "/api/deposit", new DepositRequest(5000, "Sales"));
        Assert.Equal(HttpStatusCode.Created, seed.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceControlDbContext>();
        var row = await db.Transactions
            .Include(t => t.Account)
            .FirstAsync();

        Assert.NotNull(row.Account);
        Assert.Equal(row.AccountId, row.Account.Id);
    }

    [Fact]
    public async Task UpdateAsync_WithoutNewTransactions_CommitsWithoutInserting()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateAuthenticatedClient();

        var created = await client.PostAsync("/api/accounts", content: null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var resolver = scope.ServiceProvider.GetRequiredService<IAccountResolver>();

        var account = await resolver.GetCurrentAsync();
        Assert.NotNull(account);

        // No Deposit/Withdraw since load: nothing pending, must no-op.
        await repository.UpdateAsync(account);

        var history = await resolver.GetHistoryPageAsync(1, 50);
        Assert.NotNull(history);
        Assert.Equal(0, history.TotalCount);
        Assert.Empty(history.Transactions);
    }
}
