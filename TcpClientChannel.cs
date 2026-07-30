using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace RuRuCommsServer
{
    // Wraps the original raw-TCP transport used by the desktop RuRuComms client.
    public class TcpClientChannel : IClientChannel
    {
        private readonly TcpClient _tcpClient;
        private readonly NetworkStream _stream;
        private readonly byte[] _buffer = new byte[1024];

        public TcpClientChannel(TcpClient tcpClient)
        {
            _tcpClient = tcpClient;
            _stream = tcpClient.GetStream();
        }

        public string RemoteDescription => _tcpClient.Client.RemoteEndPoint?.ToString() ?? "unknown";

        public string? Receive(int timeoutMs = Timeout.Infinite)
        {
            _stream.ReadTimeout = timeoutMs;
            int bytesRead;
            try
            {
                bytesRead = _stream.Read(_buffer, 0, _buffer.Length);
            }
            catch (IOException) when (timeoutMs != Timeout.Infinite)
            {
                throw new TimeoutException("Timed out waiting for data.");
            }

            return bytesRead == 0 ? null : Encoding.UTF8.GetString(_buffer, 0, bytesRead);
        }

        public void Send(string message)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            _stream.Write(bytes, 0, bytes.Length);
        }

        public void Close()
        {
            try { _stream.Close(); } catch { }
            _tcpClient.Close();
        }
    }
}
