using Messenger.Application.Interfaces;
using Messenger.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IPublicIdGenerator, PublicIdGenerator>();

        return services;
    }
}
