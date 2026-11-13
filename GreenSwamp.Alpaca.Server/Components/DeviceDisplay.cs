namespace GreenSwamp.Alpaca.Server.Components
{
    /// <summary>Shared display helpers for telescope device settings.</summary>
    public static class DeviceDisplay
    {
        public static string FriendlyAlignment(string? mode) => mode?.ToLowerInvariant() switch
        {
            "germanpolar" => "German Equatorial (GEM)",
            "polar" => "Polar / Fork",
            "altaz" => "Alt-Azimuth",
            _ => mode ?? "Unknown"
        };
    }
}
