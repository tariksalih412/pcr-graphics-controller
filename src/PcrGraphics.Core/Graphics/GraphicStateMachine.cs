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
        if (accepted) State = Next(State, action);
        Changed?.Invoke();
    }

    /// <summary>The command was sent but no reply came: we no longer know what is on air.</summary>
    public void Fail()
    {
        IsBusy = false;
        State = GraphicState.Unknown;
        Changed?.Invoke();
    }

    /// <summary>The command never left (e.g. not connected): nothing changed.</summary>
    public void Abort()
    {
        IsBusy = false;
        Changed?.Invoke();
    }

    /// <summary>Connection to CasparCG lost: anything we had loaded is now unknown.</summary>
    public void ConnectionLost()
    {
        IsBusy = false;
        if (State != GraphicState.Empty) State = GraphicState.Unknown;
        Changed?.Invoke();
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
