using Messenger.Application.Interfaces;
using Messenger.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IPublicIdGenerator, PublicIdGenerator>();
        services.AddSingleton<ISessionTokenGenerator, SessionTokenGenerator>();
        services.AddScoped<RegistrationService>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserSearchService>();
        services.AddScoped<DialogService>();
        services.AddScoped<AttachmentService>();
        services.AddScoped<MessageService>();
        services.AddScoped<MessageDeletionService>();

        return services;
    }
}
