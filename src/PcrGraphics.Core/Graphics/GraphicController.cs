using PcrGraphics.Core.Amcp;

namespace PcrGraphics.Core.Graphics;

/// <summary>
/// Controls one graphics layer: checks the state machine, sends the AMCP command,
/// and moves the state only according to CasparCG's real reply.
/// Every method returns true if CasparCG accepted the command; otherwise LastError says why.
/// </summary>
public sealed class GraphicController
{
    private readonly CasparClient _client;

    public GraphicController(CasparClient client, Layer layer, string? template = null)
    {
        _client = client;
        Layer = layer;
        Template = template;
        _client.StateChanged += s =>
        {
            if (s == ConnectionState.Disconnected) Machine.ConnectionLost();
        };
    }

    public Layer Layer { get; }

    /// <summary>The template this layer is for (e.g. "lower_third"); used to recognise it after a reconnect.</summary>
    public string? Template { get; }
    public GraphicStateMachine Machine { get; } = new();
    public GraphicState State => Machine.State;

    /// <summary>The item currently cued or on air (null when Empty).</summary>
    public GraphicItem? Current { get; private set; }
    public string? LastError { get; private set; }

    public Task<bool> CueAsync(GraphicItem item) =>
        RunAsync(GraphicAction.Cue, AmcpCommands.CgAdd(Layer, item.Template, playOnLoad: false, item.Fields),
            () => Current = item);

    public Task<bool> TakeInAsync() =>
        RunAsync(GraphicAction.TakeIn, AmcpCommands.CgPlay(Layer));

    public Task<bool> UpdateAsync(IReadOnlyDictionary<string, string> fields) =>
        RunAsync(GraphicAction.Update, AmcpCommands.CgUpdate(Layer, fields),
            () => Current = Current is null ? null : Current with { Fields = Merge(Current.Fields, fields) });

    public Task<bool> TakeOutAsync() =>
        RunAsync(GraphicAction.TakeOut, AmcpCommands.CgStop(Layer));

    /// <summary>Panic: clears the layer at once. Allowed in every state, even while busy.</summary>
    public Task<bool> ClearAsync() =>
        RunAsync(GraphicAction.Clear, AmcpCommands.Clear(Layer), () => Current = null);

    /// <summary>
    /// Applies what CasparCG reports for this layer (from INFO). Returns a log line describing
    /// the outcome, or null if the layer was busy and nothing changed.
    /// </summary>
    public string? Reconcile(LayerContent? onServer)
    {
        var before = State;
        var template = Template ?? Current?.Template;
        var ours = onServer is not null && template is not null && onServer.IsTemplate(template);
        if (!Machine.Reconcile(onServer is null, ours)) return null;

        if (State == GraphicState.Empty) Current = null;
        var found = onServer is null ? "empty" : $"{onServer.Producer} {onServer.FileName ?? onServer.Path ?? "?"}";
        var why = State switch
        {
            GraphicState.Unknown when !ours => " (not this app's template: CLEAR it)",
            GraphicState.Unknown => " (cued or on air cannot be told apart: CLEAR it)",
            _ when before == GraphicState.Unknown && State != GraphicState.Empty => " (restored)",
            _ => "",
        };
        return $"{Layer}: CasparCG has {found} -> {before} => {State}{why}";
    }

    private async Task<bool> RunAsync(GraphicAction action, string command, Action? onAccepted = null)
    {
        if (!Machine.CanDo(action))
        {
            LastError = $"{action} refused: graphic is {State}{(Machine.IsBusy ? ", waiting for a reply" : "")}";
            return false;
        }

        Machine.Begin(action);
        try
        {
            var reply = await _client.SendAsync(command);
            if (reply.IsSuccess)
            {
                onAccepted?.Invoke();
                LastError = null;
            }
            else
            {
                LastError = $"CasparCG rejected {action}: {reply}";
            }
            Machine.Complete(action, reply.IsSuccess);
            return reply.IsSuccess;
        }
        catch (InvalidOperationException ex)          // not connected: nothing was sent
        {
            LastError = ex.Message;
            Machine.Abort();
            return false;
        }
        catch (Exception ex)                          // sent, but no reply: result unknown
        {
            LastError = ex.Message;
            Machine.Fail();
            return false;
        }
    }

    private static IReadOnlyDictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> old, IReadOnlyDictionary<string, string> changes)
    {
        var merged = new Dictionary<string, string>(old);
        foreach (var (k, v) in changes) merged[k] = v;
        return merged;
    }
}
