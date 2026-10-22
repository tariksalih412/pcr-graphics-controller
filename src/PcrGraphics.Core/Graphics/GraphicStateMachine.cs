namespace PcrGraphics.Core.Graphics;

/// <summary>
/// Decides which operator actions are allowed and what state follows. Pure logic, no network.
///
///   Empty   --Cue-->      Cued
///   Cued    --TakeIn-->   OnAir      (Cue again replaces the cued item)
///   OnAir   --TakeOut-->  Cued       (the same item can be taken again)
///   any     --Clear-->    Empty      (panic; also the only way out of Unknown)
///   Update keeps the state (text change while cued or on air)
///
/// While a command waits for CasparCG's reply the machine is Busy: a second press is refused,
/// except Clear, which must always be possible.
///
/// After a reconnect, Reconcile() compares with what CasparCG really has on the layer.
/// CasparCG can tell empty from loaded, but not cued from on air, so a lost Cued/OnAir state
/// is only restored when nothing was in flight when the connection dropped.
/// </summary>
public sealed class GraphicStateMachine
{
    private static readonly Dictionary<GraphicState, GraphicAction[]> Allowed = new()
    {
        [GraphicState.Empty] = [GraphicAction.Cue, GraphicAction.Clear],
        [GraphicState.Cued] = [GraphicAction.Cue, GraphicAction.TakeIn, GraphicAction.Update, GraphicAction.Clear],
        [GraphicState.OnAir] = [GraphicAction.Update, GraphicAction.TakeOut, GraphicAction.Clear],
        [GraphicState.Unknown] = [GraphicAction.Clear],
    };

    public GraphicState State { get; private set; } = GraphicState.Empty;
    public bool IsBusy { get; private set; }

    /// <summary>Last confirmed Cued/OnAir before the connection dropped; null if none or no longer trustworthy.</summary>
    private GraphicState? _beforeLoss;

    /// <summary>The connection dropped while a command was waiting: it may or may not have run.</summary>
    private bool _lostDuringCommand;

    /// <summary>True if Reconcile could bring back the state we had before the connection dropped.</summary>
    public bool CanRestore => State == GraphicState.Unknown && _beforeLoss is not null && !_lostDuringCommand;

    /// <summary>Raised after every change of State or IsBusy.</summary>
    public event Action? Changed;

    public bool CanDo(GraphicAction action) =>
        (action == GraphicAction.Clear || !IsBusy) && Allowed[State].Contains(action);

    /// <summary>Call before sending the command. Throws if the action is not allowed now.</summary>
    public void Begin(GraphicAction action)
    {
        if (!CanDo(action))
            throw new InvalidOperationException($"{action} is not allowed while {State}{(IsBusy ? " (busy)" : "")}");
        IsBusy = true;
        Changed?.Invoke();
    }

    /// <summary>
    /// Call when CasparCG replied. accepted = 2xx. A rejected command (4xx/5xx) did not run,
    /// so the state stays as it was.
    /// </summary>
    public void Complete(GraphicAction action, bool accepted)
    {
        IsBusy = false;
        if (accepted)
        {
            State = Next(State, action);
            ForgetLoss();
        }
        Changed?.Invoke();
    }

    /// <summary>The command was sent but no reply came: we no longer know what is on air.</summary>
    public void Fail()
    {
        IsBusy = false;
        State = GraphicState.Unknown;
        ForgetLoss();                                 // whatever we knew before is no longer sure
        Changed?.Invoke();
    }

    /// <summary>The command never left (e.g. not connected): nothing changed.</summary>
    public void Abort()
    {
        IsBusy = false;
        _lostDuringCommand = false;                   // the command that was waiting never left
        Changed?.Invoke();
    }

    /// <summary>Connection to CasparCG lost: anything we had loaded is now unknown.</summary>
    public void ConnectionLost()
    {
        if (State is GraphicState.Cued or GraphicState.OnAir) _beforeLoss = State;
        if (IsBusy) _lostDuringCommand = true;
        IsBusy = false;
        if (State != GraphicState.Empty) State = GraphicState.Unknown;
        Changed?.Invoke();
    }

    /// <summary>
    /// After (re)connecting: what CasparCG really has on this layer.
    ///   layer empty                          -> Empty   (nothing can be on air)
    ///   something else than our template     -> Unknown (only Clear)
    ///   our template, state Cued/OnAir       -> unchanged (nothing was lost)
    ///   our template, lost while idle        -> the state we had before (Cued or OnAir)
    ///   our template, any other case         -> Unknown (e.g. a command was in flight, or the app
    ///                                           just started and finds a graphic it did not load)
    /// Ignored while a command waits for its reply: that reply decides.
    /// Returns true if it ran (false = busy, skipped).
    /// </summary>
    public bool Reconcile(bool layerEmpty, bool ourTemplate)
    {
        if (IsBusy) return false;

        if (layerEmpty) State = GraphicState.Empty;
        else if (!ourTemplate) State = GraphicState.Unknown;
        else if (State is GraphicState.Cued or GraphicState.OnAir) { /* still true */ }
        else if (CanRestore) State = _beforeLoss!.Value;
        else State = GraphicState.Unknown;

        ForgetLoss();
        Changed?.Invoke();
        return true;
    }

    private void ForgetLoss()
    {
        _beforeLoss = null;
        _lostDuringCommand = false;
    }

    public static GraphicState Next(GraphicState state, GraphicAction action) => action switch
    {
        GraphicAction.Cue => GraphicState.Cued,
        GraphicAction.TakeIn => GraphicState.OnAir,
        GraphicAction.TakeOut => GraphicState.Cued,
        GraphicAction.Clear => GraphicState.Empty,
        _ => state,                                   // Update
    };
}
