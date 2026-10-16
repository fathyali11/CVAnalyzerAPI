using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CVAnalyzerAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueNormalizedEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail",
                unique: true);

            // Accounts created before account lockout was wired up were inserted with
            // LockoutEnabled = false, which silently opts them out of it - so lockout would
            // only ever have applied to newly registered users. Flip the existing rows.
            // Deliberately not reverted in Down(): which rows were already enabled before is
            // not knowable after the fact.
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" SET "LockoutEnabled" = TRUE WHERE "LockoutEnabled" = FALSE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");
        }
    }
}
