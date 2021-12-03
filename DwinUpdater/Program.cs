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
        private static Dictionary<int, List<List<byte>>> _filesSegments;

        private static SerialPort _port;

        public static void Main(string[] args)
        {
            Console.WriteLine("DWIN UPDATER");

            Console.Write("Reading file to chunks. ");
            _filesSegments = LoadFilesSegments();
            Console.WriteLine("OK");

            Console.Write("Opening second serial port. ");
            _port = CreateSerialPort();
            _port.Open();
            Console.WriteLine("OK");

            Console.Write("Reset screen. ");
            ResetDwin();
            ReadOk();
            Console.WriteLine("OK");

            Console.Write("Await display on. ");
            Thread.Sleep(2000);

            Console.WriteLine("Starting update...");

            foreach (var f in _filesSegments)
            {
                var baseSegment = f.Key * 8;
                for (int i = 0; i < _filesSegments.Count; i++)
                {
                    var realIndex = baseSegment + (f.Value.Count - i - 1);
                    var segment = f.Value[i];
                    var bytesTotal = segment.Count;
                    var bytesLeft = segment.Count;
                    var step = (byte)240;

                    Console.WriteLine($"Update segment {realIndex}");

                    // Load every segment by 240 bytes chunks
                    while (true)
                    {
                        int chunkOffset = bytesTotal - bytesLeft;
                        byte chunkSize = bytesLeft >= step ? step : (byte)bytesLeft;

                        Console.Write(">");

                        WriteToRam(chunkOffset, chunkSize, segment);
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
            }

            Console.Write("Reset screen. ");
            ResetDwin();
            ReadOk();
            Console.WriteLine("OK");

            Thread.Sleep(15000);

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        private static Dictionary<int, List<List<byte>>> LoadFilesSegments()
        {
            var filesSegments = new Dictionary<int, List<List<byte>>>();

            foreach (var file in Directory.EnumerateFiles("DWIN_SET"))
            {
                var extension = Path.GetExtension(file);
                if (
                    extension != ".HZK" &&
                    extension != ".bin" &&
                    extension != ".icl"
                ) continue;

                var fileIndex = int.Parse(Regex.Match(file, @"\d+").Value);

                var content = File.ReadAllBytes("DWIN_SET\\32.icl");
                var fileTotal = content.Length;
                var fileLeft = content.Length;
                var segmentMaxSize = 32 * 1024;
                var segments = new List<List<byte>>();
                for (var i = 0; fileLeft > 0; i++)
                {
                    var chunkSize = fileLeft >= segmentMaxSize ? segmentMaxSize : fileLeft;
                    var segment = new byte[chunkSize];
                    Buffer.BlockCopy(
                        content,
                        fileTotal - fileLeft,
                        segment,
                        0,
                        chunkSize
                    );
                    segments.Add(new List<byte>(segment));
                    fileLeft -= chunkSize;
                }
                segments.Reverse();

                filesSegments.Add(fileIndex, segments);
            }

            return filesSegments;
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

        private static void WriteToRam(int chunk_offset, byte chunk_size, List<byte> segment)
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