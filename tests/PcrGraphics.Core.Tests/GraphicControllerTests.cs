using PcrGraphics.Core.Amcp;
using PcrGraphics.Core.Graphics;

namespace PcrGraphics.Core.Tests;

public class GraphicControllerTests
{
    private static readonly Layer L = new(1, 30);
    private static readonly GraphicItem Guest = new("Guest 1", "lower_third",
        new Dictionary<string, string> { ["name"] = "د. أحمد", ["title"] = "محلل" });

    private static async Task<(FakeCaspar server, CasparClient client, GraphicController ctrl)> Setup(
        Func<string, string?>? reply = null, TimeSpan? timeout = null)
    {
        var server = new FakeCaspar(reply ?? (_ => "202 OK\r\n"));
        var client = new CasparClient("127.0.0.1", server.Port) { CommandTimeout = timeout ?? TimeSpan.FromSeconds(2) };
        await client.ConnectAsync();
        return (server, client, new GraphicController(client, L));
    }

    [Fact]
    public async Task Cue_take_out_sends_the_right_commands_and_tracks_state()
    {
        var (server, client, ctrl) = await Setup();
        await using var _s = server; await using var _c = client;

        Assert.True(await ctrl.CueAsync(Guest));
        Assert.Equal(GraphicState.Cued, ctrl.State);
        Assert.True(await ctrl.TakeInAsync());
        Assert.Equal(GraphicState.OnAir, ctrl.State);
        Assert.True(await ctrl.TakeOutAsync());
        Assert.Equal(GraphicState.Cued, ctrl.State);

        Assert.StartsWith("CG 1-30 ADD 1 lower_third 0 ", server.Received[0]);
        Assert.Equal("CG 1-30 PLAY 1", server.Received[1]);
        Assert.Equal("CG 1-30 STOP 1", server.Received[2]);
    }

    [Fact]
    public async Task Take_without_cue_is_refused_and_nothing_is_sent()
    {
        var (server, client, ctrl) = await Setup();
        await using var _s = server; await using var _c = client;

        Assert.False(await ctrl.TakeInAsync());
        Assert.Contains("refused", ctrl.LastError!);
        await Task.Delay(100);
        Assert.Empty(server.Received);
    }

    [Fact]
    public async Task Rejected_take_keeps_the_graphic_cued()
    {
        var (server, client, ctrl) = await Setup(cmd => cmd.Contains("PLAY") ? "404 CG ERROR\r\n" : "202 OK\r\n");
        await using var _s = server; await using var _c = client;

        await ctrl.CueAsync(Guest);
        Assert.False(await ctrl.TakeInAsync());
        Assert.Equal(GraphicState.Cued, ctrl.State);
        Assert.Contains("404", ctrl.LastError!);
    }

    [Fact]
    public async Task No_reply_to_take_makes_the_graphic_unknown()
    {
        var (server, client, ctrl) = await Setup(cmd => cmd.Contains("PLAY") ? null : "202 OK\r\n", TimeSpan.FromMilliseconds(300));
        await using var _s = server; await using var _c = client;

        await ctrl.CueAsync(Guest);
        Assert.False(await ctrl.TakeInAsync());
        Assert.Equal(GraphicState.Unknown, ctrl.State);
        Assert.False(ctrl.Machine.CanDo(GraphicAction.TakeIn));
    }

    [Fact]
    public async Task Dropped_connection_turns_an_on_air_graphic_unknown()
    {
        var (server, client, ctrl) = await Setup();
        await using var _s = server; await using var _c = client;

        await ctrl.CueAsync(Guest);
        await ctrl.TakeInAsync();
        server.DropConnection();
        await ctrl.UpdateAsync(new Dictionary<string, string> { ["title"] = "x" });

        Assert.Equal(GraphicState.Unknown, ctrl.State);
    }

    [Fact]
    public async Task Update_on_air_changes_the_current_text()
    {
        var (server, client, ctrl) = await Setup();
        await using var _s = server; await using var _c = client;

        await ctrl.CueAsync(Guest);
        await ctrl.TakeInAsync();
        Assert.True(await ctrl.UpdateAsync(new Dictionary<string, string> { ["title"] = "خبير اقتصادي" }));

        Assert.Equal(GraphicState.OnAir, ctrl.State);
        Assert.Equal("خبير اقتصادي", ctrl.Current!.Fields["title"]);
        Assert.Equal("د. أحمد", ctrl.Current!.Fields["name"]);
    }

    [Fact]
    public async Task Clear_empties_the_layer_and_forgets_the_item()
    {
        var (server, client, ctrl) = await Setup();
        await using var _s = server; await using var _c = client;

        await ctrl.CueAsync(Guest);
        await ctrl.TakeInAsync();
        Assert.True(await ctrl.ClearAsync());

        Assert.Equal(GraphicState.Empty, ctrl.State);
        Assert.Null(ctrl.Current);
        Assert.Equal("CLEAR 1-30", server.Received[^1]);
    }
}
