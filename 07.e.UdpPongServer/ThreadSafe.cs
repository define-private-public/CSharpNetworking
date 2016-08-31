// Filename:  ThreadSafe.cs
// Author:    Benjamin N. Summerton <define-private-public>
// License:   Unlicense (https://unlicense.org/)

using System;

namespace PongGame
{
    // Makes one variable thread safe
    public class ThreadSafe<T>
    {
        // The data & lock
        private T _value;
        private object _lock = new object();

        // How to get & set the data
        public T Value
        {
            get
            {
                lock (_lock)
                    return _value;
            }

            set {
                lock (_lock)
                    _value = value;
            }
        }
            
        // Initializes the value
        public ThreadSafe(T value = default(T))
        {
            Value = value;
        }
    }
}
