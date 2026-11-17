using Messenger.Application.DTOs;

namespace Messenger.Application.Interfaces;

public interface IMessageEvents
{
    Task ReceivedAsync(IReadOnlyList<Guid> userIds, MessageReceivedEvent message, CancellationToken cancellationToken = default);
    Task DeletedAsync(IReadOnlyList<Guid> userIds, MessageDeletedEvent message, CancellationToken cancellationToken = default);
}
