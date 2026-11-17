using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Db.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasKey(m => m.Id);

        builder.HasOne(m => m.Dialog)
            .WithMany(d => d.Messages)
            .HasForeignKey(m => m.DialogId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: удаление пользователя не должно молча сносить историю
        builder.HasOne(m => m.Sender)
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Owner)
            .WithMany()
            .HasForeignKey(m => m.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(m => m.Version).IsRequired();
        builder.Property(m => m.SentAt).IsRequired();
        builder.Property(m => m.Iv).IsRequired();
        builder.Property(m => m.Ciphertext).IsRequired();

        // Идемпотентная отправка: у одного владельца не бывает двух копий с одним клиентским id
        builder.HasIndex(m => new { m.DialogId, m.OwnerId, m.ClientMessageId })
            .IsUnique();

        // Постраничная загрузка истории
        builder.HasIndex(m => new { m.DialogId, m.OwnerId, m.SentAt });
        builder.HasIndex(m => new { m.DialogId, m.OwnerId, m.SentAt, m.ClientMessageId });

        // IV AES-256-GCM — ровно 12 байт
        builder.ToTable(t => t.HasCheckConstraint("ck_messages_iv_length", "length(iv) = 12"));
    }
}

