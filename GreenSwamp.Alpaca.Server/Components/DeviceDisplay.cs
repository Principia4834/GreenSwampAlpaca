using GreenSwamp.Alpaca.Server.Localization;
using Microsoft.Extensions.Localization;

namespace GreenSwamp.Alpaca.Server.Components
{
    /// <summary>Shared display helpers for telescope device settings.</summary>
    public static class DeviceDisplay
    {
        public static string FriendlyAlignment(IStringLocalizer<SharedResource> loc, string? mode) => mode?.ToLowerInvariant() switch
        {
            "germanpolar" => loc["Alignment_Gem"],
            "polar" => loc["Alignment_Polar"],
            "altaz" => loc["Alignment_AltAz"],
            null or "unknown" => loc["Common_Unknown"],
            _ => mode
        };
    }
}
