using FinanceControl.API.Application;
using FinanceControl.API.Application.Services;
using FinanceControl.API.Application.Validators;

namespace FinanceControl.API.Endpoints;

/// <summary>
/// Routes of the single-account ledger. The account is created explicitly via
/// POST /api/accounts (first-run); no route carries an account ID — balance,
/// history, deposit and withdraw operate on the one account the system keeps.
/// </summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api")
            .WithTags("Accounts");

        group.MapPost("/accounts", CreateAccountAsync)
            .WithName("CreateAccount")
            .WithSummary("Creates the single ledger account")
            .Produces<AccountCreatedResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapGet("/balance", GetBalanceAsync)
            .WithName("GetBalance")
            .WithSummary("Gets the current balance of the account")
            .Produces<BalanceResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/transactions", GetHistoryAsync)
            .WithName("GetTransactionHistory")
            .WithSummary("Gets the transaction history of the account (paginated)")
            .Produces<TransactionHistoryResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/deposit", DepositAsync)
            .WithName("Deposit")
            .WithSummary("Records a credit (deposit) into the account")
            .Produces<TransactionResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/withdraw", WithdrawAsync)
            .WithName("Withdraw")
            .WithSummary("Records a debit (withdrawal) from the account")
            .Produces<TransactionResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<IResult> CreateAccountAsync(AccountService service, CancellationToken ct)
    {
        var (created, response) = await service.CreateSingleAsync(ct);
        return created
            ? Results.Json(response, statusCode: StatusCodes.Status201Created)
            : Results.Conflict(new ErrorResponse("Account already exists", "An account already exists."));
    }

    private static async Task<IResult> GetBalanceAsync(AccountService service, CancellationToken ct)
    {
        var balance = await service.GetBalanceAsync(ct);
        return balance is null
            ? Results.NotFound(new ErrorResponse("Account not found", "No account exists."))
            : Results.Ok(balance);
    }

    private static async Task<IResult> GetHistoryAsync(
        int? page,
        int? pageSize,
        AccountService service,
        CancellationToken ct)
    {
        var currentPage = page ?? 1;
        var currentPageSize = pageSize ?? 50;

        if (currentPage < 1 || currentPageSize < 1 || currentPageSize > 200)
        {
            var errors = new List<string> { "Query parameters must satisfy: page >= 1, 1 <= pageSize <= 200." };
            return Results.BadRequest(new ErrorResponse("Validation failed", errors[0], errors));
        }

        var history = await service.GetHistoryAsync(currentPage, currentPageSize, ct);
        return history is null
            ? Results.NotFound(new ErrorResponse("Account not found", "No account exists."))
            : Results.Ok(history);
    }

    private static async Task<IResult> DepositAsync(
        DepositRequest request,
        AccountService service,
        CancellationToken ct)
    {
        var errors = RequestValidators.Validate(request);
        if (errors.Count > 0)
            return Results.BadRequest(new ErrorResponse("Validation failed", string.Join("; ", errors), errors));

        var result = await service.DepositAsync(request, ct);
        return result is null
            ? Results.NotFound(new ErrorResponse("Account not found", "No account exists."))
            : Results.Json(result, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> WithdrawAsync(
        WithdrawRequest request,
        AccountService service,
        CancellationToken ct)
    {
        var errors = RequestValidators.Validate(request);
        if (errors.Count > 0)
            return Results.BadRequest(new ErrorResponse("Validation failed", string.Join("; ", errors), errors));

        var result = await service.WithdrawAsync(request, ct);
        return result is null
            ? Results.NotFound(new ErrorResponse("Account not found", "No account exists."))
            : Results.Json(result, statusCode: StatusCodes.Status201Created);
    }
}
