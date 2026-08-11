using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Core;
using Core.Configuration;
using Core.Networking;

using DotNetGameServer;

using Google.Protobuf;

using Xunit;

namespace Tests;

public sealed class TlsIntegrationTests
{
    [Fact]
    public async Task TlsTransport_LoadsCertificateAndRejectsPlaintextFrames()
    {
        const string password = "tls-integration-test";
        var certificatePath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-game-server-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(certificatePath, CreateCertificate(password));
        var server = new TlsServer(new ServerOptions
        {
            Tls = new TlsOptions
            {
                Enabled = true,
                CertificatePath = certificatePath,
                CertificatePassword = password,
                CheckClientCertificateRevocation = false
            }
        });

        try
        {
            await server.StartAsync(0);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, server.ListeningPort!.Value);
            var stream = client.GetStream();
            await ProtobufFrameCodec.WriteEnvelopeAsync(stream, new envelope
            {
                Type = "DotNetGameServer.PlaintextRequest",
                Payload = ByteString.Empty,
                RequestId = "plaintext-request",
                Channel = CommunicationChannel.Client
            }, 64 * 1024);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var firstByte = new byte[1];
            var bytesRead = await stream.ReadAsync(firstByte, timeout.Token);

            Assert.Equal(0, bytesRead);
            Assert.True(server.IsRunning);
        }
        finally
        {
            await server.StopAsync();
            File.Delete(certificatePath);
        }
    }

    private static byte[] CreateCertificate(string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") },
            false));
        var subjectNames = new SubjectAlternativeNameBuilder();
        subjectNames.AddDnsName("localhost");
        subjectNames.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(subjectNames.Build());
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        return certificate.Export(X509ContentType.Pfx, password);
    }

    private sealed class TlsServer(ServerOptions options) : BaseGameServer("tls-test", null, options);
}
