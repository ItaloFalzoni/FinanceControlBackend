using System.Net;
using System.Net.Http.Json;
using FinanceControl.API.Application;
using FinanceControl.API.Domain.Enums;
using FinanceControl.API.Infrastructure.Persistence;
using FinanceControl.API.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceControl.IntegrationTests.Endpoints;

/// <summary>
/// Legacy N-account rule: when the database already holds more than one row,
/// the oldest (created_at, id) wins for every read and for creation.
/// </summary>
public sealed class LegacyAccountTests
{
    [Fact]
    public async Task LegacyTwoAccounts_OldestWinsForBalanceHistoryAndCreation()
    {
        using var factory = new FinanceControlWebAppFactory();
        var oldestId = Guid.NewGuid();
        var newestId = Guid.NewGuid();
        var oldestAt = DateTimeOffset.UtcNow.AddDays(-1);
        var newestAt = DateTimeOffset.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceControlDbContext>();
            db.Accounts.Add(new AccountRow
            {
                Id = oldestId,
                CreatedAt = oldestAt,
                Transactions =
                [
                    new TransactionRow
                    {
                        Id = Guid.NewGuid(),
                        AccountId = oldestId,
                        Amount = 10000,
                        Type = TransactionType.Credit,
                        Description = "Oldest",
                        CreatedAt = oldestAt,
                    },
                ],
            });
            db.Accounts.Add(new AccountRow
            {
                Id = newestId,
                CreatedAt = newestAt,
                Transactions =
                [
                    new TransactionRow
                    {
                        Id = Guid.NewGuid(),
                        AccountId = newestId,
                        Amount = 99999,
                        Type = TransactionType.Credit,
                        Description = "Newest",
                        CreatedAt = newestAt,
                    },
                ],
            });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateAuthenticatedClient();

        var balance = await client.GetFromJsonAsync<BalanceResponse>("/api/balance");
        Assert.NotNull(balance);
        Assert.Equal(oldestId, balance.AccountId);
        Assert.Equal(10000L, balance.Balance);

        var history = await client.GetFromJsonAsync<TransactionHistoryResponse>("/api/transactions");
        Assert.NotNull(history);
        Assert.Equal(oldestId, history.AccountId);
        Assert.Equal(10000L, history.CurrentBalance);
        Assert.Equal(1, history.TotalCount);
        Assert.Single(history.Transactions);
        Assert.Equal("Oldest", history.Transactions[0].Description);

        var again = await client.PostAsync("/api/accounts", content: null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceControlDbContext>();
            Assert.Equal(2, await db.Accounts.CountAsync());
        }
    }
}
