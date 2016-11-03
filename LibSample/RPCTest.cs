using System;
using System.Threading;
using LiteNetLib;
using LiteNetLib.Utils;

namespace LibSample
{
    public class RPCTest
    {
        private class ClientListener : INetEventListener
        {
            public void OnPeerConnected(NetPeer peer)
            {
                Console.WriteLine("[Client] connected to: {0}:{1}", peer.EndPoint.Host, peer.EndPoint.Port);
            }

            public void OnPeerDisconnected(NetPeer peer, DisconnectReason disconnectReason, int socketErrorCode)
            {
                Console.WriteLine("[Client] disconnected: " + disconnectReason);
            }

            public void OnNetworkError(NetEndPoint endPoint, int socketErrorCode)
            {
                Console.WriteLine("[Client] error! " + socketErrorCode);
            }

            public void OnNetworkReceive(NetPeer peer, NetDataReader reader)
            {

            }

            public void OnNetworkReceiveUnconnected(NetEndPoint remoteEndPoint, NetDataReader reader, UnconnectedMessageType messageType)
            {

            }

            public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
            {

            }
        }

        private class ServerListener : INetEventListener
        {
            public NetServer Server;

            public void OnPeerConnected(NetPeer peer)
            {
                Console.WriteLine("[Server] Peer connected: " + peer.EndPoint);
            }

            public void OnPeerDisconnected(NetPeer peer, DisconnectReason disconnectReason, int socketErrorCode)
            {
                Console.WriteLine("[Server] Peer disconnected: " + peer.EndPoint + ", reason: " + disconnectReason);
            }

            public void OnNetworkError(NetEndPoint endPoint, int socketErrorCode)
            {
                Console.WriteLine("[Server] error: " + socketErrorCode);
            }

            public void OnNetworkReceive(NetPeer peer, NetDataReader reader)
            {

            }

            public void OnNetworkReceiveUnconnected(NetEndPoint remoteEndPoint, NetDataReader reader, UnconnectedMessageType messageType)
            {
                Console.WriteLine("[Server] ReceiveUnconnected: {0}", reader.GetString(100));
            }

            public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
            {

            }
        }

        private class CustomData
        {
            public int SomeNumber;
        }

        private class TestObject
        {
            [RPCMethod]
            public void ShowText(string text, CustomData data)
            {
                Console.WriteLine(text + "_" + data.SomeNumber);
            }

            [RPCMethod]
            public int GetSomeNumber()
            {
                return 5;
            }
        }

        private ClientListener _clientListener;
        private ServerListener _serverListener;

        private static void WriteCustomData(NetDataWriter writer, object o)
        {
            writer.Put(((CustomData)o).SomeNumber);
        }

        private static object ReadCustomData(NetDataReader reader)
        {
            CustomData cd = new CustomData();
            cd.SomeNumber = reader.GetInt();
            return cd;
        }

        public void Run()
        {
            LiteRPC rpc = new LiteRPC();
            rpc.RegisterCustomType<CustomData>(WriteCustomData, ReadCustomData);
            rpc.RegisterObject(new TestObject());
            NetDataWriter wr = new NetDataWriter();
            rpc.CallClassMethod<TestObject>(wr, "ShowText", "ASS", new CustomData { SomeNumber = 799 });
            NetDataReader dr = new NetDataReader(wr.CopyData());
            rpc.ExecuteData(dr);
            Console.ReadKey();
            return;

            //Server
            _serverListener = new ServerListener();

            NetServer server = new NetServer(_serverListener, 2, "myapp1");
            if (!server.Start(9050))
            {
                Console.WriteLine("Server start failed");
                Console.ReadKey();
                return;
            }
            _serverListener.Server = server;

            //Client
            _clientListener = new ClientListener();

            NetClient client1 = new NetClient(_clientListener, "myapp1");
            if (!client1.Start())
            {
                Console.WriteLine("Client1 start failed");
                return;
            }
            client1.Connect("127.0.0.1", 9050);

            NetClient client2 = new NetClient(_clientListener, "myapp1");
            client2.Start();
            client2.Connect("::1", 9050);

            while (!Console.KeyAvailable)
            {
                client1.PollEvents();
                client2.PollEvents();
                server.PollEvents();
                Thread.Sleep(15);
            }

            client1.Stop();
            client2.Stop();
            server.Stop();
            Console.ReadKey();
        }
    }
}
