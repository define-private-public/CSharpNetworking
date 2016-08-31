// Filename:  PongClient.cs
// Author:    Benjamin N. Summerton <define-private-public>
// License:   Unlicense (https://unlicense.org/)

// Uncomment the line below to play sounds
// This is here because MonoGame 3.5 on Linux throws an error when trying to play a SoundEffect
//#define CAN_PLAY_SOUNDS

using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Audio;

namespace PongGame
{
    public enum ClientState
    {
        NotConnected,
        EstablishingConnection,
        WaitingForGameStart,
        InGame,
        GameOver,
    }

    class PongClient : Game
    {
        // Game stuffs
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;

        // Network stuff
        private UdpClient _udpClient;
        public readonly string ServerHostname;
        public readonly int ServerPort;

        // Time measurement
        private DateTime _lastPacketReceivedTime = DateTime.MinValue;     // From Client Time
        private DateTime _lastPacketSentTime = DateTime.MinValue;         // From Client Time
        private long _lastPacketReceivedTimestamp = 0;                    // From Server Time
        private TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(20);
        private TimeSpan _sendPaddlePositionTimeout = TimeSpan.FromMilliseconds(1000f / 30f);  // How often to update the server

        // Messaging
        private Thread _networkThread;
        private ConcurrentQueue<NetworkMessage> _incomingMessages
            = new ConcurrentQueue<NetworkMessage>();
        private ConcurrentQueue<Packet> _outgoingMessages
            = new ConcurrentQueue<Packet>();

        // Game objects
        private Ball _ball;
        private Paddle _left;
        private Paddle _right;
        private Paddle _ourPaddle;
        private float _previousY;

        // Info messages for the user
        private Texture2D _establishingConnectionMsg;
        private Texture2D _waitingForGameStartMsg;
        private Texture2D _gamveOverMsg;

        // Audio
        private SoundEffect _ballHitSFX;
        private SoundEffect _scoreSFX;

        // State stuff
        private ClientState _state = ClientState.NotConnected;
        private ThreadSafe<bool> _running = new ThreadSafe<bool>(false);
        private ThreadSafe<bool> _sendBye = new ThreadSafe<bool>(false);

        public PongClient(string hostname, int port)
        {
            // Content
            Content.RootDirectory = "Content";

            // Graphics setup
            _graphics = new GraphicsDeviceManager(this);
            _graphics.PreferredBackBufferWidth = GameGeometry.PlayArea.X;
            _graphics.PreferredBackBufferHeight = GameGeometry.PlayArea.Y;
            _graphics.IsFullScreen = false;
            _graphics.ApplyChanges();

            // Game Objects
            _ball = new Ball();
            _left = new Paddle(PaddleSide.Left);
            _right = new Paddle(PaddleSide.Right);

            // Connection stuff
            ServerHostname = hostname;
            ServerPort = port;
            _udpClient = new UdpClient(ServerHostname, ServerPort);
        }

        protected override void Initialize()
        {
            base.Initialize();
            _left.Initialize();
            _right.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(_graphics.GraphicsDevice);

            // Load the game objects
            _ball.LoadContent(Content);
            _left.LoadContent(Content);
            _right.LoadContent(Content);

            // Load the messages
             _establishingConnectionMsg = Content.Load<Texture2D>("establishing-connection-msg.png");
            _waitingForGameStartMsg = Content.Load<Texture2D>("waiting-for-game-start-msg.png");
            _gamveOverMsg = Content.Load<Texture2D>("game-over-msg.png");

            // Load sound effects
            _ballHitSFX = Content.Load<SoundEffect>("ball-hit.wav");
            _scoreSFX = Content.Load<SoundEffect>("score.wav");
        }

        protected override void UnloadContent()
        {
            // Cleanup
            _networkThread?.Join(TimeSpan.FromSeconds(2));
            _udpClient.Close();

            base.UnloadContent();
        }

