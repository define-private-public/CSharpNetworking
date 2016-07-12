// Filename:  IPEndPointExample.cs        
// Author:    Benjamin N. Summerton <define-private-public>        
// License:   Unlicense (http://unlicense.org/)        

using System;
using System.Net;

namespace IPEndPointExample
{
    class IPEndPointExample
    {
        public static void Main(string[] args)
        {
            // Print some static info
            Console.WriteLine("Min. Port: {0}", IPEndPoint.MinPort);
            Console.WriteLine("Max. Port: {0}", IPEndPoint.MaxPort);
            Console.WriteLine();

            // Create one
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Parse("107.70.178.215"), 6000);
            Console.WriteLine("Address: {0}", endPoint.Address);
            Console.WriteLine("Port: {0}", endPoint.Port);
            Console.WriteLine("Address Family: {0}", endPoint.AddressFamily);
        }
    }
}
