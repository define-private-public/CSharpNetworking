// Filename:  GuessMyNumberGame.cs        
// Author:    Benjamin N. Summerton <define-private-public>        
// License:   Unlicense (http://unlicense.org/)        

using System;
using System.Net.Sockets;
using System.Threading;

namespace TcpGames
{
    public class GuessMyNumberGame : IGame
    {
        // Objects for the game
        private TcpGamesServer _server;
        private TcpClient _player;
        private Random _rng;
        private bool _needToDisconnectClient = false;

        // Name of the game
        public string Name
        {
            get { return "Guess My Number"; }
        }

        // Just needs only one player
        public int RequiredPlayers
        {
            get { return 3; }
        }                
                
        // Constructor
        public GuessMyNumberGame(TcpGamesServer server)
        {
            _server = server;
            _rng = new Random();
        }

        // Adds only a single player to the game
        public bool AddPlayer(TcpClient client)
        {
            // Make sure only one player was added
            if (_player == null)
            {
                _player = client;
                return true;
            }

            return false;
        }

        // If the client who disconnected is ours, we need to quit our game
        public void DisconnectClient(TcpClient client)
        {
            _needToDisconnectClient = (client == _player);
        }

        // Main loop of the Game
        // Packets are sent sent synchronously though
        public void Run()
        {
            // Make sure we have a player
            bool running = (_player != null);
            if (running)
            {
                // Send a instruction packet
                Packet introPacket = new Packet("message",
                    "Welcome player, I want you to guess my number.\n" +
                    "It's somewhere between (and including) 1 and 100.\n");
                _server.SendPacket(_player, introPacket).GetAwaiter().GetResult();
            }
            else
                return;

            // Should be [1, 100]
            int theNumber = _rng.Next(1, 101);
            Console.WriteLine("Our number is: {0}", theNumber);

            // Some bools for game state
            bool correct = false;
            bool clientConncted = true;
            bool clientDisconnectedGracefully = false;

            // Main game loop
            while (running)
            {
                // Poll for input
                Packet inputPacket = new Packet("input", "Your guess: ");
                _server.SendPacket(_player, inputPacket).GetAwaiter().GetResult();

                // Read their answer
                Packet answerPacket = null;
                while (answerPacket == null)
                {
                    answerPacket = _server.ReceivePacket(_player).GetAwaiter().GetResult();
                    Thread.Sleep(10);
                }

                // Check for graceful disconnect
                if (answerPacket.Command == "bye")
                {
                    _server.HandleDisconnectedClient(_player);
                    clientDisconnectedGracefully = true;
                }

                // Check input
                if (answerPacket.Command == "input")
                {
                    Packet responsePacket = new Packet("message");

                    int theirGuess;
                    if (int.TryParse(answerPacket.Message, out theirGuess))
                    {

                        // See if they won
                        if (theirGuess == theNumber)
                        {
                            correct = true;
                            responsePacket.Message = "Correct!  You win!\n";
                        }
                        else if (theirGuess < theNumber)
                            responsePacket.Message = "Too low.\n";
                        else if (theirGuess > theNumber)
                            responsePacket.Message = "Too high.\n";
                    }
                    else
                        responsePacket.Message = "That wasn't a valid number, try again.\n";

                    // Send the message
                    _server.SendPacket(_player, responsePacket).GetAwaiter().GetResult();
                }

                // Take a small nap
                Thread.Sleep(10);

                // If they aren't correct, keep them here
                running &= !correct;

                // Check for disconnect, may have happend gracefully before
                if (!_needToDisconnectClient && !clientDisconnectedGracefully)
                    clientConncted &= !TcpGamesServer.IsDisconnected(_player);
                else
                    clientConncted = false;
                
                running &= clientConncted;
            }

            // Thank the player and disconnect them
            if (clientConncted)
                _server.DisconnectClient(_player, "Thanks for playing \"Guess My Number\"!");
            else
                Console.WriteLine("Client disconnected from game.");

            Console.WriteLine("Ending a \"{0}\" game.", Name);
        }
    }
}