        protected override void Update(GameTime gameTime)
        {
            // Check for close
            KeyboardState kbs = Keyboard.GetState();
            if (kbs.IsKeyDown(Keys.Escape))
            {
                // Player wants to quit, send a ByePacket (if we're connected)
                if ((_state == ClientState.EstablishingConnection) ||
                    (_state == ClientState.WaitingForGameStart) ||
                    (_state == ClientState.InGame))
                {
                    // Will trigger the network thread to send the Bye Packet
                    _sendBye.Value = true;
                }

                // Will stop the network thread
                _running.Value = false;
                _state = ClientState.GameOver;
                Exit();
            }

            // Check for time out with the server
            if (_timedOut())
                _state = ClientState.GameOver;

            // Get message
            NetworkMessage message;
            bool haveMsg = _incomingMessages.TryDequeue(out message);

            // Check for Bye From server
            if (haveMsg && (message.Packet.Type == PacketType.Bye))
            {
                // Shutdown the network thread (not needed anymore)
                _running.Value = false;
                _state = ClientState.GameOver;
            }

            switch (_state)
            {
                case ClientState.EstablishingConnection:
                    _sendRequestJoin(TimeSpan.FromSeconds(1));
                    if (haveMsg)
                        _handleConnectionSetupResponse(message.Packet);                        
                    break;

                case ClientState.WaitingForGameStart:
                    // Send a heartbeat
                    _sendHeartbeat(TimeSpan.FromSeconds(0.2));

                    if (haveMsg)
                    {
                        switch (message.Packet.Type)
                        {
                            case PacketType.AcceptJoin:
                                // It's possible that they didn't receive our ACK in the previous state
                                _sendAcceptJoinAck();
                                break;

                            case PacketType.HeartbeatAck:
                                // Record ACK times
                                _lastPacketReceivedTime = message.ReceiveTime;
                                if (message.Packet.Timestamp > _lastPacketReceivedTimestamp)
                                    _lastPacketReceivedTimestamp = message.Packet.Timestamp;
                                break;

                            case PacketType.GameStart:
                                // Start the game and ACK it
                                _sendGameStartAck();
                                _state = ClientState.InGame;
                                break;
                        }

                    }
                    break;

                case ClientState.InGame:
                    // Send a heartbeat
                    _sendHeartbeat(TimeSpan.FromSeconds(0.2));

                    // update our paddle
                    _previousY = _ourPaddle.Position.Y;
                    _ourPaddle.ClientSideUpdate(gameTime);
                    _sendPaddlePosition(_sendPaddlePositionTimeout);

                    if (haveMsg)
                    {
                        switch (message.Packet.Type)
                        {
                            case PacketType.GameStart:
                                // It's possible the server didn't receive our ACK in the previous state
                                _sendGameStartAck();
                                break;

                            case PacketType.HeartbeatAck:
                                // Record ACK times
                                _lastPacketReceivedTime = message.ReceiveTime;
                                if (message.Packet.Timestamp > _lastPacketReceivedTimestamp)
                                    _lastPacketReceivedTimestamp = message.Packet.Timestamp;
                                break;

                            case PacketType.GameState:
                                // Update the gamestate, make sure its the latest
                                if (message.Packet.Timestamp > _lastPacketReceivedTimestamp)
                                {
                                    _lastPacketReceivedTimestamp = message.Packet.Timestamp;

                                    GameStatePacket gsp = new GameStatePacket(message.Packet.GetBytes());
                                    _left.Score = gsp.LeftScore;
                                    _right.Score = gsp.RightScore;
                                    _ball.Position = gsp.BallPosition;

                                    // Update what's not our paddle
                                    if (_ourPaddle.Side == PaddleSide.Left)
                                        _right.Position.Y = gsp.RightY;
                                    else
                                        _left.Position.Y = gsp.LeftY;
                                }

                                break;

                            case PacketType.PlaySoundEffect:
                                #if CAN_PLAY_SOUNDS

                                // Play a sound
                                PlaySoundEffectPacket psep = new PlaySoundEffectPacket(message.Packet.GetBytes());
                                if (psep.SFXName == "ball-hit")
                                    _ballHitSFX.Play();
                                else if (psep.SFXName == "score")
                                    _scoreSFX.Play();
                                
                                #endif
                                break;
                        }
                    }

                    break;

                case ClientState.GameOver:
                    // Purgatory is here
                    break;
            }

            base.Update(gameTime);
        }

        public void Start()
        {
            _running.Value = true;
            _state = ClientState.EstablishingConnection;

            // Start the packet receiving/sending Thread
            _networkThread = new Thread(new ThreadStart(_networkRun));
            _networkThread.Start();
        }

        #region Graphical Functions
        protected override void Draw(GameTime gameTime)
        {
            _graphics.GraphicsDevice.Clear(Color.Black);

            _spriteBatch.Begin();

            // Draw different things based on the state
            switch (_state)
            {
                case ClientState.EstablishingConnection:
                    _drawCentered(_establishingConnectionMsg);
                    Window.Title = String.Format("Pong -- Connecting to {0}:{1}", ServerHostname, ServerPort);
                    break;
                
                case ClientState.WaitingForGameStart:
                    _drawCentered(_waitingForGameStartMsg);
                    Window.Title = String.Format("Pong -- Waiting for 2nd Player");
                    break;

                case ClientState.InGame:
                    // Draw game objects
                    _ball.Draw(gameTime, _spriteBatch);
                    _left.Draw(gameTime, _spriteBatch);
                    _right.Draw(gameTime, _spriteBatch);

                    // Change the window title
                    _updateWindowTitleWithScore();
                    break;

                case ClientState.GameOver:
                    _drawCentered(_gamveOverMsg);
                    _updateWindowTitleWithScore();
                    break;
            }

            _spriteBatch.End();

            base.Draw(gameTime);
        }

