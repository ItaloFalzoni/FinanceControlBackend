using System.Net;
using System.Net.Http.Json;
using FinanceControl.API.Application;
using FinanceControl.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceControl.IntegrationTests.Endpoints;

/// <summary>
/// The explicitly created account survives host restarts and a second creation
/// is rejected (409) without duplicating the row. Two factories share one
/// throwaway database by name; a dropper factory removes it afterwards
/// (best-effort — a leaked test database never fails the run).
/// </summary>
public sealed class SeedPersistenceTests
{
    [Fact]
    public async Task RestartOnSameDatabase_KeepsBalanceAndSingleAccount()
    {
        var databaseName = $"financecontrol_seed_{Guid.NewGuid():N}";

        try
        {
            using (var factoryA = new FinanceControlWebAppFactory(
                databaseName, dropDatabaseOnDispose: false))
            {
                using var clientA = factoryA.CreateAuthenticatedClient();
                var created = await clientA.PostAsync("/api/accounts", content: null);
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);

                var deposit = await clientA.PostAsJsonAsync(
                    "/api/deposit", new DepositRequest(10000, "Sales"));
                Assert.Equal(HttpStatusCode.Created, deposit.StatusCode);
            }

            using (var factoryB = new FinanceControlWebAppFactory(
                databaseName, dropDatabaseOnDispose: false))
            {
                using var clientB = factoryB.CreateAuthenticatedClient();
                var balance = await clientB.GetFromJsonAsync<BalanceResponse>("/api/balance");
                Assert.NotNull(balance);
                Assert.Equal(10000L, balance.Balance);

                var again = await clientB.PostAsync("/api/accounts", content: null);
                Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

                using var scope = factoryB.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<FinanceControlDbContext>();
                await db.Database.OpenConnectionAsync();
                try
                {
                    using var command = db.Database.GetDbConnection().CreateCommand();
                    command.CommandText = "SELECT COUNT(*) FROM accounts";
                    var count = (long)(await command.ExecuteScalarAsync())!;
                    Assert.Equal(1, count);
                }
                finally
                {
                    await db.Database.CloseConnectionAsync();
                }
            }
        }
        finally
        {
            try
            {
                using var dropper = new FinanceControlWebAppFactory(databaseName);
            }
            catch
            {
                // Cleanup is best-effort (same policy as the factory itself).
            }
        }
    }
}
