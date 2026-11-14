using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace GreenSwamp.Alpaca.Server.Helpers
{
    /// <summary>
    /// Backend (serial protocol, JSON, Alpaca REST, logging) is always culture invariant.
    /// Only Blazor circuits opt in to a regional format.
    /// </summary>
    public static class BackendCulture
    {
        /// <summary>Makes the invariant culture the default for the current thread and every thread created later.</summary>
        public static void ApplyInvariantDefaults()
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        }

        /// <summary>
        /// Routes that must ignore the browser culture cookie and Accept-Language header:
        /// the Alpaca REST API, management API, Swagger and the SignalR hubs.
        /// </summary>
        public static bool IsInvariantPath(PathString path) =>
            path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/management", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/charthub", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/telescopestatehub", StringComparison.OrdinalIgnoreCase);
    }
}
