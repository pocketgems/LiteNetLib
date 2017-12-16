using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace LiteNetLib {
    internal sealed class FragmentChannel {
        private class Buffer {
            private readonly NetPeer peer;
            private readonly NetPacket[] packets;

            private readonly Stopwatch stopwatch = new Stopwatch();
            private TimeSpan activeTime;
            public TimeSpan InactiveTime { get { return stopwatch.Elapsed - activeTime; } }
            public bool IsActive { get { return stopwatch.IsRunning; } }

            public ushort FragmentId { get; private set; }
            private ushort fragmentsTotal;
            private int receivedCount;

            public Buffer(NetPeer peer, int maxFragmentation) {
                this.peer = peer;

                packets = new NetPacket[maxFragmentation];
            }

            public void Initialize(ushort fragmentId, ushort fragmentsTotal) {
                stopwatch.Reset();
                stopwatch.Start();

                Array.Clear(packets, 0, packets.Length);

                FragmentId = fragmentId;
                this.fragmentsTotal = fragmentsTotal;
                receivedCount = 0;
            }

            public void Abort() {
                stopwatch.Stop();

                for (int i = 0; i < packets.Length; ++i) {
                    if (packets[i] != null) {
                        peer.Recycle(packets[i]);
                    }
                }
            }
            
            public void Add(NetPacket packet) {
                activeTime = stopwatch.Elapsed;

                packets[packet.FragmentPart] = packet;
                ++receivedCount;
                if (receivedCount == fragmentsTotal) {
                    stopwatch.Stop();

                    for (int i = 0; i < fragmentsTotal; ++i) {
                        peer.AddIncomingPacket(packets[i]);
                    }
                }
            }
        }

        private readonly Queue<NetPacket> _outgoingPackets;
        private readonly NetPeer _peer;
        private readonly Buffer[] buffers;

        public FragmentChannel(NetPeer peer, int maxInTransit, int maxFragmentation) {
            _outgoingPackets = new Queue<NetPacket>();
            _peer = peer;

            buffers = new Buffer[maxInTransit];
            for (int i = 0; i < buffers.Length; ++i) {
                buffers[i] = new Buffer(peer, maxFragmentation);
            }
        }

        internal void AddToQueue(NetPacket packet) {
            lock (_outgoingPackets) {
                _outgoingPackets.Enqueue(packet);
            }
        }

        internal void ProcessPacket(NetPacket packet) {
            if (packet.IsFragmented) {
                lock (buffers) {
                    // add to an active buffer if one exists
                    for (int i = 0; i < buffers.Length; ++i) {
                        if (buffers[i].IsActive && buffers[i].FragmentId == packet.FragmentId) {
                            buffers[i].Add(packet);

                            // found the active buffer to add the packet to
                            return;
                        }
                    }

                    // could not add to an active buffer; initialize another one
                    for (int i = 0; i < buffers.Length; ++i) {
                        if (!buffers[i].IsActive) {
                            buffers[i].Initialize(packet.FragmentId, packet.FragmentsTotal);
                            buffers[i].Add(packet);

                            // initialized an active buffer and added the packet to it
                            return;
                        }
                    }

                    // all buffers are active; recycle an old one
                    var maxInactiveTime = TimeSpan.MinValue;
                    int maxInactiveIndex = -1;
                    for (int i = 0; i < buffers.Length; ++i) {
                        if (buffers[i].InactiveTime > maxInactiveTime) {
                            maxInactiveTime = buffers[i].InactiveTime;
                            maxInactiveIndex = i;
                        }
                    }

                    buffers[maxInactiveIndex].Abort();
                    buffers[maxInactiveIndex].Initialize(packet.FragmentId, packet.FragmentsTotal);
                    buffers[maxInactiveIndex].Add(packet);
                }
            } else {
                _peer.AddIncomingPacket(packet);
            }
        }

        internal bool SendNextPacket() {
            NetPacket packet;
            lock (_outgoingPackets) {
                if (_outgoingPackets.Count == 0)
                    return false;
                packet = _outgoingPackets.Dequeue();
            }
            _peer.SendRawData(packet);
            _peer.Recycle(packet);
            return true;
        }
    }
}
