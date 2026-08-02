/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bookmarkarr.Infrastructure.Persistence.Configurations;

public sealed class BookEditionConfiguration : IEntityTypeConfiguration<BookEdition>
{
    public void Configure(EntityTypeBuilder<BookEdition> builder)
    {
        builder.HasOne(e => e.Book).WithMany(b => b.Editions).HasForeignKey(e => e.BookId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.QualityProfile).WithMany().HasForeignKey(e => e.QualityProfileId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.RootFolder).WithMany().HasForeignKey(e => e.RootFolderId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(e => e.Files).WithOne(f => f.Edition).HasForeignKey(f => f.EditionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CatalogImportBatchConfiguration : IEntityTypeConfiguration<CatalogImportBatch>
{
    public void Configure(EntityTypeBuilder<CatalogImportBatch> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.NormalizedRowsJson).IsRequired();
    }
}
