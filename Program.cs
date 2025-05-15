// See https://aka.ms/new-console-template for more information

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks.Dataflow;

//This is a basic TCP server for RuRu Comms
//NOTE: using public IP does not work at the moment

public class SimpleServer
{
    public int serverPort = 5000; // Default port
    private TcpListener _listener;
    private List<TcpClient> _clients = new List<TcpClient>();
    private List<string> messageBuffer = new List<string>();
    private string lastConnectedClientId = string.Empty;
    private readonly object bufferLock = new();
    public void Start(int port)
    {
        string publicIp = GetPublicIpAddress();
        Console.WriteLine($"Public IP Address: {publicIp}");

        string localIp = GetLocalIpAddress();
        Console.WriteLine($"Local IP Address: {localIp}");

        _listener = new TcpListener(IPAddress.Any, port); // Listen on all network interfaces

        _listener.Start();
        Console.WriteLine($"Server started on port {port}");

        while (true)
        {
            TcpClient client = _listener.AcceptTcpClient();

            // if 2 clients already connected, reject the new connection
            if (_clients.Count >= 2)
            {
                Console.WriteLine("Client connection rejected: server full.");
                // send a rejection message to the client before closing
                try
                {
                    var stream = client.GetStream();
                    byte[] msg = Encoding.UTF8.GetBytes("BxFServer full. Try again later.\n");
                    stream.Write(msg, 0, msg.Length);
                }
                catch { }
                client.Close();
                continue;
            }

            _clients.Add(client);
            Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");

            // Notify other clients about the new connection
            string notification = $"BxF_SERVER_New connection: {client.Client.RemoteEndPoint}";
            byte[] notificationBytes = Encoding.UTF8.GetBytes(notification);

            foreach (var otherClient in _clients)
            {
                if (otherClient != client)
                {
                    otherClient.GetStream().Write(notificationBytes, 0, notificationBytes.Length);
                }
            }

            Thread clientThread = new Thread(() => HandleClient(client));
            clientThread.IsBackground = true;
            clientThread.Start();
        }
    }

    private void HandleClient(TcpClient client)
    {
        NetworkStream stream = client.GetStream();
        byte[] buffer = new byte[1024];

        // Read client ID as the first message
        int idBytesRead = stream.Read(buffer, 0, buffer.Length);
        string clientId = Encoding.UTF8.GetString(buffer, 0, idBytesRead);


        // send the buffered messages to the client
        // if the client is not the last connected client
        if (lastConnectedClientId != string.Empty && lastConnectedClientId != clientId)
        {
            lock (bufferLock)
            {
                sendBufferedMessages(client);
            }
        }

        // update the last connected client ID
        lastConnectedClientId = clientId;

        while (true)
        {
            try
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Console.WriteLine($"Received: {message}");

                // Relay the message to all other clients

                // send message to clients
                // if no other clients are connected, save message to buffer to send once a client connects
                bool relayed = false;

                foreach (var otherClient in _clients)
                {
                    if (otherClient != client)
                    {
                        otherClient.GetStream().Write(buffer, 0, bytesRead);
                        relayed = true;
                    }
                }

                // Buffer only if not relayed to anyone
                if (!relayed)
                {
                    lock (bufferLock)
                    {
                        messageBuffer.Add(message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client: {client.Client.RemoteEndPoint} \nException: {ex}");
                break;
            }
        }

        Console.WriteLine($"Client disconnected: {client.Client.RemoteEndPoint}");
        _clients.Remove(client);
        client.Close();
    }

    private string GetPublicIpAddress()
    {
        try
        {
            using (HttpClient client = new HttpClient())
            {
                // Use a public IP service to fetch the public IP
                HttpResponseMessage response = client.GetAsync("https://api.ipify.org").Result;
                response.EnsureSuccessStatusCode();
                return response.Content.ReadAsStringAsync().Result;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching public IP: {ex.Message}");
            return "Unavailable";
        }
    }

    private string GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("No IPv4 address found");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching local IP: {ex.Message}");
            return "127.0.0.1"; // Fallback to localhost
        }
    }

    private void sendBufferedMessages(TcpClient client)
    {
        if (messageBuffer.Count != 0)
        {
            Console.WriteLine($"Sending {messageBuffer.Count} buffered messages to client...");

            //send each message starting at the beginning
            for (int i = 0; i < messageBuffer.Count; i++)
            {
                string msgWithDelimiter = messageBuffer[i] + "\n";
                byte[] msgBytes = Encoding.UTF8.GetBytes(msgWithDelimiter);
                client.GetStream().Write(msgBytes, 0, msgBytes.Length);
            }
            //empty messageBuffer
            messageBuffer.Clear();
        }
    }
}

class Program
{
    static void Main(string[] args)
    {
        SimpleServer server = new SimpleServer();
        server.Start(server.serverPort); // Start the server on port 5000
    }
}