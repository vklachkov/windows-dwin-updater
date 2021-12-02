using System;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace DwinSimula
{
    internal class Program
    {
        static void Main(string[] args)
        {
            SerialPort port = new SerialPort
            {
                PortName = "COM4",
                BaudRate = 115200,
                Parity = Parity.None,
                ReadTimeout = 200
            };
            port.Open();

            String timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            FileStream binaryOutputStream = new FileStream($"Dump-{timestamp}.bin", FileMode.Create, FileAccess.Write);
            StreamWriter logOutputStream = new StreamWriter($"History-{timestamp}.log");

            byte[] headerBuffer = new byte[16];
            byte[] dataBuffer = new byte[512];
            while (true)
            {
                if (port.BytesToRead == 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                int headerSize = 3;
                int headerBytes = port.Read(headerBuffer, 0, headerSize);
                int bytesInPacket = headerBuffer[2];
                while (port.BytesToRead < bytesInPacket)
                {
                    Thread.Sleep(1);
                }

                int count = port.Read(dataBuffer, 0, bytesInPacket);
                Console.WriteLine($"Received {count + headerSize} bytes");

                logOutputStream.WriteLine($"{DateTime.Now.ToString("HH:mm:ss.fff")} > {BitConverter.ToString(headerBuffer, 0, headerSize).Replace("-", " ")} {BitConverter.ToString(dataBuffer, 0, count).Replace("-", " ")}");
                binaryOutputStream.Write(headerBuffer, 0, headerSize);
                binaryOutputStream.Write(dataBuffer, 0, count);

                if (bytesInPacket == 4)
                {
                    byte[] ok = { 0x5A, 0xA5, 0x06, 0x83, 0x00, 0xAA, 0x01, 0x00, 0x02 };
                    port.Write(ok, 0, ok.Length);
                }
                else if (bytesInPacket == 7)
                {
                    byte[] ok = { 0x5A, 0xA5, 0x03, 0x82, 0x4F, 0x4B };
                    port.Write(ok, 0, ok.Length);

                    port.Close();
                    logOutputStream.Close();
                    binaryOutputStream.Close();

                    break;
                }
                else
                {
                    byte[] ok = { 0x5A, 0xA5, 0x03, 0x82, 0x4F, 0x4B };
                    port.Write(ok, 0, ok.Length);
                }
            }
        }
    }
}
