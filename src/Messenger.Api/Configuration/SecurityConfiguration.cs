namespace Messenger.Api.Configuration;

public static class SecurityConfiguration
{
    public static IServiceCollection AddMessengerSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));
        return services;
    }
}
