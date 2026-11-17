using System.Text.Json.Serialization;

namespace Messenger.Application.DTOs;

/// <summary>Пакет AES-GCM. Время UTC с точностью до миллисекунд; ct включает 16-байтный тег.</summary>
public sealed record SendMessageRequest(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("id")] Guid Id,
    DateTimeOffset SentAt,
    [property: JsonPropertyName("iv")] byte[]? Iv,
    [property: JsonPropertyName("ct")] byte[]? Ciphertext);

public sealed record MessageResponse(
    Guid DialogId,
    Guid SenderId,
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("id")] Guid Id,
    DateTimeOffset SentAt,
    [property: JsonPropertyName("iv")] byte[] Iv,
    [property: JsonPropertyName("ct")] byte[] Ciphertext);


public static class MessageLimits
{
    public const int MaxCiphertextBytes = 64 * 1024;
}
