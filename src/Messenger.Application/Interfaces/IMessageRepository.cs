using Messenger.Application.DTOs;

namespace Messenger.Application.Interfaces;

public sealed record MessageSendResult(SendMessageResponse Response, IReadOnlyList<Guid> Recipients);

public interface IMessageRepository
{
    Task<MessageSendResult> SendAsync(Guid userId, Guid dialogId, SendMessageRequest request, CancellationToken cancellationToken = default);
}
