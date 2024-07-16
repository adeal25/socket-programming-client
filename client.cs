using System;
using System.Linq.Expressions;
using System.Net;
using System.Net.Sockets;
using System.Reflection.PortableExecutable;
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
            Console.Write("Masukkan IP Address server: ");
            string ipAddressInput = Console.ReadLine();
            IPAddress ipAddr;
            // IPHostEntry ipHost = Dns.GetHostEntry(Dns.GetHostName());
            // IPAddress ipAddr = ipHost.AddressList[0];
            // IPEndPoint localEndPoint = new IPEndPoint(ipAddr, 11111);
            if (!IPAddress.TryParse(ipAddressInput, out ipAddr))
            {
                Console.WriteLine("IP Address tidak valid");
                return;
            }
            Console.Write("Masukkan port server: ");
            string portInput = Console.ReadLine();
            int port;

            if (!int.TryParse(portInput, out port) || port <= 0 || port > 65535)
            {
                Console.WriteLine("Port tidak valid.");
                return;
    
            }

            IPEndPoint localEndPoint = new IPEndPoint(ipAddr, port);
            
            int retryCount = 0;
            int maxRerying = 10;
            bool isConnected = false;
            Socket sender = null;

            while (retryCount < maxRerying && !isConnected)
            {
                try
                {
                    Console.WriteLine($"Percobaan ke-{retryCount + 1}, mencoba terhubung ke server...");
                    sender = new Socket(ipAddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

                    sender.Connect(localEndPoint);
                    isConnected = true;
                    Console.WriteLine("Socket connected to -> {0} ", sender.RemoteEndPoint.ToString());
                }
                catch (SocketException)
                {
                    retryCount++;
                    Console.WriteLine($"Gagal terkoneksi ke server, mencoba terhubung dalam 2 detik...");
                    Thread.Sleep(2000);
                }
            }

            if (!isConnected)
            {
                Console.WriteLine("Gagal terkoneksi ke server setelah 10 kali percobaan");
                return;
            }
            try
            {
                while (true)
                {
                    try
                    {
                        // Wait for <SOH>
                        byte[] sohBuffer = new byte[1];
                        sender.Receive(sohBuffer);
                        if(sohBuffer[0] != 0x01)
                        {
                            var nak = "\x21";
                            sender.Send(Encoding.ASCII.GetBytes(nak));
                            Console.WriteLine("Invalid message received");
                            continue;
                        }

                        // Send <ACK> ke server
                        var ackMessage = "\x06";
                        sender.Send(Encoding.ASCII.GetBytes(ackMessage));
                        Console.WriteLine($"Socket client kirim ACK pertama: \"{(ackMessage == "\x06" ? "<ACK>" : ackMessage)}");

                        List<byte> finalMsgBuff = new List<byte>();
                        while (true)
                        {
                            //Receive data
                            byte[] messageBuffer = new byte[1024];
                            int byteReceived = sender.Receive(messageBuffer);
                            for (int i = 0; i < byteReceived; i++)
                            {
                                finalMsgBuff.Add(messageBuffer[i]);
                            }

                            // Check if message ends with <ETX>
                            int stxIdk = finalMsgBuff.IndexOf(0x02);
                            int etbIdk = finalMsgBuff.IndexOf(0x23);
                            int etxIdk = finalMsgBuff.IndexOf(0x03);

                            if (stxIdk != -1 && (etbIdk != -1 || etxIdk != -1))
                            {
                                int endIdk = etbIdk != -1 ? etbIdk : etxIdk;
                                byte[] chunkBytes = finalMsgBuff.GetRange(stxIdk + 1, endIdk - stxIdk - 1).ToArray();
                                string chunkMessage = Encoding.ASCII.GetString(chunkBytes);

                                if (etbIdk != -1)
                                {
                                    Console.WriteLine($"Socket client terima potongan: \"<STX>{chunkMessage}<ETB>\"");
                                }
                                else if (etxIdk != -1)
                                {
                                    Console.WriteLine($"Socket client terima potongan terakhir \"<STX>{chunkMessage}<ETX>\"");
                                }

                                //Send <ACK> after receivinf chunk
                                sender.Send(Encoding.ASCII.GetBytes(ackMessage));
                                Console.WriteLine($"Socket client kirim acknowledgment akhir: \"{(ackMessage == "\x06" ? "<ACK>" : ackMessage)}\"");

                                finalMsgBuff.RemoveRange(0, endIdk + 1); //Clear buffer buat messge selanjutnya
                            }

                            // Check if message contain <EOT>
                            if (finalMsgBuff.Contains(0x04))
                            {
                                Console.WriteLine("End of Transmission received");
                                finalMsgBuff.Clear();
                                break;
                            }

                        }
                    }
                    catch (SocketException se)
                    {
                        if (se.SocketErrorCode == SocketError.ConnectionReset ||
                            se.SocketErrorCode == SocketError.ConnectionAborted)
                            {
                                Console.WriteLine($"Koneksi ditutup paksa. Mencoba reconnect...");
                                
                                isConnected = false;
                                retryCount = 0;

                                if (sender != null)
                                {
                                    sender.Shutdown(SocketShutdown.Both);
                                    sender.Close();
                                    sender = null;
                                }
                                while (retryCount < maxRerying && !isConnected)
                                {
                                    try
                                    {
                                        
                                        Console.WriteLine($"Percobaan ke-{retryCount + 1}, mencoba terhubung ke server...");
                                        sender = new Socket(ipAddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                                        sender.Connect(localEndPoint);
                                        isConnected = true;
                                        Console.WriteLine("Socket connected to -> {0} ", sender.RemoteEndPoint.ToString());
                                    }
                                    catch (SocketException)
                                    {    
                                        retryCount++;
                                        Console.WriteLine($"Gagal terkoneksi ke server, mencoba terhubung dalam 2 detik...");
                                        Thread.Sleep(2000);
                                    }
                                    catch (Exception e2)
                                    {
                                        Console.WriteLine($"Exception during reconnect: {e2.Message}");
                                        break;
                                    }
                                }

                                if (!isConnected)
                                {
                                    Console.WriteLine("Gagal terkoneksi ke server setelah 10 kali percobaan");
                                    break;
                                }
                            }
                            else
                            {
                            Console.WriteLine($"SocketException: {se.Message}");
                            break;
                            }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Exception: {e.Message}");
                        break;
                    }
                }
                if (sender != null)
                {
                    sender.Shutdown(SocketShutdown.Both);
                    sender.Close();
                }
                    
            }
            catch (Exception e)
            {
                Console.WriteLine("Unexpected exception : {0}", e.ToString());
            }
        }
    }
}
