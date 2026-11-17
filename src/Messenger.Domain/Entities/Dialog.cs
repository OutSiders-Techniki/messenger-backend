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
    public long MessageRevision { get; set; }

    public ICollection<DialogParticipant> Participants { get; set; } = new List<DialogParticipant>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();

    /// <summary>
    /// Ключ пары участников в отсортированном порядке: диалог между A и B
    /// и между B и A — это один и тот же диалог.
    /// </summary>
    public static string PairKeyFor(Guid firstUserId, Guid secondUserId)
    {
        var (min, max) = firstUserId.CompareTo(secondUserId) <= 0
            ? (firstUserId, secondUserId)
            : (secondUserId, firstUserId);

        return $"{min:D}:{max:D}";
    }
}
