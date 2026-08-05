using System.Net.NetworkInformation;
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
        public const uint DefaultClientId = 0;
        public const string DefaultClientIpAddress = "0.0.0.0";
        public const uint DefaultClientPort = 0;

        /// <summary>
        /// The Alpaca ClientID from the current REST request.
        /// Defaults to 0 (legacy anonymous slot) when not explicitly set.
        /// </summary>
        public static readonly AsyncLocal<uint> ClientId = new();

        /// <summary>
        /// The IP address of the client making the current REST request.
        /// </summary>
        public static readonly AsyncLocal<string?> ClientIpAddress = new();

        /// <summary>
        /// The port number of the client making the current REST request.
        /// Defaults to 0 when not explicitly set.
        /// </summary>
        public static readonly AsyncLocal<uint> ClientPort = new();

        public static uint CurrentClientId => ClientId.Value;
        public static string CurrentClientIpAddress => string.IsNullOrWhiteSpace(ClientIpAddress.Value) ? DefaultClientIpAddress : ClientIpAddress.Value!;
        public static uint CurrentClientPort => ClientPort.Value;
        public static string CurrentClientRefId => $"{CurrentClientId}:{CurrentClientIpAddress}:{CurrentClientPort}";

        /// <summary>
        /// Begins a scoped ClientID assignment for the current async flow and restores
        /// the prior value when the returned scope is disposed.
        /// </summary>
        public static IDisposable BeginClientRefIdScope(uint clientId, string? clientIpAddress = null, uint clientPort = 0)
            => new ClientRefIdScope(clientId, clientIpAddress, clientPort);

        private sealed class ClientRefIdScope : IDisposable
        {
            private readonly uint _priorClientId;
            private readonly string? _priorIpAddress;
            private readonly uint _priorPort;

            public ClientRefIdScope(uint clientId, string? clientIpAddress, uint clientPort)
            {
                _priorClientId = ClientId.Value;
                _priorIpAddress = ClientIpAddress.Value;
                _priorPort = ClientPort.Value;

                ClientId.Value = clientId;
                ClientIpAddress.Value = string.IsNullOrWhiteSpace(clientIpAddress) ? DefaultClientIpAddress : clientIpAddress;
                ClientPort.Value = clientPort;
            }

            public void Dispose()
            {
                ClientId.Value = _priorClientId;
                ClientIpAddress.Value = _priorIpAddress;
                ClientPort.Value = _priorPort;
            }
        }
    }
}
