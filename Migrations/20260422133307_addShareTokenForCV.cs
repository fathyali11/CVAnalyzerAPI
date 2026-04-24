using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CVAnalyzerAPI.Migrations
{
    /// <inheritdoc />
    public partial class addShareTokenForCV : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ShareToken",
                table: "CVs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_CVs_ShareToken",
                table: "CVs",
                column: "ShareToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CVs_ShareToken",
                table: "CVs");

            migrationBuilder.DropColumn(
                name: "ShareToken",
                table: "CVs");
        }
    }
}
