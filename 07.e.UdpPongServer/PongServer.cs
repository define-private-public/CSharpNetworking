// Filename:  PongServer.cs
// Author:    Benjamin N. Summerton <define-private-public>
// License:   Unlicense (https://unlicense.org/)

using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace PongGame
{
    public class PongServer
    {
        // Network Stuff
        private UdpClient _udpClient;
        public readonly int Port;

        // Messaging
        Thread _networkThread;
        private ConcurrentQueue<NetworkMessage> _incomingMessages
            = new ConcurrentQueue<NetworkMessage>();
        private ConcurrentQueue<Tuple<Packet, IPEndPoint>> _outgoingMessages
            = new ConcurrentQueue<Tuple<Packet, IPEndPoint>>();
        private ConcurrentQueue<IPEndPoint> _sendByePacketTo
            = new ConcurrentQueue<IPEndPoint>();

        // Arena management
        private ConcurrentDictionary<Arena, byte> _activeArenas             // Being used as a HashSet
            = new ConcurrentDictionary<Arena, byte>();
        private ConcurrentDictionary<IPEndPoint, Arena> _playerToArenaMap
            = new ConcurrentDictionary<IPEndPoint, Arena>();
        private Arena _nextArena;

        // Used to check if we are running the server or not
        private ThreadSafe<bool> _running = new ThreadSafe<bool>(false);

        public PongServer(int port)
        {
            Port = port;

            // Create the UDP socket (IPv4)
            _udpClient = new UdpClient(Port, AddressFamily.InterNetwork);
        }

        // Notifies that we can start the server
        public void Start()
        {
            _running.Value = true;
        }

        // Starts a shutdown of the server
        public void Shutdown()
        {
            if (_running.Value)
            {
                Console.WriteLine("[Server] Shutdown requested by user.");

                // Close any active games
                Queue<Arena> arenas = new Queue<Arena>(_activeArenas.Keys);
                foreach (Arena arena in arenas)
                    arena.Stop();

                // Stops the network thread
                _running.Value = false;
            }
        }

        // Cleans up any necessary resources
        public void Close()
        {
            _networkThread?.Join(TimeSpan.FromSeconds(10));
            _udpClient.Close();
        }

        // Small lambda function to add a new arena
        private void _addNewArena ()
        {
            _nextArena = new Arena(this);
            _nextArena.Start();
            _activeArenas.TryAdd(_nextArena, 0);
        }

        // Used by an Arena to notify the PingServer that it's done
        public void NotifyDone(Arena arena)
        {
            // First remove from the Player->Arena map
            Arena a;
            if (arena.LeftPlayer.IsSet)
                _playerToArenaMap.TryRemove(arena.LeftPlayer.Endpoint, out a);
            if (arena.RightPlayer.IsSet)
                _playerToArenaMap.TryRemove(arena.RightPlayer.Endpoint, out a);

            // Remove from the active games hashset
            byte b;
            _activeArenas.TryRemove(arena, out b);
        }

        // Main loop function for the server
        public void Run()
        {
            // Make sure we've called Start()
            if (_running.Value)
            {
                // Info
                Console.WriteLine("[Server] Running Ping Game");

                // Start the packet receiving Thread
                _networkThread = new Thread(new ThreadStart(_networkRun));
                _networkThread.Start();

                // Startup the first Arena
                _addNewArena();
            }

            // Main loop of game server
            bool running = _running.Value;
            while (running)
            {
                // If we have some messages in the queue, pull them out
                NetworkMessage nm;
                bool have = _incomingMessages.TryDequeue(out nm);
                if (have)
                {
                    // Depending on what type of packet it is process it
                    if (nm.Packet.Type == PacketType.RequestJoin)
                    {
                        // We have a new client, put them into an arena
                        bool added = _nextArena.TryAddPlayer(nm.Sender);
                        if (added)
                            _playerToArenaMap.TryAdd(nm.Sender, _nextArena);

                        // If they didn't go in that means we're full, make a new arena
                        if (!added)
                        {
                            _addNewArena();

                            // Now there should be room
                            _nextArena.TryAddPlayer(nm.Sender);
                            _playerToArenaMap.TryAdd(nm.Sender, _nextArena);
                        }

                        // Dispatch the message
                        _nextArena.EnqueMessage(nm);
                    }
                    else
                    {
                        // Dispatch it to an existing arena
                        Arena arena;
                        if (_playerToArenaMap.TryGetValue(nm.Sender, out arena))
                            arena.EnqueMessage(nm);
                    }
                }
                else
                    Thread.Sleep(1);    // Take a short nap if there are no messages

                // Check for quit
                running &= _running.Value;
            }
        }

        #region Network Functions
        // This function is meant to be run in its own thread
        // Is writes and reads Packets to/from the UdpClient
        private void _networkRun()
        {
            if (!_running.Value)
                return;
             
            Console.WriteLine("[Server] Waiting for UDP datagrams on port {0}", Port);

            while (_running.Value)
            {
                bool canRead = _udpClient.Available > 0;
                int numToWrite = _outgoingMessages.Count;
                int numToDisconnect = _sendByePacketTo.Count;

                // Get data if there is some
                if (canRead)
                {
                    // Read in one datagram
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udpClient.Receive(ref ep);              // Blocks

                    // Enque a new message
                    NetworkMessage nm = new NetworkMessage();
                    nm.Sender = ep;
                    nm.Packet = new Packet(data);
                    nm.ReceiveTime = DateTime.Now;

                    _incomingMessages.Enqueue(nm);

                    //Console.WriteLine("RCVD: {0}", nm.Packet);
                }

                // Write out queued
                for (int i = 0; i < numToWrite; i++)
                {
                    // Send some data
                    Tuple<Packet, IPEndPoint> msg;
                    bool have = _outgoingMessages.TryDequeue(out msg);
                    if (have)
                        msg.Item1.Send(_udpClient, msg.Item2);

                    //Console.WriteLine("SENT: {0}", msg.Item1);
                }

                // Notify clients of Bye
                for (int i = 0; i < numToDisconnect; i++)
                {
                    IPEndPoint to;
                    bool have = _sendByePacketTo.TryDequeue(out to);
                    if (have)
                    {
                        ByePacket bp = new ByePacket();
                        bp.Send(_udpClient, to);
                    }
                }

                // If Nothing happened, take a nap
                if (!canRead && (numToWrite == 0) && (numToDisconnect == 0))
                    Thread.Sleep(1);
            }

            Console.WriteLine("[Server] Done listening for UDP datagrams");

            // Wait for all arena's thread to join
            Queue<Arena> arenas = new Queue<Arena>(_activeArenas.Keys);
            if (arenas.Count > 0)
            {
                Console.WriteLine("[Server] Waiting for active Areans to finish...");
                foreach (Arena arena in arenas)
                    arena.JoinThread();
            }

            // See which clients are left to notify of Bye
            if (_sendByePacketTo.Count > 0)
            {
                Console.WriteLine("[Server] Notifying remaining clients of shutdown...");

                // run in a loop until we've told everyone else
                IPEndPoint to;
                bool have = _sendByePacketTo.TryDequeue(out to);
                while (have)
                {
                    ByePacket bp = new ByePacket();
                    bp.Send(_udpClient, to);
                    have = _sendByePacketTo.TryDequeue(out to);
                }
            }
        }

        // Queues up a Packet to be send to another person
        public void SendPacket(Packet packet, IPEndPoint to)
        {
            _outgoingMessages.Enqueue(new Tuple<Packet, IPEndPoint>(packet, to));
        }

        // Will queue to send a ByePacket to the specified endpoint
        public void SendBye(IPEndPoint to)
        {
            _sendByePacketTo.Enqueue(to);
        }
        #endregion  // Network Functions





        #region Program Execution
        public static PongServer server;

        public static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            // Do a graceful shutdown
            args.Cancel = true;
            server?.Shutdown();
        }

        public static void Main(string[] args)
        {
            // Setup the server
            int port = 6000;//int.Parse(args[0].Trim());
            server = new PongServer(port);

            // Add the Ctrl-C handler
            Console.CancelKeyPress += InterruptHandler;

            // Run it
            server.Start();
            server.Run();
            server.Close();
        }
        #endregion // Program Execution
    }
}
