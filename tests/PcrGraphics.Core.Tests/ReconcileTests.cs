using PcrGraphics.Core.Graphics;
using static PcrGraphics.Core.Graphics.GraphicAction;
using static PcrGraphics.Core.Graphics.GraphicState;

namespace PcrGraphics.Core.Tests;

/// <summary>Phase 6: what the state becomes when CasparCG's INFO is compared after a reconnect.</summary>
public class ReconcileTests
{
    private static GraphicStateMachine At(GraphicState state)
    {
        var m = new GraphicStateMachine();
        if (state is Cued or OnAir) { m.Begin(Cue); m.Complete(Cue, true); }
        if (state is OnAir) { m.Begin(TakeIn); m.Complete(TakeIn, true); }
        if (state is Unknown) { m.Begin(Cue); m.Fail(); }
        return m;
    }

    [Theory]
    [InlineData(Cued)]
    [InlineData(OnAir)]
    public void Lost_while_idle_and_template_still_there_restores_the_state(GraphicState before)
    {
        var m = At(before);
        m.ConnectionLost();
        Assert.Equal(Unknown, m.State);
        Assert.True(m.CanRestore);

        m.Reconcile(layerEmpty: false, ourTemplate: true);
        Assert.Equal(before, m.State);
    }

    [Fact]
    public void Lost_during_a_command_stays_unknown_even_if_template_is_there()
    {
        var m = At(Cued);
        m.Begin(TakeIn);                 // PLAY sent...
        m.ConnectionLost();              // ...connection dropped before the reply
        m.Fail();
        m.Reconcile(false, true);
        Assert.Equal(Unknown, m.State);
    }

    [Fact]
    public void Lost_while_a_command_waited_but_never_left_can_still_restore()
    {
        var m = At(OnAir);
        m.Begin(TakeOut);
        m.ConnectionLost();
        m.Abort();                       // SendAsync found no connection: nothing was sent
        m.Reconcile(false, true);
        Assert.Equal(OnAir, m.State);
    }

    [Theory]
    [InlineData(Empty)]
    [InlineData(Cued)]
    [InlineData(OnAir)]
    [InlineData(Unknown)]
    public void Empty_layer_on_server_is_always_empty(GraphicState before)
    {
        var m = At(before);
        m.ConnectionLost();
        m.Reconcile(layerEmpty: true, ourTemplate: false);
        Assert.Equal(Empty, m.State);
    }

    [Fact]
    public void Graphic_found_at_start_up_is_unknown()
    {
        var m = new GraphicStateMachine();          // app just started: Empty
        m.Reconcile(false, true);
        Assert.Equal(Unknown, m.State);
        Assert.False(m.CanDo(Cue));
        Assert.True(m.CanDo(Clear));
    }

    [Fact]
    public void Foreign_content_is_unknown()
    {
        var m = At(OnAir);
        m.ConnectionLost();
        m.Reconcile(layerEmpty: false, ourTemplate: false);
        Assert.Equal(Unknown, m.State);
    }

    [Fact]
    public void Connected_and_still_matching_keeps_the_state()
    {
        var m = At(OnAir);
        m.Reconcile(false, true);
        Assert.Equal(OnAir, m.State);
    }

    [Fact]
    public void Busy_layer_is_left_alone()
    {
        var m = At(Cued);
        m.Begin(TakeIn);
        Assert.False(m.Reconcile(true, false));
        Assert.Equal(Cued, m.State);
        Assert.True(m.IsBusy);
    }

    [Fact]
    public void Restore_works_once_only()
    {
        var m = At(OnAir);
        m.ConnectionLost();
        m.Reconcile(false, true);                   // restored OnAir
        m.ConnectionLost();
        m.Begin(Clear); m.Complete(Clear, true);    // operator cleared
        m.Reconcile(false, true);                   // template still there?! (someone else loaded it)
        Assert.Equal(Unknown, m.State);
    }
}
