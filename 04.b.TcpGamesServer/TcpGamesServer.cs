// Filename:  TcpGamesServer.cs        
// Author:    Benjamin N. Summerton <define-private-public>        
// License:   Unlicense (http://unlicense.org/)        

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TcpGames
{
    public class TcpGamesServer
    {
        // Listens for new incoming connections
        private TcpListener _listener;

        // Clients objects
        private List<TcpClient> _clients = new List<TcpClient>();
        private List<TcpClient> _waitingLobby = new List<TcpClient>();

        // Game stuff
        private Dictionary<TcpClient, IGame> _gameClientIsIn = new Dictionary<TcpClient, IGame>();
        private List<IGame> _games = new List<IGame>();
        private List<Thread> _gameThreads = new List<Thread>();
        private IGame _nextGame;

        // Other data
        public readonly Guid ServerId = Guid.NewGuid();
        public readonly string Name;
        public readonly int Port;
        public bool Running { get; private set; }

        // Construct to create a new Games Server
        public TcpGamesServer(string name, int port)
        {
            // Set some of the basic data
            Name = name;
            Port = port;
            Running = false;

            // Create the listener
            _listener = new TcpListener(IPAddress.Any, Port);
        }

        // Shutsdown the server if its running
        public void Shutdown()
        {
            if (Running)
            {
                Running = false;
                Console.WriteLine("Shutting down the Game(s) Server...");
            }
        }

        // The main loop for the games server
        public void Run()
        {
            Console.WriteLine("Starting the \"{0}\" Game(s) Server on port {1}.", Name, Port); 
            Console.WriteLine("Press Ctrl-C to shutdown the server at any time.");

            // Start the next game
            // (current only the Guess My Number Game)
            _nextGame = new GuessMyNumberGame(this);

            // Start running the server
            _listener.Start();
            Running = true;
            List<Task> newConnectionTasks = new List<Task>();
            Console.WriteLine("Waiting for incommming connections...");

            while (Running)
            {
                // Handle any new clients
                if (_listener.Pending())
                    newConnectionTasks.Add(_handleNewConnection());

                // Once we have enough clients for the next game, add them in and start the game
                if (_waitingLobby.Count >= _nextGame.RequiredPlayers)
                {
                    // Get that many players from the waiting lobby and start the game
                    int numPlayers = 0;
                    while (numPlayers < _nextGame.RequiredPlayers)
                    {
                        // Pop the first one off
                        TcpClient player = _waitingLobby[0];
                        _waitingLobby.RemoveAt(0);

                        // Try adding it to the game.  If failure, put it back in the lobby
                        if (_nextGame.AddPlayer(player))
                            numPlayers++;
                        else
                            _waitingLobby.Add(player);
                    }

                    // Start the game in a new thread!
                    Console.WriteLine("Starting a \"{0}\" game.", _nextGame.Name);
                    Thread gameThread = new Thread(new ThreadStart(_nextGame.Run));
                    gameThread.Start();
                    _games.Add(_nextGame);
                    _gameThreads.Add(gameThread);

                    // Create a new game
                    _nextGame = new GuessMyNumberGame(this);
                }

                // Take a small nap
                Thread.Sleep(10);
            }

            // In the chance a client connected but we exited the loop, give them 1 second to finish
            Task.WaitAll(newConnectionTasks.ToArray(), 1000);

            // Shutdown all of the threads, regardless if they are done or not
            foreach (Thread thread in _gameThreads)
                thread.Abort();

            // Disconnect any clients still here
            Parallel.ForEach(_clients, (client) =>
                {
                    DisconnectClient(client, "The Game(s) Server is being shutdown.");
                });            

            // Cleanup our resources
            _listener.Stop();

            // Info
            Console.WriteLine("The server has been shut down.");
        }

        // Awaits for a new connection and then adds them to the waiting lobby
        private async Task _handleNewConnection()
        {
            // Get the new client using a Future
            TcpClient newClient = await _listener.AcceptTcpClientAsync();
            Console.WriteLine("New connection from {0}.", newClient.Client.RemoteEndPoint);

            // Store them and put them in the waiting lobby
            _clients.Add(newClient);
            _waitingLobby.Add(newClient);

            // Send a welcome message
            string msg = String.Format("Welcome to the \"{0}\" Games Server.\n", Name);
            await SendPacket(newClient, new Packet("message", msg));
        }

        // Will attempt to gracefully disconnect a TcpClient
        // This should be use for clients that may be in a game, or the waiting lobby
        public void DisconnectClient(TcpClient client, string message="")
        {
            Console.WriteLine("Disconnecting the client from {0}.", client.Client.RemoteEndPoint);

            // If there wasn't a message set, use the default "Goodbye."
            if (message == "")
                message = "Goodbye.";

            // Send the "bye," message
            Task byePacket = SendPacket(client, new Packet("bye", message));

            // Remove from lobby and connected clients, and notify and running games
            _clients.Remove(client);
            _waitingLobby.Remove(client);
            try
            {
                _gameClientIsIn[client]?.DisconnectClient(client);   
            } catch (KeyNotFoundException) { }

            // Give the client some time to send and proccess the graceful disconnect
            Thread.Sleep(100);

            // Cleanup resources on our end
            byePacket.GetAwaiter().GetResult();
            _cleanupClient(client);
        }

        // Will clean up resources if a client has sent a "bye," packet to us
        // meaning they've cleaned up their resources already
        public void HandleDisconnectedClient(TcpClient client)
        {
            // Remove from collections and free resources
            _clients.Remove(client);
            _waitingLobby.Remove(client);
            _cleanupClient(client);
        }

        #region Packet Transmission Methods
        // Sends a packet to a client asynchronously
        public async Task SendPacket(TcpClient client, Packet packet)
        {
            try
            {
                // convert JSON to buffer and its length to a 16 bit unsigned integer buffer
                byte[] jsonBuffer = Encoding.UTF8.GetBytes(packet.ToJson());
                byte[] lengthBuffer = BitConverter.GetBytes(Convert.ToUInt16(jsonBuffer.Length));

                // Join the buffers
                byte[] msgBuffer = new byte[lengthBuffer.Length + jsonBuffer.Length];
                lengthBuffer.CopyTo(msgBuffer, 0);
                jsonBuffer.CopyTo(msgBuffer, lengthBuffer.Length);

                // Send the packet
                await client.GetStream().WriteAsync(msgBuffer, 0, msgBuffer.Length);

                //Console.WriteLine("[SENT]\n{0}", packet);
            }
            catch (Exception e)
            {
                // There was an issue is sending
                Console.WriteLine("There was an issue receiving a packet.");
                Console.WriteLine("Reason: {0}", e.Message);
            }
        }

        // Will get a single packet from a TcpClient
        // This may return null if the client has disconnected
        public async Task<Packet> ReceivePacket(TcpClient client)
        {
            Packet packet = null;
            try
            {
                NetworkStream msgStream = client.GetStream();

                // There must be some incoming data, the first two bytes are the size of the Packet
                byte[] lengthBuffer = new byte[2];
                await msgStream.ReadAsync(lengthBuffer, 0, 2);
                ushort packetByteSize = BitConverter.ToUInt16(lengthBuffer, 0);

                // Now read that many bytes from what's left in the stream, it must be the Packet
                byte[] jsonBuffer = new byte[packetByteSize];
                await msgStream.ReadAsync(jsonBuffer, 0, jsonBuffer.Length);

                // Convert it into a packet datatype
                string jsonString = Encoding.UTF8.GetString(jsonBuffer);
                packet = Packet.FromJson(jsonString);

                //Console.WriteLine("[RECEIVED]\n{0}", packet);
            }
            catch (Exception e)
            {
                // There was an issue in receiving
                Console.WriteLine("There was an issue sending a packet to {0}.", client.Client.RemoteEndPoint);
                Console.WriteLine("Reason: {0}", e.Message);
            }

            return packet;
        }
        #endregion // Packet Transmission Methods

        #region TcpClient Helper Methods
        // Checks if a client has disconnected ungracefully
        // Adapted from: http://stackoverflow.com/questions/722240/instantly-detect-client-disconnection-from-server-socket
        public static bool IsDisconnected(TcpClient client)
        {
            try
            {
                Socket s = client.Client;
                return s.Poll(10 * 1000, SelectMode.SelectRead) && (s.Available == 0);
            }
            catch(SocketException)
            {
                // We got a socket error, assume it's disconnected
                return true;
            }
        }

        // cleans up resources for a TcpClient and closes it
        private static void _cleanupClient(TcpClient client)
        {
            client.GetStream().Close();     // Close network stream
            client.Close();                 // Close client
        }
        #endregion // TcpClient Helper Methods





        #region Program Execution
        public static TcpGamesServer gamesServer;

        // For when the user Presses Ctrl-C, this will gracefully shutdown the server
        public static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            gamesServer?.Shutdown();
        }

        public static void Main(string[] args)
        {
            // Some arguments
            string name = "Bad BBS";//args[0];
            int port = 6000;//int.Parse(args[1]);

            // Handler for Ctrl-C presses
            Console.CancelKeyPress += InterruptHandler;

            // Create and run the server
            gamesServer = new TcpGamesServer(name, port);
            gamesServer.Run();
        }
        #endregion // Program Execution
    }
}
