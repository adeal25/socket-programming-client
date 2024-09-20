using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
        private static bool receiveACK = false;
        private static bool receiveNAK = false;


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
                LogWithTime("INFO", "IP Address tidak valid");
                return;
            }

            Console.Write("Masukkan port server: ");
            string portInput = Console.ReadLine();
            int port;

            if (!int.TryParse(portInput, out port) || port <= 0 || port > 65535)
            {
                LogWithTime("ERROR", "Port tidak valid.");
                return;
            }

            IPEndPoint localEndPoint = new IPEndPoint(ipAddr, port);

            int retryCount = 0;
            int maxRetries = 10;

            // Initialize the socket
            InitializeSocket(localEndPoint, ref retryCount, maxRetries);

            if (!isConnected)
            {
                LogWithTime("ERROR", "Failed to connect to the server.");
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
                Console.WriteLine("ERROR", "Unexpected exception: {0}", e.ToString());
            }
        }

        private static void InitializeSocket(IPEndPoint endPoint, ref int retryCount, int maxRetries)
        {
            while (retryCount < maxRetries && !isConnected)
            {
                try
                {
                    LogWithTime("INFO", $"Coba {retryCount + 1}, mencoba terhubung ke server...");
                    sender = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    sender.Connect(endPoint);
                    isConnected = true;
                    string ip = sender.RemoteEndPoint.ToString();
                    LogWithTime("INFO", $"Klien terhubung ke server pada IP: {ip}");
                }
                catch (SocketException)
                {
                    retryCount++;
                    LogWithTime("ERROR", "Gagal terhubung ke server, ulangi dalam 2 detik...");
                    Thread.Sleep(2000);
                }
                catch (Exception e)
                {
                    LogWithTime($"ERROR" ,$"Exception during connection: {e.Message}");
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
                    LogWithTime($"ERROR", $"Exception during shutdown: {e.Message}");
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

                    byte[] receiveBuffer = new byte[2048]; // 4028 bytes to hold SOH, ACK, and message
                    int bytesReceived = sender.Receive(receiveBuffer);

                    
                    for (int i = 0; i < bytesReceived; i++)
                    {
                        if (SedangMengirim == true)
                        {
                            if (receiveBuffer[i] == 0x06) // ACK
                            {
                                LogWithTime("INFO", "Klien terima: <ACK>");
                                receiveACK = true;
                                // Handle ACK during sending
                                sendHandle.Set();
                            }
                            else if (receiveBuffer[i] == 0x15)
                            {
                                LogWithTime("INFO", "Klien terima: <NAK>");
                                receiveNAK = true;
                                sendHandle.Set();
                            }
                        }
                        else
                        {
                            if (receiveBuffer[i] == 0x01) // SOH
                            {
                                LogWithTime("INFO","Klien terima: <SOH>");
                                // receiveHandle.Set();

                                sender.Send(new byte[] { 0x06 }); // Send ACK
                                LogWithTime("DEBUG", "Klien kirim: <ACK>");

                                List<byte> finalMsgBuff = new List<byte>();
                                List<byte> finalMessage = new List<byte>();

                                while (true)
                                {
                                    byte[] messageBuffer = new byte[1024];
                                    int msgByteReceived = sender.Receive(messageBuffer);
                                    for (int j = 0; j < msgByteReceived; j++)
                                    {
                                        finalMsgBuff.Add(messageBuffer[j]);
                                    }
                                    // LogWithTime("finalMsgBuff saat ini: "+ BitConverter.ToString(finalMsgBuff.ToArray()));

            
                                    int stxIdk = finalMsgBuff.IndexOf(0x02);
                                    int etbIdk = finalMsgBuff.LastIndexOf(0x23);
                                    int etxIdk = finalMsgBuff.LastIndexOf(0x03);
            
                                    if (stxIdk != -1 && (etbIdk != -1 || etxIdk != -1))
                                    {
                                        int endIdk = etbIdk != -1 ? etbIdk : etxIdk;

                                        if (endIdk > stxIdk)
                                        {
                                            while (finalMsgBuff.LastIndexOf(0x03) > stxIdk && finalMsgBuff.LastIndexOf(0x03) > endIdk)
                                            {
                                                endIdk = finalMsgBuff.LastIndexOf(0x03);
                                            }
                                        }

                                        // Extract received checksum bytes
                                        int chunkLength = endIdk - stxIdk - 1;

                                        byte[] chunkBytes = finalMsgBuff.GetRange(stxIdk + 1, chunkLength - 3).ToArray();
                                        string chunkMessage = Encoding.ASCII.GetString(chunkBytes);
                                        // LogWithTime($"Potongan pesan yang digunakan untuk checksum: {BitConverter.ToString(chunkBytes)}");

                                        byte cs1Byte = finalMsgBuff[endIdk - 2];
                                        byte cs2Byte = finalMsgBuff[endIdk - 1];
                                        string receivedCs1 =  cs1Byte.ToString("X2")[1].ToString();
                                        string receivedCs2 =  cs2Byte.ToString("X2")[1].ToString();
                                        
                                        
                                        // Validate Payload data by calculating checksum while receiving data transmited
                                        if (ValidateChecksum(chunkBytes, receivedCs1, receivedCs2))
                                        {
                                            LogWithTime("DEBUG", "Checksum Cocok, Client kirim: <ACK>");
                                            sender.Send(new byte[] {0x06});
                                            finalMessage.AddRange(chunkBytes);
                                            if (etbIdk != -1)
                                            {
                                                LogWithTime("INFO", $"Klien terima potongan pesan: <STX> {chunkMessage} <ETB>");
                                            }
                                            else if (etxIdk != -1)
                                            {
                                                LogWithTime("INFO", $"Klien terima pesan terakhir: <STX> {chunkMessage} <ETX>");
                                                
                                            }
                                        }
                                        else
                                        {
                                            LogWithTime("DEBUG", "Klien kirim: <NAK> [pesan ditolak]");
                                            sender.Send(new byte[] { 0x15 });
                                        }
                                        finalMsgBuff.RemoveRange(0, endIdk + 1);
                                    }
            
                                    if (finalMsgBuff.Contains(0x04))
                                    {
                                        string fullMessage = Encoding.ASCII.GetString(finalMessage.ToArray());
                                        LogWithTime("INFO", $"Klien terima semua pesan: {fullMessage}");
                                        LogWithTime("INFO", "Akhir transmisi pesan: <EOT>");
                                        finalMsgBuff.Clear();
                                        finalMessage.Clear();
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
                    LogWithTime("ERROR","Koneksi ditutup paksa. Mencoba menghubungkan ulang...");
                    IPEndPoint endPoint = sender.RemoteEndPoint as IPEndPoint;
                    int retryCount = 0;
                    int maxRetries = 10;
                    ReconnectSocket(endPoint, ref retryCount, maxRetries);
                }
                else
                {
                    LogWithTime("ERROR", $"SocketException: {se.Message}");
                }
            }
            catch (Exception e)
            {
                LogWithTime("ERROR", $"Exception: {e.Message}");
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

                

                KirimDariAwal:

                SedangMengirim = true;
                bool sendSuccess = false;
                byte[] soh = new byte[] { 0x01 };
                sender.Send(soh);
                LogWithTime("DEBUG","Klien kirim: <SOH>");

                // Wait for ACK
                sendHandle.WaitOne();

                byte[] messageBuffer = Encoding.ASCII.GetBytes(userMessage);
                int bufferSize = 255;

                for (int i = 0; i < messageBuffer.Length; i += bufferSize)
                {
                    int retryCountCs = 0;
                    sendSuccess = false;

                    bool isLastChunk = i + bufferSize >= messageBuffer.Length;
                    int chunkSize = isLastChunk ? messageBuffer.Length - i : bufferSize;
                    byte[] chunkBuffer = new byte[chunkSize];
                    Array.Copy(messageBuffer, i, chunkBuffer, 0, chunkSize);

                    string chunkMessage = Encoding.ASCII.GetString(chunkBuffer);

                    while (retryCountCs < 5 && !sendSuccess)
                    {
                        KirimUlangPotongan:

                        string checksumValues = CalculateChecksum(chunkBuffer);
                        byte cs1 = Convert.ToByte(checksumValues[0].ToString(), 16);
                        byte cs2 = Convert.ToByte(checksumValues[1].ToString(), 16);
                        
                        byte[] messageToSend = Encoding.ASCII.GetBytes($"\x02{chunkMessage}\x0D");
                        
                        messageToSend = AppendBytes(messageToSend, new byte[] { cs1, cs2 });
                        messageToSend = AppendBytes(messageToSend, new byte[] { isLastChunk? (byte)0x03 : (byte)0x23 });

                        LogWithTime("DEBUG", $"Klien kirim pesan: <STX> {chunkMessage} <CR>{cs1}{cs2}{(isLastChunk ? "<ETX>" : "<ETB>")}");
                        sender.Send(messageToSend);

                        sendHandle.Reset();
                        sendHandle.WaitOne(1000);
                        if (receiveNAK == true && retryCountCs < 5)
                        {
                            LogWithTime("DEBUG", $"Klien kirim ulang: Percobaan ke {retryCountCs+1}: {chunkMessage}");
                            retryCountCs++;
                            receiveNAK = false;
                            goto KirimUlangPotongan;
                        }
                        else if (receiveACK == true)
                        {
                            receiveACK = false;
                            sendSuccess = true;
                        }
                        else if (retryCountCs >= 5)
                        {
                            LogWithTime("ERROR", $"Gagal mengirim pesan setelah {retryCountCs} percobaan. Mengirim <EOT> dan berhenti.");
                            sender.Send(new byte[] { 0x04 }); // Send EOT
                            LogWithTime("DEBUG", "Klien kirim: <EOT>");
                            SedangMengirim = false;
                            break;
                        }
                        
                        else
                        {
                            LogWithTime("DEBUG", "Tidak ada respons dari server. Mengulang dari awal...");
                            goto KirimDariAwal;
                        }
                    }
                }
                if (sendSuccess == true)
                {
                    sender.Send(new byte[] { 0x04 }); // Send EOT
                    LogWithTime("DEBUG", "Klien kirim: <EOT>");
                    SedangMengirim = false;
                }
            }
        }

        private static void LogWithTime(string logLevel, string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss:ff");
            Console.WriteLine($"[{timestamp}] {logLevel}: {message}");
        }

        private static byte[] AppendBytes(byte[] original, byte[] toAppend)
        {
            byte[] result = new byte[original.Length + toAppend.Length];
            Array.Copy(original, result, original.Length);
            Array.Copy(toAppend, 0, result, original.Length, toAppend.Length);
            return result;
        }


        private static string CalculateChecksum(byte[] message)
        {
            int checksum = 0;

            foreach (byte b in message)
            {
                checksum += b;
            }

            checksum = checksum % 256;

            return checksum.ToString("X2");
        }

        private static bool ValidateChecksum(byte[] chunkBytes, string receivedCs1, string receivedCs2)
        {
            // Now that the Checksum method returns a single string instead of an array, you need to modify this method accordingly.
            string calculatedChecksum = CalculateChecksum(chunkBytes);
            
            // Log the received and calculated checksums for debugging.
            LogWithTime("INFO", $"Checksum - Received: {receivedCs1}{receivedCs2}, Calculated: {calculatedChecksum}");

            // Compare the entire checksum strings instead of splitting them.
            return calculatedChecksum == $"{receivedCs1}{receivedCs2}";
        }

    }
}