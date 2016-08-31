// Filename:  GameGeometry.cs
// Author:    Benjamin N. Summerton <define-private-public>
// License:   Unlicense (https://unlicense.org/)

using System;
using Microsoft.Xna.Framework;

namespace PongGame
{
    // This is a class that contains all the information for the geometry
    // of the objects in the game (play area, paddle/ball sizes, etc.)
    public static class GameGeometry
    {
        public static readonly Point PlayArea = new Point(320, 240);    // Client area
        public static readonly Vector2 ScreenCenter                     // Center point of the screen
            = new Vector2(PlayArea.X / 2f, PlayArea.Y / 2f);
        public static readonly Point BallSize = new Point(8, 8);        // Size of Ball
        public static readonly Point PaddleSize = new Point(8, 44);     // Size of the Paddles
        public static readonly int GoalSize = 12;                       // Width behind paddle
        public static readonly float PaddleSpeed = 100f;                // Speed of the paddle, (pixels/sec)
    }
}
