using FinanceControl.API.Application;
using FinanceControl.API.Application.Services;
using FinanceControl.API.Domain.Exceptions;
using FinanceControl.API.Domain.Repositories;
using FinanceControl.API.Endpoints;
using FinanceControl.API.Infrastructure.Authentication;
using FinanceControl.API.Infrastructure.Persistence;
using FinanceControl.API.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ApiKeyOptions = FinanceControl.API.Infrastructure.Authentication.ApiKeyOptions;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI document (machine-readable contract at /openapi/v1.json in Development). The transformer advertises
// the X-Api-Key scheme so callers know every /api/* operation needs it.
builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<ApiKeySecuritySchemeTransformer>());

// Health checks: /health is liveness-only (no dependencies — the container
// healthcheck and orchestrators use it); /health/ready includes PostgreSQL.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<FinanceControlDbContext>("postgres", tags: ["ready"]);

// API key (fail-closed): the host never starts without a key — same policy as
// the connection string. Config key Authentication:ApiKey, environment variable
// Authentication__ApiKey (docker compose maps it from the API_KEY .env value).
builder.Services.AddOptions<ApiKeyOptions>()
    .Bind(builder.Configuration.GetSection(ApiKeyOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ApiKey),
        $"{ApiKeyOptions.SectionName}:{ApiKeyOptions.KeyName} must be configured with a non-empty value (set the Authentication__ApiKey environment variable).")
    .ValidateOnStart();

// Database (PostgreSQL) — fail-closed: the host never starts without a
// connection string (config key ConnectionStrings:Postgres, environment
// variable ConnectionStrings__Postgres).
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Postgres),
        $"{DatabaseOptions.SectionName}:{DatabaseOptions.KeyName} must be configured with a non-empty value (set the ConnectionStrings__Postgres environment variable).")
    .ValidateOnStart();

builder.Services.AddDbContext<FinanceControlDbContext>((serviceProvider, options) =>
    options.UseNpgsql(
        serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Postgres));

// Repository: Scoped — the EF DbContext is not thread-safe, so it cannot be shared
// across requests the way a Singleton could. Registered once as concrete type
// and forwarded to both interfaces so each request shares a single instance
// (same DbContext, single transaction scope per request).
builder.Services.AddScoped<PostgresAccountRepository>();
builder.Services.AddScoped<IAccountRepository>(sp => sp.GetRequiredService<PostgresAccountRepository>());
builder.Services.AddScoped<IAccountResolver>(sp => sp.GetRequiredService<PostgresAccountRepository>());

// Application service
builder.Services.AddScoped<AccountService>();

var app = builder.Build();

// Reject known weak API keys outside Development: fail-closed must also cover
// "key configured but still the dev default". Tests inject their own key via
// FinanceControlWebAppFactory, so this only fires on real hosts.
{
    var configuredKey = app.Services.GetRequiredService<IOptions<ApiKeyOptions>>().Value.ApiKey;
    var isWeakDefault = configuredKey is "dev-local-api-key-change-me" or "change-me" or "integration-test-api-key";
    if (!app.Environment.IsDevelopment() && (isWeakDefault || configuredKey.Length < 32))
    {
        Console.Error.WriteLine("Refusing to start: Authentication:ApiKey is a weak/default value. Set a long random Authentication__ApiKey in Production.");
        throw new InvalidOperationException("Authentication:ApiKey must be a long random value in Production (set the Authentication__ApiKey environment variable).");
    }
}

// Apply pending migrations. The application depends on PostgreSQL: if the
// database is unreachable, the host does not start — there is no fallback.
//
// Single-account ledger: the account is created explicitly via
// POST /api/accounts (welcome screen in the front). Startup only migrates;
// an empty database answers 404 on balance/history/deposit/withdraw until
// the account is created.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FinanceControlDbContext>();
    dbContext.Database.Migrate();
}

app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        throw;
    }
    catch (AccountNotFoundException ex)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ErrorResponse("Account not found", ex.Message, [ex.Message]));
    }
    catch (BadHttpRequestException ex)
    {
        if (context.Response.HasStarted) throw;
        var message = "Invalid request body.";
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ErrorResponse("Validation failed", message, [message, ex.Message]));
    }
    catch (System.Text.Json.JsonException ex)
    {
        if (context.Response.HasStarted) throw;
        var message = "Invalid request body.";
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ErrorResponse("Validation failed", message, [message, ex.Message]));
    }
    catch (OverflowException)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ErrorResponse("Domain error", "Arithmetic overflow: amount too large.", ["Arithmetic overflow: amount too large."]));
    }
    catch (Npgsql.PostgresException ex) when (ex.SqlState == "22003")
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ErrorResponse("Domain error", "Arithmetic overflow: amount too large.", ["Arithmetic overflow: amount too large."]));
    }
    catch (DbUpdateException ex) when (UnwrapPostgresException(ex) is { SqlState: "22003" })
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ErrorResponse("Domain error", "Arithmetic overflow: amount too large.", ["Arithmetic overflow: amount too large."]));
    }
    catch (InsufficientFundsException ex)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ErrorResponse("Insufficient funds", ex.Message, [ex.Message]));
    }
    catch (DomainException ex)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new ErrorResponse("Domain error", ex.Message, [ex.Message]));
    }
    catch (Exception ex)
    {
        if (context.Response.HasStarted) throw;
        app.Logger.LogError(ex, "Unhandled request {Method} {Path}", context.Request.Method, context.Request.Path);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            new ErrorResponse("Unexpected error",
                app.Environment.IsDevelopment() ? ex.Message : null));
    }
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// HTTPS redirection only when HTTPS is configured (local Development with
// 5000/5001). In Production compose (HTTP 8080 only, no cert) it would
// 307-redirect to a non-existent HTTPS endpoint.
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Every /api/* request must carry the X-Api-Key header: anything else is
// answered 401 before reaching the endpoints. See ApiKeyMiddleware.
app.UseApiKeyAuthentication();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });
app.MapAccountEndpoints();

static Npgsql.PostgresException? UnwrapPostgresException(Exception ex)
{
    var current = ex.InnerException;
    while (current is not null)
    {
        if (current is Npgsql.PostgresException postgres)
            return postgres;
        current = current.InnerException;
    }
    return null;
}

app.Run();

// Extension point for a future WebApplicationFactory (see README → Testes (evolução futura))
public partial class Program { }
