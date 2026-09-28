using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FinanceControl.API.Infrastructure.Authentication;
using FinanceControl.API.Infrastructure.Persistence;
using Npgsql;

namespace FinanceControl.IntegrationTests;

/// <summary>
/// Spins up the full ASP.NET Core pipeline against a dedicated PostgreSQL database.
/// Each test class gets its own throwaway database (created on start, dropped on
/// dispose), so tests stay isolated and can run in parallel. A caller-supplied
/// database name reuses one database across factories — e.g. to assert that data
/// survives a host restart. The application under test now depends on the
/// database — there is no in-memory repository to swap in.
/// <para>
/// Authentication: the host is fail-closed (it refuses to start without an API
/// key), so the factory injects a known key. Tests opt in per client:
/// <see cref="CreateAuthenticatedClient"/> sends it in the X-Api-Key header,
/// while <c>CreateClient()</c> / <see cref="CreateUnauthenticatedClient"/> do not.
/// </para>
/// </summary>
public sealed class FinanceControlWebAppFactory : WebApplicationFactory<Program>
{
    /// <summary>Header the API expects on every /api/* request (ApiKeyOptions.HeaderName).</summary>
    public const string ApiKeyHeaderName = "X-Api-Key";

    /// <summary>Key configured on the host under test; tests never read it from appsettings.</summary>
    public const string TestApiKey = "integration-test-api-key";

    /// <summary>
    /// Admin connection (database "postgres") used to create/drop per-factory test
    /// databases. Defaults match .env.example; override with the environment
    /// variable POSTGRES_TEST_ADMIN_CONNECTION when credentials differ.
    /// </summary>
    private static readonly string AdminConnectionString =
        Environment.GetEnvironmentVariable("POSTGRES_TEST_ADMIN_CONNECTION")
        ?? "Host=localhost;Port=5432;Username=financecontrol;Password=dev-local-pg-password-change-me";

    private readonly string _databaseName;
    private readonly bool _dropDatabaseOnDispose;

    /// <summary>Throwaway database: unique name, created on start, dropped on dispose.</summary>
    public FinanceControlWebAppFactory()
        : this($"financecontrol_test_{Guid.NewGuid():N}")
    {
    }

    /// <summary>
    /// Factory over a caller-supplied database (created when missing). Pass
    /// <paramref name="dropDatabaseOnDispose"/> = false to keep the data alive across
    /// factories — e.g. to assert persistence after a host restart — and drop the
    /// database from the test when the scenario is over.
    /// </summary>
    public FinanceControlWebAppFactory(string databaseName, bool dropDatabaseOnDispose = true)
    {
        _databaseName = databaseName;
        _dropDatabaseOnDispose = dropDatabaseOnDispose;
    }

    // Pooling is disabled so every connection is closed for real and the database
    // can be dropped on dispose without lingering backends.
    private string TestConnectionString =>
        $"{AdminConnectionString};Database={_databaseName};Pooling=false";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // The API has no in-memory fallback: isolation now comes from the database
        // itself, created before the host starts (Program runs migrations on startup).
        CreateTestDatabase();

        builder.ConfigureServices(services =>
        {
            // Point the application at this factory's database.
            services.PostConfigure<DatabaseOptions>(options => options.Postgres = TestConnectionString);

            // The host is fail-closed on Authentication:ApiKey, so the key is set
            // here (after binding) instead of relying on any appsettings file.
            services.PostConfigure<ApiKeyOptions>(options => options.ApiKey = TestApiKey);
        });

        return base.CreateHost(builder);
    }

    /// <summary>
    /// Client that sends the valid X-Api-Key header on every request — the
    /// baseline for tests that exercise business routes. <c>CreateClient()</c>
    /// (the base factory) sends no key and is what the 401 scenarios use.
    /// </summary>
    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyHeaderName, TestApiKey);
        return client;
    }

    /// <summary>Client with no API key at all — used by the 401 scenarios.</summary>
    public HttpClient CreateUnauthenticatedClient() => CreateClient();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _dropDatabaseOnDispose)
            DropTestDatabase();
    }

    private void CreateTestDatabase()
    {
        using var connection = new NpgsqlConnection(AdminConnectionString);
        connection.Open();

        // A second host over the same database (restart scenarios) must reuse it.
        using (var existsCommand = connection.CreateCommand())
        {
            existsCommand.CommandText = "SELECT 1 FROM pg_database WHERE datname = @name";
            existsCommand.Parameters.AddWithValue("name", _databaseName);
            if (existsCommand.ExecuteScalar() is not null)
                return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{_databaseName}\"";
        command.ExecuteNonQuery();
    }

    private void DropTestDatabase()
    {
        try
        {
            // Closed by base.Dispose() above; clearing pools covers any straggler.
            NpgsqlConnection.ClearAllPools();

            using var connection = new NpgsqlConnection(AdminConnectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
            command.ExecuteNonQuery();
        }
        catch
        {
            // Cleanup is best-effort: a leaked test database never fails the test run.
        }
    }
}
