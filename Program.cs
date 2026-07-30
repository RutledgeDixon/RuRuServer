// See https://aka.ms/new-console-template for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using RuRuCommsServer;
using System.Threading;

//This is a basic TCP + WebSocket relay server for RuRu Comms
//NOTE: buffered messages have a client Id attached to them, sending buffered messages
//      will send messages to any client other than the one that sent that specific message
//NOTE: uses a ton of try-catch blocks, very messy but hopefully will stop it from crashing
//NOTE: the WebSocket listener (for the web client) requires a one-time host setup on Windows:
//      netsh http add urlacl url=https://+:<webSocketPort>/ user=<account>
//      netsh http add sslcert ipport=0.0.0.0:<webSocketPort> certhash=<thumbprint> appid={<guid>}

public class SimpleServer
{
    //net stuff
    public int serverPort = 50512; // Non-default port
    public int webSocketPort = 50513; // Internal WS listener port
    public string webSocketPath = "/ruru/";
    public string webSocketScheme = OperatingSystem.IsWindows() ? "https" : "http";
    public string webSocketHost = "+";
    private TcpListener? _listener;
    private HttpListener? _wsListener;
    private List<Client> _clients = new List<Client>();
    //message buffer
    private List<string> messageBuffer = new List<string>();
    //locks
    private readonly object bufferLock = new();
    private readonly object clientsLock = new();

    public SimpleServer()
    {
        serverPort = ReadIntEnv("RURU_TCP_PORT", serverPort);
        webSocketPort = ReadIntEnv("RURU_WS_PORT", webSocketPort);
        webSocketPath = NormalizeWebSocketPath(Environment.GetEnvironmentVariable("RURU_WS_PATH") ?? webSocketPath);
        webSocketScheme = (Environment.GetEnvironmentVariable("RURU_WS_SCHEME") ?? webSocketScheme).Trim().ToLowerInvariant();
        webSocketHost = (Environment.GetEnvironmentVariable("RURU_WS_HOST") ?? webSocketHost).Trim();

        if (webSocketScheme != "http" && webSocketScheme != "https")
        {
            Console.WriteLine($"Invalid RURU_WS_SCHEME '{webSocketScheme}', defaulting to {(OperatingSystem.IsWindows() ? "https" : "http")}.");
            webSocketScheme = OperatingSystem.IsWindows() ? "https" : "http";
        }

        if (string.IsNullOrWhiteSpace(webSocketHost))
        {
            webSocketHost = "+";
        }

        Console.WriteLine($"Config: TCP={serverPort}, WS={webSocketScheme}://{webSocketHost}:{webSocketPort}{webSocketPath}");
    }

