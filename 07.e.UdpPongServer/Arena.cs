// Filename:  Arena.cs
// Author:    Benjamin N. Summerton <define-private-public>
// License:   Unlicense (https://unlicense.org/)

using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Collections.Concurrent;
using Microsoft.Xna.Framework;

namespace PongGame
{
    public enum ArenaState
    {
        NotRunning,
        WaitingForPlayers,
        NotifyingGameStart,
        InGame,
        GameOver
    }

    // This is where the game takes place
    public class Arena
    {
        // Game objects & state info
        public ThreadSafe<ArenaState> State { get; private set; } = new ThreadSafe<ArenaState>();
        private Ball _ball = new Ball();
        public PlayerInfo LeftPlayer { get; private set; } = new PlayerInfo();      // contains Paddle
        public PlayerInfo RightPlayer { get; private set; } = new PlayerInfo();     // contains Paddle
        private object _setPlayerLock = new object();
        private Stopwatch _gameTimer = new Stopwatch();

        // Connection info
        private PongServer _server;
        private TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(20);

        // Packet queue
        private ConcurrentQueue<NetworkMessage> _messages = new ConcurrentQueue<NetworkMessage>();

        // Shutdown data
        private ThreadSafe<bool> _stopRequested = new ThreadSafe<bool>(false);

        // Other
        private Thread _arenaThread;
        private Random _random = new Random();
        public readonly int Id;
        private static int _nextId = 1;

        public Arena(PongServer server)
        {
            _server = server;
            Id = _nextId++;
            State.Value = ArenaState.NotRunning;

            // Some other data
            LeftPlayer.Paddle = new Paddle(PaddleSide.Left);
            RightPlayer.Paddle = new Paddle(PaddleSide.Right);
        }

        // Returns true if the player was added,
        // false otherwise (max two players will be accepeted)
        public bool TryAddPlayer(IPEndPoint playerIP)
        {
            if (State.Value == ArenaState.WaitingForPlayers)
            {
                lock (_setPlayerLock)
                {
                    // First do the left
                    if (!LeftPlayer.IsSet)
                    {
                        LeftPlayer.Endpoint = playerIP;
                        return true;
                    }

                    // Then the Right
                    if (!RightPlayer.IsSet)
                    {
                        RightPlayer.Endpoint = playerIP;
                        return true;
                    }
                }
            }

            // Couldn't add any more
            return false;
        }

        // Initializes the game objects to a default state and start a new Thread
        public void Start()
        {
            // Shift the state
            State.Value = ArenaState.WaitingForPlayers;

            // Start the internal thread on Run
            _arenaThread = new Thread(new ThreadStart(_arenaRun));
            _arenaThread.Start();
        }

        // Tells the game to stop
        public void Stop()
        {
            _stopRequested.Value = true;
        }

