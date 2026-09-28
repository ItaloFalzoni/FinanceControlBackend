using FinanceControl.API.Domain.Enums;
using FinanceControl.API.Domain.Exceptions;

namespace FinanceControl.API.Domain.Entities;

public sealed class Transaction
{
    public Guid Id { get; private init; }
    /// <summary>
    /// Amount in minor units (cents). Integer only — the wire, domain and
    /// database layers never handle fractional units.
    /// </summary>
    public long Amount { get; private init; }
    public TransactionType Type { get; private init; }
    public string Description { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }

    private Transaction() 
    {
        Description = string.Empty;
    }

    private Transaction(long amount, TransactionType type, string description)
    {
        if (amount <= 0)
            throw new InvalidAmountException(amount);

        Id = Guid.NewGuid();
        Amount = amount;
        Type = type;
        Description = description?.Trim() ?? string.Empty;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public static Transaction CreateCredit(long amount, string description)
        => new(amount, TransactionType.Credit, description);

    public static Transaction CreateDebit(long amount, string description)
        => new(amount, TransactionType.Debit, description);

    /// <summary>
    /// Rebuilds a persisted transaction keeping its stored ID, type and timestamps.
    /// Persistence-only entry point: the public factories always mint new IDs,
    /// so loading a row through them would break identity. Validates the same
    /// invariant (amount &gt; 0) the factories enforce.
    /// </summary>
    internal static Transaction Reconstitute(
        Guid id,
        long amount,
        TransactionType type,
        string description,
        DateTimeOffset createdAt)
    {
        if (amount <= 0)
            throw new InvalidAmountException(amount);

        return new Transaction
        {
            Id = id,
            Amount = amount,
            Type = type,
            Description = description?.Trim() ?? string.Empty,
            CreatedAt = createdAt
        };
    }

    /// <summary>
    /// Signed amount in cents: positive for credits, negative for debits.
    /// </summary>
    public long SignedAmount => Type == TransactionType.Credit ? Amount : -Amount;
}
