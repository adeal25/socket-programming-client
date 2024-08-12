using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ClientAsync
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await ExecuteClientAsync();
        }

        static async Task ExecuteClientAsync()
        {

            Console.Write("Masukkan IP Address server: ");
            string ipAddressInput = Console.ReadLine();
            if(IPAddress.TryParse(ipAddressInput, out IPAddress ipAddr))
            {
                Console.WriteLine("IP Address tidak valid");
                return;
            } 

            Console.Write("Masukkan port server: ");
            string portInput = Console.ReadLine();
            if (!int.TryParse(portInput, out int port) || port <= 0 || port > 65535)
            {
                Console.WriteLine("Port tidak valid");
                return;
            }

            IPEndPoint localEndPoint = new IPEndPoint(ipAddr, port);
            int retryCount = 0;
            int maxRetry = 10;
            bool isConnected = false;
            Socket sender = null;

            while (retryCount < maxRetry && !isConnected)
            {
                try
                {
                    Console.WriteLine($"Percobaan ke-{retryCount + 1}, mencoba terhubung ke server...");
                    sender = new Socket(ipAddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    await sender.ConnectAsync(localEndPoint);
                    isConnected = true;
                    Console.WriteLine("Socket connected to -> {0}", sender.RemoteEndPoint.ToString());

                }
                catch (SocketException)
                {
                    retryCount++;
                    Console.WriteLine($"Gagal terkoneksi ke server, mencoba terhubung dalam 2 detik...");
                    await Task.Delay(2000);
                }
            }

            if (!isConnected)
            {
                Console.WriteLine($"Gagal terkoneksi ke server setelah 10 kali percobaan");
                return;
            }

            try
            {
                while (true)
                {
                    try
                    {
                        if (!await ClientReceiveMessageAsync(sender))
                        {
                            break;
                        }
                        await ClientSendMessageAsync(sender);
                    }
                    catch (SocketException se)
                    {
                        if (se.SocketErrorCode == SocketError.ConnectionReset ||
                        se.SocketErrorCode == SocketError.ConnectionAborted)
                        {
                            Console.WriteLine("Koneksi ditutup paksa. Mencoba reconnect...");

                            isConnected = false;
                            retryCount = 0;

                            if (sender != null)
                            {
                                sender.Shutdown(SocketShutdown.Both);
                                sender.Close();
                                sender = null;
                            }

                            while (retryCount < maxRetry && !isConnected)
                            {
                                try
                                {
                                    Console.WriteLine($"Percobaan ke-{retryCount + 1}, mencoba terhubung ke server...");
                                    sender = new Socket(ipAddr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                                    await sender.ConnectAsync(localEndPoint);
                                    isConnected = true;
                                    Console.WriteLine("Socket connected to -> {0}", sender.RemoteEndPoint.ToString());

                                }
                                catch (SocketException)
                                {
                                    retryCount++;
                                    Console.WriteLine($"Gagal terkoneksin ke server, mecoba tehubung dalam 2 detik...");
                                    await Task.Delay(2000);
                                }
                                catch (Exception e2)
                                {
                                    Console.WriteLine($"Exception selama menghungkan ulang: {e2.Message}");
                                    break;
                                }
                            }

                            if (!isConnected)
                            {
                                Console.WriteLine($"SocketException: {se.Message}");
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
                Console.WriteLine("Unexpected Exception : {0}", e.ToString());
            }
        }

        private static async Task ClientSendMessageAsync(Socket sender)
        {
            Console.Write("Masukkan pesan untuk dikirimkan ke server: ");
            string userMessage = Console.ReadLine();

            if (userMessage.ToLower() == "exit")
            {
                return;
            }

            var soh = "\x01";
            var stx = "\x02";
            var etb = "\x23";
            var etx = "\x03";
            var eot = "\x04";

            await sender.SendAsync(Encoding.ASCII.GetBytes(soh), SocketFlags.None);
            Console.WriteLine($"Socket client kirim: \"{(soh == "\x01" ? "<SOH>" : soh)}\"");

            byte[] ackBuffer = new byte[1];
            await sender.ReceiveAsync(ackBuffer, SocketFlags.None);

            if (ackBuffer[0] != 0x06)
            {
                Console.WriteLine("Gagal menerima ACK setelah SOH");
                return;
            }
            else
            {
                Console.WriteLine("Socket client terima ACK");
            }
            byte[] messageBuffer = Encoding.ASCII.GetBytes(userMessage);
            int bufferSize = 255;

            for (int i = 0; i < messageBuffer.Length; i += bufferSize)
            {
                bool isLastChunk = i + bufferSize >= messageBuffer.Length;
                int chunkSize = isLastChunk ? messageBuffer.Length - i : bufferSize;
                byte[] chunkBuffer = new byte[chunkSize];
                Array.Copy(messageBuffer, i, chunkBuffer, 0, chunkSize);

                string chunkMessage = stx + Encoding.ASCII.GetString(chunkBuffer) + (isLastChunk ? etx : etb);
                byte[] messageToSend = Encoding.ASCII.GetBytes(chunkMessage);
                Console.WriteLine($"Socket client kirim pesan: \"{(stx == "\x02" ? "<STX>" : stx)}{Encoding.ASCII.GetString(chunkBuffer)}{(isLastChunk ? "<ETX>" : "<ETB>")}\"");

                await sender.SendAsync(messageToSend, SocketFlags.None);

                await sender.ReceiveAsync(ackBuffer, SocketFlags.None);

                if (ackBuffer[0] != 0x06)
                {
                    Console.WriteLine($"Gagal menerima ACK setelah mengirim chunk dimlai dari byte {i}");
                    return;
                }
                else
                {
                    Console.WriteLine("Socket client terima ACK");
                }
    
            }

            await sender.SendAsync(Encoding.ASCII.GetBytes(eot), SocketFlags.None);
            Console.WriteLine($"Socket client kirim: \"{(eot == "\x04" ? "<EOT>" : eot)}\"");
        }


        private static async Task<bool> ClientReceiveMessageAsync(Socket sender)
        {
            Console.WriteLine("Menunggu pesan yang diterima dari server...");

            byte[] sohBuffer = new byte[1];
            await sender.ReceiveAsync(sohBuffer, SocketFlags.None);
            if (sohBuffer[0] != 0x01)
            {
                Console.WriteLine("SOH yang diterma dari server tidak valid");
                return false;
            }

            var ackMessage = "\x06";
            await sender.SendAsync(Encoding.ASCII.GetBytes(ackMessage), SocketFlags.None);
            Console.WriteLine($"Socket client kirim ACK: \"{(ackMessage == "\x06" ? "<ACK>" : ackMessage)}\"");

            List<byte> finalMsgBuff = new List<byte>();
            while (true)
            {

                byte[] messageBuffer = new byte[1024];
                int byteReceived = await sender.ReceiveAsync(messageBuffer, SocketFlags.None);
                for (int i = 0; i < byteReceived; i++)
                {
                    finalMsgBuff.Add(messageBuffer[i]);
                }

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

                    await sender.SendAsync(Encoding.ASCII.GetBytes(ackMessage), SocketFlags.None);
                    Console.WriteLine($"Socket client kirim acknowledgment akhir: \"{(ackMessage == "\x06" ? "<ACK>" : ackMessage)}\"");

                    finalMsgBuff.RemoveRange(0, endIdk + 1);
                }

                if (finalMsgBuff.Contains(0x04))
                {
                    Console.WriteLine("Akhir penerimaan transmisi dari server");
                    finalMsgBuff.Clear();
                    break;
                }

            }
            return true;
        }
    }
}