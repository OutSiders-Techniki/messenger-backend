using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Db.Configurations;

public sealed class DialogLabelConfiguration : IEntityTypeConfiguration<DialogLabel>
{
    public void Configure(EntityTypeBuilder<DialogLabel> builder)
    {
        builder.HasKey(x => new { x.DialogId, x.OwnerId });
        builder.Property(x => x.Version).IsRequired();
        builder.Property(x => x.Iv).IsRequired();
        builder.Property(x => x.Ciphertext).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.HasOne<Dialog>().WithMany(d => d.Labels).HasForeignKey(x => x.DialogId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
    }
}