        // This runs in its own Thread
        // It is the actual game
        private void _arenaRun()
        {
            Console.WriteLine("[{0:000}] Waiting for players", Id);
            GameTime gameTime = new GameTime();

            // Varibables used in the switch
            TimeSpan notifyGameStartTimeout = TimeSpan.FromSeconds(2.5);
            TimeSpan sendGameStateTimeout = TimeSpan.FromMilliseconds(1000f / 30f);  // How often to update the players

            // The loop
            bool running = true;
            bool playerDropped = false;
            while (running)
            {
                // Pop off a message (if there is one)
                NetworkMessage message;
                bool haveMsg = _messages.TryDequeue(out message);

                switch (State.Value)
                {
                    case ArenaState.WaitingForPlayers:
                        if (haveMsg)
                        {
                            // Wait until we have two players
                            _handleConnectionSetup(LeftPlayer, message);
                            _handleConnectionSetup(RightPlayer, message);

                            // Check if we are ready or not
                            if (LeftPlayer.HavePaddle && RightPlayer.HavePaddle)
                            {
                                // Try sending the GameStart packet immediately
                                _notifyGameStart(LeftPlayer, new TimeSpan());
                                _notifyGameStart(RightPlayer, new TimeSpan());

                                // Shift the state
                                State.Value = ArenaState.NotifyingGameStart;
                            }
                        }
                        break;

                    case ArenaState.NotifyingGameStart:
                        // Try sending the GameStart packet
                        _notifyGameStart(LeftPlayer, notifyGameStartTimeout);
                        _notifyGameStart(RightPlayer, notifyGameStartTimeout);

                        // Check for ACK
                        if (haveMsg && (message.Packet.Type == PacketType.GameStartAck))
                        {
                            // Mark true for those who have sent something
                            if (message.Sender.Equals(LeftPlayer.Endpoint))
                                LeftPlayer.Ready = true;
                            else if (message.Sender.Equals(RightPlayer.Endpoint))
                                RightPlayer.Ready = true;
                        }

                        // Are we ready to send/received game data?
                        if (LeftPlayer.Ready && RightPlayer.Ready)
                        {
                            // Initlize some game object positions
                            _ball.Initialize();
                            LeftPlayer.Paddle.Initialize();
                            RightPlayer.Paddle.Initialize();

                            // Send a basic game state
                            _sendGameState(LeftPlayer, new TimeSpan());
                            _sendGameState(RightPlayer, new TimeSpan());

                            // Start the game timer
                            State.Value = ArenaState.InGame;
                            Console.WriteLine("[{0:000}] Starting Game", Id);
                            _gameTimer.Start();
                        }

                        break;

                    case ArenaState.InGame:
                        // Update the game timer
                        TimeSpan now = _gameTimer.Elapsed;
                        gameTime = new GameTime(now, now - gameTime.TotalGameTime);

                        // Get paddle postions from clients
                        if (haveMsg)
                        {
                            switch (message.Packet.Type)
                            {
                                case PacketType.PaddlePosition:
                                    _handlePaddleUpdate(message);
                                    break;

                                case PacketType.Heartbeat:
                                    // Respond with an ACK
                                    HeartbeatAckPacket hap = new HeartbeatAckPacket();
                                    PlayerInfo player = message.Sender.Equals(LeftPlayer.Endpoint) ? LeftPlayer : RightPlayer;
                                    _sendTo(player, hap);

                                    // Record time
                                    player.LastPacketReceivedTime = message.ReceiveTime;
                                    break;
                            }
                        }

                        //Update the game components
                        _ball.ServerSideUpdate(gameTime);
                        _checkForBallCollisions();

                        // Send the data
                        _sendGameState(LeftPlayer, sendGameStateTimeout);
                        _sendGameState(RightPlayer, sendGameStateTimeout);
                        break;
                }

                // Check for a quit from one of the clients
                if (haveMsg && (message.Packet.Type == PacketType.Bye))
                {
                    // Well, someone dropped
                    PlayerInfo player = message.Sender.Equals(LeftPlayer.Endpoint) ? LeftPlayer : RightPlayer;
                    running = false;
                    Console.WriteLine("[{0:000}] Quit detected from {1} at {2}",
                        Id, player.Paddle.Side, _gameTimer.Elapsed);

                    // Tell the other one
                    if (player.Paddle.Side == PaddleSide.Left)
                    {
                        // Left Quit, tell Right
                        if (RightPlayer.IsSet)
                            _server.SendBye(RightPlayer.Endpoint);
                    }
                    else
                    {
                        // Right Quit, tell Left
                        if (LeftPlayer.IsSet)
                            _server.SendBye(LeftPlayer.Endpoint);
                    }
                }

                // Check for timeouts
                playerDropped |= _timedOut(LeftPlayer);
                playerDropped |= _timedOut(RightPlayer);

                // Small nap
                Thread.Sleep(1);

                // Check quit values
                running &= !_stopRequested.Value;
                running &= !playerDropped;
            }

            // End the game
            _gameTimer.Stop();
            State.Value = ArenaState.GameOver;
            Console.WriteLine("[{0:000}] Game Over, total game time was {1}", Id, _gameTimer.Elapsed);

            // If the stop was requested, gracefully tell the players to quit
            if (_stopRequested.Value)
            {
                Console.WriteLine("[{0:000}] Notifying Players of server shutdown", Id);

                if (LeftPlayer.IsSet)
                    _server.SendBye(LeftPlayer.Endpoint);
                if (RightPlayer.IsSet)
                    _server.SendBye(RightPlayer.Endpoint);
            }

            // Tell the server that we're finished
            _server.NotifyDone(this);
        }

