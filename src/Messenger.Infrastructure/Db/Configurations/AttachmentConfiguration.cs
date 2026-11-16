using Messenger.Application;
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

        builder.Property(a => a.ClientAttachmentId).IsRequired();

        // Клиентский идентификатор уникален в пределах копии сообщения.
        builder.HasIndex(a => new { a.MessageId, a.ClientAttachmentId })
            .IsUnique();

        // Максимум 25 МБ на вложение. Лимит задан один раз в коде.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_attachments_size",
            $"size > 0 AND size <= {MediaLimits.MaxAttachmentSizeBytes}"));

        // Идентификатор задаёт клиент: пустой быть не может.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_attachments_client_id",
            "client_attachment_id <> '00000000-0000-0000-0000-000000000000'"));
    }
}