using Messenger.Application.Interfaces;
using Messenger.Application.Options;
using Messenger.Infrastructure.Db;
using Messenger.Infrastructure.Repositories;
using Messenger.Infrastructure.Services;
using Messenger.Infrastructure.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options
                .UseNpgsql(
                    configuration.GetConnectionString("DefaultConnection"),
                    npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IDialogRepository, DialogRepository>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddScoped<ISessionStore, SessionRepository>();
        services.AddSingleton<IPasswordHasher, Argon2IdPasswordHasher>();

        services.AddSingleton<IAttachmentContentStore>(provider =>
        {
            var finalConfiguration = provider.GetRequiredService<IConfiguration>();
            var storageRoot = finalConfiguration[$"{AttachmentStorageOptions.SectionName}:RootPath"];

            if (string.IsNullOrWhiteSpace(storageRoot))
            {
                storageRoot = new AttachmentStorageOptions().RootPath;
            }

            return new FileSystemAttachmentContentStore(storageRoot);
        });

        return services;
    }
}
