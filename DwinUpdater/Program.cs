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
        private static readonly byte[] Ok = {0x5A, 0xA5, 0x03, 0x82, 0x4F, 0x4B};

        private static SerialPort _port;

        private static StreamWriter file;

        public static void Main(string[] args)
        {
            var outBuffer = new byte[64];

            OpenSerial();

            file = new StreamWriter("Log.txt") { AutoFlush = true };

            ResetScreen();
            ReadOk(ref outBuffer);

            foreach (var f in GetFiles())
            {
                file.WriteLine($"Upload file {f.Value}");

                var fileContent = File.ReadAllBytes(f.Value);
                var subspacesCount = Math.Ceiling(fileContent.Length / (256.0 * 1024.0));
                var segmentsCount = subspacesCount * 8;
                var baseSegment = f.Key * 8;

                for (var s = baseSegment; s < baseSegment + segmentsCount; s++)
                {
                    var fileEnd = false;

                    file.WriteLine("---------------------------------------------------------");

                    const int step = 240;
                    for (var i = 0; i < (32 * 1024); i += step)
                    {
                        var part = new byte[step];

                        var available = fileContent.Length - i;
                        Buffer.BlockCopy(
                            fileContent,
                            i,
                            part,
                            0,
                            available < step ? available : step
                        );

                        WriteToRam(i / 2, part);
                        ReadOk(ref outBuffer);

                        if (available < step)
                        {
                            fileEnd = true;
                            break;
                        }

                        Thread.Sleep(30);
                    }

                    WriteFromRamToFlash(f.Key, s);
                    ReadOk(ref outBuffer);

                    if (fileEnd) break;
                }
            }

            ResetScreen();
            ReadOk(ref outBuffer);
        }

        private static void OpenSerial()
        {
            var ports = SerialPort.GetPortNames();

            _port = new SerialPort {PortName = ports.First(), BaudRate = 115200};
            _port.Open();
        }

        private static void ResetScreen()
        {
            var reset = new byte[] {0x5A, 0xA5, 0x07, 0x82, 0x00, 0x04, 0x55, 0xAA, 0x5A, 0xA5};
            _port.Write(reset, 0, reset.Length);
        }

        private static void WriteToRam(int offset, byte[] bytes)
        {
            var address = 0x8000 + offset;
            var buffer = new byte[4 + 2 + bytes.Length];
            buffer[0] = 0x5A;
            buffer[1] = 0xA5;
            buffer[2] = (byte) (bytes.Length + 0x02 + 0x01); // Data length + address + mode
            buffer[3] = 0x82;
            buffer[4] = (byte) ((address & 0xFF00) >> 8);
            buffer[5] = (byte) ((address & 0x00FF) >> 0);
            bytes.CopyTo(buffer, 6);

            WriteBufferToLog("Write to ram", buffer);
            _port.Write(buffer, 0, buffer.Length);
        }

        private static void WriteFromRamToFlash(
            int index,
            int segment
        )
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
                (byte) ((segment & 0xFF00) >> 8), (byte) (segment & 0xFF),

                /* First address of the data stored in the data variable space (must be even) */
                0x80, 0x00,

                /* ??? */
                0x03, 0xE8,

                /* Always zeros */
                0x00, 0x00, 0x00, 0x00
            };

            WriteBufferToLog("Write to flash", buffer);
            _port.Write(buffer, 0, buffer.Length);
        }

        private static void ReadOk(ref byte[] outBuffer)
        {
            while (_port.BytesToRead < 6) ;
            _port.Read(outBuffer, 0, 6);
        }

        private static Dictionary<int, string> GetFiles()
        {
            var files = new Dictionary<int, string>();

            foreach (var file in Directory.EnumerateFiles("DWIN_SET"))
            {
                var extension = Path.GetExtension(file);
                if (
                    extension != ".HZK" &&
                    extension != ".bin" &&
                    extension != ".icl"
                ) continue;

                files.Add(
                    int.Parse(Regex.Match(file, @"\d+").Value),
                    file
                );
            }

            return files;
        }

        private static void WriteBufferToLog(string title, byte[] buffer)
        {
            var hex = new StringBuilder(buffer.Length * 2);
            foreach (var b in buffer) hex.AppendFormat("{0:X2} ", b);

            file.WriteLine(title);
            file.WriteLine(hex);
        }
    }
}