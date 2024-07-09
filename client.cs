using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Client
{
    class Program
    {
        static void Main(string[] args)
        {
            ExecuteClient();
        }

        static void ExecuteClient()
        {
            try
            {
                IPHostEntry ipHost = Dns.GetHostEntry(Dns.GetHostName());
                IPAddress ipAddr = ipHost.AddressList[0];
                IPEndPoint localEndPoint = new IPEndPoint(ipAddr, 11111);

                Socket sender = new Socket(ipAddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                try
                {
                    sender.Connect(localEndPoint);
                    Console.WriteLine("Socket connected to -> {0} ", sender.RemoteEndPoint.ToString());

                    // Send <SOH>
                    var soh = "\x01";
                    sender.Send(Encoding.ASCII.GetBytes(soh));
                    Console.WriteLine($"Socket client kirim: \"{(soh == "\x01" ? "<SOH>" : soh)}\"");


                    // Wait for <ACK>
                    byte[] ackBuffer = new byte[1];
                    sender.Receive(ackBuffer);
                    if (ackBuffer[0] != 0x06)
                    {
                        throw new Exception("Failed to receive ACK after SOH");
                    }
                    else
                    {
                        Console.WriteLine("Socket client terima ACK");
                    }
                    
                    while (true) 
                    {
                        Console.Write("Enter the message to send to the server: ");
                        string userMessage = Console.ReadLine();

                        if (userMessage.ToLower() == "exit")
                        {
                            break;
                        }
                    
                        // Send <STX>userMessage<ETX>
                        var stx = "\x02";
                        var etx = "\x03";
                        byte[] messageToSend = Encoding.ASCII.GetBytes(stx + userMessage + etx);
                        Console.WriteLine($"Socket client kirim pesan: \"{(stx == "\x02" ? "<STX>" : stx)}\"{userMessage}\"{(etx == "\x03" ? "<ETX>" : etx)}\" ");

                        sender.Send(messageToSend);
                    
                        // Wait for <ACK>
                        sender.Receive(ackBuffer);
                        Console.WriteLine("Socket client terima ACK");

                        if (ackBuffer[0] != 0x06)
                        {
                            throw new Exception("Failed to receive ACK after STX...ETX");
                        }

                    }
                    
                    // Send <EOT>
                    var eot = "\x04";
                    sender.Send(Encoding.ASCII.GetBytes(eot));
                    Console.WriteLine($"Socket client kirim: \"{(eot == "\x04" ? "<EOT>" : eot)}\"");

                    // Close connection
                    sender.Shutdown(SocketShutdown.Both);
                    sender.Close();
                }
                catch (Exception e)
                {
                    Console.WriteLine("Unexpected exception : {0}", e.ToString());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }
}
