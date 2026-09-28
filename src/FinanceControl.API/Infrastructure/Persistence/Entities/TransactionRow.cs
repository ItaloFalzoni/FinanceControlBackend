using FinanceControl.API.Domain.Enums;

namespace FinanceControl.API.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence model for the transactions table. Append-only: the domain never
/// edits or deletes a transaction, only adds new ones.
/// </summary>
internal sealed class TransactionRow
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>
    /// Amount in minor units (cents), stored as bigint.
    /// </summary>
    public long Amount { get; set; }

    public TransactionType Type { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public AccountRow? Account { get; set; }
}
