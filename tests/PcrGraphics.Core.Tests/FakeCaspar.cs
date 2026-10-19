using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PcrGraphics.Core.Tests;

/// <summary>
/// A tiny in-process AMCP server for tests. It records every command and answers
/// with whatever the test's reply function returns (null = stay silent).
/// </summary>
public sealed class FakeCaspar : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Func<string, string?> _reply;
    private readonly CancellationTokenSource _cts = new();
    private TcpClient? _client;

    public FakeCaspar(Func<string, string?> reply)
    {
        _reply = reply;
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public List<string> Received { get; } = new();

    private async Task AcceptLoop()
    {
        try
        {
            _client = await _listener.AcceptTcpClientAsync(_cts.Token);
            var stream = _client.GetStream();
            var utf8 = new UTF8Encoding(false);
            using var reader = new StreamReader(stream, utf8);
            using var writer = new StreamWriter(stream, utf8) { NewLine = "\r\n", AutoFlush = true };
            while (await reader.ReadLineAsync(_cts.Token) is { } line)
            {
                lock (Received) Received.Add(line);
                var answer = _reply(line);
                if (answer is not null) await writer.WriteAsync(answer);
            }
        }
        catch { /* closed by the test */ }
    }

    /// <summary>Simulates CasparCG crashing: drops the connection.</summary>
    public void DropConnection() => _client?.Dispose();

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _client?.Dispose();
        _listener.Stop();
        return ValueTask.CompletedTask;
    }
}
