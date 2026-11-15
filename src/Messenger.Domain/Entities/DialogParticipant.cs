namespace Messenger.Domain.Entities;

public class DialogParticipant
{
    public Guid DialogId { get; set; }
    public Guid UserId { get; set; }
    public DateTime JoinedAt { get; set; }

    public Dialog Dialog { get; set; } = null!;
    public User User { get; set; } = null!;
}
