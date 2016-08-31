// Filename:  Packet.cs
// Author:    Benjamin N. Summerton <define-private-public>
// License:   Unlicense (https://unlicense.org/)

using System;
using System.Text;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Xna.Framework;
using System.Runtime.InteropServices;


namespace PongGame
{
    // These are used to define what each packet means
    // they all have different payloads (if any)
    // Check for subclasses below the `Packet` class
    public enum PacketType : uint
    {
        RequestJoin = 1,    // Client Request to join a game
        AcceptJoin,         // Server accepts join
        AcceptJoinAck,      // Client acknowledges the AcceptJoin
        Heartbeat,          // Client tells Server its alive (before GameStart)
        HeartbeatAck,       // Server acknowledges Client's Heartbeat (before GameStart)
        GameStart,          // Server tells Clients game is starting
        GameStartAck,       // Client acknowledges the GameStart
        PaddlePosition,     // Client tell Server position of the their paddle
        GameState,          // Server tells Clients ball & paddle position, and scores
        PlaySoundEffect,    // Server tells the clinet to play a sound effect
        Bye,                // Either Server or Client tells the other to end the connection
    }

    public class Packet
    {
        // Packet Data
        public PacketType Type;
        public long Timestamp;                  // 64 bit timestamp from DateTime.Ticks 
        public byte[] Payload = new byte[0];

        #region Constructors
        // Creates a Packet with the set type and an empty Payload
        public Packet(PacketType type)
        {
            this.Type = type;
            Timestamp = DateTime.Now.Ticks;
        }

        // Creates a Packet from a byte array
        public Packet(byte[] bytes)
        {
            // Start peeling out the data from the byte array
            int i = 0;

            // Type
            this.Type = (PacketType)BitConverter.ToUInt32(bytes, 0);
            i += sizeof(PacketType);

            // Timestamp
            Timestamp = BitConverter.ToInt64(bytes, i);
            i += sizeof(long);

            // Rest is payload
            Payload = bytes.Skip(i).ToArray();
        }
        #endregion // Constructors

        // Gets the packet as a byte array
        public byte[] GetBytes()
        {
            int ptSize = sizeof(PacketType);
            int tsSize = sizeof(long);

            // Join the Packet data
            int i = 0;
            byte[] bytes = new byte[ptSize + tsSize + Payload.Length];

            // Type
            BitConverter.GetBytes((uint)this.Type).CopyTo(bytes, i);
            i += ptSize;

            // Timestamp
            BitConverter.GetBytes(Timestamp).CopyTo(bytes, i);
            i += tsSize;

            // Payload
            Payload.CopyTo(bytes, i);
            i += Payload.Length;

            return bytes;
        }

        public override string ToString()
        {
            return string.Format("[Packet:{0}\n  timestamp={1}\n  payload size={2}]",
                this.Type, new DateTime(Timestamp), Payload.Length);
        }

        // Sends a Packet to a specific receiver 
        public void Send(UdpClient client, IPEndPoint receiver)
        {
            // TODO maybe be async instead?
            byte[] bytes = GetBytes();
            client.Send(bytes, bytes.Length, receiver);
        }

        // Send a Packet to the default remote receiver (will throw error if not set)
        public void Send(UdpClient client)
        {
            byte[] bytes = GetBytes();
            client.Send(bytes, bytes.Length);
        }
    }

    #region Specific Packets
    // Client Join Request
    public class RequestJoinPacket : Packet
    {
        public RequestJoinPacket()
            : base(PacketType.RequestJoin)
        {
        }
    }

    // Server Accept Request Join, assigns a paddle
    public class AcceptJoinPacket : Packet
    {
        // Paddle side
        public PaddleSide Side {
            get { return (PaddleSide)BitConverter.ToUInt32(Payload, 0); }
            set { Payload = BitConverter.GetBytes((uint)value); }
        }

        public AcceptJoinPacket()
            : base(PacketType.AcceptJoin)
        {
            Payload = new byte[sizeof(PaddleSide)];

            // Set a dfeault paddle of None
            Side = PaddleSide.None;
        }

        public AcceptJoinPacket(byte[] bytes)
            : base(bytes)
        {
        }
    }

    // Ack packet for the one above
    public class AcceptJoinAckPacket : Packet
    {
        public AcceptJoinAckPacket()
            : base(PacketType.AcceptJoinAck)
        {
        }
    }

    // Client tells the Server it's alive
    public class HeartbeatPacket : Packet
    {
        public HeartbeatPacket()
            : base(PacketType.Heartbeat)
        {
        }
    }

