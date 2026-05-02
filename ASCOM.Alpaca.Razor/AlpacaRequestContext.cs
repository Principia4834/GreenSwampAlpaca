using System.Threading;

namespace ASCOM.Alpaca
{
    /// <summary>
    /// Carries the Alpaca ClientID across the interface boundary into the device driver.
    /// Set by AlpacaController before invoking Connect() or Disconnect() so that the
    /// driver can forward a stable per-client key to the mount's counted connection store.
    /// </summary>
    public static class AlpacaRequestContext
    {
        /// <summary>
        /// The Alpaca ClientID from the current REST request.
        /// Defaults to 0 (legacy anonymous slot) when not explicitly set.
        /// </summary>
        public static readonly AsyncLocal<uint> ClientId = new();
    }
}
