using Messenger.Application.DTOs;

namespace Messenger.Application.Interfaces;

/// <param name="Response">Ответ клиенту: что произошло с отправкой.</param>
/// <param name="Recipients">Участники, у которых появилась копия сообщения.</param>
/// <param name="Packet">Пакет для realtime-события; null, если копий не появилось.</param>
public sealed record MessageSendResult(
    SendMessageResponse Response,
    IReadOnlyList<Guid> Recipients,
    MessageResponse? Packet = null);
public sealed record MessageDeleteResult(bool Changed, long Revision, IReadOnlyList<Guid> Recipients);

public interface IMessageRepository
{
    Task<MessageSendResult> SendAsync(Guid userId, Guid dialogId, SendMessageRequest request, CancellationToken cancellationToken = default);
    Task<MessageHistoryResponse> GetHistoryAsync(Guid userId, Guid dialogId, int limit, string? before, CancellationToken cancellationToken = default);
    Task<SyncMessagesResponse> SyncAsync(Guid userId, Guid dialogId, Guid[] ids, CancellationToken cancellationToken = default);
    Task<MessageDeleteResult> DeleteAsync(Guid userId, Guid dialogId, Guid messageId, bool forEveryone, CancellationToken cancellationToken = default);
}
