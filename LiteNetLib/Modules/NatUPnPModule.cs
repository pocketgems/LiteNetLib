using System;
using System.Globalization;
using System.Text;

namespace LiteNetLib
{
    public sealed class NatUPNPModule
    {
        private enum UPNPState
        {
            None,
            Discover,
            TryOpenPort,
            Failed,
            Success
        }

        private readonly NetSocket _socket;
        private NetThread _logicThread;
        private UPNPState _state;
        private static readonly string IPv4MulticastAddr = "239.255.255.250";
        private static readonly string IPv6MulticastAddr = "FF02::C";
        private const int MulticastPort = 1900;
        private int _discoverRetry;
        private const int DiscoverRetriesMax = 3;
        private const int ThreadSleepTime = 1000;

        private static readonly string[] ServiceTypes = {
            "WANIPConnection:2",
            "WANPPPConnection:2",
            "WANIPConnection:1",
            "WANPPPConnection:1"
        };

        public NatUPNPModule()
        {
            _socket = new NetSocket(OnMessageReceived);
        }

        public void RequestPortOpen(int port)
        {
            if (!_socket.Bind(0))
            {
                NetUtils.DebugWriteForce(ConsoleColor.Cyan, "[UPNP] Fail to bind");
                Fail();
                return;
            }
            if (!_socket.JoinMulticastGroup(IPv4MulticastAddr))
            {
                NetUtils.DebugWriteForce(ConsoleColor.Cyan, "[UPNP] Fail to join IPv4 multicast");
                Fail();
                return;
            }
            if (!_socket.JoinMulticastGroup(IPv6MulticastAddr))
            {
                NetUtils.DebugWriteForce(ConsoleColor.Cyan, "[UPNP] Fail to join IPv6 multicast");
                Fail();
                return;
            }
            if (_logicThread != null)
            {
                _logicThread.Stop();
            }
            _logicThread = new NetThread("NAT-PNP logic", ThreadSleepTime, Logic);
            _discoverRetry = 0;
            _state = UPNPState.Discover;
        }

        private static byte[] GetDiscoverRequest(string serviceType)
        {
            const string s = "M-SEARCH * HTTP/1.1\r\n"
                             + "HOST: 239.255.255.250:1900\r\n"
                             + "MAN: \"ssdp:discover\"\r\n"
                             + "MX: 3\r\n"
                             + "ST: urn:schemas-upnp-org:service:{0}\r\n\r\n";

            var str = string.Format(CultureInfo.InvariantCulture, s, serviceType);
            return Encoding.ASCII.GetBytes(str);
        }

        private void TryDiscover()
        {
            NetUtils.DebugWriteForce(ConsoleColor.Cyan, "[UPNP] SendDiscoverRequest");
            foreach (var serviceType in ServiceTypes)
            {
                byte[] discoverRequest = GetDiscoverRequest(serviceType);
                bool ipv4Send = _socket.SendMulticast(discoverRequest, 0, discoverRequest.Length, MulticastPort);
                bool ipv6Send = _socket.SendMulticast(discoverRequest, 0, discoverRequest.Length, MulticastPort);
                if (!ipv4Send && !ipv6Send)
                {
                    Fail();
                    return;
                }
            }
            _discoverRetry++;
            if (_discoverRetry == DiscoverRetriesMax)
            {
                Fail();
            }
        }

        private void Logic()
        {
            switch (_state)
            {
                case UPNPState.Discover:
                    TryDiscover();
                    break;
            }
        }

        private void Fail()
        {
            NetUtils.DebugWriteForce(ConsoleColor.Cyan, "[PNP] Fail.");
            _state = UPNPState.Failed;
            if (_logicThread != null)
            {
                _logicThread.Stop();
            }
            _socket.Close();
        }

        private void OnMessageReceived(byte[] data, int length, int socketErrorCode, NetEndPoint remoteendpoint)
        {
            switch (_state)
            {
                case UPNPState.Discover:
                    var response = Encoding.UTF8.GetString(data, 0, length);
                    NetUtils.DebugWriteForce(ConsoleColor.Cyan, "[UPNP] Received some data: " + response);
                    break;
            }
        }
    }
}
