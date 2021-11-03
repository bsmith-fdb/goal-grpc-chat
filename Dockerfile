FROM mcr.microsoft.com/dotnet/sdk:5.0 AS builder
WORKDIR /source
COPY . .
RUN dotnet build GrpcChatServer/GrpcChatServer.csproj

FROM mcr.microsoft.com/dotnet/runtime:5.0
WORKDIR /app
EXPOSE 1337
COPY --from=builder /source/GrpcChatServer/bin/Debug/net5.0 .
ENTRYPOINT ["dotnet", "GrpcChatServer.dll"]
