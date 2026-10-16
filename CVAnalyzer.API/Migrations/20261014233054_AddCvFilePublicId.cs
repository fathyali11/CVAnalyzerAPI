using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CVAnalyzerAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddCvFilePublicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FilePublicId",
                table: "CVs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilePublicId",
                table: "CVs");
        }
    }
}
