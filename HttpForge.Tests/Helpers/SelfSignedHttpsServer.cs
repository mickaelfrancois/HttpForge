using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace HttpForge.Tests.Helpers;

/// <summary>
/// A minimal HTTPS server backed by a self-signed certificate, for testing TLS
/// behavior. Listens on a loopback ephemeral port and answers every successful
/// handshake with "200 OK" / body "OK". The certificate is untrusted, so a client
/// using default validation rejects the handshake.
/// </summary>
public sealed class SelfSignedHttpsServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly X509Certificate2 _cert;
    private readonly CancellationTokenSource _cts = new();

    public SelfSignedHttpsServer()
    {
        _cert = CreateSelfSignedCertificate();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _ = AcceptLoopAsync(_cts.Token);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public string Url => $"https://127.0.0.1:{Port}/";

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            await using (var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false))
            {
                await ssl.AuthenticateAsServerAsync(_cert, clientCertificateRequired: false,
                    enabledSslProtocols: SslProtocols.None, checkCertificateRevocation: false);

                // Consume whatever the client sends; we don't parse the request.
                var buf = new byte[4096];
                _ = await ssl.ReadAsync(buf, ct);

                var body = "OK"u8.ToArray();
                var headers = $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                await ssl.WriteAsync(Encoding.ASCII.GetBytes(headers), ct);
                await ssl.WriteAsync(body, ct);
                await ssl.FlushAsync(ct);
            }
        }
        catch
        {
            // Client aborted the handshake (default validation rejected the
            // self-signed cert). Expected for the negative test.
        }
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=HttpForge Test", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        san.AddDnsName("localhost");
        req.CertificateExtensions.Add(san.Build());

        using var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // Round-trip through PFX so the private key is usable by SslStream as a
        // server certificate on Windows (ephemeral keys are rejected there).
        var pfx = cert.Export(X509ContentType.Pfx);
        return X509CertificateLoader.LoadPkcs12(pfx, null);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cert.Dispose();
        _cts.Dispose();
    }
}
