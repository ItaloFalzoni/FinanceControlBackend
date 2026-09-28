namespace FinanceControl.API.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence model for the accounts table. Kept separate from the domain entity
/// so the Domain layer keeps zero infrastructure dependencies (no EF attributes there).
/// </summary>
internal sealed class AccountRow
{
    public Guid Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<TransactionRow> Transactions { get; set; } = [];
}
