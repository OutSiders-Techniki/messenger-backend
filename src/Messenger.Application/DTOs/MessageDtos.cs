using System.Text.Json.Serialization;
using System.Text.Json;

namespace Messenger.Application.DTOs;

/// <summary>Пакет AES-GCM. Время UTC с точностью до миллисекунд; ct включает 16-байтный тег.</summary>
public sealed record SendMessageRequest(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonConverter(typeof(UnixMillisecondsJsonConverter))] DateTimeOffset SentAt,
    [property: JsonPropertyName("iv"), JsonConverter(typeof(Base64UrlBytesJsonConverter))] byte[]? Iv,
    [property: JsonPropertyName("ct"), JsonConverter(typeof(Base64UrlBytesJsonConverter))] byte[]? Ciphertext);

public sealed record MessageResponse(
    Guid DialogId,
    string SenderId,
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonConverter(typeof(UnixMillisecondsJsonConverter))] DateTimeOffset SentAt,
    [property: JsonPropertyName("iv"), JsonConverter(typeof(Base64UrlBytesJsonConverter))] byte[] Iv,
    [property: JsonPropertyName("ct"), JsonConverter(typeof(Base64UrlBytesJsonConverter))] byte[] Ciphertext);

public sealed record SendMessageResponse(MessageResponse? Message, bool Created, bool Deleted, long Revision);
public sealed record MessageHistoryResponse(IReadOnlyList<MessageResponse> Items, string? NextCursor, long Revision, Guid? NextBeforeId = null);
public sealed record SyncMessagesRequest(Guid[]? Ids);
public sealed record SyncMessagesResponse(IReadOnlyList<Guid> MissingIds, long Revision);
public sealed record MessageDeletedEvent(Guid DialogId, Guid Id, bool ForEveryone, long Revision);
public sealed record MessageReceivedEvent(MessageResponse Message, long Revision);

public static class MessageLimits
{
    public const int MaxCiphertextBytes = 64 * 1024;
    public const int MaxHistoryPageSize = 100;
    public const int MaxSyncIds = 500;
}

/// <summary>Wire v1 использует JavaScript Date.now(): целое число миллисекунд Unix, не ISO-строку.</summary>
public sealed class UnixMillisecondsJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt64(out var milliseconds)
            || milliseconds < 0 || milliseconds > 253402300799999L)
            throw new JsonException("sentAt должен быть целым неотрицательным числом миллисекунд Unix.");
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.ToUnixTimeMilliseconds());
}

/// <summary>Канонический Base64url без padding; standard Base64 принимается для прежних API-клиентов.</summary>
public sealed class Base64UrlBytesJsonConverter : JsonConverter<byte[]>
{
    public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Ожидается строка Base64url.");
        var text = reader.GetString()!;
        try
        {
            var standard = text.Replace('-', '+').Replace('_', '/');
            standard = standard.PadRight((standard.Length + 3) / 4 * 4, '=');
            var bytes = Convert.FromBase64String(standard);
            if (Encode(bytes) != text && Convert.ToBase64String(bytes) != text)
                throw new FormatException();
            return bytes;
        }
        catch (FormatException)
        {
            throw new JsonException("Некорректная строка Base64url.");
        }
    }

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Encode(value));

    public static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
