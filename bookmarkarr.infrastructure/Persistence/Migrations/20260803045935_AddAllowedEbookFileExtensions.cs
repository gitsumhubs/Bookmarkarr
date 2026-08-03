using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookmarkarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowedEbookFileExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rows deliberately keep an empty list, which falls back to the
            // built-in ebook extension set. Writing an explicit list here would silently
            // narrow what an upgraded install already accepts (it would drop .azw, .djvu,
            // and .fb2). Fresh installs get the curated default from the entity instead.
            // The audiobook list is not touched, so customised audio settings survive.
            migrationBuilder.AddColumn<string>(
                name: "AllowedEbookFileExtensions",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedEbookFileExtensions",
                table: "ApplicationSettings");
        }
    }
}
