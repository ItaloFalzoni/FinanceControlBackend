using FinanceControl.API.Domain.Entities;

namespace FinanceControl.API.Domain.Repositories;

public interface IAccountRepository
{
    Task UpdateAsync(Account account, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically creates the single ledger account when none exists.
    /// Returns (Created: true, new id) on success, or (Created: false,
    /// existing id) when one already exists — concurrent creators are
    /// serialized, so at most one wins and the other observes the winner.
    /// Only the id is returned: creation never needs the transaction list.
    /// </summary>
    Task<(bool Created, Guid AccountId)> CreateSingleAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Read side of the single-account ledger. Every method resolves the same
/// account (oldest row wins) but loads only what its use case needs — writes
/// keep going through IAccountRepository with the full aggregate.
/// </summary>
public interface IAccountResolver
{
    /// <summary>
    /// Full aggregate (account + all transactions) for Deposit/Withdraw, where
    /// the domain enforces its rules before persisting.
    /// </summary>
    Task<Account?> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Account id plus computed balance (SUM in the database, never stored).
    /// Null when no account exists.
    /// </summary>
    Task<(Guid AccountId, long Balance)?> GetCurrentBalanceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// History page with balance and total count, all computed in the database
    /// (COUNT + SUM + Skip/Take in SQL — never paginated in memory).
    /// Null when no account exists.
    /// </summary>
    Task<AccountHistoryPage?> GetHistoryPageAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}

/// <summary>
/// Domain result of a paginated history read: the page items plus the
/// aggregates the response needs, all resolved against the same account.
/// </summary>
public sealed record AccountHistoryPage(
    Guid AccountId,
    long Balance,
    int TotalCount,
    IReadOnlyList<Transaction> Transactions);
