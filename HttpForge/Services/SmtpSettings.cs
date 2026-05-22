namespace HttpForge.Services;

public class SmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public bool EnableSsl { get; set; } = true;
    public string From { get; set; } = "HttpForge <noreply@httpforge.local>";
    public string? AppUrl { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
