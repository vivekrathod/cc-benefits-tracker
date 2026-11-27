using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chitragupta.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BalanceAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BalanceAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Account = table.Column<string>(type: "TEXT", nullable: false),
                    Owner = table.Column<string>(type: "TEXT", nullable: false),
                    Program = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Problem = table.Column<string>(type: "TEXT", nullable: true),
                    CheckedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    Retryable = table.Column<bool>(type: "INTEGER", nullable: false),
                    SignedIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    Advice = table.Column<string>(type: "TEXT", nullable: true),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: true),
                    ToolVersion = table.Column<string>(type: "TEXT", nullable: true),
                    Trail = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceAttempts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BalanceAttempts_Account_CheckedAt",
                table: "BalanceAttempts",
                columns: new[] { "Account", "CheckedAt" },
                unique: true);

            // What was known about each account becomes the first line of its history.
            migrationBuilder.Sql(
                """
                INSERT INTO "BalanceAttempts" ("Account", "Owner", "Program", "Status", "Problem", "CheckedAt", "Retryable", "SignedIn")
                SELECT "Account", "Owner", "Program", "Status", "Problem", "CheckedAt", 0, 1 FROM "BalanceChecks";
                """);

            migrationBuilder.DropTable(
                name: "BalanceChecks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BalanceChecks",
                columns: table => new
                {
                    Account = table.Column<string>(type: "TEXT", nullable: false),
                    CheckedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Owner = table.Column<string>(type: "TEXT", nullable: false),
                    Problem = table.Column<string>(type: "TEXT", nullable: true),
                    Program = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalanceChecks", x => x.Account);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO "BalanceChecks" ("Account", "CheckedAt", "Owner", "Problem", "Program", "Status")
                SELECT "Account", "CheckedAt", "Owner", "Problem", "Program", "Status" FROM "BalanceAttempts" AS latest
                WHERE NOT EXISTS (
                    SELECT 1 FROM "BalanceAttempts" AS newer
                    WHERE newer."Account" = latest."Account" AND newer."CheckedAt" > latest."CheckedAt");
                """);

            migrationBuilder.DropTable(
                name: "BalanceAttempts");
        }
    }
}
