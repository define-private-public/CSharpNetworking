// Filename:  Paddle.cs
// Author:    Benjamin N. Summerton <define-private-public>
// License:   Unlicense (https://unlicense.org/)

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Input;

namespace PongGame
{
    public enum PaddleSide : uint
    {
        None,
        Left,
        Right
    };

    // Type of collision with paddle
    public enum PaddleCollision
    {
        None,
        WithTop,
        WithFront,
        WithBottom
    };

    // This is the Paddle class for the Server
    public class Paddle
    {
        // Private data members
        private Texture2D _sprite;
        private DateTime _lastCollisiontime = DateTime.MinValue;
        private TimeSpan _minCollisionTimeGap = TimeSpan.FromSeconds(0.2);

        // Public data members
        public readonly PaddleSide Side;
        public int Score = 0;
        public Vector2 Position = new Vector2();
        public int TopmostY { get; private set; }               // Bounds
        public int BottommostY { get; private set; }

        #region Collision objects
        public Rectangle TopCollisionArea
        {
            get { return new Rectangle(Position.ToPoint(), new Point(GameGeometry.PaddleSize.X, 4)); }
        }

        public Rectangle BottomCollisionArea
        {
            get
            {
                return new Rectangle(
                    (int)Position.X, FrontCollisionArea.Bottom,
                    GameGeometry.PaddleSize.X, 4
                );
            }
        }

        public Rectangle FrontCollisionArea
        {
            get
            {
                Point pos = Position.ToPoint();
                pos.Y += 4;
                Point size = new Point(GameGeometry.PaddleSize.X, GameGeometry.PaddleSize.Y - 8);

                return new Rectangle(pos, size);
            }
        }
        #endregion // Collision objects

        // Sets which side the paddle is
        public Paddle(PaddleSide side)
        {
            Side = side;
        }

        public void LoadContent(ContentManager content)
        {
            _sprite = content.Load<Texture2D>("paddle.png");
        }

        // Puts the paddle in the middle of where it can move
        public void Initialize()
        {
            // Figure out where to place the paddle
            int x;
            if (Side == PaddleSide.Left)
                x = GameGeometry.GoalSize;
            else if (Side == PaddleSide.Right)
                x = GameGeometry.PlayArea.X - GameGeometry.PaddleSize.X - GameGeometry.GoalSize;
            else
                throw new Exception("Side is not `Left` or `Right`");

            Position = new Vector2(x, (GameGeometry.PlayArea.Y / 2) - (GameGeometry.PaddleSize.Y / 2));
            Score = 0;

            // Set bounds
            TopmostY = 0;
            BottommostY = GameGeometry.PlayArea.Y - GameGeometry.PaddleSize.Y;
        }

        // Moves the paddle based on user input (called on Client)
        public void ClientSideUpdate(GameTime gameTime)
        {
            float timeDelta = (float)gameTime.ElapsedGameTime.TotalSeconds;
            float dist = timeDelta * GameGeometry.PaddleSpeed;

            // Check Up & Down keys
            KeyboardState kbs = Keyboard.GetState();
            if (kbs.IsKeyDown(Keys.Up))
                Position.Y -= dist;
            else if (kbs.IsKeyDown(Keys.Down))
                Position.Y += dist;

            // bounds checking
            if (Position.Y < TopmostY)
                Position.Y = TopmostY;
            else if (Position.Y > BottommostY)
                Position.Y = BottommostY;
        }

        public void Draw(GameTime gameTime, SpriteBatch spriteBatch)
        {
            spriteBatch.Draw(_sprite, Position);
        }

        // Sees what part of the Paddle collises with the ball (if it does)
        public bool Collides(Ball ball, out PaddleCollision typeOfCollision)
        {
            typeOfCollision = PaddleCollision.None;

            // Make sure enough time has passed for a new collisions
            // (this prevents a bug where a user can build up a lot of speed in the ball)
            if (DateTime.Now < (_lastCollisiontime.Add(_minCollisionTimeGap)))
                return false;

            // Top & bottom get first priority
            if (ball.CollisionArea.Intersects(TopCollisionArea))
            {
                typeOfCollision =  PaddleCollision.WithTop;
                _lastCollisiontime = DateTime.Now;
                return true;
            }

            if (ball.CollisionArea.Intersects(BottomCollisionArea))
            {
                typeOfCollision =  PaddleCollision.WithBottom;
                _lastCollisiontime = DateTime.Now;
                return true;
            }

            // And check the front
            if (ball.CollisionArea.Intersects(FrontCollisionArea))
            {
                typeOfCollision =  PaddleCollision.WithFront;
                _lastCollisiontime = DateTime.Now;
                return true;
            }

            // Nope, nothing
            return false;
        }
    }
}

