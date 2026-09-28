using System.Net;
using System.Net.Http.Json;
using FinanceControl.API.Application;
using FinanceControl.API.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceControl.IntegrationTests.Endpoints;

/// <summary>
/// Explicit first-run creation via POST /api/accounts: empty database answers
/// 404 until the account is created, double creation answers 409, and
/// concurrent creators serialize (one 201, one 409, single row).
/// </summary>
public sealed class AccountCreationTests
{
    [Fact]
    public async Task EmptyDatabase_BalanceIs404_UntilAccountIsCreated()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateAuthenticatedClient();

        var before = await client.GetAsync("/api/balance");
        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);

        var created = await client.PostAsync("/api/accounts", content: null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<AccountCreatedResponse>();
        Assert.NotNull(body);

        var after = await client.GetFromJsonAsync<BalanceResponse>("/api/balance");
        Assert.NotNull(after);
        Assert.Equal(body.AccountId, after.AccountId);
        Assert.Equal(0L, after.Balance);
    }

    [Fact]
    public async Task SecondCreation_Returns409AndKeepsSingleAccount()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var client = factory.CreateAuthenticatedClient();

        var first = await client.PostAsync("/api/accounts", content: null);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsync("/api/accounts", content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = await second.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("Account already exists", error.Error);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceControlDbContext>();
        var count = await db.Accounts.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ConcurrentCreations_OneWins201_OtherGets409()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var clientA = factory.CreateAuthenticatedClient();
        using var clientB = factory.CreateAuthenticatedClient();

        var results = await Task.WhenAll(
            clientA.PostAsync("/api/accounts", content: null),
            clientB.PostAsync("/api/accounts", content: null));

        var statuses = results.Select(r => r.StatusCode).OrderBy(s => s).ToList();
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, statuses);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceControlDbContext>();
        var count = await db.Accounts.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Create_WithoutApiKey_Returns401()
    {
        using var factory = new FinanceControlWebAppFactory();
        using var anonymous = factory.CreateUnauthenticatedClient();

        var response = await anonymous.PostAsync("/api/accounts", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
