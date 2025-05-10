// See https://aka.ms/new-console-template for more information

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

//Console.WriteLine("Hello, World!");

public class SimpleServer
{
    public int serverPort = 5000; // Default port
    private TcpListener _listener;
    private List<TcpClient> _clients = new List<TcpClient>();

    public void Start(int port)
    {
        string publicIp = GetPublicIpAddress();
        Console.WriteLine($"Public IP Address: {publicIp}");

        string localIp = Dns.GetHostEntry(Dns.GetHostName()).AddressList[0].ToString();
        Console.WriteLine($"Local IP Address: {localIp}");

        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Console.WriteLine($"Server started on port {port}");

        while (true)
        {
            TcpClient client = _listener.AcceptTcpClient();
            _clients.Add(client);
            Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");

            // Notify other clients about the new connection
            string notification = $"A new client has connected: {client.Client.RemoteEndPoint}";
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

        while (true)
        {
            try
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                Console.WriteLine($"Received: {message}");

                // Relay the message to all other clients
                foreach (var otherClient in _clients)
                {
                    if (otherClient != client)
                    {
                        otherClient.GetStream().Write(buffer, 0, bytesRead);
                    }
                }
            }
            catch
            {
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
}

class Program
{
    static void Main(string[] args)
    {
        SimpleServer server = new SimpleServer();
        server.Start(server.serverPort); // Start the server on port 5000
    }
}