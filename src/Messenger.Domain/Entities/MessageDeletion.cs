namespace Messenger.Domain.Entities;

/// <summary>
/// Отметка об удалении копии сообщения. Осталась и после физического удаления
/// строк: поздняя отправка с тем же идентификатором не воскресит удалённую копию.
/// </summary>
public sealed class MessageDeletion
{
    public Guid DialogId { get; set; }
    public Guid ClientMessageId { get; set; }
    public Guid OwnerId { get; set; }
    public DateTime DeletedAt { get; set; }
}
