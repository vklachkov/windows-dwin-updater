using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Security.Authentication.ExtendedProtection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DwinUpdater
{
    internal static class Program
    {
        private static List<List<byte>> _segments;

        private static SerialPort _port;

        public static void Main(string[] args)
        {
            Console.WriteLine("DWIN UPDATER");

            Console.Write("Reading file to chunks. ");
            _segments = LoadSegments();
            Console.WriteLine("OK");

            Console.Write("Opening second serial port. ");
            _port = CreateSerialPort();
            Console.WriteLine("OK");

            // Reset screen
            Console.Write("Reset screen. ");
            ResetDwin();
            ReadOk();
            Console.WriteLine("OK");

            // Wait screen
            Console.Write("Await display on. ");
            Thread.Sleep(2000);

            // Start update
            Console.WriteLine("Starting update...");

            // Iterate all
            var baseSegment = 32 * 8;
            for (int i = 0; i < _segments.Count; i++)
            {
                var realIndex = baseSegment + (_segments.Count - i - 1);
                var segment = _segments[i];
                var bytesTotal = segment.Count;
                var bytesLeft = segment.Count;
                var step = (byte)240;

                Console.WriteLine($"Update segment {realIndex:X3}");

                // Load every segment by 240 bytes chunks
                while (true)
                {
                    int chunkOffset = bytesTotal - bytesLeft;
                    byte chunkSize = bytesLeft >= step ? step : (byte)bytesLeft;

                    Console.Write(">");

                    WriteToRam(chunkOffset, chunkSize, ref segment);
                    ReadOk();

                    Console.Write("<");

                    bytesLeft -= chunkSize;
                    if (bytesLeft <= 0) break;
                }

                Console.WriteLine();

                Console.Write("Write to flash... ");
                WriteFromRamToFlash(realIndex);
                Console.WriteLine("OK");
            }

            // Reset screen
            Console.Write("Reset screen. ");
            ResetDwin();
            ReadOk();
            Console.WriteLine("OK");

            // Wait screen
            Console.Write("End... ");
        }

        private static List<List<byte>> LoadSegments()
        {
            var chunks = new List<List<byte>>();
            var content = File.ReadAllBytes("DWIN_SET\\32.icl");
            var contentTotal = content.Length;
            var contentLeft = content.Length;

            var _32kb = 32 * 1024;
            for (var i = 0; contentLeft > 0; i++)
            {
                var chunkSize = contentLeft >= _32kb ? _32kb : contentLeft;
                var chunk = new byte[chunkSize];
                Buffer.BlockCopy(
                    content,
                    contentTotal - contentLeft,
                    chunk,
                    0,
                    chunkSize
                );
                chunks.Add(new List<byte>(chunk));
                contentLeft -= chunkSize;
            }

            chunks.Reverse();

            return chunks;
        }

        private static SerialPort CreateSerialPort()
        {
            var ports = SerialPort.GetPortNames();
            return new SerialPort
            {
                PortName = ports[1],
                BaudRate = 115200,
                Parity = Parity.None,
            };
        }

        private static void ResetDwin()
        {
            var reset = new byte[] { 0x5A, 0xA5, 0x07, 0x82, 0x00, 0x04, 0x55, 0xAA, 0x5A, 0xA5 };
            _port.Write(reset, 0, reset.Length);
        }

        private static void ReadOk()
        {
            while (true)
            {
                int available = _port.BytesToRead;
                if (available < 6)
                {
                    Thread.Sleep(2);
                    continue;
                }

                if (_port.ReadByte() != 0x5A) continue;
                if (_port.ReadByte() != 0xA5) continue;
                if (_port.ReadByte() != 0x03) continue;
                if (_port.ReadByte() != 0x82) continue;
                if (_port.ReadByte() != 0x4F) continue;
                if (_port.ReadByte() != 0x4B) continue;

                break;
            }
        }

        private static void WriteToRam(int chunk_offset, byte chunk_size, ref List<byte> segment)
        {
            var address = 0x8000 + (chunk_offset / 2);

            var buffer = new byte[6];
            buffer[0] = 0x5A;
            buffer[1] = 0xA5;
            buffer[2] = (byte)(0x01 + 0x02 + chunk_size); // Data length + address + mode
            buffer[3] = 0x82;
            buffer[4] = (byte)((address & 0xFF00) >> 8);
            buffer[5] = (byte)((address & 0x00FF) >> 0);

            _port.Write(buffer, 0, buffer.Length);
            _port.Write(segment.ToArray(), chunk_offset, chunk_size);
        }

        private static void WriteFromRamToFlash(int segment)
        {
            var buffer = new byte[]
            {
                /* Magic */
                0x5A, 0xA5,

                /* Size of package (exclude this and magic bytes) */
                0x0F,

                /* Mode: Write */
                0x82,

                /* Address */
                0x00, 0xAA,

                /* Fixed */
                0x5A, 0x02,

                /* 32kb block address, 0x0000-0x01FF */
                (byte) ((segment & 0xFF00) >> 8), (byte) (segment & 0x00FF),

                /* First address of the data stored in the data variable space (must be even) */
                0x80, 0x00,

                /* Timing */
                0x17, 0x70,

                /* Always zeros */
                0x00, 0x00, 0x00, 0x00
            };

            _port.Write(buffer, 0, buffer.Length);

            // FIXME: Check status
        }
    }
}