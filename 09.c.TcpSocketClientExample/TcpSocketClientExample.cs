// Filename:  TcpSocketClientExample.cs
// Author:    Benjamin N. Summerton <define-private-public>
// License:   Unlicense (https://unlicense.org/)
//
// Adapted & Ported From:
//   https://en.wikibooks.org/wiki/C_Programming/Networking_in_UNIX

using System;
using System.Text;
using System.Net;
using System.Net.Sockets;

namespace Client
{
    class TcpSocketClientExample
    {
        public static int MaxReceiveLength = 255;
        public static int PortNumber = 6000;


        // Main method
        public static void Main(string[] args)
        {
            int len;
            byte[] buffer = new byte[MaxReceiveLength + 1];

            // Create a TCP/IP Socket
            Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint serv = new IPEndPoint(IPAddress.Loopback, PortNumber);

            // Connect to the server
            Console.WriteLine("Connecting to the server...");
            clientSocket.Connect(serv);

            // Get a message (blocks)
            len = clientSocket.Receive(buffer);
            Console.Write("Got a message from the server[{0} bytes]:\n{1}",
                len, Encoding.ASCII.GetString(buffer, 0, len));

            // Cleanup
            clientSocket.Close();
        }
    }
}
