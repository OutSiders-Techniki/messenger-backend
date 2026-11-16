namespace Messenger.Application;

/// <summary>
/// Ограничения медиа из технического задания: максимальный размер одного
/// вложения — 25 МБ.
/// </summary>
public static class MediaLimits
{
    /// <summary>Максимальный размер одного вложения в байтах (25 МБ).</summary>
    public const int MaxAttachmentSizeBytes = 26_214_400;
}