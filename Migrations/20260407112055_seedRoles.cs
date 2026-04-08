using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CVAnalyzerAPI.Migrations
{
    /// <inheritdoc />
    public partial class seedRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { "37428717-385c-4bf6-bb31-dcbe09bf6625", "3781e88b-83b5-401c-8786-a37eb7cd4e67", "Admin", "ADMIN" },
                    { "b6366753-89c0-426a-8679-21b8f209b463", "7a798498-92ae-4d76-b15a-a5935fbe5261", "User", "USER" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "37428717-385c-4bf6-bb31-dcbe09bf6625");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: "b6366753-89c0-426a-8679-21b8f209b463");
        }
    }
}
