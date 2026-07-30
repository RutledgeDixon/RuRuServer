using System;
using System.Collections.Generic;

namespace RuRuCommsServer
{
    public class Client
    {
        public IClientChannel Channel { get; }
        public string Id { get; }

        public Client(IClientChannel channel, string id)
        {
            Channel = channel;
            Id = id;
        }
        public List<string> receiveBufferedMessages(List<string> messageBuffer)
        {

            //send all messages that were not from this client
            if (messageBuffer.Count != 0)
            {
                int numSentMessages = 0;

                //loop through all messages in the buffer
                for (int i = messageBuffer.Count - 1; i >= 0; i--)
                {
                    //check if the sender is not the same as this client
                    string message = messageBuffer[i];
                    string msgId = message.Substring(0, message.IndexOf(":"));
                    string msg = message.Substring(msgId.Length+2);
                    if (this.Id != msgId)
                    {
                        //send the message
                        this.Channel.Send(msg + "\n");

                        //increment numSentMessages
                        numSentMessages++;

                        //remove the message from messageBuffer
                        messageBuffer.RemoveAt(i);
                    }
                }
                Console.WriteLine($"Sent {numSentMessages} buffered messages to client");

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
                    client.Channel.Send(message + "\n");
                    Console.WriteLine($"Sent message to {client.Id}: {message}");
                }
            }
        }
    }
}
