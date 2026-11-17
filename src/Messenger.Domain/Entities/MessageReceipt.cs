namespace Messenger.Domain.Entities;

/// <summary>Минимальный отпечаток принятого пакета. Не хранит шифротекст; не позволяет повтору восстановить удалённые копии.</summary>
public sealed class MessageReceipt
{
    public Guid DialogId { get; set; }
    public Guid ClientMessageId { get; set; }
    public Guid SenderId { get; set; }
    public required byte[] Fingerprint { get; set; }
    public long Revision { get; set; }
}
