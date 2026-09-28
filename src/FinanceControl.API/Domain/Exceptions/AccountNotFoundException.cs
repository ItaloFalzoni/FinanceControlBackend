namespace FinanceControl.API.Domain.Exceptions;

public sealed class AccountNotFoundException(Guid accountId) : DomainException($"Account '{accountId}' was not found.")
{
    public Guid AccountId { get; } = accountId;
}
