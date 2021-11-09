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

            var svr = new Grpc.Core.Server()
            {
                Services = { ChatService.BindService(svc) },
                Ports = { new ServerPort(host, port, ServerCredentials.Insecure) }
            };

            svr.Start();

            while (Console.ReadLine() != "quit")
            {
                continue;
            }
        }
    }
}
