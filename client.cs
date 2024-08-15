using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Client
{
    enum ClientState
    {
        Sending,
        Receiving
    }
    class Program
    {
        private static Socket sender;
        private static bool isConnected = false;
        private static bool isRunning = true;
        private static ClientState currentState = ClientState.Sending;
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
                    if (currentState != ClientState.Receiving)
                    {
                        Thread.Sleep(100);
                        continue;
                    }
                    receiveHandle.WaitOne();

                    byte[] sohBuffer = new byte[1];
                    sender.Receive(sohBuffer);
                    if (sohBuffer[0] != 0x01)
                    {
                        Console.WriteLine("SOH yang diterima dari server tidak valid");
                        continue;
                    }

                    var ackMessage = "\x06";
                    sender.Send(Encoding.ASCII.GetBytes(ackMessage));
                    Console.WriteLine($"Socket client kirim ACK: \"{(ackMessage == "\x06" ? "<ACK>" : ackMessage)}\"");

                    List<byte> finalMsgBuff = new List<byte>();
                    while (true)
                    {
                        byte[] messageBuffer = new byte[1024];
                        int byteReceived = sender.Receive(messageBuffer);
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

                            sender.Send(Encoding.ASCII.GetBytes(ackMessage));
                            Console.WriteLine($"Socket client kirim acknowledgment akhir: \"{(ackMessage == "\x06" ? "<ACK>" : ackMessage)}\"");

                            finalMsgBuff.RemoveRange(0, endIdk + 1);
                        }

                        if (finalMsgBuff.Contains(0x04))
                        {
                            Console.WriteLine("Akhir penerimaan transmisi dari server.");
                            finalMsgBuff.Clear();
                            break;
                        }
                    }
                    currentState = ClientState.Sending;
                    sendHandle.Set();
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
            // AutoResetEvent canSend = new AutoResetEvent(true);
            if (sender == null) return;

            while (isRunning)
            {
                if (currentState != ClientState.Sending) continue;
                
                // canSend.WaitOne();
                sendHandle.WaitOne();
                

                Console.Write("Masukkan pesan untuk dikirimkan ke server: ");
                string userMessage = Console.ReadLine();

                if (userMessage.ToLower() == "exit")
                {
                    isRunning = false;
                    break;
                }

            

                var soh = "\x01";
                var stx = "\x02";
                var etb = "\x23";
                var etx = "\x03";
                var eot = "\x04";

                sender.Send(Encoding.ASCII.GetBytes(soh));
                Console.WriteLine($"Socket client kirim: \"{(soh == "\x01" ? "<SOH>" : soh)}\"");

                byte[] ackBuffer = new byte[1];
                sender.Receive(ackBuffer);

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

                    sender.Send(messageToSend);

                    sender.Receive(ackBuffer);

                    if (ackBuffer[0] != 0x06)
                    {
                        Console.WriteLine($"Gagal menerima ACK setelah mengirim chunk dimulai dari byte {i}");
                        return;
                    }
                    else
                    {
                        Console.WriteLine("Socket client terima ACK");
                    }
                }

                sender.Send(Encoding.ASCII.GetBytes(eot));
                Console.WriteLine($"Socket client kirim: \"{(eot == "\x04" ? "<EOT>" : eot)}\"");
                currentState = ClientState.Receiving;
                receiveHandle.Set();
            }
        }

    }
}
