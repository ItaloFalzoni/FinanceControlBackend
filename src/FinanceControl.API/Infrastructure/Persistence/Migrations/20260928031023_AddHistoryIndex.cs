using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceControl.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transactions_account_id",
                table: "transactions");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_account_history",
                table: "transactions",
                columns: new[] { "account_id", "created_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transactions_account_history",
                table: "transactions");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_account_id",
                table: "transactions",
                column: "account_id");
        }
    }
}
