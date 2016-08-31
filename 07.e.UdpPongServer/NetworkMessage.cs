// Filename:  NetworkMessage.cs
// Author:    Benjamin N. Summerton <define-private-public>
// License:   Unlicense (https://unlicense.org/)

using System;
using System.Net;

namespace PongGame
{
    // Data structure used to store Packets along with their sender
    public class NetworkMessage
    {
        public IPEndPoint Sender { get; set; }
        public Packet Packet { get; set; }
        public DateTime ReceiveTime { get; set; }
    }
}