    // Server tells the client is knows it's alive
    public class HeartbeatAckPacket : Packet
    {
        public HeartbeatAckPacket()
            : base(PacketType.HeartbeatAck)
        {
        }
    }

    // Tells the client to begin sending data
    public class GameStartPacket : Packet
    {
        public GameStartPacket()
            : base(PacketType.GameStart)
        {
        }
    }

    // Ack for the packet above
    public class GameStartAckPacket : Packet
    {
        public GameStartAckPacket()
            : base(PacketType.GameStartAck)
        {
        }
    }

    // Sent by the client to tell the server it's Y Position for the Paddle
    public class PaddlePositionPacket : Packet
    {
        // The Paddle's Y position
        public float Y {
            get { return BitConverter.ToSingle(Payload, 0); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, 0); }
        }

        public PaddlePositionPacket()
            : base(PacketType.PaddlePosition)
        {
            Payload = new byte[sizeof(float)];

            // Default value is zero
            Y = 0;
        }

        public PaddlePositionPacket(byte[] bytes)
            : base(bytes)
        {
        }

        public override string ToString()
        {
            return string.Format("[Packet:{0}\n  timestamp={1}\n  payload size={2}" +
                "\n  Y={3}]",
                this.Type, new DateTime(Timestamp), Payload.Length, Y);
        }
    }

    // Sent by the server to thd Clients to update the game information
    public class GameStatePacket : Packet
    {
        // Payload array offets
        private static readonly int _leftYIndex = 0;
        private static readonly int _rightYIndex = 4;
        private static readonly int _ballPositionIndex = 8;
        private static readonly int _leftScoreIndex = 16;
        private static readonly int _rightScoreIndex = 20;

        // The Left Paddle's Y position
        public float LeftY
        {
            get { return BitConverter.ToSingle(Payload, _leftYIndex); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, _leftYIndex); }
        }

        // Right Paddle's Y Position
        public float RightY
        {
            get { return BitConverter.ToSingle(Payload, _rightYIndex); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, _rightYIndex); }
        }

        // Ball position
        public Vector2 BallPosition
        {
            get {
                return new Vector2(
                    BitConverter.ToSingle(Payload, _ballPositionIndex),
                    BitConverter.ToSingle(Payload, _ballPositionIndex + sizeof(float))
                );
            }
            set {
                BitConverter.GetBytes(value.X).CopyTo(Payload, _ballPositionIndex);
                BitConverter.GetBytes(value.Y).CopyTo(Payload, _ballPositionIndex + sizeof(float));
            }
        }

        // Left Paddle's Score
        public int LeftScore
        {
            get { return BitConverter.ToInt32(Payload, _leftScoreIndex); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, _leftScoreIndex); }
        }

        // Right Paddle's Score
        public int RightScore
        {
            get { return BitConverter.ToInt32(Payload, _rightScoreIndex); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, _rightScoreIndex); }
        }

        public GameStatePacket()
            : base(PacketType.GameState)
        {
            // Allocate data for the payload (we really shouldn't hardcode this in...)
            Payload = new byte[24];

            // Set default data
            LeftY = 0;
            RightY = 0;
            BallPosition = new Vector2();
            LeftScore = 0;
            RightScore = 0;
        }

        public GameStatePacket(byte[] bytes)
            : base(bytes)
        {
        }

        public override string ToString()
        {
            return string.Format(
                "[Packet:{0}\n  timestamp={1}\n  payload size={2}" +
                "\n  LeftY={3}" +
                "\n  RightY={4}" +
                "\n  BallPosition={5}" +
                "\n  LeftScore={6}" +
                "\n  RightScore={7}]",
                this.Type, new DateTime(Timestamp), Payload.Length, LeftY, RightY, BallPosition, LeftScore, RightScore);
        }
    }

    // Sent by the Server to tell the client they should play a sound effect
    public class PlaySoundEffectPacket : Packet
    {
        public string SFXName {
            get { return Encoding.UTF8.GetString(Payload); }
            set { Payload = Encoding.UTF8.GetBytes(value); }
        }

        public PlaySoundEffectPacket()
            : base(PacketType.PlaySoundEffect)
        {
            SFXName = "";
        }

        public PlaySoundEffectPacket(byte[] bytes)
            : base(bytes)
        {
        }

        public override string ToString()
        {
            return string.Format(
                "[Packet:{0}\n  timestamp={1}\n  payload size={2}" +
                "\n  SFXName={3}",
                this.Type, new DateTime(Timestamp), Payload.Length, SFXName);
        }
    }

    // Sent by either the Client or the Server to end the game/connection
    public class ByePacket : Packet
    {
        public ByePacket()
            : base(PacketType.Bye)
        {
        }
    }
    #endregion  // Specific Packets
}
