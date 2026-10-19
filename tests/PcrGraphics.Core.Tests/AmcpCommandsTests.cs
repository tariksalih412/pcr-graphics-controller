using PcrGraphics.Core.Amcp;

namespace PcrGraphics.Core.Tests;

public class AmcpCommandsTests
{
    private static readonly Layer L = new(1, 30);
    private static readonly Dictionary<string, string> Name = new() { ["name"] = "د. أحمد", ["title"] = "Analyst" };

    [Fact]
    public void Layer_is_written_channel_dash_layer() =>
        Assert.Equal("1-30", L.ToString());

    [Fact]
    public void Cue_loads_hidden_with_play_on_load_zero()
    {
        var cmd = AmcpCommands.CgAdd(L, "lower_third", playOnLoad: false, Name);
        Assert.StartsWith("CG 1-30 ADD 1 lower_third 0 \"", cmd);
    }

    [Fact]
    public void Arabic_text_is_kept_readable_and_quotes_are_escaped()
    {
        var cmd = AmcpCommands.CgAdd(L, "lower_third", false, Name);
        Assert.Contains("د. أحمد", cmd);
        Assert.Contains("\\\"name\\\"", cmd);
    }

    [Fact]
    public void Quote_escapes_backslash_and_double_quote() =>
        Assert.Equal("\"a\\\\b\\\"c\"", AmcpCommands.Quote("a\\b\"c"));

    [Theory]
    [InlineData("play", "CG 1-30 PLAY 1")]
    [InlineData("stop", "CG 1-30 STOP 1")]
    [InlineData("clear", "CLEAR 1-30")]
    public void Take_out_and_panic_commands(string which, string expected)
    {
        var cmd = which switch
        {
            "play" => AmcpCommands.CgPlay(L),
            "stop" => AmcpCommands.CgStop(L),
            _ => AmcpCommands.Clear(L),
        };
        Assert.Equal(expected, cmd);
    }

    [Fact]
    public void Template_name_with_space_is_quoted() =>
        Assert.Contains(" ADD 1 \"my template\" 1 ", AmcpCommands.CgAdd(L, "my template", true, Name));
}
