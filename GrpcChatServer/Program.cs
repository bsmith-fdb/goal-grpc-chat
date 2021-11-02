using Grpc.Core;
using System;
using System.Linq;

namespace GrpcChatServer
{
    class Program
    {
        static void Main(string[] args)
        {
            const int port = 1337;
            const string host = "0.0.0.0";

            var svc = new ChatServiceImpl();
            svc.ClientConnected += Svc_ClientConnected;
            svc.ClientDisconnected += Svc_ClientDisconnected;
            svc.ChatMessageReceived += Svc_ChatMessageReceived;

            var svr = new Grpc.Core.Server()
            {
                Services = { ChatService.BindService(svc) },
                Ports = { new ServerPort(host, port, ServerCredentials.Insecure) }
            };
            svr.Start();

            Console.WriteLine($"Started server on {host}:{port}");

            Console.WriteLine("Press [Enter] to exit");

            while (Console.Read() != (int)ConsoleKey.Enter)
            {
                continue;
            }
        }

        private static void Svc_ChatMessageReceived(ChatMessage cm)
        {
            Console.WriteLine($"Message from {cm.Name}: {cm.Message}");
        }

        private static void Svc_ClientDisconnected(string username)
        {
            Console.WriteLine($"Client disconnected: {username}");
        }

        private static void Svc_ClientConnected(string username)
        {
            Console.WriteLine($"Client connected: {username}");
        }
    }
}
