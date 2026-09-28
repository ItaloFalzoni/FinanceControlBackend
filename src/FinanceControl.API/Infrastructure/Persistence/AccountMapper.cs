using FinanceControl.API.Domain.Entities;
using FinanceControl.API.Infrastructure.Persistence.Entities;

namespace FinanceControl.API.Infrastructure.Persistence;

/// <summary>
/// Maps between domain entities and persistence rows. Domain entities are rebuilt
/// through their internal Reconstitute factories — reads never mutate them.
/// </summary>
internal static class AccountMapper
{
    internal static Account ToDomain(AccountRow row)
    {
        var transactions = row.Transactions
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Select(ToDomain)
            .ToList();

        return Account.Reconstitute(row.Id, row.CreatedAt, transactions);
    }

    internal static Transaction ToDomain(TransactionRow row) =>
        Transaction.Reconstitute(row.Id, row.Amount, row.Type, row.Description, row.CreatedAt);

    internal static AccountRow ToRow(Account account) =>
        new()
        {
            Id = account.Id,
            CreatedAt = account.CreatedAt,
            Transactions = []
        };

    internal static TransactionRow ToRow(Transaction transaction, Guid accountId) =>
        new()
        {
            Id = transaction.Id,
            AccountId = accountId,
            Amount = transaction.Amount,
            Type = transaction.Type,
            Description = transaction.Description,
            CreatedAt = transaction.CreatedAt
        };
}
