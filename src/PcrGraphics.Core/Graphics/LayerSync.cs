using PcrGraphics.Core.Amcp;

namespace PcrGraphics.Core.Graphics;

/// <summary>
/// Every time the connection comes up (first start and every reconnect) asks CasparCG
/// what is really loaded on this app's layers, with one INFO per channel, and lets each
/// controller reconcile its state with it. See GraphicStateMachine.Reconcile for the rules.
/// </summary>
public sealed class LayerSync
{
    private readonly CasparClient _client;
    private readonly IReadOnlyList<GraphicController> _controllers;

    public LayerSync(CasparClient client, IEnumerable<GraphicController> controllers, bool syncOnConnect = true)
    {
        _client = client;
        _controllers = controllers.ToList();
        if (syncOnConnect)
            _client.StateChanged += s =>
            {
                // Started right here, not with Task.Run: in the app this event comes on the UI thread,
                // so the states are reconciled on the UI thread, like every other state change.
                if (s == ConnectionState.Connected) _ = SyncAsync();
            };
    }

    /// <summary>One line per layer checked, and any problem. May be raised on a background thread.</summary>
    public event Action<string>? Log;

    /// <summary>Raised after a sync attempt: true if every channel was read and applied.</summary>
    public event Action<bool>? Completed;

    /// <summary>Queries and applies once. Never throws: a failure leaves the states as they are.</summary>
    public async Task<bool> SyncAsync(CancellationToken ct = default)
    {
        var ok = true;
        foreach (var channel in _controllers.GroupBy(c => c.Layer.Channel))
        {
            ChannelInfo info;
            try
            {
                var reply = await _client.SendAsync(AmcpCommands.Info(channel.Key), ct, quiet: true);
                if (!reply.IsSuccess)
                {
                    Log?.Invoke($"-- sync channel {channel.Key}: CasparCG answered {reply.Code} {reply.Status}");
                    ok = false;
                    continue;
                }
                info = ChannelInfo.Parse(reply.Data);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"-- sync channel {channel.Key} failed: {ex.Message}");
                ok = false;
                continue;
            }

            foreach (var controller in channel)
            {
                var line = controller.Reconcile(info.Foreground(controller.Layer.Number));
                Log?.Invoke("-- sync " + (line ?? $"{controller.Layer}: busy, left to the command's reply"));
            }
        }
        Completed?.Invoke(ok);
        return ok;
    }
}
