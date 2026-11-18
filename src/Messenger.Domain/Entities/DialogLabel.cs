namespace Messenger.Domain.Entities;

/// <summary>
/// Подпись контакта: зашифрованный ключом диалога пакет, видимый только
/// владельцу. Собеседник подпись не получает, сервер её прочитать не может.
/// </summary>
public sealed class DialogLabel
{
    public Guid DialogId { get; set; }
    public Guid OwnerId { get; set; }
    public int Version { get; set; }
    public required byte[] Iv { get; set; }
    public required byte[] Ciphertext { get; set; }
    public DateTime UpdatedAt { get; set; }
}
