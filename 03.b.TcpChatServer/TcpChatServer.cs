using System;
using System.Text;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

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
        private List<string> _names = new List<string>();

        // Messages that need to be sent
        private Queue<string> _messageQueue = new Queue<string>();

        // Extra fun data
        public readonly string ChatName;
        public readonly int Port;
        public bool Running { get; private set; }

        // Buffer
        public readonly int BufferSize = 2 * 1024;  // 2KB

        // Make a new TCP chat server, with our provided name
        public TcpChatServer(string chatName, int port)
        {
            // Set the basic data
            ChatName = chatName;
            Port = port;
            Running = false;

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
            Console.WriteLine("Starting the \"{0}\" TCP Chat Server", ChatName);
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

                // Do the rest
                _checkForDisconnects();

                // TODO get new messages

                // TODO send messages

                // Use less CPU
                Thread.Sleep(10);
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
            NetworkStream netStream = newClient.GetStream();

            // Modify the default buffer sizes
            newClient.SendBufferSize = BufferSize;
            newClient.ReceiveBufferSize = BufferSize;

            // Print some info
            EndPoint endPoint = newClient.Client.RemoteEndPoint;
            Console.WriteLine("Handling a new client from {0}...", endPoint);

            // Let them identify themselves
            byte[] msgBuffer = new byte[BufferSize];
            int bytesRead = netStream.Read(msgBuffer, 0, msgBuffer.Length);     // Blocks
            //Console.WriteLine("Got {0} bytes.", bytesRead);
            if (bytesRead > 0)
            {
                string msg = Encoding.UTF8.GetString(msgBuffer, 0, bytesRead);

                if (msg == "viewer")
                {
                    // They just want to watch
                    good = true;
                    _viewers.Add(newClient);

                    Console.WriteLine("{0} is a viewer.", endPoint);

                    // Send them a "hello message"
                    msg = String.Format("Welcome to the \"{0}\" Chat Server!\n", ChatName);
                    msgBuffer = Encoding.UTF8.GetBytes(msg);
                    netStream.Write(msgBuffer, 0, msgBuffer.Length);    // Blocks
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

                        Console.WriteLine("{0} is a messenger with the name {1}.", endPoint, name);

                        // Tell the viewers we have a new messenger
                        _messageQueue.Enqueue(String.Format("{0} has joined the chat.\n", name));
                    }
                }
            }

            // Do we really want them?
            if (!good)
                newClient.Close();
        }

        // Sees if any of the clients have left the chat server
        private void _checkForDisconnects()
        {
            // Check the viewers first
            foreach (TcpClient v in _viewers.ToArray())
            {
                if (_isDisconnected(v))
                {
                    Console.WriteLine("Viewer {0} has left.", v.Client.RemoteEndPoint);

                    // cleanup on our end
                    _viewers.Remove(v);         // Remove from list
                    v.GetStream().Close();      // close Network Stream
                    v.Close();                  // Close TCP Client
                }
            }

            // Check the messengers second
            foreach (TcpClient m in _messengers.ToArray())
            {
                if (_isDisconnected(m))
                {
                    // Get info about the messenger
                    int index = _messengers.IndexOf(m);
                    string name = _names[index];

                    // Tell the viewers someone has left
                    Console.WriteLine("Messeger {0} has left.", name);
                    _messageQueue.Enqueue(String.Format("{0} has left the chat", name));

                    // clean up on our end 
                    _messengers.RemoveAt(index);    // Remove from list
                    _names.RemoveAt(index);         // Remove taken name
                    m.GetStream().Close();          // Close Network Stream
                    m.Close();                      // Close TCP Client

                }
            }
        }

        // Checks if a socket has disconnected
        // Adapted from -- http://stackoverflow.com/questions/722240/instantly-detect-client-disconnection-from-server-socket
        private static bool _isDisconnected(TcpClient client)
        {
            try
            {
                Socket s = client.Client;
                return s.Poll(10 * 1000, SelectMode.SelectRead) && (s.Available == 0);
            }
            catch(SocketException se)
            {
                // We got a socket error, assume it's disconnected
                return true;
            }
        }



        // TODO need to move TcpChatServer to its own thing
        public static TcpChatServer chat;
        protected static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            chat.Shutdown();
            args.Cancel = true;
        }
            
        public static void Main(string[] args)
        {
            // Create the server
            chat = new TcpChatServer("Bad IRC", 6000);

            // Add a handler for a Ctrl-C press
            Console.CancelKeyPress += InterruptHandler;

            // run the chat server
            chat.Run();
        }
    }
}
