using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RuRuCommsServer
{
    public class Client
    {
        public TcpClient TcpClient { get; }
        public string Id { get; }

        public Client(TcpClient tcpClient, string id)
        {
            TcpClient = tcpClient;
            Id = id;
        }
        public List<string> receiveBufferedMessages(List<string> messageBuffer)
        {

            //send all messages that were not from this client
            if (messageBuffer.Count != 0)
            {
                Console.WriteLine($"Sending {messageBuffer.Count} buffered messages to client...");

                //loop through all messages in the buffer
                for (int i = messageBuffer.Count - 1; i >= 0; i--)
                {
                    //check if the sender is not the same as this client
                    string message = messageBuffer[i];
                    string msgId = message.Substring(0, message.IndexOf(":"));
                    string msg = message.Substring(message.IndexOf(':') + 1);
                    if (this.Id != msgId)
                    {
                        //send the message
                        string msgWithDelimiter = msg + "\n";
                        byte[] msgBytes = Encoding.UTF8.GetBytes(msgWithDelimiter);
                        this.TcpClient.GetStream().Write(msgBytes, 0, msgBytes.Length);

                        //remove the message from messageBuffer
                        messageBuffer.RemoveAt(i);
                    }
                }

            }
            return messageBuffer;
        }

        public void sendMessage(List<Client> clients, string message)
        {
            //send the message to all clients except this one
            foreach (var client in clients)
            {
                if (client != this)
                {
                    byte[] msgBytes = Encoding.UTF8.GetBytes(message + "\n");
                    client.TcpClient.GetStream().Write(msgBytes, 0, msgBytes.Length);
                    Console.WriteLine($"Sent message to {client.Id}: {message}");
                }
            }
        }
    }
}
