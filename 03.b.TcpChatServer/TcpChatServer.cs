using System;
using System.Text;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace TcpChatServer
{
    class TcpChatServer
    {
        // What listens in
        private TcpListener _listener;

        // types of clients connected
        private List<TcpClient> _viewers = new List<TcpClient>();
        private List<TcpClient> _messengers = new List<TcpClient>();

        // Names that are taken by other messengers
        private HashSet<string> _names = new HashSet<string>();

        // Messages that need to be sent
        private Queue<string> _messageQueue = new Queue<string>();

        // Extra fun data
        public readonly string ChatName;
        public readonly int Port;
        public bool Running { get; private set; }

        // buffer stuff
        public readonly int BufferSize = 8 * 1024;  // 8KB
        private byte[] _recvBuffer;

        // Make a new TCP chat server, with our provided name
        public TcpChatServer(string chatName, int port)
        {
            // Set the basic data
            ChatName = chatName;
            Port = port;
            Running = false;

            // Create the buffer
            _recvBuffer = new byte[BufferSize];

            // make the listener
            _listener = new TcpListener(IPAddress.Loopback, Port);
        }

        // If the server is running, this will shut down the server
        public void Shutdown()
        {
            Console.WriteLine("Shutting down server");
            Running = false;
        }

        // Start running the server.  Will stop when `Shutdown()` has been called
        public void Run()
        {
            // Some info
            Console.WriteLine("Starting \"{0}\" TCP Chat Server", ChatName);
            Console.WriteLine("Press Ctrl-C to shut down the server at any time.");

            // Make the server run
            _listener.Start();           // No backlog
            Running = true;

            // Main server loop
            while (Running)
            {
                // Check for new clients
                if (_listener.Pending())
                    _handleNewConnection();

                // TODO check for disconnects

                // TODO get new messages

                // TODO send messages
            }

            // Stop the server, and clean up any connected clients
            foreach (TcpClient v in _viewers)
                v.Close();
            foreach (TcpClient m in _messengers)
                m.Close();
            _listener.Stop();

            // Some info
            Console.WriteLine("Server is shut down.");
        }
            
        private void _handleNewConnection()
        {
            // There is (at least) one, see what they want
            bool good = false;
            TcpClient newClient = _listener.AcceptTcpClient();      // Blocks
            NetworkStream stream = newClient.GetStream();

            // Print some info
            Console.WriteLine("Handling a new client from {0}", newClient.Client);

            // Let them identify themselves
            int bytesRead = stream.Read(_recvBuffer, 0, _recvBuffer.Length);    // Blocks
            if (bytesRead > 0)
            {
                string msg = Encoding.UTF8.GetString(_recvBuffer, 0, bytesRead);

                if (msg == "viewer")
                {
                    // They just want to watch
                    good = true;
                    _viewers.Add(newClient);

                    Console.WriteLine("{0} is a viewer.", newClient.Client);
                }
                else if (msg.StartsWith("name:"))
                {
                    // Okay, so they might be a messenger
                    string name = msg.Substring(msg.IndexOf(':') + 1);

                    if ((name != string.Empty) && (!_names.Contains(name)))
                    {
                        // They're new here, add them in
                        good = true;
                        _names.Add(name);
                        _messengers.Add(newClient);

                        Console.WriteLine("{0} is a messenger.", newClient.Client);
                    }
                }
            }

            // Do we really want them?
            if (!good)
                newClient.Close();
        }


        // TODO need to move TcpChatServer to its own thing
        public static TcpChatServer chat;
        protected static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            chat.Shutdown();
        }


        public static void Main(string[] args)
        {
            // Create the server
            chat = new TcpChatServer("Bad IRC", 6000);

            // Add a handler for Ctrl-C presses
            Console.CancelKeyPress += InterruptHandler;

            // run the chat server
            chat.Run();
        }
    }
}
