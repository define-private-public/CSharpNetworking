// Filename:  CompressionExample.cs        
// Author:    Benjamin N. Summerton <define-private-public>        
// License:   Unlicense (http://unlicense.org/)      

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace Compression
{
    class CompressionExample
    {
        // Tiny helper to get a number in MegaBytes
        public static float ComputeSizeInMB(long size)
        {
            return (float)size / 1024f / 1024f;
        }

        public static void Main(string[] args)
        {
            // Our testing file
            string fileToCompress = "film.mp4";
            byte[] uncompressedBytes = File.ReadAllBytes(fileToCompress);

            // Benchmarking
            Stopwatch timer = new Stopwatch();

            // Display some information
            long uncompressedFileSize = uncompressedBytes.LongLength;
            Console.WriteLine("{0} uncompressed is {1:0.0000} MB large.",
                fileToCompress,
                ComputeSizeInMB(uncompressedFileSize));

            // Compress it using Deflate (optimal)
            using (MemoryStream compressedStream = new MemoryStream())
            {
                // init
                DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionLevel.Optimal, true);

                // Run the compression
                timer.Start();
                deflateStream.Write(uncompressedBytes, 0, uncompressedBytes.Length);
                deflateStream.Close();
                timer.Stop();

                // Print some info
                long compressedFileSize = compressedStream.Length;
                Console.WriteLine("Compressed using DeflateStream (Optimal): {0:0.0000} MB [{1:0.00}%] in {2}ms",
                    ComputeSizeInMB(compressedFileSize),
                    100f * (float)compressedFileSize / (float)uncompressedFileSize,
                    timer.ElapsedMilliseconds);

                // cleanup
                timer.Reset();
            }

            // Compress it using Deflate (fast)
            using (MemoryStream compressedStream = new MemoryStream())
            {
                // init
                DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionLevel.Fastest, true);

                // Run the compression
                timer.Start();
                deflateStream.Write(uncompressedBytes, 0, uncompressedBytes.Length);
                deflateStream.Close();
                timer.Stop();

                // Print some info
                long compressedFileSize = compressedStream.Length;
                Console.WriteLine("Compressed using DeflateStream (Fast): {0:0.0000} MB [{1:0.00}%] in {2}ms",
                    ComputeSizeInMB(compressedFileSize),
                    100f * (float)compressedFileSize / (float)uncompressedFileSize,
                    timer.ElapsedMilliseconds);

                // cleanup
                timer.Reset();
            }

            // Compress it using GZip (save it)
            string savedArchive = fileToCompress + ".gz";
            using (MemoryStream compressedStream = new MemoryStream())
            {
                // init
                GZipStream gzipStream = new GZipStream(compressedStream, CompressionMode.Compress, true);

                // Run the compression
                timer.Start();
                gzipStream.Write(uncompressedBytes, 0, uncompressedBytes.Length);
                gzipStream.Close();
                timer.Stop();

                // Print some info
                long compressedFileSize = compressedStream.Length;
                Console.WriteLine("Compressed using GZipStream: {0:0.0000} MB [{1:0.00}%] in {2}ms",
                    ComputeSizeInMB(compressedFileSize),
                    100f * (float)compressedFileSize / (float)uncompressedFileSize,
                    timer.ElapsedMilliseconds);

                // Save it
                using (FileStream saveStream = new FileStream(savedArchive, FileMode.Create))
                {
                    compressedStream.Position = 0;
                    compressedStream.CopyTo(saveStream);
                }

                // cleanup
                timer.Reset();
            }
        }
    }
}
