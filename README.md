# Socket Programming in C# with .NET
https://github.com/user-attachments/assets/e63b166d-1d8a-45c1-83b9-2d4af01395b1

### How does Socket work in C#?
**Sockets** allow communication methods between two processes through the same or different networks/machines. C# itself provides the System.Net.Sockets namespace to handle the sending and receiving of data during network communication.

### What is socket programming ?
**Socket programming** is a way of connecting two nodes so that they can communicate with each other over a network. To be connected, one node (Client) will act as a socket that listens on particular port, while the other node (Server) acts by reaching the port of the other socket.

## Stage of Client
- **Protocol:** Menggunakan TCP (Transmission Control Protocol) sebagai protocol komunikasi jaringan yang andal, dengan tipe socket Stream
- **Connection:** Socket client terhubung ke server, dapat mengirim maupun menerima data dari socket server
- **Threading:** Menggunakan `threads ` to handle sending and receiving messages concurently.
- **Checksum:** It includes functionality to calculate and validate checksums for message integrity.
- **Logging:** It logs various events with timestamps for debugging and monitoring.