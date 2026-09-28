using FinanceControl.API.Domain.Enums;

namespace FinanceControl.API.Application;

// ── Requests (amounts in minor units: cents) ────────────────────────────────

public record DepositRequest(long Amount, string Description);

public record WithdrawRequest(long Amount, string Description);

// ── Responses (amounts in minor units: cents) ──────────────────────────────

public record TransactionResponse(
    Guid Id,
    long Amount,
    TransactionType Type,
    string Description,
    DateTimeOffset CreatedAt);

public record BalanceResponse(Guid AccountId, long Balance, DateTimeOffset AsOf);

public record AccountCreatedResponse(Guid AccountId);

public record TransactionHistoryResponse(
    Guid AccountId,
    long CurrentBalance,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<TransactionResponse> Transactions);

public record ErrorResponse(string Error, string? Detail = null, IReadOnlyList<string>? Errors = null);
