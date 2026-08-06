using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookmarkarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBlocklistEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IndexerId",
                table: "Downloads",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseGuid",
                table: "Downloads",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BlocklistEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AudiobookId = table.Column<int>(type: "INTEGER", nullable: true),
                    EditionId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReleaseGuid = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    NormalizedTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    FailureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlocklistEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlocklistEntries_AudiobookId",
                table: "BlocklistEntries",
                column: "AudiobookId");

            migrationBuilder.CreateIndex(
                name: "IX_BlocklistEntries_NormalizedTitle",
                table: "BlocklistEntries",
                column: "NormalizedTitle");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlocklistEntries");

            migrationBuilder.DropColumn(
                name: "IndexerId",
                table: "Downloads");

            migrationBuilder.DropColumn(
                name: "ReleaseGuid",
                table: "Downloads");
        }
    }
}
