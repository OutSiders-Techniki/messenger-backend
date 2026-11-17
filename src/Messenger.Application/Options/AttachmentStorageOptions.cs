namespace Messenger.Application.Options;

/// <summary>
/// Настройки управляемого файлового хранилища зашифрованных вложений.
/// Сервер кладёт туда только зашифрованные клиентом байты.
/// </summary>
public class AttachmentStorageOptions
{
    /// <summary>Раздел конфигурации.</summary>
    public const string SectionName = "AttachmentStorage";

    /// <summary>Корневой каталог хранилища на диске.</summary>
    public string RootPath { get; set; } = "storage/attachments";
}