using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Db.Configurations;

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.HasKey(a => a.Id);

        builder.HasOne(a => a.Message)
            .WithMany(m => m.Attachments)
            .HasForeignKey(a => a.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(a => a.StorageKey)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(a => a.Size).IsRequired();
        builder.Property(a => a.EncryptedMetadata).IsRequired();
        builder.Property(a => a.UploadedAt).IsRequired();

        // Максимум 25 МБ на вложение
        builder.ToTable(t => t.HasCheckConstraint("ck_attachments_size", "size > 0 AND size <= 26214400"));
    }
}
