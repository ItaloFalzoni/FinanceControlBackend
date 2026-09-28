using FinanceControl.API.Domain.Exceptions;

namespace FinanceControl.API.Domain.Entities;

/// <summary>
/// Aggregate root that owns all business rules for balance and transactions.
/// The system tracks a single account (the ledger), so the entity carries no
/// name or other descriptive state — only identity, creation time and movements.
/// </summary>
public sealed class Account
{
    private readonly List<Transaction> _transactions = [];

    public Guid Id { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }

    public IReadOnlyList<Transaction> Transactions => _transactions.AsReadOnly();

    /// <summary>
    /// Balance in cents, always computed as the sum of signed amounts.
    /// Uses checked arithmetic so overflow throws instead of wrapping.
    /// </summary>
    public long Balance
    {
        get
        {
            checked
            {
                long total = 0;
                foreach (var t in _transactions)
                    total += t.SignedAmount;
                return total;
            }
        }
    }

    public Account()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Transaction Deposit(long amount, string description)
    {
        var transaction = Transaction.CreateCredit(amount, description);
        checked
        {
            _ = Balance + amount;
        }
        _transactions.Add(transaction);
        return transaction;
    }

    public Transaction Withdraw(long amount, string description)
    {
        if (amount <= 0)
            throw new InvalidAmountException(amount);

        var currentBalance = Balance;
        long projectedBalance;
        checked
        {
            projectedBalance = currentBalance - amount;
        }
        if (projectedBalance < 0)
            throw new InsufficientFundsException(currentBalance, amount);

        var transaction = Transaction.CreateDebit(amount, description);
        _transactions.Add(transaction);
        return transaction;
    }

    /// <summary>
    /// Rebuilds a persisted account with its stored transactions. The balance stays
    /// computed from them (the central invariant is never stored anywhere).
    /// Persistence-only entry point: mutation keeps going through Deposit/Withdraw,
    /// which append new transactions for the repository to persist.
    /// </summary>
    internal static Account Reconstitute(
        Guid id,
        DateTimeOffset createdAt,
        IReadOnlyList<Transaction> transactions)
    {
        var account = new Account
        {
            Id = id,
            CreatedAt = createdAt
        };
        account._transactions.AddRange(transactions);
        return account;
    }
}
