namespace Messenger.Application.DTOs;

public record RegisterRequest(string Password);

public record RegisterResponse(string PublicId);
