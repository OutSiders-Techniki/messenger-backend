namespace Messenger.Domain.Entities;

/// <summary>
/// Зашифрованное вложение. Сервер хранит только путь к шифрованным байтам,
/// размер и зашифрованные метаданные; исходное имя файла недопустимо.
/// </summary>
public class Attachment
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }

    /// <summary>
    /// Идентификатор вложения, заданный клиентом. Одинаков у копий
    /// сообщения у обоих участников, поэтому по нему обе стороны
    /// собирают AAD шифрования метаданных одинаково.
    /// </summary>
    public Guid ClientAttachmentId { get; set; }

    /// <summary>Путь/ключ зашифрованного файла в управляемом хранилище.</summary>
    public required string StorageKey { get; set; }

    /// <summary>Размер в байтах (открыто — нужен для лимита 25 МБ).</summary>
    public long Size { get; set; }

    /// <summary>Зашифрованные клиентом метаданные (имя файла, тип).</summary>
    public required byte[] EncryptedMetadata { get; set; }

    public DateTime UploadedAt { get; set; }

    public Message Message { get; set; } = null!;
}