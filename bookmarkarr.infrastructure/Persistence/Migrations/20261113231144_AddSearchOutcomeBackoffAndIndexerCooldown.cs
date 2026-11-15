using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookmarkarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchOutcomeBackoffAndIndexerCooldown : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                table: "Indexers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastFailureReason",
                table: "Indexers",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UnavailableUntilUtc",
                table: "Indexers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSearchOutcome",
                table: "BookEditions",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextSearchTime",
                table: "BookEditions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SearchMisses",
                table: "BookEditions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Upgrades become opt-in. The flag defaulted to on and no screen ever offered it, so
            // every edition carries "on" without anybody having chosen it — and an edition below
            // its profile cutoff was searched again on every pass. It can be set back per edition
            // through the API. Down() does not restore it: which rows were on by choice is unknown.
            migrationBuilder.Sql("UPDATE BookEditions SET UpgradeAllowed = 0;");

            // A downloaded book stops being monitored by default. The setting arrived off, so on an
            // existing install "off" is almost always the untouched default rather than a choice;
            // it can be turned back off under Settings > Search.
            migrationBuilder.Sql("UPDATE ApplicationSettings SET UnmonitorImportedEditions = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "LastFailureReason",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "UnavailableUntilUtc",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "LastSearchOutcome",
                table: "BookEditions");

            migrationBuilder.DropColumn(
                name: "NextSearchTime",
                table: "BookEditions");

            migrationBuilder.DropColumn(
                name: "SearchMisses",
                table: "BookEditions");
        }
    }
}
