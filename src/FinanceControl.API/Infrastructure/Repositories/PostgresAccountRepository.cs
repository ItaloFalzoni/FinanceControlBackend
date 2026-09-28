using FinanceControl.API.Domain.Entities;
using FinanceControl.API.Domain.Enums;
using FinanceControl.API.Domain.Exceptions;
using FinanceControl.API.Domain.Repositories;
using FinanceControl.API.Infrastructure.Persistence;
using FinanceControl.API.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceControl.API.Infrastructure.Repositories;

/// <summary>
/// PostgreSQL implementation of IAccountRepository and IAccountResolver: the
/// application now depends on the database (no in-memory fallback). Writes run inside a database
/// transaction; UpdateAsync re-validates available funds against the stored
/// balance under a row lock (SELECT ... FOR UPDATE), so concurrent withdrawals
/// cannot overdraw an account. The balance itself is never stored: it is always
/// the sum of signed amounts.
/// </summary>
public sealed class PostgresAccountRepository(FinanceControlDbContext dbContext)
    : IAccountRepository, IAccountResolver
{
    /// <summary>
    /// Full aggregate for Deposit/Withdraw: the ledger keeps a single account
    /// (oldest row wins), loaded with all transactions so the domain can
    /// enforce its rules before persisting.
    /// </summary>
    public Task<Account?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        GetSingleAccountAsync(cancellationToken);

    /// <summary>
    /// Id plus SUM-computed balance (two lightweight queries, no transaction
    /// rows loaded). Null when no account exists yet (first-run).
    /// </summary>
    public async Task<(Guid AccountId, long Balance)?> GetCurrentBalanceAsync(
        CancellationToken cancellationToken = default)
    {
        var header = await GetOldestHeaderAsync(cancellationToken);
        if (header is null) return null;

        var balance = await GetStoredBalanceAsync(header.Value.Id, cancellationToken);
        return (header.Value.Id, balance);
    }

    /// <summary>
    /// History page resolved entirely in the database: COUNT for the total,
    /// SUM for the balance, Skip/Take for the items (most recent first).
    /// Null when no account exists yet (first-run).
    /// </summary>
    public async Task<AccountHistoryPage?> GetHistoryPageAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var header = await GetOldestHeaderAsync(cancellationToken);
        if (header is null) return null;

        var baseQuery = dbContext.Transactions
            .AsNoTracking()
            .Where(t => t.AccountId == header.Value.Id);

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var balance = await baseQuery.SumAsync(
            t => (long?)(t.Type == TransactionType.Credit ? t.Amount : -t.Amount),
            cancellationToken) ?? 0L;

        var rows = await baseQuery
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Skip(checked((page - 1) * pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(AccountMapper.ToDomain).ToList();
        return new AccountHistoryPage(header.Value.Id, balance, totalCount, items);
    }

    /// <summary>
    /// Atomic check-then-insert for the single account. The advisory transaction
    /// lock serializes concurrent creators on the same database (double-click,
    /// retry, two replicas): at most one inserts, the loser re-reads the winner
    /// under the same lock and reports already-exists instead of duplicating.
    /// </summary>
    public async Task<(bool Created, Guid AccountId)> CreateSingleAsync(
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtext('financecontrol-single-account-create'))",
            cancellationToken);

        var existing = await GetOldestHeaderAsync(cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return (false, existing.Value.Id);
        }

        var account = new Account();
        dbContext.Accounts.Add(AccountMapper.ToRow(account));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (true, account.Id);
    }

    public async Task UpdateAsync(Account account, CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await LockAccountRowAsync(account.Id, cancellationToken);
        if (!await dbContext.Accounts.AnyAsync(a => a.Id == account.Id, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new AccountNotFoundException(account.Id);
        }
        var storedBalance = await GetStoredBalanceAsync(account.Id, cancellationToken);
        var existing = await GetExistingTransactionIdsAsync(account.Id, cancellationToken);

        var pending = account.Transactions.Where(t => !existing.Contains(t.Id)).ToList();
        if (pending.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        // Authoritative funds check: the domain already validated against its own
        // (possibly stale) view; this runs against locked, current data.
        // Amounts are in cents; checked so overflow throws instead of wrapping.
        long projectedBalance;
        long requestedAmount;
        checked
        {
            projectedBalance = storedBalance + pending.Sum(t => t.SignedAmount);
            requestedAmount = pending
                .Where(t => t.Type == TransactionType.Debit)
                .Sum(t => t.Amount);
        }
        if (projectedBalance < 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InsufficientFundsException(storedBalance, requestedAmount);
        }

        foreach (var transactionRow in pending)
            dbContext.Transactions.Add(AccountMapper.ToRow(transactionRow, account.Id));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // Single source of the oldest-wins rule for all account reads (existence,
    // balance, history) and for the write aggregate below: accounts ordered by
    // creation time, id as tie-breaker. Callers add AsNoTracking/Include/Select
    // on top so these paths can never diverge on ordering.
    private IOrderedQueryable<AccountRow> OldestAccountsFirst() =>
        dbContext.Accounts
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id);

    // Accounts row only, no transaction join: cheap existence check used by
    // reads and by CreateSingleAsync.
    private async Task<(Guid Id, DateTimeOffset CreatedAt)?> GetOldestHeaderAsync(
        CancellationToken cancellationToken)
    {
        var header = await OldestAccountsFirst()
            .AsNoTracking()
            .Select(a => new { a.Id, a.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        return header is null ? null : (header.Id, header.CreatedAt);
    }

    // Full aggregate for writes: same oldest-wins ordering, with transactions
    // so the domain can enforce Deposit/Withdraw rules before persisting.
    private async Task<Account?> GetSingleAccountAsync(CancellationToken cancellationToken)
    {
        var row = await OldestAccountsFirst()
            .AsNoTracking()
            .Include(a => a.Transactions)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : AccountMapper.ToDomain(row);
    }

    // Serialize writers of this account: EF Core has no fluent FOR UPDATE,
    // so the row lock is taken with raw SQL before the balance is read.
    private async Task LockAccountRowAsync(Guid accountId, CancellationToken cancellationToken) =>
        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT id FROM accounts WHERE id = {0} FOR UPDATE",
            [accountId],
            cancellationToken);

    // Fresh stored balance in cents (still computed — never persisted). NULL-safe sum for
    // accounts without transactions.
    private async Task<long> GetStoredBalanceAsync(Guid accountId, CancellationToken cancellationToken) =>
        await dbContext.Transactions
            .Where(t => t.AccountId == accountId)
            .SumAsync(
                t => (long?)(t.Type == TransactionType.Credit ? t.Amount : -t.Amount),
                cancellationToken) ?? 0L;

    private async Task<HashSet<Guid>> GetExistingTransactionIdsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var existingIds = await dbContext.Transactions
            .Where(t => t.AccountId == accountId)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        return existingIds.ToHashSet();
    }
}
