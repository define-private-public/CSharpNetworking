// Filename:  IPAddressExample.cs        
// Author:    Benjamin N. Summerton <define-private-public>        
// License:   Unlicense (http://unlicense.org/)        

using System;
using System.Net;

namespace IPAddressExample
{
    class IPAddressExample
    {
        public static readonly byte[] ipv6 = {
            0x20, 0x01,
            0x0d, 0xb8,
            0x00, 0x00,
            0x00, 0x42,
            0x00, 0x00,
            0x8a, 0x2e,
            0x03, 0x70,
            0x73, 0x34
        };

        public static void Main(string[] args)
        {
            // Make an IP address
            IPAddress ipAddr;
            ipAddr = new IPAddress(new byte[] {107, 70, 178, 215});  // IPv4, byte array
            //ipAddr = new IPAddress(ipv6);                          // IPv6, byte array
            //ipAddr = IPAddress.Parse("127.0.0.1");                 // IPv4, string
            //ipAddr = IPAddress.Parse("::1");                       // IPv6, string

            // Print some info
            Console.WriteLine("IPAddress: {0}", ipAddr);
            Console.WriteLine("Address Family: {0}", ipAddr.AddressFamily);
            Console.WriteLine("Loopback: {0}", IPAddress.IsLoopback(ipAddr));
        }
    }
}
