using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookmarkarr.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnifiedBookEditionsAndCatalogImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AudiobookExternalIdentifiers_Audiobooks_AudiobookId",
                table: "AudiobookExternalIdentifiers");

            migrationBuilder.DropForeignKey(
                name: "FK_AudiobookFiles_Audiobooks_AudiobookId",
                table: "AudiobookFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_Audiobooks_QualityProfiles_QualityProfileId",
                table: "Audiobooks");

            migrationBuilder.DropForeignKey(
                name: "FK_AudiobookSeriesMemberships_Audiobooks_AudiobookId",
                table: "AudiobookSeriesMemberships");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Audiobooks",
                table: "Audiobooks");

            migrationBuilder.RenameTable(
                name: "Audiobooks",
                newName: "Books");

            migrationBuilder.RenameIndex(
                name: "IX_Audiobooks_QualityProfileId",
                table: "Books",
                newName: "IX_Books_QualityProfileId");

            migrationBuilder.RenameIndex(
                name: "IX_Audiobooks_Monitored",
                table: "Books",
                newName: "IX_Books_Monitored");

            migrationBuilder.RenameIndex(
                name: "IX_Audiobooks_LastSearchTime",
                table: "Books",
                newName: "IX_Books_LastSearchTime");

            migrationBuilder.AddColumn<int>(
                name: "EditionId",
                table: "History",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaType",
                table: "History",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EditionId",
                table: "Downloads",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoodreadsId",
                table: "Books",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Books",
                table: "Books",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "BookEditions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BookId = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpgradeAllowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    RootFolderId = table.Column<int>(type: "INTEGER", nullable: true),
                    RootPath = table.Column<string>(type: "TEXT", nullable: true),
                    DownloadCategory = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastSearchTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookEditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookEditions_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookEditions_QualityProfiles_QualityProfileId",
                        column: x => x.QualityProfileId,
                        principalTable: "QualityProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BookEditions_RootFolders_RootFolderId",
                        column: x => x.RootFolderId,
                        principalTable: "RootFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CatalogImportBatches",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CommittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NormalizedRowsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CommitSummaryJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EditionFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EditionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditionFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EditionFiles_BookEditions_EditionId",
                        column: x => x.EditionId,
                        principalTable: "BookEditions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_History_EditionId_Timestamp",
                table: "History",
                columns: new[] { "EditionId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_History_MediaType_Timestamp",
                table: "History",
                columns: new[] { "MediaType", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Books_GoodreadsId",
                table: "Books",
                column: "GoodreadsId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookEditions_BookId_MediaType",
                table: "BookEditions",
                columns: new[] { "BookId", "MediaType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookEditions_MediaType_Monitored_Status",
                table: "BookEditions",
                columns: new[] { "MediaType", "Monitored", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BookEditions_QualityProfileId",
                table: "BookEditions",
                column: "QualityProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_BookEditions_RootFolderId",
                table: "BookEditions",
                column: "RootFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogImportBatches_ExpiresAt",
                table: "CatalogImportBatches",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_EditionFiles_EditionId",
                table: "EditionFiles",
                column: "EditionId");

            migrationBuilder.CreateIndex(
                name: "IX_EditionFiles_Path",
                table: "EditionFiles",
                column: "Path",
                unique: true);

            // Preserve every inherited library entry as an independently managed
            // audiobook edition. This is idempotent because (BookId, MediaType) is unique.
            migrationBuilder.Sql("""
                INSERT OR IGNORE INTO BookEditions
                    (BookId, MediaType, Monitored, UpgradeAllowed, Status, QualityProfileId,
                     RootPath, DownloadCategory, CreatedAt, UpdatedAt)
                SELECT Id, 0, Monitored, 1,
                       CASE WHEN Monitored = 0 THEN 0
                            WHEN FilePath IS NULL OR trim(FilePath) = '' THEN 1
                            ELSE 4 END,
                       QualityProfileId, BasePath, 'audiobooks', CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                FROM Books
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_AudiobookExternalIdentifiers_Books_AudiobookId",
                table: "AudiobookExternalIdentifiers",
                column: "AudiobookId",
                principalTable: "Books",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AudiobookFiles_Books_AudiobookId",
                table: "AudiobookFiles",
                column: "AudiobookId",
                principalTable: "Books",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AudiobookSeriesMemberships_Books_AudiobookId",
                table: "AudiobookSeriesMemberships",
                column: "AudiobookId",
                principalTable: "Books",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Books_QualityProfiles_QualityProfileId",
                table: "Books",
                column: "QualityProfileId",
                principalTable: "QualityProfiles",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AudiobookExternalIdentifiers_Books_AudiobookId",
                table: "AudiobookExternalIdentifiers");

            migrationBuilder.DropForeignKey(
                name: "FK_AudiobookFiles_Books_AudiobookId",
                table: "AudiobookFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_AudiobookSeriesMemberships_Books_AudiobookId",
                table: "AudiobookSeriesMemberships");

            migrationBuilder.DropForeignKey(
                name: "FK_Books_QualityProfiles_QualityProfileId",
                table: "Books");

            migrationBuilder.DropTable(
                name: "CatalogImportBatches");

            migrationBuilder.DropTable(
                name: "EditionFiles");

            migrationBuilder.DropTable(
                name: "BookEditions");

            migrationBuilder.DropIndex(
                name: "IX_History_EditionId_Timestamp",
                table: "History");

            migrationBuilder.DropIndex(
                name: "IX_History_MediaType_Timestamp",
                table: "History");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Books",
                table: "Books");

            migrationBuilder.DropIndex(
                name: "IX_Books_GoodreadsId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "EditionId",
                table: "History");

            migrationBuilder.DropColumn(
                name: "MediaType",
                table: "History");

            migrationBuilder.DropColumn(
                name: "EditionId",
                table: "Downloads");

            migrationBuilder.DropColumn(
                name: "GoodreadsId",
                table: "Books");

            migrationBuilder.RenameTable(
                name: "Books",
                newName: "Audiobooks");

            migrationBuilder.RenameIndex(
                name: "IX_Books_QualityProfileId",
                table: "Audiobooks",
                newName: "IX_Audiobooks_QualityProfileId");

            migrationBuilder.RenameIndex(
                name: "IX_Books_Monitored",
                table: "Audiobooks",
                newName: "IX_Audiobooks_Monitored");

            migrationBuilder.RenameIndex(
                name: "IX_Books_LastSearchTime",
                table: "Audiobooks",
                newName: "IX_Audiobooks_LastSearchTime");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Audiobooks",
                table: "Audiobooks",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AudiobookExternalIdentifiers_Audiobooks_AudiobookId",
                table: "AudiobookExternalIdentifiers",
                column: "AudiobookId",
                principalTable: "Audiobooks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AudiobookFiles_Audiobooks_AudiobookId",
                table: "AudiobookFiles",
                column: "AudiobookId",
                principalTable: "Audiobooks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Audiobooks_QualityProfiles_QualityProfileId",
                table: "Audiobooks",
                column: "QualityProfileId",
                principalTable: "QualityProfiles",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AudiobookSeriesMemberships_Audiobooks_AudiobookId",
                table: "AudiobookSeriesMemberships",
                column: "AudiobookId",
                principalTable: "Audiobooks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
