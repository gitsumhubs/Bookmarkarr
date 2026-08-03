using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookmarkarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaTypeToQualityProfilesAndRootFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MediaType",
                table: "RootFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MediaType",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Existing installs predate media-aware roots, so every row defaults to
            // Audiobook (0). Reclassify roots that are unambiguously ebook libraries so
            // an upgrade keeps pointing ebook imports at the tree the user already uses.
            // "%ebook%" cannot match "audiobooks" ("audiobooks" contains "obook", not
            // "ebook"), so audiobook roots are never reclassified. Only the new column is
            // touched; names, paths, and IsDefault selections are left exactly as-is.
            migrationBuilder.Sql(
                "UPDATE RootFolders SET MediaType = 1 WHERE lower(Path) LIKE '%ebook%';");

            // Quality profiles are deliberately NOT classified by name. Media-specific
            // ebook profiles did not exist before this migration, so every existing
            // profile is genuinely an audiobook profile and correctly stays at 0.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediaType",
                table: "RootFolders");

            migrationBuilder.DropColumn(
                name: "MediaType",
                table: "QualityProfiles");
        }
    }
}
