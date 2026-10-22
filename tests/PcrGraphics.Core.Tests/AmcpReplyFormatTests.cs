using PcrGraphics.Core.Amcp;

namespace PcrGraphics.Core.Tests;

/// <summary>Reply shapes measured on CasparCG 2.5.1 (phase 6 probe).</summary>
public class AmcpReplyFormatTests
{
    private static string Answer(string cmd) => cmd switch
    {
        "INFO 1" => InfoSamples.AsReply(InfoSamples.LowerThirdOn30),
        "VERSION" => "201 VERSION OK\r\n2.5.1 ec38d94 Stable\r\n",
        "CG 1-30 INFO 1" => "400 ERROR\r\nCG 1-30 INFO 1\r\n",
        "JUNK" => "202 OK\r\nleft over\r\n",               // an extra line we did not expect
        _ => "202 OK\r\n",
    };

    private static async Task<(FakeCaspar, CasparClient)> Connect()
    {
        var server = new FakeCaspar(Answer);
        var client = new CasparClient("127.0.0.1", server.Port);
        await client.ConnectAsync();
        return (server, client);
    }

    [Fact]
    public async Task Info_201_reads_the_whole_xml_and_the_next_reply_stays_matched()
    {
        var (server, client) = await Connect();
        await using var _s = server; await using var _c = client;

        var info = await client.SendAsync("INFO 1");
        var version = await client.SendAsync("VERSION");

        Assert.Equal(201, info.Code);
        Assert.StartsWith("<?xml", info.Data[0]);
        Assert.Equal("</channel>", info.Data[^1].Trim());
        Assert.NotNull(ChannelInfo.Parse(info.Data).Foreground(30));
        Assert.Equal("2.5.1 ec38d94 Stable", Assert.Single(version.Data));
    }

    [Fact]
    public async Task Error_400_consumes_the_echoed_command()
    {
        var (server, client) = await Connect();
        await using var _s = server; await using var _c = client;

        var bad = await client.SendAsync("CG 1-30 INFO 1");
        var next = await client.SendAsync("CG 1-30 PLAY 1");

        Assert.Equal(400, bad.Code);
        Assert.Equal("CG 1-30 INFO 1", Assert.Single(bad.Data));
        Assert.Equal(202, next.Code);
    }

    [Fact]
    public async Task Stray_line_before_a_status_line_is_skipped()
    {
        var (server, client) = await Connect();
        await using var _s = server; await using var _c = client;
        var log = new List<string>();
        client.Log += log.Add;

        await client.SendAsync("JUNK");
        var reply = await client.SendAsync("VERSION");

        Assert.Equal(201, reply.Code);
        Assert.Contains(log, l => l.Contains("stray line"));
    }
}
