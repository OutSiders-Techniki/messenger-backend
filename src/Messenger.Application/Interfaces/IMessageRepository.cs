using Messenger.Application.DTOs;

namespace Messenger.Application.Interfaces;

public sealed record MessageSendResult(SendMessageResponse Response, IReadOnlyList<Guid> Recipients);
public sealed record MessageDeleteResult(bool Changed, long Revision, IReadOnlyList<Guid> Recipients);

public interface IMessageRepository
{
    Task<MessageSendResult> SendAsync(Guid userId, Guid dialogId, SendMessageRequest request, CancellationToken cancellationToken = default);
    Task<MessageHistoryResponse> GetHistoryAsync(Guid userId, Guid dialogId, int limit, string? before, CancellationToken cancellationToken = default);
    Task<MessageDeleteResult> DeleteAsync(Guid userId, Guid dialogId, Guid messageId, CancellationToken cancellationToken = default);
}
