using Messenger.Application.DTOs;
using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Domain.Entities;

namespace Messenger.Application.Services;

public class RegistrationService(
    IUserRepository users,
    IPublicIdGenerator publicIdGenerator,
    IPasswordHasher passwordHasher)
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;

    private const int MaxIdAttempts = 5;

    public async Task<RegisterResponse> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request.Password);

        var passwordHash = passwordHasher.Hash(request.Password);

        for (var attempt = 0; attempt < MaxIdAttempts; attempt++)
        {
            var publicId = publicIdGenerator.Generate();

            var user = new User
            {
                Id = Guid.NewGuid(),
                PublicId = publicId,
                PasswordHash = passwordHash,
                CreatedAt = DateTime.UtcNow,
            };

            try
            {
                await users.AddAsync(user, cancellationToken);
                return new RegisterResponse(publicId);
            }
            catch (DuplicatePublicIdException)
            {
                // Публичный ID занят — пробуем сгенерировать следующий.
            }
        }

        throw new InvalidOperationException(
            $"Не удалось сгенерировать уникальный публичный ID за {MaxIdAttempts} попыток.");
    }

    private static void Validate(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ValidationException("Пароль обязателен.");
        }

        switch (password.Length)
        {
            case < MinPasswordLength:
                throw new ValidationException(
                    $"Пароль должен содержать не менее {MinPasswordLength} символов.");
            case > MaxPasswordLength:
                throw new ValidationException(
                    $"Пароль должен содержать не более {MaxPasswordLength} символов.");
        }
    }
}
