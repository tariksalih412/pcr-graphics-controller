using PcrGraphics.Core.Amcp;
using PcrGraphics.Core.Graphics;

namespace PcrGraphics.Core.Tests;

public class LayerSyncTests
{
    private static readonly GraphicItem Guest = new("Guest 1", "lower_third",
        new Dictionary<string, string> { ["name"] = "د. أحمد", ["title"] = "محلل" });

    [Fact]
    public async Task One_INFO_per_channel_sets_every_layer()
    {
        await using var server = new FakeCaspar(cmd => cmd == "INFO 1"
            ? InfoSamples.AsReply(InfoSamples.LowerThirdOn30) : "202 OK\r\n");
        await using var client = new CasparClient("127.0.0.1", server.Port);
        await client.ConnectAsync();
        var lt = new GraphicController(client, new Layer(1, 30), "lower_third");
        var bn = new GraphicController(client, new Layer(1, 31), "breaking_news");
        var sync = new LayerSync(client, [lt, bn], syncOnConnect: false);

        Assert.True(await sync.SyncAsync());

        Assert.Equal(GraphicState.Unknown, lt.State);       // found, but this session never loaded it
        Assert.Equal(GraphicState.Empty, bn.State);
        Assert.Equal("INFO 1", Assert.Single(server.Received));
    }

    [Fact]
    public async Task Heartbeat_loss_then_sync_restores_on_air()
    {
        var connection = 0;
        await using var server = new FakeCaspar(cmd => cmd switch
        {
            "INFO 1" => InfoSamples.AsReply(InfoSamples.LowerThirdOn30),
            "VERSION" when connection > 0 => null,              // silent: the heartbeat times out
            _ => "202 OK\r\n",
        });
        await using var client = new CasparClient("127.0.0.1", server.Port) { CommandTimeout = TimeSpan.FromMilliseconds(300) };
        await client.ConnectAsync();
        var lt = new GraphicController(client, new Layer(1, 30), "lower_third");
        var sync = new LayerSync(client, [lt], syncOnConnect: false);

        await lt.CueAsync(Guest);
        await lt.TakeInAsync();
        connection = 1;
        await Assert.ThrowsAsync<TimeoutException>(() => client.SendAsync("VERSION"));   // heartbeat lost
        Assert.Equal(GraphicState.Unknown, lt.State);

        // reconnect (the fake accepts one client, so reconcile directly with the same reply)
        var info = ChannelInfo.Parse(InfoSamples.Lines(InfoSamples.LowerThirdOn30));
        lt.Reconcile(info.Foreground(30));

        Assert.Equal(GraphicState.OnAir, lt.State);
        Assert.Equal("Guest 1", lt.Current!.Label);
    }

    [Fact]
    public async Task Empty_server_clears_current_item()
    {
        await using var server = new FakeCaspar(cmd => cmd == "INFO 1"
            ? InfoSamples.AsReply(InfoSamples.Empty) : "202 OK\r\n");
        await using var client = new CasparClient("127.0.0.1", server.Port);
        await client.ConnectAsync();
        var lt = new GraphicController(client, new Layer(1, 30), "lower_third");
        await lt.CueAsync(Guest);

        await new LayerSync(client, [lt], syncOnConnect: false).SyncAsync();

        Assert.Equal(GraphicState.Empty, lt.State);
        Assert.Null(lt.Current);
    }

    [Fact]
    public async Task Rejected_INFO_keeps_states()
    {
        await using var server = new FakeCaspar(cmd => cmd == "INFO 1"
            ? "400 ERROR\r\nINFO 1\r\n" : "202 OK\r\n");
        await using var client = new CasparClient("127.0.0.1", server.Port);
        await client.ConnectAsync();
        var lt = new GraphicController(client, new Layer(1, 30), "lower_third");
        await lt.CueAsync(Guest);

        Assert.False(await new LayerSync(client, [lt], syncOnConnect: false).SyncAsync());
        Assert.Equal(GraphicState.Cued, lt.State);
    }

    [Fact]
    public async Task Sync_runs_by_itself_on_connect()
    {
        await using var server = new FakeCaspar(cmd => cmd == "INFO 1"
            ? InfoSamples.AsReply(InfoSamples.LowerThirdOn30) : "202 OK\r\n");
        await using var client = new CasparClient("127.0.0.1", server.Port);
        var lt = new GraphicController(client, new Layer(1, 30), "lower_third");
        var done = new TaskCompletionSource<bool>();
        var sync = new LayerSync(client, [lt]);
        sync.Completed += ok => done.TrySetResult(ok);

        await client.ConnectAsync();

        Assert.True(await done.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(GraphicState.Unknown, lt.State);
    }
}
