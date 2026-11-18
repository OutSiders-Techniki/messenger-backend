using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Messenger.Application.DTOs;

/// <summary>Запрос на сохранение подписи контакта: зашифрованный пакет.</summary>
public sealed record PutLabelRequest(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("iv"), JsonConverter(typeof(Base64UrlBytesJsonConverter))] byte[]? Iv,
    [property: JsonPropertyName("ct"), JsonConverter(typeof(Base64UrlBytesJsonConverter))] byte[]? Ciphertext);

/// <summary>
/// Подпись контакта для её владельца: шифротекст и время сохранения.
/// Отдаётся только владельцу — собеседник получает label = null.
/// </summary>
public sealed record DialogLabelView(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("iv"), JsonConverter(typeof(Base64UrlBytesJsonConverter))] byte[] Iv,
    [property: JsonPropertyName("ct"), JsonConverter(typeof(Base64UrlBytesJsonConverter))] byte[] Ciphertext,
    DateTime UpdatedAt);

/// <summary>Запрос на создание диалога: публичный ID собеседника.</summary>
public class CreateDialogRequest
{
    [Required(ErrorMessage = "Укажите публичный ID собеседника.")]
    [MaxLength(32, ErrorMessage = "Публичный ID не может быть длиннее 32 символов.")]
    public string? PublicId { get; init; }
}

/// <summary>Созданный или уже существующий диалог.</summary>
public record DialogResponse(Guid Id, string CompanionPublicId, DateTime CreatedAt);

/// <summary>
/// Результат создания диалога. <see cref="Created"/> = false, если диалог
/// между этой парой уже был и новый не создавался.
/// </summary>
public record DialogCreationResult(DialogResponse Dialog, bool Created);
