using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookmarkarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexerRequestQuota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RequestsPerHour",
                table: "Indexers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IndexerQuotaUsages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Purpose = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexerQuotaUsages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IndexerQuotaUsages_IndexerId_OccurredAtUtc",
                table: "IndexerQuotaUsages",
                columns: new[] { "IndexerId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IndexerQuotaUsages");

            migrationBuilder.DropColumn(
                name: "RequestsPerHour",
                table: "Indexers");
        }
    }
}
