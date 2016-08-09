// Filename:  UdpFileSender.cs        
// Author:    Benjamin N. Summerton <define-private-public>        
// License:   Unlicense (http://unlicense.org/)      

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace UdpFileTransfer
{
    public class UdpFileSender
    {
        #region Statics
        public static readonly UInt32 MaxBlockSize = 8 * 1024;   // 8KB
        #endregion // Statics

        enum SenderState
        {
            NotRunning,
            WaitingForFileRequest,
            PreparingFileForTransfer,
            SendingFileInfo,
            WaitingForInfoACK,
            Transfering
        }

        // Connection data
        private UdpClient _client;
        public readonly int Port;
        public bool Running { get; private set; } = false;

        // Transfer data
        public readonly string FilesDirectory;
        private HashSet<string> _transferableFiles;
        private Dictionary<UInt32, Block> _blocks = new Dictionary<UInt32, Block>();
        private Queue<NetworkMessage> _packetQueue = new Queue<NetworkMessage>();

        // Other stuff
        private MD5 _hasher;

        // Constructor, creates a UdpClient on <port>
        public UdpFileSender(string filesDirectory, int port)
        {
            FilesDirectory = filesDirectory;
            Port = port;
            _client = new UdpClient(Port, AddressFamily.InterNetwork);      // Bind in IPv4
            _hasher = MD5.Create();
        }

        // Prepares the Sender for file transfers
        public void Init()
        {
            // Scan files (only the top directory)
            List<string> files = new List<string>(Directory.EnumerateFiles(FilesDirectory));
            _transferableFiles = new HashSet<string>(files.Select(s => s.Substring(FilesDirectory.Length + 1)));

            // Make sure we have at least one to send
            if (_transferableFiles.Count != 0)
            {
                // Modify the state
                Running = true;

                // Print info
                Console.WriteLine("I'll transfer these files:");
                foreach (string s in _transferableFiles)
                    Console.WriteLine("  {0}", s);
            }
            else
                Console.WriteLine("I don't have any files to transfer.");
        }

        // signals for a (graceful) shutdown)
        public void Shutdown()
        {
            Running = false;
        }

        // Main loop of the sender
        public void Run()
        {
            // Transfer state
            SenderState state = SenderState.WaitingForFileRequest;
            string requestedFile = "";
            IPEndPoint receiver = null;

            // This is a handly little function to reset the transfer state
            Action ResetTransferState = new Action(() =>
                {
                    state = SenderState.WaitingForFileRequest;
                    requestedFile = "";
                    receiver = null;
                    _blocks.Clear();
                });

            while (Running)
            {
                // Check for some new messages
                _checkForNetworkMessages();
                NetworkMessage nm = (_packetQueue.Count > 0) ? _packetQueue.Dequeue() : null;

                // Check to see if we have a BYE
                bool isBye = (nm == null) ? false : nm.Packet.IsBye;
                if (isBye)
                {
                    // Set back to the original state
                    ResetTransferState();
                    Console.WriteLine("Received a BYE message, waiting for next client.");
                }

                // Do an action depending on the current state
                switch (state)
                {
                    case SenderState.WaitingForFileRequest:
                        // Check to see that we got a file request
                        
                        // If there was a packet, and it's a request file, send and ACK and switch the state
                        bool isRequestFile = (nm == null) ? false : nm.Packet.IsRequestFile;
                        if (isRequestFile)
                        {
                            // Prepare the ACK
                            RequestFilePacket REQF = new RequestFilePacket(nm.Packet);
                            AckPacket ACK = new AckPacket();
                            requestedFile = REQF.Filename;

                            // Print info
                            Console.WriteLine("{0} has requested file file \"{1}\".", nm.Sender, requestedFile);

                            // Check that we have the file
                            if (_transferableFiles.Contains(requestedFile))
                            {
                                // Mark that we have the file, save the sender as our current receiver
                                receiver = nm.Sender;
                                ACK.Message = requestedFile;
                                state = SenderState.PreparingFileForTransfer;

                                Console.WriteLine("  We have it.");
                            }
                            else
                                ResetTransferState();

                            // Send the message
                            byte[] buffer = ACK.GetBytes();
                            _client.Send(buffer, buffer.Length, nm.Sender);
                        }
                        break;

                    case SenderState.PreparingFileForTransfer:
                        // Using the requested file, prepare it in memory
                        byte[] checksum;
                        UInt32 fileSize;
                        if (_prepareFile(requestedFile, out checksum, out fileSize))
                        {
                            // It's good, send an info Packet
                            InfoPacket INFO = new InfoPacket();
                            INFO.Checksum = checksum;
                            INFO.FileSize = fileSize;
                            INFO.MaxBlockSize = MaxBlockSize;
                            INFO.BlockCount = Convert.ToUInt32(_blocks.Count);

                            // Send it
                            byte[] buffer = INFO.GetBytes();
                            _client.Send(buffer, buffer.Length, receiver);

                            // Move the state
                            Console.WriteLine("Sending INFO, waiting for ACK...");
                            state = SenderState.WaitingForInfoACK;
                        }
                        else
                            ResetTransferState();   // File not good, reset the state
                        break;

                    case SenderState.WaitingForInfoACK:
                        // If it is an ACK and the payload is the filename, we're good
                        bool isAck = (nm == null) ? false : (nm.Packet.IsAck);
                        if (isAck)
                        {
                            AckPacket ACK = new AckPacket(nm.Packet);
                            if (ACK.Message == "INFO")
                            {
                                Console.WriteLine("Starting Transfer...");
                                state = SenderState.Transfering;
                            }
                        }
                        break;

                    case SenderState.Transfering:
                        // If there is a block request, send it
                        bool isRequestBlock = (nm == null) ? false : nm.Packet.IsRequestBlock;
                        if (isRequestBlock)
                        {
                            // Pull out data
                            RequestBlockPacket REQB = new RequestBlockPacket(nm.Packet);
                            Console.WriteLine("Got request for Block #{0}", REQB.Number);

                            // Create the response packet
                            Block block = _blocks[REQB.Number];
                            SendPacket SEND = new SendPacket();
                            SEND.Block = block;

                            // Send it
                            byte[] buffer = SEND.GetBytes();
                            _client.Send(buffer, buffer.Length, nm.Sender);
                            Console.WriteLine("Sent Block #{0} [{1} bytes]", block.Number, block.Data.Length); 
                        }
                        break;
                }

                Thread.Sleep(1);
            }

            // If there was a receiver set, that means we need to notify it to shutdown
            if (receiver != null)
            {
                Packet BYE = new Packet(Packet.Bye);
                byte[] buffer = BYE.GetBytes();
                _client.Send(buffer, buffer.Length, receiver);
            }

            state = SenderState.NotRunning;
        }

        // Shutsdown the underlying UDP client
        public void Close()
        {
            _client.Close();
        }

        // Trys to fill the queue of packets
        private void _checkForNetworkMessages()
        {
            if (!Running)
                return;

            // Check that there is something available (and at least four bytes for type)
            int bytesAvailable = _client.Available;
            if (bytesAvailable >= 4)
            {
                // This will read ONE datagram (even if multiple have been received)
                IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                byte[] buffer = _client.Receive(ref ep);

                // Create the message structure and queue it up for processing
                NetworkMessage nm = new NetworkMessage();
                nm.Sender = ep;
                nm.Packet = new Packet(buffer);
                _packetQueue.Enqueue(nm);
            }
        }

        // Loads the file into the blocks, returns true if the requested file is ready
        private bool _prepareFile(string requestedFile, out byte[] checksum, out UInt32 fileSize)
        {
            Console.WriteLine("Preparing the file to send...");
            bool good = false;
            fileSize = 0;

            try
            {
                // Read it in & compute a checksum of the original file
                byte[] fileBytes = File.ReadAllBytes(Path.Combine(FilesDirectory, requestedFile));
                checksum = _hasher.ComputeHash(fileBytes);
                fileSize = Convert.ToUInt32(fileBytes.Length);
                Console.WriteLine("{0} is {1} bytes large.", requestedFile, fileSize);

                // Compress it
                Stopwatch timer = new Stopwatch();
                using (MemoryStream compressedStream = new MemoryStream())
                {
                    // Perform the actual compression
                    DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionMode.Compress, true);
                    timer.Start();
                    deflateStream.Write(fileBytes, 0, fileBytes.Length);
                    deflateStream.Close();
                    timer.Stop();

                    // Put it into blocks
                    compressedStream.Position = 0;
                    long compressedSize = compressedStream.Length;
                    UInt32 id = 1;
                    while (compressedStream.Position < compressedSize)
                    {
                        // Grab a chunk
                        long numBytesLeft = compressedSize - compressedStream.Position;
                        long allocationSize = (numBytesLeft > MaxBlockSize) ? MaxBlockSize : numBytesLeft;
                        byte[] data = new byte[allocationSize];
                        compressedStream.Read(data, 0, data.Length);

                        // Create a new block
                        Block b = new Block(id++);
                        b.Data = data;
                        _blocks.Add(b.Number, b);
                    }

                    // Print some info and say we're good
                    Console.WriteLine("{0} compressed is {1} bytes large in {2:0.000}s.", requestedFile, compressedSize, timer.Elapsed.TotalSeconds);
                    Console.WriteLine("Sending the file in {0} blocks, using a max block size of {1} bytes.", _blocks.Count, MaxBlockSize);
                    good = true;
                }
            }
            catch (Exception e)
            {
                // Crap...
                Console.WriteLine("Could not prepare the file for transfer, reason:");
                Console.WriteLine(e.Message);

                // Reset a few things
                _blocks.Clear();
                checksum = null;
            }

            return good;
        }




        #region Program Execution
        public static UdpFileSender fileSender;

        public static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            fileSender?.Shutdown();
        }

        public static void Main(string[] args)
        {
            // Setup the sender
            string filesDirectory = "Files";//args[0].Trim();
            int port = 6000;//int.Parse(args[1].Trim());
            fileSender = new UdpFileSender(filesDirectory, port);

            // Add the Ctrl-C handler
            Console.CancelKeyPress += InterruptHandler;

            // Run it
            fileSender.Init();
            fileSender.Run();
            fileSender.Close();
        }
        #endregion // Program Execution
    }
}
