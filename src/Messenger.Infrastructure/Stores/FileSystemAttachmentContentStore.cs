using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;

namespace Messenger.Infrastructure.Stores;

/// <summary>
/// Управляемое файловое хранилище зашифрованных байтов вложений. Хранит
/// только то, что прислал клиент в зашифрованном виде: ни имени файла,
/// ни типа содержимого сервер не знает и не сохраняет.
/// </summary>
public class FileSystemAttachmentContentStore : IAttachmentContentStore
{
    private readonly string _rootPath;

    public FileSystemAttachmentContentStore(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException(
                "Каталог хранилища вложений не задан.",
                nameof(rootPath));
        }

        _rootPath = Path.GetFullPath(rootPath);
    }

    public async Task<StoredContent> SaveAsync(
        Stream content,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_rootPath);

        // Имя файла сервер генерирует сам: имя, присланное клиентом,
        // не используется и не сохраняется.
        var storageKey = $"{Guid.NewGuid():N}.bin";
        var path = Path.Combine(_rootPath, storageKey);

        long size = 0;

        try
        {
            await using var target = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);

            var buffer = new byte[81920];
            int read;

            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                size += read;

                if (size > maxBytes)
                {
                    // Лимит превышен: файл удаляется, наружу уходит понятная
                    // ошибка с указанием максимума.
                    throw new AttachmentTooLargeException(maxBytes);
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch
        {
            TryDelete(path);

            throw;
        }

        return new StoredContent(storageKey, size);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storageKey))
        {
            return Task.CompletedTask;
        }

        ValidateKey(storageKey);

        var path = Path.Combine(_rootPath, storageKey);
        // Не скрываем ошибку: транзакционная очередь сохранит задание для следующей попытки.
        File.Delete(path);

        return Task.CompletedTask;
    }

    public Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(storageKey))
        {
            throw new InvalidOperationException("Некорректный ключ файлового хранилища.");
        }

        ValidateKey(storageKey);

        var path = Path.Combine(_rootPath, storageKey);
        if (new FileInfo(path).LinkTarget is not null)
            throw new InvalidOperationException("Символические ссылки в хранилище не поддерживаются.");

        try
        {
            Stream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);

            return Task.FromResult<Stream?>(stream);
        }
        catch (FileNotFoundException)
        {
            // Файл мог быть удалён уборкой после проверки в сервисе.
            return Task.FromResult<Stream?>(null);
        }
    }

    /// <summary>Ключи выдаёт только это хранилище. Произвольный путь из БД не является разрешением на доступ к файлу.</summary>
    private void ValidateKey(string storageKey)
    {
        if (storageKey.Length != 36 || !storageKey.EndsWith(".bin", StringComparison.Ordinal)
            || !Guid.TryParseExact(storageKey[..32], "N", out _))
            throw new InvalidOperationException("Некорректный ключ файлового хранилища.");

        if (new FileInfo(Path.Combine(_rootPath, storageKey)).LinkTarget is not null)
            throw new InvalidOperationException("Символические ссылки в хранилище не поддерживаются.");
    }

    /// <summary>
    /// Удаляет файл, не бросая исключений: уборка не должна ломать
    /// уже сформированный ответ клиенту.
    /// </summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Оставляем файл: удаление повторит фоновая уборка.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
