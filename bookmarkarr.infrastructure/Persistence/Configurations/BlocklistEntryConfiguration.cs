/*
 * Bookmarkarr - Audiobook Management System
 * Copyright (C) 2024-2026 Bookmarkarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bookmarkarr.Infrastructure.Persistence.Configurations
{
    public class BlocklistEntryConfiguration : IEntityTypeConfiguration<BlocklistEntry>
    {
        public void Configure(EntityTypeBuilder<BlocklistEntry> builder)
        {
            builder.ToTable("BlocklistEntries");

            builder.HasKey(entry => entry.Id);

            builder.Property(entry => entry.Title)
                .IsRequired()
                .HasMaxLength(500);

            builder.Property(entry => entry.NormalizedTitle)
                .IsRequired()
                .HasMaxLength(500);

            builder.Property(entry => entry.ReleaseGuid)
                .HasMaxLength(500);

            builder.Property(entry => entry.Source)
                .HasMaxLength(200);

            builder.Property(entry => entry.Protocol)
                .HasMaxLength(50);

            builder.Property(entry => entry.Reason)
                .HasMaxLength(2000);

            // Blocklist lookups always start from the book being searched, so this is the index
            // that matters; the release comparison itself runs in memory over that short list.
            builder.HasIndex(entry => entry.AudiobookId);

            builder.HasIndex(entry => entry.NormalizedTitle);
        }
    }
}
