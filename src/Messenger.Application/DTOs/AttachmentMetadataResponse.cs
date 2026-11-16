namespace Messenger.Application.DTOs;

/// <summary>
/// Метаданные зашифрованного вложения. Исходное имя файла и тип
/// содержимого серверу неизвестны: клиент зашифровал их сам, поэтому
/// здесь только то, что сервер знает наверняка, плюс зашифрованные
/// клиентом метаданные.
/// </summary>
/// <param name="ClientMessageId">
/// Идентификатор сообщения, заданный клиентом (поле id в пакете
/// {v, id, sentAt, iv, ct}). Одинаков у копий сообщения у обоих
/// участников, поэтому по нему обе стороны собирают AAD шифрования
/// метаданных одинаково.
/// </param>
/// <param name="ClientAttachmentId">
/// Идентификатор вложения, заданный клиентом. Уникален в пределах
/// копии сообщения; одинаков у копий сообщения у обоих участников.
/// </param>
/// <param name="Size">Размер зашифрованных байт в байтах.</param>
/// <param name="UploadedAt">Момент загрузки.</param>
/// <param name="EncryptedMetadata">
/// Зашифрованные клиентом метаданные (имя файла, тип содержимого).
/// Формат пакета — как у сообщения: {v, id, sentAt, iv, ct},
/// где ct — AES-256-GCM с проверяемым тегом. Передаётся как base64,
/// расшифровывает только клиент.
/// </param>
public record AttachmentMetadataResponse(
    Guid ClientMessageId,
    Guid ClientAttachmentId,
    long Size,
    DateTime UploadedAt,
    byte[] EncryptedMetadata);