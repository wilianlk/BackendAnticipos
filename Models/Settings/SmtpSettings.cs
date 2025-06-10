namespace BackendAnticipos.Models.Settings
{
    public record SmtpSettings
    {
        public string Host { get; init; } = default!;
        public int Port { get; init; }
        public string User { get; init; } = default!;
        public string Pass { get; init; } = default!;
    }
}
