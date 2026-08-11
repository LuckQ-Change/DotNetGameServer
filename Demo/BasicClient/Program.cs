using System.Net.Sockets;

using BasicServer.Protocol;

using Core.Networking;

using DotNetGameServer;

using Google.Protobuf;

var host = args.ElementAtOrDefault(0) ?? "127.0.0.1";
var port = int.TryParse(args.ElementAtOrDefault(1), out var parsedPort) ? parsedPort : 5500;
var message = args.ElementAtOrDefault(2) ?? "hello";

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
using var client = new TcpClient { NoDelay = true };

Console.WriteLine($"正在连接 {host}:{port} ...");
await client.ConnectAsync(host, port, timeout.Token);

var request = new envelope
{
    Type = PingRequest.Descriptor.FullName,
    Payload = new PingRequest { Message = message }.ToByteString(),
    RequestId = Guid.NewGuid().ToString("N"),
    Channel = CommunicationChannel.Client
};

await ProtobufFrameCodec.WriteEnvelopeAsync(client.GetStream(), request, 1024 * 1024, timeout.Token);
var responseEnvelope = await ProtobufFrameCodec.ReadEnvelopeAsync(
    client.GetStream(),
    1024 * 1024,
    timeout.Token) ?? throw new EndOfStreamException("服务器未返回响应。");

if (responseEnvelope.ErrorCode != 0)
{
    Console.Error.WriteLine($"请求失败: {responseEnvelope.ErrorCode} {responseEnvelope.ErrorMessage}");
    return 1;
}

if (!string.Equals(responseEnvelope.Type, PingResponse.Descriptor.FullName, StringComparison.Ordinal))
    throw new InvalidDataException($"响应类型不匹配: {responseEnvelope.Type}");

var response = PingResponse.Parser.ParseFrom(responseEnvelope.Payload);
Console.WriteLine($"服务端响应: {response.Message}");
Console.WriteLine($"服务端 Unix 时间: {response.ServerTimeUnix}");
return 0;
