using PcrGraphics.Core.Graphics;
using static PcrGraphics.Core.Graphics.GraphicAction;
using static PcrGraphics.Core.Graphics.GraphicState;

namespace PcrGraphics.Core.Tests;

public class GraphicStateMachineTests
{
    private static GraphicStateMachine At(GraphicState state)
    {
        var m = new GraphicStateMachine();
        var path = state switch
        {
            Cued => new[] { Cue },
            OnAir => new[] { Cue, TakeIn },
            _ => Array.Empty<GraphicAction>(),
        };
        foreach (var a in path) { m.Begin(a); m.Complete(a, accepted: true); }
        if (state == Unknown) { m.Begin(Cue); m.Fail(); }
        return m;
    }

    [Theory]
    [InlineData(Empty, Cue, true)]
    [InlineData(Empty, TakeIn, false)]
    [InlineData(Empty, TakeOut, false)]
    [InlineData(Empty, Update, false)]
    [InlineData(Cued, TakeIn, true)]
    [InlineData(Cued, Cue, true)]
    [InlineData(Cued, Update, true)]
    [InlineData(Cued, TakeOut, false)]
    [InlineData(OnAir, TakeIn, false)]
    [InlineData(OnAir, Cue, false)]
    [InlineData(OnAir, Update, true)]
    [InlineData(OnAir, TakeOut, true)]
    [InlineData(Unknown, TakeIn, false)]
    [InlineData(Unknown, TakeOut, false)]
    [InlineData(Unknown, Cue, false)]
    public void Allowed_actions_per_state(GraphicState state, GraphicAction action, bool allowed) =>
        Assert.Equal(allowed, At(state).CanDo(action));

    [Theory]
    [InlineData(Empty)]
    [InlineData(Cued)]
    [InlineData(OnAir)]
    [InlineData(Unknown)]
    public void Clear_is_always_allowed_and_ends_empty(GraphicState state)
    {
        var m = At(state);
        Assert.True(m.CanDo(Clear));
        m.Begin(Clear);
        m.Complete(Clear, accepted: true);
        Assert.Equal(Empty, m.State);
    }

    [Fact]
    public void Full_cycle_cue_take_out_and_take_again()
    {
        var m = new GraphicStateMachine();
        foreach (var (a, expected) in new[] { (Cue, Cued), (TakeIn, OnAir), (TakeOut, Cued), (TakeIn, OnAir) })
        {
            m.Begin(a);
            m.Complete(a, accepted: true);
            Assert.Equal(expected, m.State);
        }
    }

    [Fact]
    public void Second_press_while_waiting_is_refused_but_panic_is_not()
    {
        var m = At(Cued);
        m.Begin(TakeIn);
        Assert.True(m.IsBusy);
        Assert.False(m.CanDo(TakeIn));
        Assert.True(m.CanDo(Clear));
    }

    [Fact]
    public void Rejected_command_keeps_the_state()
    {
        var m = At(Cued);
        m.Begin(TakeIn);
        m.Complete(TakeIn, accepted: false);
        Assert.Equal(Cued, m.State);
        Assert.False(m.IsBusy);
    }

    [Fact]
    public void No_reply_makes_the_state_unknown()
    {
        var m = At(Cued);
        m.Begin(TakeIn);
        m.Fail();
        Assert.Equal(Unknown, m.State);
    }

    [Fact]
    public void Lost_connection_makes_loaded_graphics_unknown_but_empty_stays_empty()
    {
        var loaded = At(OnAir);
        loaded.ConnectionLost();
        Assert.Equal(Unknown, loaded.State);

        var empty = new GraphicStateMachine();
        empty.ConnectionLost();
        Assert.Equal(Empty, empty.State);
    }

    [Fact]
    public void Begin_throws_for_a_forbidden_action() =>
        Assert.Throws<InvalidOperationException>(() => new GraphicStateMachine().Begin(TakeIn));
}
