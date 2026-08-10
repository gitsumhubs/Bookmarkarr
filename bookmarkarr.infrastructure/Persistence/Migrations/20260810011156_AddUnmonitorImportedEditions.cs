using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookmarkarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnmonitorImportedEditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UnmonitorImportedEditions",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnmonitorImportedEditions",
                table: "ApplicationSettings");
        }
    }
}
