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
    private List<Client> _clients = new List<Client>();
    private List<string> messageBuffer = new List<string>();
    private string lastConnectedClientId = string.Empty;
    public class Client
    {
        public TcpClient TcpClient { get; }
        public string Id { get; }

        public Client(TcpClient tcpClient, string id)
        {
            TcpClient = tcpClient;
            Id = id;
        }
    }
    private readonly object bufferLock = new();
    private readonly object clientsLock = new();

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
            TcpClient tcpClient = _listener.AcceptTcpClient();

            // Read the client ID from the stream
            NetworkStream stream = tcpClient.GetStream();
            byte[] buffer = new byte[1024];
            int idBytesRead = stream.Read(buffer, 0, buffer.Length);
            string clientId = Encoding.UTF8.GetString(buffer, 0, idBytesRead).Trim();

            var client = new Client(tcpClient, clientId);

            lock (clientsLock)
            {
                if (_clients.Count >= 2)
                {
                    Console.WriteLine("Client connection rejected: server full.");
                    try
                    {
                        byte[] msg = Encoding.UTF8.GetBytes("Server full. Try again later.\n");
                        stream.Write(msg, 0, msg.Length);
                    }
                    catch { }
                    tcpClient.Close();
                    continue;
                }
                _clients.Add(client);
            }

            //otherwise keep the client
            Console.WriteLine($"Client connected: {client.Id}: {tcpClient.Client.RemoteEndPoint}");

            // Notify other clients about the new connection
            string notification = $"BxF_SERVER_New connection: {tcpClient.Client.RemoteEndPoint}";
            byte[] notificationBytes = Encoding.UTF8.GetBytes(notification);

            lock (clientsLock)
            {
                foreach (var otherClient in _clients)
                {
                    if (otherClient != client)
                    {
                        otherClient.TcpClient.GetStream().Write(notificationBytes, 0, notificationBytes.Length);
                    }
                }
            }

            Thread clientThread = new Thread(() => HandleClient(client));
            clientThread.IsBackground = true;
            clientThread.Start();
        }
    }

    private void HandleClient(Client client)
    {
        NetworkStream stream = client.TcpClient.GetStream();
        byte[] buffer = new byte[1024];

        // Send buffered messages if this client hasn't sent them
        if(client.Id != lastConnectedClientId)
        {
            sendBufferedMessages(client);
        }

        while (true)
        {
            try
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Console.WriteLine($"Received: {message}");

                bool relayed = false;
                lock (clientsLock) { 
                    foreach (var otherClient in _clients)
                    {
                        if (otherClient != client)
                        {
                            otherClient.TcpClient.GetStream().Write(buffer, 0, bytesRead);
                            Console.WriteLine($"Relayed to: {otherClient.Id}");
                            relayed = true;
                        }
                    }
                }

                // Buffer only if there is no other client to relay to
                if (!relayed)
                {
                    lock (bufferLock)
                    {
                        messageBuffer.Add(message);
                        Console.WriteLine($"Adding to buffer message: {message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client: {client.TcpClient.Client.RemoteEndPoint} \nException: {ex}");
                break;
            }
        }

        Console.WriteLine($"Client disconnected: {client.TcpClient.Client.RemoteEndPoint}");
        
        lock (clientsLock)
        {
            _clients.Remove(client);
        }
        client.TcpClient.Close();

        //if this was the only client, change last connected client id
        if (_clients.Count == 0)
        {
            lastConnectedClientId = client.Id;
        }
        else
        {
            lastConnectedClientId = _clients[0].Id;
        }
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

    private void sendBufferedMessages(Client client)
    {
        if (messageBuffer.Count != 0)
        {
            Console.WriteLine($"Sending {messageBuffer.Count} buffered messages to client...");

            //send each message starting at the beginning
            for (int i = 0; i < messageBuffer.Count; i++)
            {
                string msgWithDelimiter = messageBuffer[i] + "\n";
                byte[] msgBytes = Encoding.UTF8.GetBytes(msgWithDelimiter);
                client.TcpClient.GetStream().Write(msgBytes, 0, msgBytes.Length);
            }
            //empty messageBuffer
            messageBuffer.Clear();
        }
        lastConnectedClientId = client.Id; // Update the last connected client ID
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