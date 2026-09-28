using FinanceControl.API.Domain.Enums;
using FinanceControl.API.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanceControl.API.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the PostgreSQL schema. The relational model (snake_case
/// tables, check constraints) is configured here with the fluent API so the
/// Domain layer stays free of persistence concerns.
/// </summary>
public sealed class FinanceControlDbContext(DbContextOptions<FinanceControlDbContext> options)
    : DbContext(options)
{
    internal DbSet<AccountRow> Accounts => Set<AccountRow>();

    internal DbSet<TransactionRow> Transactions => Set<TransactionRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<AccountRow>();
        account.ToTable("accounts");
        account.HasKey(a => a.Id);
        account.Property(a => a.Id).HasColumnName("id");
        account.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");
        account.HasMany(a => a.Transactions)
            .WithOne(t => t.Account!)
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        var transaction = modelBuilder.Entity<TransactionRow>();
        transaction.ToTable("transactions", table =>
        {
            // Defense in depth mirroring the domain invariants (InvalidAmountException / TransactionType).
            // Both are unreachable through the API — input validation and the entities reject first.
            table.HasCheckConstraint("ck_transactions_amount_positive", "amount > 0");
            table.HasCheckConstraint(
                "ck_transactions_type_valid",
                $"type IN ({(int)TransactionType.Credit}, {(int)TransactionType.Debit})");
        });
        transaction.HasKey(t => t.Id);
        transaction.Property(t => t.Id).HasColumnName("id");
        transaction.Property(t => t.AccountId).HasColumnName("account_id");
        transaction.Property(t => t.Amount).HasColumnName("amount").HasColumnType("bigint");
        transaction.Property(t => t.Type).HasColumnName("type");
        transaction.Property(t => t.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        transaction.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");
        transaction.HasIndex(t => new { t.AccountId, t.CreatedAt, t.Id })
            .HasDatabaseName("IX_transactions_account_history");
    }
}
