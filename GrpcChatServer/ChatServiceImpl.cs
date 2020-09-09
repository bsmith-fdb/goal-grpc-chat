using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GrpcChatServer
{
    public class ChatServiceImpl : ChatService.ChatServiceBase
    {
        public delegate void ClientConnectedEventHandler(string username);
        public event ClientConnectedEventHandler ClientConnected;

        public delegate void ClientDisconnectedEventHandler(string username);
        public event ClientDisconnectedEventHandler ClientDisconnected;

        public delegate void ChatMessageReceivedEventHandler(ChatMessage cm);
        public event ChatMessageReceivedEventHandler ChatMessageReceived;

        public class ConnectedClient
        {
            public IAsyncStreamReader<ChatMessage> ChatReader { get; set; }
            public IAsyncStreamWriter<ServerChatMessage> ChatWriter { get; set; }
            public ServerCallContext ChatContext { get; set; }
            public string Username { get; set; }
        }

        private List<ConnectedClient> clients = new List<ConnectedClient>();
        SemaphoreSlim mutex = new SemaphoreSlim(1);

        private async Task Broadcast(ChatMessage cm)
        {
            var sm = new ServerChatMessage()
            {
                Message = cm
            };

            await Broadcast(sm);
        }

        private async Task Broadcast(ServerChatMessage sm)
        {
            Console.WriteLine($"ThreadID={System.Threading.Thread.CurrentThread.ManagedThreadId} Broadcast");
            foreach (var client in clients)
            {
                await client.ChatWriter.WriteAsync(sm);
            }
        }

        private async Task AddClient(ConnectedClient client)
        {
            await mutex.WaitAsync().ConfigureAwait(false);

            try
            {
                Console.WriteLine($"ThreadID={System.Threading.Thread.CurrentThread.ManagedThreadId} AddClient");
                clients.Add(client);
                var st = new Status();
                st.AddClient = client.Username;
                st.CurrentClients.Add(clients.Select(x => x.Username));
                var sm = new ServerChatMessage();
                sm.Status = st;

                await Broadcast(sm);
            }
            finally
            {
                mutex.Release();
            }
        }

        private async Task DeleteClient(ConnectedClient client)
        {
            await mutex.WaitAsync().ConfigureAwait(false);

            try
            {
                Console.WriteLine($"ThreadID={System.Threading.Thread.CurrentThread.ManagedThreadId} DeleteClient");
                clients.Remove(client);
                var st = new Status();
                st.DeleteClient = client.Username;
                st.CurrentClients.Add(clients.Select(x => x.Username));
                var sm = new ServerChatMessage();
                sm.Status = st;

                await Broadcast(sm);
            }
            finally
            {
                mutex.Release();
            }
        }

        public override async Task ChatStream(IAsyncStreamReader<ChatMessage> requestStream, IServerStreamWriter<ServerChatMessage> responseStream, ServerCallContext context)
        {
            string username = context.RequestHeaders.GetValue("username");
            ClientConnected.Invoke(username);

            var client = new ConnectedClient()
            {
                ChatReader = requestStream,
                ChatWriter = responseStream,
                ChatContext = context,
                Username = username
            };

            await AddClient(client);

            while (await requestStream.MoveNext())
            {
                await mutex.WaitAsync().ConfigureAwait(false);

                try
                {
                    ChatMessageReceived.Invoke(requestStream.Current);
                    await Broadcast(requestStream.Current);
                }
                finally
                {
                    mutex.Release();
                }
            }

            ClientDisconnected.Invoke(username);

            await DeleteClient(client);
        }
    }
}
