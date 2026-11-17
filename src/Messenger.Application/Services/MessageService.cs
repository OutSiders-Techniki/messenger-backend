using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;

namespace Messenger.Application.Services;

public sealed class MessageService(IMessageRepository messages)
{
    public async Task<SendMessageResponse> SendAsync(Guid userId, Guid dialogId, SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        var result = await messages.SendAsync(userId, dialogId, request, cancellationToken);
        return result.Response;
    }

    public static void Validate(SendMessageRequest request)
    {
        if (request.Version != 1)
            throw new ValidationException("Поддерживается только версия криптопакета 1.");
        if (request.Id == Guid.Empty)
            throw new ValidationException("Идентификатор сообщения не может быть пустым.");
        if (request.SentAt == default || request.SentAt.Offset != TimeSpan.Zero || request.SentAt.Ticks % TimeSpan.TicksPerMillisecond != 0)
            throw new ValidationException("sentAt должен содержать время UTC с точностью до миллисекунд.");
        if (request.Iv is not { Length: 12 })
            throw new ValidationException("IV AES-GCM должен содержать ровно 12 байт.");
        if (request.Ciphertext is null || request.Ciphertext.Length is < 16 or > MessageLimits.MaxCiphertextBytes)
            throw new ValidationException("Шифротекст с тегом должен содержать от 16 до 65536 байт.");
    }
}
