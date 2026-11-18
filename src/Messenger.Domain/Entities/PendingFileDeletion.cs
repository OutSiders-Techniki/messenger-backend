namespace Messenger.Domain.Entities;

/// <summary>Транзакционная очередь удаления осиротевших файлов; остаётся после сбоя процесса.</summary>
public sealed class PendingFileDeletion
{
    public required string StorageKey { get; set; }
}