    private static int ReadIntEnv(string name, int fallback)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out int parsed) && parsed > 0 && parsed <= 65535)
        {
            return parsed;
        }
        return fallback;
    }

    private static string NormalizeWebSocketPath(string path)
    {
        string normalized = path.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }
        if (!normalized.EndsWith('/'))
        {
            normalized += "/";
        }
        return normalized;
    }

    public void Start(int port)
    {
        Thread wsThread = new Thread(() => StartWebSocketListener(webSocketPort, webSocketPath));
        wsThread.IsBackground = true;
        wsThread.Start();

        while (true)
        {
            try
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
                    try
                    {
                        TcpClient tcpClient = _listener.AcceptTcpClient();
                        OnboardAndServe(new TcpClientChannel(tcpClient));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error accepting client: {ex.Message}");
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Server error: {ex.Message}");
            }
            finally
            {
                try { _listener?.Stop(); } catch { }
                _listener = null;
            }
            Console.WriteLine("Error - most likely network. Retrying in 5 seconds...");
            Thread.Sleep(5000);
        }
    }

    private void StartWebSocketListener(int port, string path)
    {
        while (true)
        {
            try
            {
                _wsListener = new HttpListener();
                _wsListener.Prefixes.Add($"{webSocketScheme}://{webSocketHost}:{port}{path}");
                _wsListener.Start();
                Console.WriteLine($"WebSocket listener started on {webSocketScheme}://{webSocketHost}:{port}{path}");

                while (true)
                {
                    HttpListenerContext context = _wsListener.GetContext();
                    if (!context.Request.IsWebSocketRequest)
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                        continue;
                    }

                    ThreadPool.QueueUserWorkItem(_ => AcceptWebSocketClient(context));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket listener error: {ex.Message}");
            }
            finally
            {
                try { _wsListener?.Stop(); } catch { }
                _wsListener = null;
            }
            Console.WriteLine("WebSocket listener error - retrying in 5 seconds...");
            Thread.Sleep(5000);
        }
    }

    private void AcceptWebSocketClient(HttpListenerContext context)
    {
        try
        {
            HttpListenerWebSocketContext wsContext = context.AcceptWebSocketAsync(subProtocol: null).GetAwaiter().GetResult();
            string remoteDescription = context.Request.RemoteEndPoint?.ToString() ?? "unknown";
            OnboardAndServe(new WebSocketClientChannel(wsContext.WebSocket, remoteDescription));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error accepting WebSocket client: {ex.Message}");
            try { context.Response.Close(); } catch { }
        }
    }

    // Shared handshake (ID frame + valid_ids.txt allow-list + 2-client cap) used by
    // both the raw-TCP and WebSocket accept paths.
    private void OnboardAndServe(IClientChannel channel)
    {
        string? idFrame;
        try
        {
            // Read the client ID. If the client does not send one in time, or sends
            // an ID not in valid_ids.txt, kick them because they sus
            idFrame = channel.Receive(5000);
        }
        catch (TimeoutException)
        {
            Console.WriteLine("Client did not send ID in time. Connection closed.");
            channel.Close();
            return;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading client ID: {ex.Message}");
            channel.Close();
            return;
        }

        if (idFrame == null)
        {
            Console.WriteLine("Client disconnected before sending ID.");
            channel.Close();
            return;
        }

        string clientId = idFrame.Trim();

        // check if the client ID is valid and is in the valid_ids.txt file
        if (!clientId.StartsWith("BxF_ID_"))
        {
            Console.WriteLine($"Client {clientId} did not send a valid ID. Connection closed.");
            channel.Close();
            return;
        }

        //remove the prefix "BxF_ID_"
        clientId = clientId.Substring("BxF_ID_".Length);

        //check for valid id
        if (!System.IO.File.Exists("valid_ids.txt"))
        {
            Console.WriteLine("No file for valid ids. Please create a file named valid_ids.txt and populate with valid ids.");
            channel.Close();
            return;
        }
        if (!System.IO.File.ReadAllLines("valid_ids.txt").Contains(clientId))
        {
            Console.WriteLine($"Client {clientId} not on the nice list. Connection closed.");
            channel.Close();
            return;
        }

        //now we know the client is valid, so we can create them
        var client = new Client(channel, clientId);

        // kick out the client if there are already 2 connected (across both transports)
        // otherwise add the client to the list and continue
        lock (clientsLock)
        {
            if (_clients.Count >= 2)
            {
                Console.WriteLine("Client connection rejected: server full.");
                try { channel.Send("Server full. Try again later.\n"); } catch { }
                channel.Close();
                return;
            }
            _clients.Add(client);
        }

        //at this point, the client is valid, connected, and <= 2 clients are connected

        Console.WriteLine($"Client connected: {client.Id}: {channel.RemoteDescription}");

        // Notify other clients about the new connection
        string notification = $"BxF_SERVER_New connection: {client.Id}";
        lock (clientsLock)
        {
            client.sendMessage(_clients, notification);
        }

        //create a thread for the new client
        Thread clientThread = new Thread(() => HandleClient(client));
        clientThread.IsBackground = true;
        clientThread.Start();
    }

    private void HandleClient(Client client)
    {
        // send buffered messages to the client
        messageBuffer = client.receiveBufferedMessages(messageBuffer);

        while (true)
        {
            string? message;
            try
            {
                message = client.Channel.Receive();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client: {client.Channel.RemoteDescription}: {ex}");
                break;
            }

            if (message == null) break;

            Console.WriteLine($"Received: {message}");
            lock (clientsLock) { 
                client.sendMessage(_clients, message);
            }

            // Buffer only if there is no other client to relay to
            if (_clients.Count <= 1)
            {
                lock (bufferLock)
                {
                    //message buffer is a queue, so add messages to the beginning
                    messageBuffer.Insert(0, client.Id + "::" + message);
                    Console.WriteLine($"Adding to buffer: {client.Id}::{message}");
                }
            }
        }

        Console.WriteLine($"Client disconnected: {client.Channel.RemoteDescription}");
        
        //send a message that the client has disconnected
        string disconnectMessage = $"BxF_SERVER_Client disconnected: {client.Id}";
        lock (clientsLock)
        {
            client.sendMessage(_clients, disconnectMessage);
        }

        //remove the client from the client list
        lock (clientsLock)
        {
            _clients.Remove(client);
        }
        client.Channel.Close();

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
        server.Start(server.serverPort); // Start the server
    }

}
