namespace Messenger.Application.Exceptions;

/// <summary>Публичный ID уже занят другим пользователем.</summary>
public class DuplicatePublicIdException(string publicId)
    : Exception($"Публичный ID «{publicId}» уже занят.")
{
    public string PublicId { get; } = publicId;
}
