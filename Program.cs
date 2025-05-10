// See https://aka.ms/new-console-template for more information

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

//This is a basic TCP server for RuRu Comms
//NOTE: using public IP does not work at the moment

public class SimpleServer
{
    public int serverPort = 5000; // Default port
    private TcpListener _listener;
    private List<TcpClient> _clients = new List<TcpClient>();

    public void Start(int port)
    {
        string publicIp = GetPublicIpAddress();
        Console.WriteLine($"Public IP Address: {publicIp}");

        string localIp = GetLocalIpAddress();
        Console.WriteLine($"Local IP Address: {localIp}");

        Console.WriteLine("\nType P for using public IP and L for local IP: ");
        string? ans = Console.ReadLine();

        if (ans.Equals("P", StringComparison.CurrentCultureIgnoreCase))
        {
            Console.WriteLine("Using public IP address.");
            _listener = new TcpListener(IPAddress.Parse(GetPublicIpAddress()), port);
        } 
        else if (ans.Equals("L", StringComparison.CurrentCultureIgnoreCase))
        {
            Console.WriteLine("Using local IP address.");
            _listener = new TcpListener(IPAddress.Parse(GetLocalIpAddress()), port);
        } 
        else
        {
            Console.WriteLine("Invalid input. Defaulting to local IP.");
            _listener = new TcpListener(IPAddress.Parse(GetLocalIpAddress()), port);
        }
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