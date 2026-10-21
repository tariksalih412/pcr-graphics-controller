using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcrGraphics.Core.Amcp;

namespace PcrGraphics.App.ViewModels;

/// <summary>
/// The whole control panel: one connection to CasparCG, several graphic slots
/// (each on its own layer), a PANIC that clears all of them, and the AMCP log.
/// Hotkeys act on the selected slot.
/// </summary>
public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxLogLines = 300;

    private readonly CasparClient _client;
    private readonly SynchronizationContext? _ui;
    private readonly CancellationTokenSource _cts = new();
    private GraphicSlotViewModel _selectedSlot;
    private ConnectionState _connection = ConnectionState.Disconnected;
    private string _message = "Select a graphic, type the text, then CUE (F1)";

    public MainViewModel(string host = "127.0.0.1", int port = 5250, int channel = 1)
    {
        _ui = SynchronizationContext.Current;      // the UI thread; network events are sent back here
        _client = new CasparClient(host, port);

        Slots.Add(new GraphicSlotViewModel("Lower third", "lower_third", new Layer(channel, 30), _client,
            [new FieldSpec("name", "Name  /  الاسم", 40, Required: true), new FieldSpec("title", "Title  /  الصفة", 60)],
            () => IsConnected, OnUi, m => Message = m));
        Slots.Add(new GraphicSlotViewModel("Breaking news", "breaking_news", new Layer(channel, 31), _client,
            [new FieldSpec("headline", "Headline  /  الخبر", 90, Required: true)],
            () => IsConnected, OnUi, m => Message = m));
        _selectedSlot = Slots[0];

        PanicCommand = new AsyncRelayCommand(PanicAsync, () => IsConnected);

        _client.StateChanged += s => OnUi(() => Connection = s);
        _client.Log += line => OnUi(() => AddLog(line));
        _ = _client.RunAsync(_cts.Token);           // connect now and keep reconnecting in the background
    }

    public ObservableCollection<GraphicSlotViewModel> Slots { get; } = new();

    /// <summary>The slot the editor, preview and hotkeys work on.</summary>
    public GraphicSlotViewModel SelectedSlot
    {
        get => _selectedSlot;
        set
        {
            if (value is null) return;
            if (SetProperty(ref _selectedSlot, value)) SelectedSlotChanged?.Invoke();
        }
    }

    /// <summary>Raised when another slot is selected, so the preview loads its template.</summary>
    public event Action? SelectedSlotChanged;

    public AsyncRelayCommand PanicCommand { get; }

    public ConnectionState Connection
    {
        get => _connection;
        private set
        {
            if (!SetProperty(ref _connection, value)) return;
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(ConnectionText));
            PanicCommand.NotifyCanExecuteChanged();
            foreach (var s in Slots) s.Refresh();
        }
    }

    public bool IsConnected => Connection == ConnectionState.Connected;
    public string ConnectionText => $"CasparCG {_client.Endpoint}  ·  {Connection}";

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public ObservableCollection<string> Log { get; } = new();

    /// <summary>Clears every graphics layer this app owns, one after the other, no animation.</summary>
    private async Task PanicAsync()
    {
        var failed = new List<string>();
        foreach (var slot in Slots)
        {
            if (!await slot.Controller.ClearAsync()) failed.Add($"{slot.Title}: {slot.Controller.LastError}");
            slot.Refresh();
        }
        Message = failed.Count == 0 ? "PANIC: all graphics cleared" : "PANIC incomplete: " + string.Join("; ", failed);
    }

    private void AddLog(string line)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {line}");
        while (Log.Count > MaxLogLines) Log.RemoveAt(Log.Count - 1);
    }

    private void OnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui) action();
        else _ui.Post(_ => action(), null);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        await _client.DisposeAsync();
    }
}
