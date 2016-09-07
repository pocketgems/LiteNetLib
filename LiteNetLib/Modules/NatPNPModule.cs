using System;
using LiteNetLib.Utils;

namespace LiteNetLib
{
    public sealed class NatPNPModule
    {
        private enum PNPState
        {
            None,
            Discover,
            TryOpenPort,
            Failed,
            Success
        }

        private readonly NetSocket _socket;
        private readonly NetDataWriter _writer;
        private NetThread _logicThread;
        private PNPState _state;
        private NetEndPoint _gatewayEndPoint;
        private NetEndPoint _routerEndPoint;

        private const byte PnpVersion = 0;
        private const byte OperationExternalAddressRequest = 0;
        private const int ClientPort = 5350;
        private const int ServerPort = 5351;
        public const byte ServerNoop = 128;

        public NatPNPModule()
        {
            _socket = new NetSocket(OnMessageReceived);
            _writer = new NetDataWriter();
        }

        public void RequestPortOpen(int port)
        {
            if (_logicThread != null)
            {
                _logicThread.Stop();
            }
            _logicThread = new NetThread("NAT-PNP logic", 15, Logic);
            _state = PNPState.Discover;

            _gatewayEndPoint = new NetEndPoint("gateway", ServerPort);
        }

        private void Logic()
        {
            switch (_state)
            {
                case PNPState.Discover:
                    NetUtils.DebugWriteForce(ConsoleColor.Cyan, "[PNP] SendDiscoverRequest to " + _gatewayEndPoint);
                    _writer.Reset();
                    _writer.Put(PnpVersion);
                    _writer.Put(OperationExternalAddressRequest);
                    int errorCode = 0;
                    int size = _socket.SendTo(_writer.Data, 0, _writer.Length, _gatewayEndPoint,
                        ref errorCode);
                    if (size <= 0)
                    {
                        Fail();
                    }
                    break;
            }
        }

        private void Fail()
        {
            _state = PNPState.Failed;
            _logicThread.Stop();
        }


        private static short NetworkToHostOrder(short host)
        {
#if BIGENDIAN
            return host;
#else
            return (short)(((host & 0xFF) << 8) | ((host >> 8) & 0xFF));
#endif
        }

        private void OnMessageReceived(byte[] data, int length, int socketErrorCode, NetEndPoint remoteendpoint)
        {
            switch (_state)
            {
                case PNPState.Discover:
                    NetUtils.DebugWriteForce(ConsoleColor.Cyan, "[PNP] Received some data");
                    if (!remoteendpoint.Equals(_gatewayEndPoint) || socketErrorCode != 0)
                    {
                        NetUtils.DebugWriteForce(ConsoleColor.Cyan, "[PNP] EndPoint not equal or error");
                        return;
                    }

                    if (length != 12 || data[0] != PnpVersion || data[1] != ServerNoop)
                    {
                        NetUtils.DebugWriteForce(ConsoleColor.Cyan, "[PNP] Invalid data received");
                        Fail();
                        return;
                    }
                    int errorcode = NetworkToHostOrder(BitConverter.ToInt16(data, 2));
                    if (errorcode != 0)
                    {
                        NetUtils.DebugWriteForce(ConsoleColor.Cyan, "[PNP] Error in response");
                        Fail();
                        return;
                    }

                    var hostStr = string.Format("{0}.{1}.{2}.{3}", data[8], data[9], data[10], data[11]);
                    _routerEndPoint = new NetEndPoint(hostStr, ServerPort);
                    NetUtils.DebugWriteForce(ConsoleColor.Cyan, "[PNP] Discover OK. Target: " + _routerEndPoint);
                    _state = PNPState.TryOpenPort;
                    break;
            }
        }
    }
}
