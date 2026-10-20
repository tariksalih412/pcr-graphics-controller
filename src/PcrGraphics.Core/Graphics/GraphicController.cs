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

    public GraphicController(CasparClient client, Layer layer)
    {
        _client = client;
        Layer = layer;
        _client.StateChanged += s =>
        {
            if (s == ConnectionState.Disconnected) Machine.ConnectionLost();
        };
    }

    public Layer Layer { get; }
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
