namespace Messenger.Domain.Entities;

public class Dialog
{
    public Guid Id { get; set; }

    /// <summary>
    /// Нормализованный ключ пары участников "minUserId:maxUserId".
    /// Защищает от создания дубликата диалога 1-на-1 на уровне БД.
    /// </summary>
    public required string PairKey { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<DialogParticipant> Participants { get; set; } = new List<DialogParticipant>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
