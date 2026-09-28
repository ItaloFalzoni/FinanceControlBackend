using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinanceControl.API.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                table: "idempotency_keys",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now() + interval '24 hours'");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_keys_expires_at",
                table: "idempotency_keys",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_idempotency_keys_expires_at",
                table: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "idempotency_keys");
        }
    }
}
