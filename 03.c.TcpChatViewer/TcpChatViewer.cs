using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace TcpChatViewer
{
    class TcpChatViewer
    {
        // Connection objects
        public readonly string ServerAddress;
        public readonly int Port;
        private TcpClient _client;
        public bool Running { get; private set; }

        // Buffer & messaging
        public readonly int BufferSize = 2 * 1024;  // 2KB
        private byte[] _msgBuffer;
        private NetworkStream _msgStream = null;

        public TcpChatViewer(string serverAddress, int port)
        {
            // Create a non-connected TcpClient
            _client = new TcpClient();          // Other constructors will start a connection
            _client.SendBufferSize = BufferSize;
            _client.ReceiveBufferSize = BufferSize;
            Running = false;

            // Set the other things
            ServerAddress = serverAddress;
            Port = port;
        }

        public void Connect()
        {
            // Now try to connect
            _client.Connect(ServerAddress, Port);   // Will resolve DNS for us, blocks

            // check that we're connected
            if (_client.Connected)
            {
                // got in!
                Console.WriteLine("Able to connect to the server at {0}:{1}", ServerAddress, Port);

                // Send them the message that we're a viewer
                _msgStream = _client.GetStream();
                _msgBuffer = Encoding.UTF8.GetBytes("viewer");
                _msgStream.Write(_msgBuffer, 0, _msgBuffer.Length);     // Blocks

                // check that we're still connected, if the server has not kicked us, then we're in!
                if (_client.Connected)
                {
                    Running = true;
                }
                else
                {
                    // Server doens't see us as a viewer, cleanup
                    _cleanupNetworkResources();
                    Console.WriteLine("The server didn't recognise us as a Viewer.");
                    Console.WriteLine(":[");
                }
            }
            else
            {
                _cleanupNetworkResources();
                Console.WriteLine("Not able to connect to the server at {0}:{1}", ServerAddress, Port);
            }
        }

        public void Disconnect()
        {
            Running = false;
            Console.WriteLine("Disconnecting from the chat...");
        }

        public void ListenForMessages()
        {
            bool wasRunning = Running;

            // Listen for messages
            while (Running)
            {
                // Do we have a new message?
                int messageLength = _client.Available;
                if (messageLength > 0)
                {
                    //Console.WriteLine("New incoming message of {0} bytes", messageLength);

                    // Read the whole message
                    _msgBuffer = new byte[messageLength];
                    _msgStream.Read(_msgBuffer, 0, messageLength);      // Blocks

                    // An alternative way of reading
                    //int bytesRead = 0;
                    //while (bytesRead < messageLength)
                    //{
                    //    bytesRead += _msgStream.Read(_msgBuffer,
                    //                                 bytesRead,
                    //                                 _msgBuffer.Length - bytesRead);
                    //    Thread.Sleep(1);    // Use less CPU
                    //}

                    // Decode it and print it
                    string msg = Encoding.UTF8.GetString(_msgBuffer);
                    Console.Write(msg);
                }

                // Use less CPU
                Thread.Sleep(10);

                // Check the server didn't disconnect us
                Running = _client.Connected;
            }

            // Clean up network resources
            _cleanupNetworkResources();
            if (wasRunning)
                Console.WriteLine("Disconnected.");
        }

        // Cleans any leftover network resources
        private void _cleanupNetworkResources()
        {
            _msgStream?.Close();
            _msgStream = null;
            _client.Close();
        }



        public static TcpChatViewer viewer;
        protected static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            viewer.Disconnect();
        }

        public static void Main(string[] args)
        {
            // Setup the viewer
            viewer = new TcpChatViewer("localhost", 6000);

            // Add a handler for a Ctrl-C press
            Console.CancelKeyPress += InterruptHandler;

            // Try to connect & view messages
            viewer.Connect();
            viewer.ListenForMessages();
        }
    }
}
