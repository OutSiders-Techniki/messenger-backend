using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Db.Configurations;

public sealed class PendingFileDeletionConfiguration : IEntityTypeConfiguration<PendingFileDeletion>
{
    public void Configure(EntityTypeBuilder<PendingFileDeletion> builder)
    {
        builder.HasKey(x => x.StorageKey);
        builder.Property(x => x.StorageKey).HasMaxLength(512);
    }
}
