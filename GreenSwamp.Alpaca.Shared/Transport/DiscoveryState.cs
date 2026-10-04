using System.Net;

namespace GreenSwamp.Alpaca.Shared.Transport
{
    public class DiscoveryState(IPAddress interfaceAddress, CancellationTokenSource cts)
    {
        public IPAddress InterfaceAddress { get; } = interfaceAddress;

        public CancellationTokenSource Cts { get; } = cts;
    }
}
