using FinanceControl.API.Domain.Entities;
using FinanceControl.API.Domain.Enums;
using FinanceControl.API.Domain.Exceptions;

namespace FinanceControl.UnitTests.Domain;

public sealed class AccountTests
{
    [Fact]
    public void NewAccount_HasZeroBalance()
    {
        var account = new Account();

        Assert.Equal(0L, account.Balance);
        Assert.Empty(account.Transactions);
    }

    [Fact]
    public void Deposit_ValidAmount_IncreasesBalanceAndReturnsCredit()
    {
        var account = new Account();

        var transaction = account.Deposit(10000, "Sales");

        Assert.Equal(10000L, account.Balance);
        Assert.Equal(TransactionType.Credit, transaction.Type);
        Assert.Equal(10000L, transaction.Amount);
        Assert.Single(account.Transactions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1050)]
    public void Deposit_InvalidAmount_ThrowsInvalidAmountException(long amount)
    {
        var account = new Account();

        Assert.Throws<InvalidAmountException>(() => account.Deposit(amount, "Sales"));
        Assert.Equal(0L, account.Balance);
    }

    [Fact]
    public void Withdraw_WithSufficientFunds_DecreasesBalanceAndReturnsDebit()
    {
        var account = new Account();
        account.Deposit(10000, "Sales");

        var transaction = account.Withdraw(3000, "Supplier");

        Assert.Equal(7000L, account.Balance);
        Assert.Equal(TransactionType.Debit, transaction.Type);
        Assert.Equal(3000L, transaction.Amount);
    }

    [Fact]
    public void Withdraw_WithInsufficientFunds_ThrowsInsufficientFundsException()
    {
        var account = new Account();
        account.Deposit(5000, "Sales");

        Assert.Throws<InsufficientFundsException>(() => account.Withdraw(6000, "Supplier"));
        Assert.Equal(5000L, account.Balance);
        Assert.Single(account.Transactions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Withdraw_InvalidAmount_ThrowsInvalidAmountException(long amount)
    {
        var account = new Account();
        account.Deposit(5000, "Sales");

        Assert.Throws<InvalidAmountException>(() => account.Withdraw(amount, "Supplier"));
        Assert.Equal(5000L, account.Balance);
    }

    [Fact]
    public void Withdraw_ExactBalance_AllowsZeroBalance()
    {
        var account = new Account();
        account.Deposit(7000, "Sales");

        account.Withdraw(7000, "All out");

        Assert.Equal(0L, account.Balance);
    }

    [Fact]
    public void MultipleOperations_AccumulateSignedAmounts()
    {
        var account = new Account();
        account.Deposit(10000, "A");
        account.Deposit(5000, "B");
        account.Withdraw(3000, "C");

        Assert.Equal(12000L, account.Balance);
        Assert.Equal(3, account.Transactions.Count);
    }

    [Fact]
    public void Balance_Overflow_ThrowsOverflowException()
    {
        var account = new Account();
        account.Deposit(long.MaxValue, "A");

        Assert.Throws<OverflowException>(() => account.Deposit(1, "B"));
        Assert.Equal(long.MaxValue, account.Balance);
    }
}