        // Gives the underlying thread 1/10 a second to finish
        public void JoinThread()
        {
            _arenaThread.Join(100);
        }

        // This is called by the server to dispatch messages to this Arena
        public void EnqueMessage(NetworkMessage nm)
        {
            _messages.Enqueue(nm);
        }

        #region Network Functions
        // Sends a packet to a player and marks other info
        private void _sendTo(PlayerInfo player, Packet packet)
        {
            _server.SendPacket(packet, player.Endpoint);
            player.LastPacketSentTime = DateTime.Now;
        }

        // Returns true if a player has timed out or not
        // If we haven't recieved a heartbeat at all from them, they're not timed out
        private bool _timedOut(PlayerInfo player)
        {
            // We haven't recorded it yet
            if (player.LastPacketReceivedTime == DateTime.MinValue)
                return false;    

            // Do math
            bool timeoutDetected = (DateTime.Now > (player.LastPacketReceivedTime.Add(_heartbeatTimeout)));
            if (timeoutDetected)
                Console.WriteLine("[{0:000}] Timeout detected on {1} Player at {2}", Id, player.Paddle.Side, _gameTimer.Elapsed);

            return timeoutDetected;
        }

        // This will Handle the initial connection setup and Heartbeats of a client
        private void _handleConnectionSetup(PlayerInfo player, NetworkMessage message)
        {
            // Make sure the message is from the correct client provided
            bool sentByPlayer = message.Sender.Equals(player.Endpoint);
            if (sentByPlayer)
            {
                // Record the last time we've heard from them
                player.LastPacketReceivedTime = message.ReceiveTime;

                // Do they need their Side? or a heartbeat ACK
                switch (message.Packet.Type)
                {
                    case PacketType.RequestJoin:
                        Console.WriteLine("[{0:000}] Join Request from {1}", Id, player.Endpoint);
                        _sendAcceptJoin(player);
                        break;

                    case PacketType.AcceptJoinAck:
                        // They acknowledged (they will send heartbeats until game start)
                        player.HavePaddle = true;
                        break;

                    case PacketType.Heartbeat:
                        // They are waiting for the game start, Respond with an ACK
                        HeartbeatAckPacket hap = new HeartbeatAckPacket();
                        _sendTo(player, hap);

                        // Incase their ACK didn't reach us
                        if (!player.HavePaddle)
                            _sendAcceptJoin(player);

                        break;
                }
            }
        }

        // Sends an AcceptJoinPacket to a player
        public void _sendAcceptJoin(PlayerInfo player)
        {
            // They need to know which paddle they are
            AcceptJoinPacket ajp = new AcceptJoinPacket();
            ajp.Side = player.Paddle.Side;
            _sendTo(player, ajp);
        }

        // Tries to notify the players of a GameStart
        // retryTimeout is how long to wait until to resending the packet
        private void _notifyGameStart(PlayerInfo player, TimeSpan retryTimeout)
        {
            // check if they are ready already
            if (player.Ready)
                return;

            // Make sure not to spam them
            if (DateTime.Now >= (player.LastPacketSentTime.Add(retryTimeout)))
            {
                GameStartPacket gsp = new GameStartPacket();
                _sendTo(player, gsp);
            }
        }

        // Sends information about the current game state to the players
        // resendTimeout is how long to wait until sending another GameStatePacket
        private void _sendGameState(PlayerInfo player, TimeSpan resendTimeout)
        {
            if (DateTime.Now >= (player.LastPacketSentTime.Add(resendTimeout)))
            {
                // Set the data
                GameStatePacket gsp = new GameStatePacket();
                gsp.LeftY = LeftPlayer.Paddle.Position.Y;
                gsp.RightY = RightPlayer.Paddle.Position.Y;
                gsp.BallPosition = _ball.Position;
                gsp.LeftScore = LeftPlayer.Paddle.Score;
                gsp.RightScore = RightPlayer.Paddle.Score;

                _sendTo(player, gsp);
            }
        }

