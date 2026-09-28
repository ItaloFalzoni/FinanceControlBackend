using FinanceControl.API.Domain.Entities;
using FinanceControl.API.Domain.Exceptions;
using FinanceControl.API.Domain.Repositories;

namespace FinanceControl.API.Application.Services;

/// <summary>
/// Orchestrates the use cases of the single-account ledger (balance, history,
/// deposit, withdraw, create). The account is created explicitly via
/// POST /api/accounts — every other method resolves the same account through
/// the resolver and returns null when it does not exist yet (first-run).
/// </summary>
public sealed class AccountService(IAccountRepository repository, IAccountResolver resolver)
{
    /// <summary>
    /// Creates the single account when none exists. Returns (Created: true)
    /// with the new id, or (Created: false) with the existing id when one
    /// already exists (including a concurrent creator winning the race).
    /// </summary>
    public async Task<(bool Created, AccountCreatedResponse Response)> CreateSingleAsync(
        CancellationToken ct = default)
    {
        var (created, accountId) = await repository.CreateSingleAsync(ct);
        return (created, new AccountCreatedResponse(accountId));
    }

    public async Task<BalanceResponse?> GetBalanceAsync(CancellationToken ct = default)
    {
        var balance = await resolver.GetCurrentBalanceAsync(ct);
        if (balance is null) return null;

        return new BalanceResponse(balance.Value.AccountId, balance.Value.Balance, DateTimeOffset.UtcNow);
    }

    public async Task<TransactionHistoryResponse?> GetHistoryAsync(CancellationToken ct = default)
    {
        return await GetHistoryAsync(1, 50, ct);
    }

    public async Task<TransactionHistoryResponse?> GetHistoryAsync(int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page), "Page must be greater than or equal to 1.");
        if (pageSize < 1 || pageSize > 200)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "PageSize must be between 1 and 200.");

        var history = await resolver.GetHistoryPageAsync(page, pageSize, ct);
        if (history is null) return null;

        var paged = history.Transactions
            .Select(MapTransactionToResponse)
            .ToList()
            .AsReadOnly();

        return new TransactionHistoryResponse(history.AccountId, history.Balance, page, pageSize, history.TotalCount, paged);
    }

    public async Task<TransactionResponse?> DepositAsync(
        DepositRequest request,
        CancellationToken ct = default)
    {
        var account = await GetAccountOrNullAsync(ct);
        if (account is null) return null;

        var transaction = account.Deposit(request.Amount, request.Description);
        try
        {
            await repository.UpdateAsync(account, ct);
        }
        catch (AccountNotFoundException)
        {
            return null;
        }

        return MapTransactionToResponse(transaction);
    }

    public async Task<TransactionResponse?> WithdrawAsync(
        WithdrawRequest request,
        CancellationToken ct = default)
    {
        var account = await GetAccountOrNullAsync(ct);
        if (account is null) return null;

        var transaction = account.Withdraw(request.Amount, request.Description);
        try
        {
            await repository.UpdateAsync(account, ct);
        }
        catch (AccountNotFoundException)
        {
            return null;
        }

        return MapTransactionToResponse(transaction);
    }

    private Task<Account?> GetAccountOrNullAsync(CancellationToken ct) =>
        resolver.GetCurrentAsync(ct);

    private static TransactionResponse MapTransactionToResponse(Transaction t) =>
        new(
            t.Id,
            t.Amount,
            t.Type,
            t.Description,
            t.CreatedAt);
}
