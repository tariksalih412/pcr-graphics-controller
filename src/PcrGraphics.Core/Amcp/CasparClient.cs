using System.Net.Sockets;
using System.Text;

namespace PcrGraphics.Core.Amcp;

public enum ConnectionState { Disconnected, Connecting, Connected }

/// <summary>
/// One TCP connection to CasparCG's AMCP port.
/// Commands are sent one at a time and each waits for its own reply, so a reply
/// always belongs to the command that caused it. Any timeout or socket error closes
/// the connection: after that, what is on air is unknown until we reconnect.
/// </summary>
public sealed class CasparClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TcpClient? _tcp;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public CasparClient(string host = "127.0.0.1", int port = 5250)
    {
        _host = host;
        _port = port;
    }

    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromSeconds(5);

    public string Endpoint => $"{_host}:{_port}";
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    /// <summary>Raised on every state change. May be raised on a background thread.</summary>
    public event Action<ConnectionState>? StateChanged;

    /// <summary>Every line sent (">> ...") and received ("<< ..."), for a log panel.</summary>
    public event Action<string>? Log;

    /// <summary>One connection attempt. Throws if CasparCG is not reachable.</summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        Close();
        SetState(ConnectionState.Connecting);
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            tcp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ConnectTimeout);
            await tcp.ConnectAsync(_host, _port, timeout.Token);
        }
        catch
        {
            tcp.Dispose();
            SetState(ConnectionState.Disconnected);
            throw;
        }

        var stream = tcp.GetStream();
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _tcp = tcp;
        _reader = new StreamReader(stream, utf8);
        _writer = new StreamWriter(stream, utf8) { NewLine = "\r\n", AutoFlush = false };
        SetState(ConnectionState.Connected);
    }

    /// <summary>
    /// Keeps the connection alive until cancelled: connects, checks it with a heartbeat,
    /// and reconnects with backoff (1, 2, 4, then every 5 seconds) after any failure.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAsync(ct);
                Log?.Invoke($"-- connected to {Endpoint}");
                delay = TimeSpan.FromSeconds(1);
                while (State == ConnectionState.Connected)
                {
                    await Task.Delay(HeartbeatInterval, ct);
                    await SendAsync(AmcpCommands.Version(), ct, quiet: true);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"-- {Endpoint} unavailable: {ex.Message}");
            }

            Close();
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxBackoff.Ticks));
        }
        Close();
    }

    /// <summary>Sends one command and waits for its reply. Throws if not connected or on timeout.</summary>
    public async Task<AmcpReply> SendAsync(string command, CancellationToken ct = default, bool quiet = false)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            if (State != ConnectionState.Connected || _writer is null || _reader is null)
                throw new InvalidOperationException("Not connected to CasparCG");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CommandTimeout);
            try
            {
                if (!quiet) Log?.Invoke(">> " + command);
                await _writer.WriteLineAsync(command.AsMemory(), timeout.Token);
                await _writer.FlushAsync(timeout.Token);
                var reply = await ReadReplyAsync(_reader, timeout.Token);
                if (!quiet || !reply.IsSuccess) Log?.Invoke("<< " + reply);
                return reply;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Close();
                throw new TimeoutException($"No reply from CasparCG within {CommandTimeout.TotalSeconds:0.#} s to: {command}");
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                Close();
                throw;
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<AmcpReply> ReadReplyAsync(StreamReader reader, CancellationToken ct)
    {
        // Skip anything that is not a status line (left over from an earlier reply whose
        // length we could not know), so this reply still belongs to this command.
        int code;
        string status;
        while (true)
        {
            var line = await reader.ReadLineAsync(ct) ?? throw new IOException("CasparCG closed the connection");
            (code, status) = AmcpReply.ParseStatusLine(line);
            if (code != 0) break;
            if (line.Length > 0) Log?.Invoke("<< (skipped stray line) " + line);
        }

        var data = new List<string>();
        var expected = AmcpReply.DataLinesFor(code);
        if (expected == 1)
        {
            var first = await reader.ReadLineAsync(ct) ?? "";
            data.Add(first);
            if (code == 201 && AmcpReply.StartsXml(first))       // INFO on 2.5.1: XML until an empty line
                await ReadUntilEmptyLineAsync(reader, data, ct);
        }
        else if (expected == -1)
        {
            await ReadUntilEmptyLineAsync(reader, data, ct);
        }
        return new AmcpReply(code, status, data);
    }

    private static async Task ReadUntilEmptyLineAsync(StreamReader reader, List<string> data, CancellationToken ct)
    {
        while (await reader.ReadLineAsync(ct) is { Length: > 0 } line)
            data.Add(line);
    }

    private void Close()
    {
        _reader?.Dispose();
        _writer = null;
        _reader = null;
        _tcp?.Dispose();
        _tcp = null;
        if (State != ConnectionState.Disconnected) SetState(ConnectionState.Disconnected);
    }

    private void SetState(ConnectionState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }

    public ValueTask DisposeAsync()
    {
        Close();
        _sendLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
