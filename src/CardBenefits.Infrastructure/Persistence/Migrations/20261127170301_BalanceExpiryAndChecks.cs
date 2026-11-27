using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CardBenefits.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BalanceExpiryAndChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpiresOn",
                table: "BalanceSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BalanceChecks",
                columns: table => new
                {
                    Account = table.Column<string>(type: "TEXT", nullable: false),
                    Owner = table.Column<string>(type: "TEXT", nullable: false),
                    Program = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Problem = table.Column<string>(type: "TEXT", nullable: true),
                    CheckedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceChecks", x => x.Account);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BalanceSnapshots_PersonId_Program",
                table: "BalanceSnapshots",
                columns: new[] { "PersonId", "Program" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BalanceChecks");

            migrationBuilder.DropIndex(
                name: "IX_BalanceSnapshots_PersonId_Program",
                table: "BalanceSnapshots");

            migrationBuilder.DropColumn(
                name: "ExpiresOn",
                table: "BalanceSnapshots");
        }
    }
}
