using Messenger.Application.Exceptions;
using Messenger.Application.Interfaces;
using Messenger.Domain.Entities;
using Messenger.Infrastructure.Db;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Messenger.Infrastructure.Repositories;

public class UserRepository(AppDbContext context) : IUserRepository
{
    public Task<User?> GetByPublicIdAsync(string publicId, CancellationToken cancellationToken = default)
    {
        return context.Users
            .SingleOrDefaultAsync(u => u.PublicId == publicId, cancellationToken);
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return context.Users
            .SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        context.Users.Add(user);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Убираем сущность из трекера, иначе следующий SaveChanges
            // попытается вставить её снова и упадёт на том же конфликте.
            context.Entry(user).State = EntityState.Detached;

            throw new DuplicatePublicIdException(user.PublicId);
        }
    }
}
