namespace Messenger.Domain.Entities;

/// <summary>
/// Одна строка = одна копия сообщения, принадлежащая конкретному участнику (OwnerId).
/// У сообщения в диалоге 1-на-1 две строки: для отправителя и для получателя.
/// «Удалить у себя» — физическое удаление своей строки, «у обоих» — обеих.
/// </summary>
public class Message
{
    public Guid Id { get; set; }
    public Guid DialogId { get; set; }
    public Guid SenderId { get; set; }
    public Guid OwnerId { get; set; }

    /// <summary>Клиентский id сообщения — дедупликация и идемпотентная отправка.</summary>
    public Guid ClientMessageId { get; set; }

    /// <summary>Версия криптопакета {v, id, sentAt, iv, ct}.</summary>
    public int Version { get; set; }
    public DateTime SentAt { get; set; }

    /// <summary>IV AES-256-GCM, ровно 12 байт.</summary>
    public required byte[] Iv { get; set; }
    public required byte[] Ciphertext { get; set; }

    public Dialog Dialog { get; set; } = null!;
    public User Sender { get; set; } = null!;
    public User Owner { get; set; } = null!;
    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
}