        // Tells both of the clients to play a sound effect
        private void _playSoundEffect(string sfxName)
        {
            // Make the packet
            PlaySoundEffectPacket packet = new PlaySoundEffectPacket();
            packet.SFXName = sfxName;

            _sendTo(LeftPlayer, packet);
            _sendTo(RightPlayer, packet);
        }

        // This updates a paddle's postion from a client
        // `message.Packet.Type` must be `PacketType.PaddlePosition`
        // TODO add some "cheat detection,"
        private void _handlePaddleUpdate(NetworkMessage message)
        {
            // Only two possible players
            PlayerInfo player = message.Sender.Equals(LeftPlayer.Endpoint) ? LeftPlayer : RightPlayer;

            // Make sure we use the latest message **SENT BY THE CLIENT**  ignore it otherwise
            if (message.Packet.Timestamp > player.LastPacketReceivedTimestamp)
            {
                // record timestamp and time
                player.LastPacketReceivedTimestamp = message.Packet.Timestamp;
                player.LastPacketReceivedTime = message.ReceiveTime;

                // "cast" the packet and set data
                PaddlePositionPacket ppp = new PaddlePositionPacket(message.Packet.GetBytes());
                player.Paddle.Position.Y = ppp.Y;
            }
        }
        #endregion // Network Functions

        #region Collision Methods
        // Does all of the collision logic for the ball (including scores)
        private void _checkForBallCollisions()
        {
            // Top/Bottom
            float ballY = _ball.Position.Y;
            if ((ballY <= _ball.TopmostY) || (ballY >= _ball.BottommostY))
            {
                _ball.Speed.Y *= -1;
                _playSoundEffect("ball-hit");
            }

            // Ball left and right (the goals!)
            float ballX = _ball.Position.X;
            if (ballX <= _ball.LeftmostX)
            {
                // Right player scores! (reset ball)
                RightPlayer.Paddle.Score += 1;
                Console.WriteLine("[{0:000}] Right Player scored ({1} -- {2}) at {3}",
                    Id, LeftPlayer.Paddle.Score, RightPlayer.Paddle.Score, _gameTimer.Elapsed);
                _ball.Initialize();
                _playSoundEffect("score");
            }
            else if (ballX >= _ball.RightmostX)
            {
                // Left palyer scores! (reset ball)
                LeftPlayer.Paddle.Score += 1;
                Console.WriteLine("[{0:000}] Left Player scored ({1} -- {2}) at {3}",
                    Id, LeftPlayer.Paddle.Score, RightPlayer.Paddle.Score, _gameTimer.Elapsed);
                _ball.Initialize();
                _playSoundEffect("score");
            }

            // Ball with paddles
            PaddleCollision collision;
            if (LeftPlayer.Paddle.Collides(_ball, out collision))
                _processBallHitWithPaddle(collision);
            if (RightPlayer.Paddle.Collides(_ball, out collision))
                _processBallHitWithPaddle(collision);
            
        }

        // Modifies the ball state based on what the collision is
        private void _processBallHitWithPaddle(PaddleCollision collision)
        {
            // Safety check
            if (collision == PaddleCollision.None)
                return;

            // Increase the speed
            _ball.Speed.X *= _map((float)_random.NextDouble(), 0, 1, 1, 1.25f);
            _ball.Speed.Y *= _map((float)_random.NextDouble(), 0, 1, 1, 1.25f);

            // Shoot in the opposite direction
            _ball.Speed.X *= -1;

            // Hit with top or bottom?
            if ((collision == PaddleCollision.WithTop) || (collision == PaddleCollision.WithBottom))
                _ball.Speed.Y *= -1;

            // Play a sound on the client
            _playSoundEffect("ballHit");
        }
        #endregion // Collision Methods

        // Small utility function that maps one value range to another
        private float _map(float x, float a, float b, float p, float q)
        {
            return p + (x - a) * (q - p) / (b - a);
        }
    }
}

