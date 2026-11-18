using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Db.Configurations;

public sealed class MessageDeletionConfiguration : IEntityTypeConfiguration<MessageDeletion>
{
    public void Configure(EntityTypeBuilder<MessageDeletion> builder)
    {
        builder.HasKey(x => new { x.DialogId, x.ClientMessageId, x.OwnerId });
        builder.Property(x => x.DeletedAt).IsRequired();
        builder.HasOne<Dialog>().WithMany().HasForeignKey(x => x.DialogId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.OwnerId);
    }
}
