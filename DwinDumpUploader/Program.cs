using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DwinDumpUploader
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string[] ports = SerialPort.GetPortNames();
            SerialPort port = new SerialPort
            {
                PortName = ports[1],
                BaudRate = 115200,
                Parity = Parity.None,
            };
            port.Open();

            byte[] content = File.ReadAllBytes("Dump.bin");
            int offset = 0;
            try
            {
                while (true)
                {
                    offset += 2;  // 0x5A 0xA5
                    Byte size = content[offset++];
                    port.Write(content, offset - 3, size + 3);
                    offset += size;

                    while (port.BytesToRead == 0)
                    {
                        Thread.Sleep(2);
                    }

                    byte[] buffer = new byte[128];
                    port.Read(buffer, 0, port.BytesToRead);

                    Console.Write(".");
                }
            }
            catch (IndexOutOfRangeException e)
            {
                return;
            }
        }
    }
}
