// Filename:  DnsExample.cs        
// Author:    Benjamin N. Summerton <define-private-public>        
// License:   Unlicense (http://unlicense.org/)        

using System;
using System.Net;

namespace DnsExample
{
    class DnsExample
    {
        public static string domain = "16bpp.net";

        public static void Main(string[] args)
        {
            // Print a little info about us
            Console.WriteLine("Local Hostname: {0}", Dns.GetHostName());
            Console.WriteLine();

            // Get DNS info synchronously
            IPHostEntry hostInfo = Dns.GetHostEntry(domain);

            // Print aliases
            if (hostInfo.Aliases.Length > 0)
            {
                Console.WriteLine("Aliases for {0}:", hostInfo.HostName);
                foreach (string alias in hostInfo.Aliases)
                    Console.WriteLine("  {0}", alias);
                Console.WriteLine();
            }

            // Print IP addresses
            if (hostInfo.AddressList.Length > 0)
            {
                Console.WriteLine("IP Addresses for {0}", hostInfo.HostName);
                foreach(IPAddress addr in hostInfo.AddressList)
                    Console.WriteLine("  {0}", addr);
                Console.WriteLine();
            }
        }
    }
}

