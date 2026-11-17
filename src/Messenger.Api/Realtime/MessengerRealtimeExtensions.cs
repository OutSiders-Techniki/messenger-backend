using Messenger.Api.Hubs;
using Messenger.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Api.Realtime;

public static class MessengerRealtimeExtensions
{
    public static IServiceCollection AddMessengerRealtime(this IServiceCollection services)
    {
        services.AddSingleton<MessageConnections>();
        services.AddScoped<SessionHubFilter>();
        services.AddScoped<IMessageEvents, SignalRMessageEvents>();
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false;
            options.MaximumReceiveMessageSize = 4096;
            options.AddFilter<SessionHubFilter>();
        });
        return services;
    }

    public static IEndpointRouteBuilder MapMessengerRealtime(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHub<MessagesHub>("/hubs/messages").RequireAuthorization();
        return endpoints;
    }
}
