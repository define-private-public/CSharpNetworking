// Filename:  UdpFileReceiver.cs        
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
    class UdpFileReceiver
    {
        #region Statics
        public static readonly int MD5ChecksumByteSize = 16;
        #endregion // Statics

        enum ReceiverState {
            NotRunning,
            RequestingFile,
            WaitingForRequestFileACK,
            WaitingForInfo,
            PreparingForTransfer,
            Transfering,
            TransferSuccessful,
        }

        // Connection data
        private UdpClient _client;
        public readonly int Port;
        public readonly string Hostname;
        private bool _shutdownRequested = false;
        private bool _running = false;

        // Receive Data
        private Dictionary<UInt32, Block> _blocksReceived = new Dictionary<UInt32, Block>();
        private Queue<UInt32> _blockRequestQueue = new Queue<UInt32>();
        private Queue<NetworkMessage> _packetQueue = new Queue<NetworkMessage>();

        // Other data
        private MD5 _hasher;

        // Constructor, sets up connection to <hostname> on <port>
        public UdpFileReceiver(string hostname, int port)
        {
            Port = port;
            Hostname = hostname;

            // Sets a default client to send/receive packets with
            _client = new UdpClient(Hostname, Port);    // will resolve DNS for us
            _hasher = MD5.Create();
        }

        // Tries to perform a graceful shutdown
        public void Shutdown()
        {
            _shutdownRequested = true;
        }

        // Tries to grab a file and download it to our local machine
        public void GetFile(string filename)
        {
            // Init the get file state
            Console.WriteLine("Requesting file: {0}", filename);
            ReceiverState state = ReceiverState.RequestingFile;
            byte[] checksum = null;
            UInt32 fileSize = 0;
            UInt32 numBlocks = 0;
            UInt32 totalRequestedBlocks = 0;
            Stopwatch transferTimer = new Stopwatch();

            // Small function to reset the transfer state
            Action ResetTransferState = new Action(() =>
                {
                    state = ReceiverState.RequestingFile;
                    checksum = null;
                    fileSize = 0;
                    numBlocks = 0;
                    totalRequestedBlocks = 0;
                    _blockRequestQueue.Clear();
                    _blocksReceived.Clear();
                    transferTimer.Reset();
                });

            // Main loop
            _running = true;
            bool senderQuit = false;
            bool wasRunning = _running;
            while (_running)
            {
                // Check for some new packets (if there are some)
                _checkForNetworkMessages();
                NetworkMessage nm = (_packetQueue.Count > 0) ? _packetQueue.Dequeue() : null;

                // In case the sender is shutdown, quit
                bool isBye = (nm == null) ? false : nm.Packet.IsBye;
                if (isBye)
                    senderQuit = true;
                
                // The state
                switch (state)
                {
                    case ReceiverState.RequestingFile:
                        // Create the REQF
                        RequestFilePacket REQF = new RequestFilePacket();
                        REQF.Filename = filename;

                        // Send it
                        byte[] buffer = REQF.GetBytes();
                        _client.Send(buffer, buffer.Length);

                        // Move the state to waiting for ACK
                        state = ReceiverState.WaitingForRequestFileACK;
                        break;

                    case ReceiverState.WaitingForRequestFileACK:
                        // If it is an ACK and the payload is the filename, we're good
                        bool isAck = (nm == null) ? false : (nm.Packet.IsAck);
                        if (isAck)
                        {
                            AckPacket ACK = new AckPacket(nm.Packet);

                            // Make sure they respond with the filename
                            if (ACK.Message == filename)
                            {
                                // They got it, shift the state
                                state = ReceiverState.WaitingForInfo;
                                Console.WriteLine("They have the file, waiting for INFO...");
                            }
                            else
                                ResetTransferState();   // Not what we wanted, reset
                        }
                        break;

                    case ReceiverState.WaitingForInfo:
                        // Verify it's file info
                        bool isInfo = (nm == null) ? false : (nm.Packet.IsInfo);
                        if (isInfo)
                        {
                            // Pull data
                            InfoPacket INFO = new InfoPacket(nm.Packet);
                            fileSize = INFO.FileSize;
                            checksum = INFO.Checksum;
                            numBlocks = INFO.BlockCount;

                            // Allocate some client side resources
                            Console.WriteLine("Received an INFO packet:");
                            Console.WriteLine("  Max block size: {0}", INFO.MaxBlockSize);
                            Console.WriteLine("  Num blocks: {0}", INFO.BlockCount);

                            // Send an ACK for the INFO
                            AckPacket ACK = new AckPacket();
                            ACK.Message = "INFO";
                            buffer = ACK.GetBytes();
                            _client.Send(buffer, buffer.Length);

                            // Shift the state to ready
                            state = ReceiverState.PreparingForTransfer;
                        }
                        break;

                    case ReceiverState.PreparingForTransfer:
                        // Prepare the request queue
                        for (UInt32 id = 1; id <= numBlocks; id++)
                            _blockRequestQueue.Enqueue(id);
                        totalRequestedBlocks += numBlocks;

                        // Shift the state
                        Console.WriteLine("Starting Transfer...");
                        transferTimer.Start();
                        state = ReceiverState.Transfering;
                        break;

                    case ReceiverState.Transfering:
                        // Send a block request
                        if (_blockRequestQueue.Count > 0)
                        {
                            // Setup a request for a Block
                            UInt32 id = _blockRequestQueue.Dequeue();
                            RequestBlockPacket REQB = new RequestBlockPacket();
                            REQB.Number = id;

                            // Send the Packet
                            buffer = REQB.GetBytes();
                            _client.Send(buffer, buffer.Length);

                            // Some handy info
                            Console.WriteLine("Sent request for Block #{0}", id);
                        }

                        // Check if we have any blocks ourselves in the queue
                        bool isSend = (nm == null) ? false : (nm.Packet.IsSend);
                        if (isSend)
                        {
                            // Get the data (and save it
                            SendPacket SEND = new SendPacket(nm.Packet);
                            Block block = SEND.Block;
                            _blocksReceived.Add(block.Number, block);

                            // Print some info
                            Console.WriteLine("Received Block #{0} [{1} bytes]", block.Number, block.Data.Length);
                        }

                        // Requeue any requests that we haven't received
                        if ((_blockRequestQueue.Count == 0) && (_blocksReceived.Count != numBlocks))
                        {
                            for (UInt32 id = 1; id <= numBlocks; id++)
                            {
                                if (!_blocksReceived.ContainsKey(id) && !_blockRequestQueue.Contains(id))
                                {
                                    _blockRequestQueue.Enqueue(id);
                                    totalRequestedBlocks++;
                                }
                            }
                        }

                        // Did we get all the block we need?  Move to the "transfer successful state."
                        if (_blocksReceived.Count == numBlocks)
                            state = ReceiverState.TransferSuccessful;
                        break;

                    case ReceiverState.TransferSuccessful:
                        transferTimer.Stop();

                        // Things were good, send a BYE message
                        Packet BYE = new Packet(Packet.Bye);
                        buffer = BYE.GetBytes();
                        _client.Send(buffer, buffer.Length);

                        Console.WriteLine("Transfer successful; it took {0:0.000}s with a success ratio of {1:0.000}.",
                            transferTimer.Elapsed.TotalSeconds, (double)numBlocks / (double)totalRequestedBlocks);
                        Console.WriteLine("Decompressing the Blocks...");

                        // Reconstruct the data
                        if (_saveBlocksToFile(filename, checksum, fileSize))
                            Console.WriteLine("Saved file as {0}.", filename);
                        else
                            Console.WriteLine("There was some trouble in saving the Blocks to {0}.", filename);

                        // And we're done here
                        _running = false;
                        break;

                }

                // Sleep
                Thread.Sleep(1);

                // Check for shutdown
                _running &= !_shutdownRequested;
                _running &= !senderQuit;
            }

            // Send a BYE message if the user wanted to cancel
            if (_shutdownRequested && wasRunning)
            {
                Console.WriteLine("User canceled transfer.");

                Packet BYE = new Packet(Packet.Bye);
                byte[] buffer = BYE.GetBytes();
                _client.Send(buffer, buffer.Length);
            }

            // If the server told us to shutdown
            if (senderQuit && wasRunning)
                Console.WriteLine("The sender quit on us, canceling the transfer.");

            ResetTransferState();           // This also cleans up collections
            _shutdownRequested = false;     // In case we shut down one download, but want to start a new one
        }

        public void Close()
        {
            _client.Close();
        }

        // Trys to fill the queue of packets
        private void _checkForNetworkMessages()
        {
            if (!_running)
                return;

            // Check that there is something available (and at least four bytes for type)
            int bytesAvailable = _client.Available;
            if (bytesAvailable >= 4)
            {
                // This will read ONE datagram (even if multiple have been received)
                IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                byte[] buffer = _client.Receive(ref ep);
                Packet p = new Packet(buffer);

                // Create the message structure and queue it up for processing
                NetworkMessage nm = new NetworkMessage();
                nm.Sender = ep;
                nm.Packet = p;
                _packetQueue.Enqueue(nm);
            }
        }

        // Trys to uncompress the blocks and save them to a file
        private bool _saveBlocksToFile(string filename, byte[] networkChecksum, UInt32 fileSize)
        {
            bool good = false;

            try
            {
                // Allocate some memory
                int compressedByteSize = 0;
                foreach (Block block in _blocksReceived.Values)
                    compressedByteSize += block.Data.Length;
                byte[] compressedBytes = new byte[compressedByteSize];

                // Reconstruct into one big block
                int cursor = 0;
                for (UInt32 id = 1; id <= _blocksReceived.Keys.Count; id++)
                {
                    Block block = _blocksReceived[id];
                    block.Data.CopyTo(compressedBytes, cursor);
                    cursor += Convert.ToInt32(block.Data.Length);
                }

                // Now save it
                using (MemoryStream uncompressedStream = new MemoryStream())
                using (MemoryStream compressedStream = new MemoryStream(compressedBytes))
                using (DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress))
                {
                    deflateStream.CopyTo(uncompressedStream);

                    // Verify checksums
                    uncompressedStream.Position = 0;
                    byte[] checksum = _hasher.ComputeHash(uncompressedStream);
                    if (!Enumerable.SequenceEqual(networkChecksum, checksum))
                        throw new Exception("Checksum of uncompressed blocks doesn't match that of INFO packet.");

                    // Write it to the file
                    uncompressedStream.Position = 0;
                    using (FileStream fileStream = new FileStream(filename, FileMode.Create))
                        uncompressedStream.CopyTo(fileStream);
                }

                good = true;
            }
            catch (Exception e)
            {
                // Crap...
                Console.WriteLine("Could not save the blocks to \"{0}\", reason:", filename);
                Console.WriteLine(e.Message);
            }

            return good;
        }





        #region Program Execution
        public static UdpFileReceiver fileReceiver;

        public static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            fileReceiver?.Shutdown();
        }

        public static void Main(string[] args)
        {
            // setup the receiver
            string hostname = "localhost";//args[0].Trim();
            int port = 6000;//int.Parse(args[1].Trim());
            string filename = "short_message.txt";//args[2].Trim();
            fileReceiver = new UdpFileReceiver(hostname, port);

            // Add the Ctrl-C handler
            Console.CancelKeyPress += InterruptHandler;

            // Get a file
            fileReceiver.GetFile(filename);
            fileReceiver.Close();
        }
        #endregion // Program Execution
    }
}
