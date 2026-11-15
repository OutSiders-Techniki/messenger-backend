namespace Messenger.Application.Options;

public class SessionCookieOptions
{
    public const string SectionName = "Session";

    public string CookieName { get; set; } = "messenger_session";

    /// <summary>Secure — cookie передаётся только по HTTPS.</summary>
    public bool Secure { get; set; } = true;

    /// <summary>Strict / Lax / None.</summary>
    public string SameSite { get; set; } = "Strict";

    public int LifetimeHours { get; set; } = 168;
}
