// Filename:  Ball.cs
// Author:    Benjamin N. Summerton <define-private-public>
// License:   Unlicense (https://unlicense.org/)

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;

namespace PongGame
{
    // The ball that's bounced around
    public class Ball
    {
        // Statics
        public static Vector2 InitialSpeed = new Vector2(60f, 60f);

        // Private data members
        private Texture2D _sprite;
        private Random _random = new Random();     // Random Number Generator

        // Public data members
        public Vector2 Position = new Vector2();
        public Vector2 Speed;
        public int LeftmostX { get; private set; }      // Bounds
        public int RightmostX { get; private set; }
        public int TopmostY { get; private set; }
        public int BottommostY { get; private set; }

        // What gets hit
        public Rectangle CollisionArea
        {
            get { return new Rectangle(Position.ToPoint(), GameGeometry.BallSize); }
        }

        public void LoadContent(ContentManager content)
        {
            _sprite = content.Load<Texture2D>("ball.png");
        }

        // this is used to reset the postion of the ball to the center of the board
        public void Initialize()
        {
            // Center the ball
            Rectangle playAreaRect = new Rectangle(new Point(0, 0), GameGeometry.PlayArea);
            Position = playAreaRect.Center.ToVector2();
            Position = Vector2.Subtract(Position, GameGeometry.BallSize.ToVector2() / 2f);

            // Set the velocity
            Speed = InitialSpeed;

            // Randomize direction
            if (_random.Next() % 2 == 1)
                Speed.X *= -1;
            if (_random.Next() % 2 == 1)
                Speed.Y *= -1;

            // Set bounds
            LeftmostX = 0;
            RightmostX = playAreaRect.Width - GameGeometry.BallSize.X;
            TopmostY = 0;
            BottommostY = playAreaRect.Height - GameGeometry.BallSize.Y;
        }

        // Moves the ball, should only be called on the server
        public void ServerSideUpdate(GameTime gameTime)
        {
            float timeDelta = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Add the distance
            Position = Vector2.Add(Position, timeDelta * Speed);
        }

        // Draws the ball to the screen, only called on the client
        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            spriteBatch.Draw(_sprite, Position);
        }
    }
}

