using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CVAnalyzerAPI.Migrations
{
    /// <inheritdoc />
    public partial class addSkillsAtAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DomainExperience",
                table: "Analyses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SoftSkillsFit",
                table: "Analyses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TechnicalAlignment",
                table: "Analyses",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DomainExperience",
                table: "Analyses");

            migrationBuilder.DropColumn(
                name: "SoftSkillsFit",
                table: "Analyses");

            migrationBuilder.DropColumn(
                name: "TechnicalAlignment",
                table: "Analyses");
        }
    }
}
