// See https://aka.ms/new-console-template for more information

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using RuRuCommsServer;
using System.Threading;
using System.Threading.Tasks.Dataflow;

//This is a basic TCP server for RuRu Comms
//NOTE: buffered messages have a client Id attached to them, sending buffered messages
//      will send messages to any client other than the one that sent that specific message

public class SimpleServer
{
    //net stuff
    public int serverPort = 5000; // Default port
    private TcpListener? _listener;
    private List<Client> _clients = new List<Client>();
    //message buffer
    private List<string> messageBuffer = new List<string>();
    //locks
    private readonly object bufferLock = new();
    private readonly object clientsLock = new();

    public void Start(int port)
    {
        string publicIp = GetPublicIpAddress();
        Console.WriteLine($"Public IP Address: {publicIp}");

        string localIp = GetLocalIpAddress();
        Console.WriteLine($"Local IP Address: {localIp}");

        _listener = new TcpListener(IPAddress.Any, port); // Listen on public and private IPs
        _listener.Start();
        Console.WriteLine($"Server started on port {port}");

        while (true)
        {
            TcpClient tcpClient = _listener.AcceptTcpClient();

            // Read the client ID from the stream
            // If the client does not send an ID, kick them bc they sus
            NetworkStream stream = tcpClient.GetStream();
            stream.ReadTimeout = 5000; // 5 seconds timeout
            byte[] buffer = new byte[1024];
            int idBytesRead = 0;
            try
            {
                idBytesRead = stream.Read(buffer, 0, buffer.Length);
            }
            catch (IOException)
            {
                Console.WriteLine("Client did not send ID in time. Connection closed.");
                tcpClient.Close();
                continue;
            }
            if (idBytesRead == 0)
            {
                Console.WriteLine("Client disconnected before sending ID.");
                tcpClient.Close();
                continue;
            }
            string clientId = Encoding.UTF8.GetString(buffer, 0, idBytesRead).Trim();

            //create a new client
            var client = new Client(tcpClient, clientId);

            // kick out the client if there are already 2 connected
            // otherwise add the client to the list and continue
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
                    client.TcpClient.Close();
                    continue;
                }
                _clients.Add(client);
            }

            Console.WriteLine($"Client connected: {client.Id}: {client.TcpClient.Client.RemoteEndPoint}");

            // Notify other clients about the new connection
            string notification = $"BxF_SERVER_New connection: {client.Id}: {client.TcpClient.Client.RemoteEndPoint}";
            lock (clientsLock)
            {
                client.sendMessage(_clients, notification);
            }

            //create a thread for the new client
            Thread clientThread = new Thread(() => HandleClient(client));
            clientThread.IsBackground = true;
            clientThread.Start();
        }
    }

    private void HandleClient(Client client)
    {
        NetworkStream stream = client.TcpClient.GetStream();
        byte[] buffer = new byte[1024];

        // send buffered messages to the client
        messageBuffer = client.receiveBufferedMessages(messageBuffer);

        while (true)
        {
            try
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Console.WriteLine($"Received: {message}");
                lock (clientsLock) { 
                    client.sendMessage(_clients, message);
                }

                // Buffer only if there is no other client to relay to
                if (_clients.Count <= 1)
                {
                    lock (bufferLock)
                    {
                        messageBuffer.Add(client.Id + ": " + message);
                        Console.WriteLine($"Adding to buffer: {client.Id}: {message}");
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
        
        //send a message that the client has disconnected
        string disconnectMessage = $"BxF_SERVER_Client disconnected: {client.Id}: {client.TcpClient.Client.RemoteEndPoint}";
        lock (clientsLock)
        {
            client.sendMessage(_clients, disconnectMessage);
        }

        //remove the client from the client list
        lock (clientsLock)
        {
            _clients.Remove(client);
        }
        client.TcpClient.Close();

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

}

class Program
{
    static void Main(string[] args)
    {
        SimpleServer server = new SimpleServer();
        server.Start(server.serverPort); // Start the server on port 5000
    }
}