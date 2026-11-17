namespace Messenger.Application;

/// <summary>
/// Ограничения медиа из технического задания: максимальный размер одного
/// вложения — 25 МБ.
/// </summary>
public static class MediaLimits
{
    /// <summary>
    /// Максимальный размер одного вложения в байтах (25 МБ — десятичных,
    /// как в техническом задании).
    /// </summary>
    public const int MaxAttachmentSizeBytes = 25_000_000;

    /// <summary>
    /// Максимальный размер зашифрованных метаданных вложения (имя файла и
    /// тип содержимого) в байтах: ограничивает мусор в базе.
    /// </summary>
    public const int MaxAttachmentMetadataBytes = 16_384;
}