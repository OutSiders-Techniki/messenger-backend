namespace Messenger.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public required string PublicId { get; set; }
    public required string PasswordHash { get; set; }
    public DateTime CreatedAt { get; set; }
}
