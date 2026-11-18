namespace Messenger.Application.Interfaces;

/// <summary>
/// Сохранённое содержимое вложения: ключ в хранилище и размер в байтах.
/// </summary>
/// <param name="StorageKey">Ключ файла в управляемом хранилище.</param>
/// <param name="Size">Размер сохранённых байт.</param>
public sealed record StoredContent(string StorageKey, long Size);

/// <summary>
/// Управляемое файловое хранилище зашифрованных байтов вложений. Сервер
/// кладёт туда только то, что прислал клиент в зашифрованном виде:
/// ни имени файла, ни типа содержимого.
/// </summary>
public interface IAttachmentContentStore
{
    /// <summary>
    /// Сохраняет поток в хранилище, не сохраняя больше <paramref name="maxBytes"/>.
    /// При превышении лимита содержимое не сохраняется и выбрасывается
    /// <see cref="Exceptions.AttachmentTooLargeException"/>.
    /// </summary>
    Task<StoredContent> SaveAsync(
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken = default);

    /// <summary>Удаляет сохранённое содержимое по ключу.</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Открывает сохранённое содержимое на чтение по ключу. Возвращает
    /// размер в байтах; ключ не существует — <c>null</c>.
    /// </summary>
    Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default);
}