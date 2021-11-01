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
            public Guid Guid { get; set; }
        }

        private readonly List<ConnectedClient> clients = new List<ConnectedClient>();
        private readonly SemaphoreSlim mutex = new SemaphoreSlim(1);

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
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var taskList = clients.Select(c => 
                Task.Run(async () => {
                    try
                    {
                        await c.ChatWriter.WriteAsync(sm);
                    }
                    catch (Exception ex)
                    {
                        ex.Data.Add("Username", c.Username);
                        throw;
                    }
            }));

            var tasks = Task.WhenAll(taskList);

            try
            {
                await tasks;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Caught exception in Broadcast: {ex.Message}");
            }
            
            tasks.Exception?.Handle(ex => { 
                Console.WriteLine($"Caught exception in Brodcast: Username='{ex.Data["Username"]}' -- {ex.Message}"); 
                return true; 
            });

            Console.WriteLine($"ThreadID={System.Threading.Thread.CurrentThread.ManagedThreadId} Broadcast {sw.Elapsed:G}");
        }

        private async Task AddClient(ConnectedClient client)
        {
            try
            {
                await mutex.WaitAsync();
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
            try
            {
                await mutex.WaitAsync();
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
            var responseHeaders = new Metadata();
            responseHeaders.Add("status", "OK");
            await context.WriteResponseHeadersAsync(responseHeaders);
            
            string username = context.RequestHeaders.GetValue("username");
            ClientConnected.Invoke(username);

            var client = new ConnectedClient()
            {
                ChatReader = requestStream,
                ChatWriter = responseStream,
                ChatContext = context,
                Username = username,
                Guid = new Guid()
            };

            await AddClient(client);

            while (await requestStream.MoveNext())
            {
                try
                {
                    await mutex.WaitAsync();
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
