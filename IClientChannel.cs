using System;
using System.Threading;

namespace RuRuCommsServer
{
    // Abstracts the transport a client is connected over (raw TCP or WebSocket)
    // so the relay/auth/buffering logic in Program.cs and Client.cs stays transport-agnostic.
    public interface IClientChannel
    {
        string RemoteDescription { get; }

        // Blocks until one full message arrives. Returns null on graceful close.
        // Throws TimeoutException if nothing arrives within timeoutMs.
        string? Receive(int timeoutMs = Timeout.Infinite);

        void Send(string message);

        void Close();
    }
}
