using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookmarkarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAudiobookBayPatchState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AudiobookBayDefinitionsDirectory",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudiobookBayPatchDefinitionPath",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudiobookBayPatchIndexerId",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudiobookBayPatchPages",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudiobookBayPatchPreviousIndexerUrl",
                table: "ApplicationSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudiobookBayPatchProwlarrIndexerId",
                table: "ApplicationSettings",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudiobookBayDefinitionsDirectory",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "AudiobookBayPatchDefinitionPath",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "AudiobookBayPatchIndexerId",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "AudiobookBayPatchPages",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "AudiobookBayPatchPreviousIndexerUrl",
                table: "ApplicationSettings");

            migrationBuilder.DropColumn(
                name: "AudiobookBayPatchProwlarrIndexerId",
                table: "ApplicationSettings");
        }
    }
}
