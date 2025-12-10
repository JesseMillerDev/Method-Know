using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Know.ApiService.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomCss : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomCss",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "Articles",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomCss",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Articles");
        }
    }
}
