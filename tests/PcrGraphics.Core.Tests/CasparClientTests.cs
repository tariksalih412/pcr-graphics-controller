using PcrGraphics.Core.Amcp;

namespace PcrGraphics.Core.Tests;

public class CasparClientTests
{
    private static string Answer(string cmd) => cmd switch
    {
        "VERSION" => "201 VERSION OK\r\n2.5.1 test\r\n",
        "INFO" => "200 INFO OK\r\n1 1080i5000 PLAYING\r\n2 720p5000 PLAYING\r\n\r\n",
        _ when cmd.StartsWith("CG 1-30 PLAY") => "404 CG ERROR\r\n",
        _ => "202 OK\r\n",
    };

    [Fact]
    public async Task Version_reply_has_one_data_line()
    {
        await using var server = new FakeCaspar(Answer);
        await using var client = new CasparClient("127.0.0.1", server.Port);
        await client.ConnectAsync();

        var reply = await client.SendAsync("VERSION");

        Assert.Equal(201, reply.Code);
        Assert.Equal("2.5.1 test", Assert.Single(reply.Data));
    }

    [Fact]
    public async Task Code_200_reads_data_until_empty_line()
    {
        await using var server = new FakeCaspar(Answer);
        await using var client = new CasparClient("127.0.0.1", server.Port);
        await client.ConnectAsync();

        var reply = await client.SendAsync("INFO");

        Assert.Equal(2, reply.Data.Count);
        Assert.True(reply.IsSuccess);
    }

    [Fact]
    public async Task Replies_stay_matched_to_their_commands()
    {
        await using var server = new FakeCaspar(Answer);
        await using var client = new CasparClient("127.0.0.1", server.Port);
        await client.ConnectAsync();

        var a = await client.SendAsync("VERSION");
        var b = await client.SendAsync("CG 1-30 STOP 1");
        var c = await client.SendAsync("CG 1-30 PLAY 1");

        Assert.Equal(new[] { 201, 202, 404 }, new[] { a.Code, b.Code, c.Code });
        Assert.False(c.IsSuccess);
    }

    [Fact]
    public async Task Silent_server_times_out_and_marks_disconnected()
    {
        await using var server = new FakeCaspar(_ => null);
        await using var client = new CasparClient("127.0.0.1", server.Port) { CommandTimeout = TimeSpan.FromMilliseconds(300) };
        await client.ConnectAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => client.SendAsync("CG 1-30 PLAY 1"));
        Assert.Equal(ConnectionState.Disconnected, client.State);
    }

    [Fact]
    public async Task Dropped_connection_marks_disconnected()
    {
        await using var server = new FakeCaspar(Answer);
        await using var client = new CasparClient("127.0.0.1", server.Port);
        await client.ConnectAsync();
        await client.SendAsync("VERSION");

        server.DropConnection();

        await Assert.ThrowsAnyAsync<Exception>(() => client.SendAsync("VERSION"));
        Assert.Equal(ConnectionState.Disconnected, client.State);
    }

    [Fact]
    public async Task Sending_while_disconnected_throws_without_touching_the_network()
    {
        await using var client = new CasparClient("127.0.0.1", 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("VERSION"));
    }

    [Fact]
    public async Task Unreachable_engine_throws_and_stays_disconnected()
    {
        await using var client = new CasparClient("127.0.0.1", 1) { ConnectTimeout = TimeSpan.FromMilliseconds(500) };
        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync());
        Assert.Equal(ConnectionState.Disconnected, client.State);
    }

    [Fact]
    public async Task RunAsync_connects_and_stops_cleanly()
    {
        await using var server = new FakeCaspar(Answer);
        await using var client = new CasparClient("127.0.0.1", server.Port) { HeartbeatInterval = TimeSpan.FromMilliseconds(100) };
        var states = new List<ConnectionState>();
        client.StateChanged += s => { lock (states) states.Add(s); };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var run = client.RunAsync(cts.Token);
        await WaitUntil(() => client.State == ConnectionState.Connected);
        Assert.Equal(ConnectionState.Connected, client.State);

        cts.Cancel();
        await run;
        Assert.Equal(ConnectionState.Disconnected, client.State);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++) await Task.Delay(50);
    }
}
