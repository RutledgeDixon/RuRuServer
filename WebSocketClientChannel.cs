using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

namespace RuRuCommsServer
{
    // Wraps a WebSocket connection (from the web client) behind the same
    // IClientChannel contract the TCP transport uses.
    public class WebSocketClientChannel : IClientChannel
    {
        private readonly WebSocket _socket;
        private readonly string _remoteDescription;
        private readonly byte[] _buffer = new byte[1024];

        public WebSocketClientChannel(WebSocket socket, string remoteDescription)
        {
            _socket = socket;
            _remoteDescription = remoteDescription;
        }

        public string RemoteDescription => _remoteDescription;

        public string? Receive(int timeoutMs = Timeout.Infinite)
        {
            using var cts = timeoutMs == Timeout.Infinite
                ? new CancellationTokenSource()
                : new CancellationTokenSource(timeoutMs);

            WebSocketReceiveResult result;
            try
            {
                result = _socket.ReceiveAsync(new ArraySegment<byte>(_buffer), cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("Timed out waiting for data.");
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            // Assumes each logical message fits in one 1024-byte frame, matching
            // the TCP transport's read-boundary assumption (see PROTOCOL.md).
            return Encoding.UTF8.GetString(_buffer, 0, result.Count);
        }

        public void Send(string message)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        public void Close()
        {
            try
            {
                _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch { }
        }
    }
}
