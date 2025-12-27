using System.Net;

namespace GreenSwamp.Alpaca.Shared.Transport
{
    public class DiscoveryState
    {
        public DiscoveryState(IPAddress interfaceAddress, CancellationTokenSource cts)
        {
            InterfaceAddress = interfaceAddress;
            Cts = cts;
        }

        public IPAddress InterfaceAddress { get; }

        public CancellationTokenSource Cts { get; }
    }
}
