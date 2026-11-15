using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Db.Configurations;

public class DialogConfiguration : IEntityTypeConfiguration<Dialog>
{
    public void Configure(EntityTypeBuilder<Dialog> builder)
    {
        builder.HasKey(d => d.Id);

        // Guid:Guid = 36 + 1 + 36
        builder.Property(d => d.PairKey)
            .HasMaxLength(73)
            .IsRequired();
        builder.HasIndex(d => d.PairKey)
            .IsUnique();

        builder.Property(d => d.CreatedAt).IsRequired();
    }
}