        private void _drawCentered(Texture2D texture)
        {
            Vector2 textureCenter = new Vector2(texture.Width / 2, texture.Height / 2);
            _spriteBatch.Draw(texture, GameGeometry.ScreenCenter, null, null, textureCenter);
        }

        private void _updateWindowTitleWithScore()
        {
            string fmt = (_ourPaddle.Side == PaddleSide.Left) ? 
                "[{0}] -- Pong -- {1}" : "{0} -- Pong -- [{1}]";
            Window.Title = String.Format(fmt, _left.Score, _right.Score);
        }
        #endregion // Graphical Functions

        #region Network Functions
        // This function is meant to be run in its own thread
        // and will populate the _incomingMessages queue
        private void _networkRun()
        {
            while (_running.Value)
            {
                bool canRead = _udpClient.Available > 0;
                int numToWrite = _outgoingMessages.Count;

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
                    Packet packet;
                    bool have = _outgoingMessages.TryDequeue(out packet);
                    if (have)
                        packet.Send(_udpClient);

                    //Console.WriteLine("SENT: {0}", packet);
                }

                // If Nothing happened, take a nap
                if (!canRead && (numToWrite == 0))
                    Thread.Sleep(1);
            }

            // Check to see if a bye was requested, one last operation
            if (_sendBye.Value)
            {
                ByePacket bp = new ByePacket();
                bp.Send(_udpClient);
                Thread.Sleep(1000);     // Needs some time to send through
            }
        }

        // Queues up to send a single Packet to the server
        private void _sendPacket(Packet packet)
        {
            _outgoingMessages.Enqueue(packet);
            _lastPacketSentTime = DateTime.Now;
        }

        // Sends a RequestJoinPacket,
        private void _sendRequestJoin(TimeSpan retryTimeout)
        {
            // Make sure not to spam them
            if (DateTime.Now >= (_lastPacketSentTime.Add(retryTimeout)))
            {
                RequestJoinPacket gsp = new RequestJoinPacket();
                _sendPacket(gsp);
            }
        }

        // Acks the AcceptJoinPacket
        private void _sendAcceptJoinAck()
        {
            AcceptJoinAckPacket ajap = new AcceptJoinAckPacket();
            _sendPacket(ajap);
        }

        // Responds to the Packets where we are establishing our connection with the server
        private void _handleConnectionSetupResponse(Packet packet)
        {
            // Check for accept and ACK
            if (packet.Type == PacketType.AcceptJoin)
            {
                // Make sure we haven't gotten it before
                if (_ourPaddle == null)
                {
                    // See which paddle we are
                    AcceptJoinPacket ajp = new AcceptJoinPacket(packet.GetBytes());
                    if (ajp.Side == PaddleSide.Left)
                        _ourPaddle = _left;
                    else if (ajp.Side == PaddleSide.Right)
                        _ourPaddle = _right;
                    else
                        throw new Exception("Error, invalid paddle side given by server.");     // Should never hit this, but just incase
                }

                // Send a response
                _sendAcceptJoinAck();

                // Move the state
                _state = ClientState.WaitingForGameStart;
            }
        }

        // Sends a HearbeatPacket to the server
        private void _sendHeartbeat(TimeSpan resendTimeout)
        {
            // Make sure not to spam them
            if (DateTime.Now >= (_lastPacketSentTime.Add(resendTimeout)))
            {
                HeartbeatPacket hp = new HeartbeatPacket();
                _sendPacket(hp);
            }
        }

        // Acks the GameStartPacket
        private void _sendGameStartAck()
        {
            GameStartAckPacket gsap = new GameStartAckPacket();
            _sendPacket(gsap);
        }

        // Sends the server our current paddle's Y Position (if it's changed)
        private void _sendPaddlePosition(TimeSpan resendTimeout)
        {
            // Don't send anything if there hasn't been an update
            if (_previousY == _ourPaddle.Position.Y)
                return;

            // Make sure not to spam them
            if (DateTime.Now >= (_lastPacketSentTime.Add(resendTimeout)))
            {
                PaddlePositionPacket ppp = new PaddlePositionPacket();
                ppp.Y = _ourPaddle.Position.Y;

                _sendPacket(ppp);
            }
        }

        // Returns true if out connection to the server has timed out or not
        // If we haven't recieved a packet at all from them, they're not timed out
        private bool _timedOut()
        {
            // We haven't recorded it yet
            if (_lastPacketReceivedTime == DateTime.MinValue)
                return false;    

            // Do math
            return (DateTime.Now > (_lastPacketReceivedTime.Add(_heartbeatTimeout)));
        }
        #endregion // Network Functions





        #region Program Execution
        public static void Main(string[] args)
        {
            // Get arguements
            string hostname = "localhost";//args[0].Trim();
            int port = 6000;//int.Parse(args[1].Trim());

            // Start the client
            PongClient client = new PongClient(hostname, port);
            client.Start();
            client.Run();
        }
        #endregion  // Program Execution
    }
}
