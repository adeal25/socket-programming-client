using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Client
{
    class Program
    {
        private static Socket sender;
        private static bool isConnected = false;
        private static bool isRunning = true;
        private static bool SedangMengirim = false;
        private static EventWaitHandle sendHandle = new AutoResetEvent(true);
        private static EventWaitHandle receiveHandle = new AutoResetEvent(true);

        static void Main(string[] args)
        {
            ExecuteClient();
        }

        static void ExecuteClient()
        {
            Console.Write("Masukkan IP Address server: ");
            string ipAddressInput = Console.ReadLine();
            IPAddress ipAddr;
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
            int maxRetries = 10;

            // Initialize the socket
            InitializeSocket(localEndPoint, ref retryCount, maxRetries);

            if (!isConnected)
            {
                Console.WriteLine("Gagal terkoneksi ke server setelah 10 kali percobaan");
                return;
            }

            try
            {
                // Mulai thread untuk mengirim dan menerima pesan secara bersamaan
                Thread thMainSocket = new Thread(ClientReceiveMessage);
                Thread thInputUser = new Thread(ClientSendMessage);

                thMainSocket.Start();
                thInputUser.Start();

                thInputUser.Join();
                thMainSocket.Join();

                CloseSocket();
            }
            catch (Exception e)
            {
                Console.WriteLine("Unexpected exception: {0}", e.ToString());
            }
        }

        private static void InitializeSocket(IPEndPoint endPoint, ref int retryCount, int maxRetries)
        {
            while (retryCount < maxRetries && !isConnected)
            {
                try
                {
                    Console.WriteLine($"Percobaan ke-{retryCount + 1}, mencoba terhubung ke server...");
                    sender = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    sender.Connect(endPoint);
                    isConnected = true;
                    Console.WriteLine("Socket connected to -> {0} ", sender.RemoteEndPoint.ToString());
                }
                catch (SocketException)
                {
                    retryCount++;
                    Console.WriteLine("Gagal terkoneksi ke server, mencoba terhubung dalam 2 detik...");
                    Thread.Sleep(2000);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Exception during connection: {e.Message}");
                    break;
                }
            }
        }

        private static void ReconnectSocket(IPEndPoint endPoint, ref int retryCount, int maxRetries)
        {
            isConnected = false;
            retryCount = 0;

            CloseSocket();

            InitializeSocket(endPoint, ref retryCount, maxRetries);
        }

        private static void CloseSocket()
        {
            if (sender != null)
            {
                try
                {
                    sender.Shutdown(SocketShutdown.Both);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Exception during shutdown: {e.Message}");
                }
                finally
                {
                    sender.Close();
                    sender = null;
                }
            }
        }

        private static void ClientReceiveMessage()
        {
            if (sender == null) return;

            try
            {
                while (isRunning)
                {
                    // receiveHandle.WaitOne();

                    byte[] receiveBuffer = new byte[2048]; // 4028 bytes to hold SOH, ACK, and message
                    int bytesReceived = sender.Receive(receiveBuffer);

                    
                    for (int i = 0; i < bytesReceived; i++)
                    {
                        if (SedangMengirim == true)
                        {
                            if (receiveBuffer[i] == 0x06) // ACK
                            {
                                Console.WriteLine("Socket client terima ACK");
                                // Handle ACK during sending
                                sendHandle.Set();
                            }
                        }
                        else
                        {
                            if (receiveBuffer[i] == 0x01) // SOH
                            {
                                Console.WriteLine("SOH diterima");
                                // receiveHandle.Set();

                                sender.Send(new byte[] { 0x06 }); // Send ACK
                                Console.WriteLine("Socket client kirim ACK");

                                List<byte> finalMsgBuff = new List<byte>();

                                while (true)
                                {
                                    byte[] messageBuffer = new byte[1024];
                                    int msgByteReceived = sender.Receive(messageBuffer);
                                    for (int j = 0; j < msgByteReceived; j++)
                                    {
                                        finalMsgBuff.Add(messageBuffer[j]);
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
                                            Console.WriteLine($"Socket client terima potongan pesan: \"<STX>{chunkMessage}<ETB>\"");
                                        }
                                        else if (etxIdk != -1)
                                        {
                                            Console.WriteLine($"Socket client terima potongan pesan terakhir \"<STX>{chunkMessage}<ETX>\"");
                                        }
            
                                        sender.Send(new byte[] {0x06});
                                        Console.WriteLine("Socket client kirim ACK");
            
                                        finalMsgBuff.RemoveRange(0, endIdk + 1);
                                    }
            
                                    if (finalMsgBuff.Contains(0x04))
                                    {
                                        Console.WriteLine("Akhir penerimaan transmisi dari server.");
                                        finalMsgBuff.Clear();
                                        break;
                                    }
                                }
                            }
                        }
                    }

                }
            }
            catch (SocketException se)
            {
                if (se.SocketErrorCode == SocketError.ConnectionReset || se.SocketErrorCode == SocketError.ConnectionAborted)
                {
                    Console.WriteLine("Koneksi ditutup paksa. Mencoba reconnect...");
                    IPEndPoint endPoint = sender.RemoteEndPoint as IPEndPoint;
                    int retryCount = 0;
                    int maxRetries = 10;
                    ReconnectSocket(endPoint, ref retryCount, maxRetries);
                }
                else
                {
                    Console.WriteLine($"SocketException: {se.Message}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: {e.Message}");
            }
        }

        private static void ClientSendMessage()
        {
            if (sender == null) return;

            while (isRunning)
            {
                // sendHandle.WaitOne();

                Console.Write("Masukkan pesan untuk dikirimkan ke server: ");
                string userMessage = Console.ReadLine();

                if (userMessage.ToLower() == "exit")
                {
                    isRunning = false;
                    return;
                }

                SedangMengirim = true;

                byte[] soh = new byte[] { 0x01 };
                sender.Send(soh);
                Console.WriteLine("Socket client kirim: <SOH>");

                // Wait for ACK
                sendHandle.WaitOne();
                // CEK IF ACK MASUK

                byte[] messageBuffer = Encoding.ASCII.GetBytes(userMessage);
                int bufferSize = 255;

                for (int i = 0; i < messageBuffer.Length; i += bufferSize)
                {
                    bool isLastChunk = i + bufferSize >= messageBuffer.Length;
                    int chunkSize = isLastChunk ? messageBuffer.Length - i : bufferSize;
                    byte[] chunkBuffer = new byte[chunkSize];
                    Array.Copy(messageBuffer, i, chunkBuffer, 0, chunkSize);

                    string chunkMessage = Encoding.ASCII.GetString(chunkBuffer);
                    byte[] messageToSend = Encoding.ASCII.GetBytes($"\x02{chunkMessage}{(isLastChunk ? "\x03" : "\x23")}");
                    Console.WriteLine($"Socket client kirim pesan: <STX>{chunkMessage}{(isLastChunk ? "<ETX>" : "<ETB>")}");

                    sender.Send(messageToSend);

                    // Wait for ACK
                    // sender.Send(new byte[] { 0x06 }); // Send ACK after each chunk
                    // Console.WriteLine("Socket client kirim ACK");
                    sendHandle.WaitOne();
                }

                sender.Send(new byte[] { 0x04 }); // Send EOT
                Console.WriteLine("Socket client kirim: <EOT>");
                SedangMengirim = false;
                // receiveHandle.Set();
            }
        }
    }
}
