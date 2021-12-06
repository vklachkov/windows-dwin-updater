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
        private static SerialPort _port;

        private static byte[] _fileContent;

        private static FileStream _binaryOutputStream;

        public static void Main(string[] args)
        {
            Console.WriteLine("DWIN UPDATER");

            String timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            _binaryOutputStream = new FileStream($"Dump-{timestamp}.bin", FileMode.Create, FileAccess.Write);

            Console.Write("Reading file content. ");
            _fileContent = LoadFileContent();
            Console.WriteLine("OK");

            Console.Write("Opening second serial port. ");
            //_port = CreateSerialPort();
            //_port.Open();
            Console.WriteLine("OK");

            Console.Write("Reset screen. ");
            //ResetDwin();
            //ReadOk();
            Console.WriteLine("OK");

            //_port.Write(new byte[] { 0x5A, 0xA5, 0x07, 0x82, 0x00, 0x84, 0x5A, 0x01, 0x00, 0x02 }, 0, 10);
            //ReadOk();

            Console.Write("Await display on. ");
            //Thread.Sleep(2000);

            Console.WriteLine("Starting update...");

            var segmentSize = 32 * 1024;
            var chunkSize = (byte)240;

            var segmentsCount = (int)Math.Ceiling((double)_fileContent.Length / segmentSize);
            var previewSegmentSize = _fileContent.Length - (segmentsCount - 1) * (32 * 1024);

            for (int s = (segmentsCount - 1); s >= 0; s--)
            {
                var actualSegmentSize = (s == segmentsCount - 1) ? previewSegmentSize : segmentSize;
                var actualSegmentOffset = s * segmentSize;

                var chunksCount = (int)Math.Ceiling((double)actualSegmentSize / chunkSize);

                var segmentBytesLeft = actualSegmentSize;

                for (var c = 0; c < chunksCount; c++)
                {
                    int actualChunkOffset = actualSegmentSize - segmentBytesLeft;
                    byte actualChunkSize = segmentBytesLeft >= chunkSize ? chunkSize : (byte)segmentBytesLeft;

                    byte[] chunk = new byte[actualChunkSize];
                    Buffer.BlockCopy(
                        _fileContent,
                        actualSegmentOffset + actualChunkOffset,
                        chunk,
                        0,
                        actualChunkSize
                    );

                    WriteToRam(actualChunkOffset, ref chunk, actualChunkSize);
                    //ReadOk();

                    segmentBytesLeft -= actualChunkSize;
                }

                WriteFromRamToFlash(32 * 8 + s);
                //ReadOk();
            }

            Console.Write("Reset screen. ");
            ResetDwin();
            //ReadOk();
            Console.WriteLine("OK");

            //Thread.Sleep(15000);

            //_port.Write(new byte[] { 0x5A, 0xA5, 0x07, 0x82, 0x00, 0x84, 0x5A, 0x01, 0x00, 0x02 }, 0, 10);
            //ReadOk();

            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }

        private static byte[] LoadFileContent()
        {
            return File.ReadAllBytes("DWIN_SET\\32.icl");
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
            //_port.Write(reset, 0, reset.Length);
            _binaryOutputStream.Write(reset, 0, reset.Length);
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

        private static void WriteToRam(int chunk_offset, ref byte[] chunk, byte chunk_size)
        {
            var address = 0x8000 + (chunk_offset / 2);

            var buffer = new byte[6];
            buffer[0] = 0x5A;
            buffer[1] = 0xA5;
            buffer[2] = (byte)(0x01 + 0x02 + chunk_size); // Data length + address + mode
            buffer[3] = 0x82;
            buffer[4] = (byte)((address & 0xFF00) >> 8);
            buffer[5] = (byte)((address & 0x00FF) >> 0);

            //_port.Write(buffer, 0, buffer.Length);
            //_port.Write(chunk, 0, chunk_size);

            _binaryOutputStream.Write(buffer, 0, buffer.Length);
            _binaryOutputStream.Write(chunk, 0, chunk_size);
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

            //_port.Write(buffer, 0, buffer.Length);

            _binaryOutputStream.Write(buffer, 0, buffer.Length);

            byte[] checkStatus = new byte[] { 0x5A, 0xA5, 0x04, 0x83, 0x00, 0xAA, 0x01 };
            _binaryOutputStream.Write(checkStatus, 0, checkStatus.Length);

            // FIXME: Check status
        }
    }
}