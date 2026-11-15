using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Db.Configurations;

public class DialogParticipantConfiguration : IEntityTypeConfiguration<DialogParticipant>
{
    public void Configure(EntityTypeBuilder<DialogParticipant> builder)
    {
        builder.HasKey(p => new { p.DialogId, p.UserId });

        builder.HasOne(p => p.Dialog)
            .WithMany(d => d.Participants)
            .HasForeignKey(p => p.DialogId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.UserId);

        builder.Property(p => p.JoinedAt).IsRequired();
    }
}
