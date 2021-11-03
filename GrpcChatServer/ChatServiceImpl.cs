using Grpc.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace GrpcChatServer
{
    public class ChatServiceImpl : ChatService.ChatServiceBase
    {
        public class ConnectedClient
        {
            public IAsyncStreamReader<ChatMessage> ChatReader { get; set; }
            public IAsyncStreamWriter<ServerChatMessage> ChatWriter { get; set; }
            public ServerCallContext ChatContext { get; set; }
            public string Username { get; set; }
            public Guid Guid { get; set; }
        }

        private readonly HashSet<ConnectedClient> clients = new HashSet<ConnectedClient>();
        private readonly SemaphoreSlim mutex = new SemaphoreSlim(1);
        private readonly Logger logger;

        public ChatServiceImpl()
        {
            var config = new LoggingConfiguration();
            var consoleTarget = new ConsoleTarget
            {
                Name = "console",
                Layout = "${longdate}|${level:uppercase=true}|${message}",
            };
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, consoleTarget, "*");
            LogManager.Configuration = config;

            logger = LogManager.GetCurrentClassLogger();

            logger.Info("Server started");
        }

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
            await mutex.WaitAsync();

            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var taskList = clients.Select(c =>
                    Task.Run(async () =>
                    {
                        try
                        {
                            await c.ChatWriter.WriteAsync(sm);
                        }
                        catch (Exception ex)
                        {
                            ex.Data.Add("ClientObject", c);
                            ex.Data.Add("Username", c?.Username);
                            ex.Data.Add("Guid", c?.Guid);
                            throw;
                        }
                    }));

                var tasks = Task.WhenAll(taskList);

                await tasks;

                tasks.Exception?.Handle(ex =>
                {
                    logger.Warn(ex, $"!!! Broadcast Exception: Username='{ex.Data["Username"]}' !!! {ex.Message}");
                    return true;
                });

                logger.Info($"Broadcast: {(sm.Message != null ? "ChatMessage" : string.Empty)} {(sm.Status != null ? "Status" : string.Empty)} ClientCount={clients.Count} Elapsed={sw.Elapsed:G}");
            }
            catch (Exception ex)
            {
                logger.Error($"!!! Broadcast Exception: {ex.Message}");
            }
            finally
            {
                mutex.Release();
            }
        }

        private async Task AddClient(ConnectedClient client)
        {
            await mutex.WaitAsync();

            ServerChatMessage sm = null;

            try
            {
                clients.Add(client);

                var st = new Status();
                st.AddClient = client.Username;
                st.CurrentClients.Add(clients.Select(c => c.Username));

                sm = new ServerChatMessage();
                sm.Status = st;

                logger.Info($"AddClient: Username='{client.Username}'");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"!!! AddClient Exception: Username='{client.Username}' !!! {ex.Message}");
                sm = null;
            }
            finally
            {
                mutex.Release();
            }

            if (sm != null)
            {
                await Broadcast(sm);
            }
        }

        private async Task DeleteClient(ConnectedClient client)
        {
            await mutex.WaitAsync();

            ServerChatMessage sm = null;

            try
            {
                clients.Remove(client);

                var st = new Status();
                st.DeleteClient = client.Username;
                st.CurrentClients.Add(clients.Select(x => x.Username).ToList());

                sm = new ServerChatMessage();
                sm.Status = st;

                logger.Info($"DeleteClient: Username='{client.Username}'");
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"!!! DeleteClient Exception: Username='{client.Username}' !!! {ex.Message}");
                sm = null;
            }
            finally
            {
                mutex.Release();
            }

            if (sm != null)
            {
                await Broadcast(sm);
            }
        }

        public override async Task ChatStream(IAsyncStreamReader<ChatMessage> requestStream, IServerStreamWriter<ServerChatMessage> responseStream, ServerCallContext context)
        {
            string username = context.RequestHeaders.GetValue("username");
            var responseHeaders = new Metadata();

            if (string.IsNullOrEmpty(username))
            {
                responseHeaders.Add("status", "Error");
                responseHeaders.Add("message", "Username is required in request headers");
                await context.WriteResponseHeadersAsync(responseHeaders);
                return;
            }

            var client = new ConnectedClient()
            {
                ChatReader = requestStream,
                ChatWriter = responseStream,
                ChatContext = context,
                Username = username,
                Guid = Guid.NewGuid()
            };

            responseHeaders.Add("status", "OK");
            responseHeaders.Add("guid", client.Guid.ToString());
            
            await context.WriteResponseHeadersAsync(responseHeaders);

            await AddClient(client);

            try
            {
                while (await requestStream.MoveNext())
                {
                    await Broadcast(requestStream.Current);
                    throw new Exception("Test exception");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"!!! RequestStream Exception: Username='{client.Username}' !!! {ex.Message}");
            }

            await DeleteClient(client);
        }

    }
}
