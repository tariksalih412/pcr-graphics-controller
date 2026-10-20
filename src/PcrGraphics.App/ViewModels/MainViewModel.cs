using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcrGraphics.Core.Amcp;
using PcrGraphics.Core.Graphics;

namespace PcrGraphics.App.ViewModels;

/// <summary>
/// Everything the window shows and every button it has. No WPF types here:
/// the window binds to these properties and commands, and this class talks to Core.
/// </summary>
public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    public const int MaxNameLength = 40;
    public const int MaxTitleLength = 60;
    private const int MaxLogLines = 300;
    private const string Template = "lower_third";

    private readonly CasparClient _client;
    private readonly GraphicController _lowerThird;
    private readonly SynchronizationContext? _ui;
    private readonly CancellationTokenSource _cts = new();

    private string _guestName = "";
    private string _guestTitle = "";
    private string _message = "Type a name, then CUE (F1)";
    private ConnectionState _connection = ConnectionState.Disconnected;

    public MainViewModel(string host = "127.0.0.1", int port = 5250, int channel = 1, int layer = 30)
    {
        _ui = SynchronizationContext.Current;      // the UI thread; events from the network are sent back here
        _client = new CasparClient(host, port);
        _lowerThird = new GraphicController(_client, new Layer(channel, layer));

        CueCommand = new AsyncRelayCommand(CueAsync, () => Can(GraphicAction.Cue) && FieldsValid);
        TakeInCommand = new AsyncRelayCommand(() => RunAsync("Take in", _lowerThird.TakeInAsync), () => Can(GraphicAction.TakeIn));
        UpdateCommand = new AsyncRelayCommand(UpdateAsync, () => Can(GraphicAction.Update) && FieldsValid && HasPendingEdit);
        TakeOutCommand = new AsyncRelayCommand(() => RunAsync("Take out", _lowerThird.TakeOutAsync), () => Can(GraphicAction.TakeOut));
        PanicCommand = new AsyncRelayCommand(() => RunAsync("PANIC", _lowerThird.ClearAsync), () => IsConnected);

        _client.StateChanged += s => OnUi(() => Connection = s);
        _client.Log += line => OnUi(() => AddLog(line));
        _lowerThird.Machine.Changed += () => OnUi(RefreshGraphic);

        _ = _client.RunAsync(_cts.Token);           // connect now and keep reconnecting in the background
    }

    // ---------- commands (buttons and hotkeys) ----------
    public AsyncRelayCommand CueCommand { get; }
    public AsyncRelayCommand TakeInCommand { get; }
    public AsyncRelayCommand UpdateCommand { get; }
    public AsyncRelayCommand TakeOutCommand { get; }
    public AsyncRelayCommand PanicCommand { get; }

    // ---------- input fields ----------
    public string GuestName
    {
        get => _guestName;
        set
        {
            if (!SetProperty(ref _guestName, value)) return;
            OnPropertyChanged(nameof(NameCounter));
            OnPropertyChanged(nameof(NameTooLong));
            OnPropertyChanged(nameof(HasPendingEdit));
            RefreshCommands();
        }
    }

    public string GuestTitle
    {
        get => _guestTitle;
        set
        {
            if (!SetProperty(ref _guestTitle, value)) return;
            OnPropertyChanged(nameof(TitleCounter));
            OnPropertyChanged(nameof(TitleTooLong));
            OnPropertyChanged(nameof(HasPendingEdit));
            RefreshCommands();
        }
    }

    public string NameCounter => $"{GuestName.Trim().Length}/{MaxNameLength}";
    public string TitleCounter => $"{GuestTitle.Trim().Length}/{MaxTitleLength}";
    public bool NameTooLong => GuestName.Trim().Length > MaxNameLength;
    public bool TitleTooLong => GuestTitle.Trim().Length > MaxTitleLength;
    public bool FieldsValid => GuestName.Trim().Length > 0 && !NameTooLong && !TitleTooLong;

    /// <summary>The fields differ from what is cued or on air: UPDATE (F4) would send them.</summary>
    public bool HasPendingEdit =>
        _lowerThird.Current is { } c &&
        (c.Fields.GetValueOrDefault("name") != GuestName.Trim() || c.Fields.GetValueOrDefault("title") != GuestTitle.Trim());

    // ---------- connection ----------
    public ConnectionState Connection
    {
        get => _connection;
        private set
        {
            if (!SetProperty(ref _connection, value)) return;
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(ConnectionText));
            RefreshCommands();
        }
    }

    public bool IsConnected => Connection == ConnectionState.Connected;
    public string ConnectionText => $"CasparCG {_client.Endpoint}  ·  {Connection}";

    // ---------- graphic state and tally ----------
    public GraphicState State => _lowerThird.State;
    public bool IsBusy => _lowerThird.Machine.IsBusy;
    public bool TallyPreview => State == GraphicState.Cued;
    public bool TallyProgram => State == GraphicState.OnAir;
    public bool TallyUnknown => State == GraphicState.Unknown;

    public string StateText => State switch
    {
        GraphicState.Empty => "EMPTY",
        GraphicState.Cued => "CUED",
        GraphicState.OnAir => "ON AIR",
        _ => "UNKNOWN",
    } + (IsBusy ? "  …" : "");

    public string CurrentText => _lowerThird.Current is { } c
        ? c.Fields.GetValueOrDefault("name") + "\n" + c.Fields.GetValueOrDefault("title")
        : "—";

    // ---------- feedback ----------
    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public ObservableCollection<string> Log { get; } = new();

    // ---------- actions ----------
    private bool Can(GraphicAction action) => IsConnected && _lowerThird.Machine.CanDo(action);

    private Dictionary<string, string> Fields() => new()
    {
        ["name"] = GuestName.Trim(),
        ["title"] = GuestTitle.Trim(),
    };

    private Task CueAsync()
    {
        var item = new GraphicItem(GuestName.Trim(), Template, Fields());
        return RunAsync("Cue", () => _lowerThird.CueAsync(item));
    }

    private Task UpdateAsync() => RunAsync("Update", () => _lowerThird.UpdateAsync(Fields()));

    private async Task RunAsync(string label, Func<Task<bool>> action)
    {
        var ok = await action();
        Message = ok ? $"{label}: OK" : $"{label} not done: {_lowerThird.LastError}";
        RefreshGraphic();
    }

    // ---------- plumbing ----------
    private void RefreshGraphic()
    {
        foreach (var name in new[]
                 {
                     nameof(State), nameof(IsBusy), nameof(StateText), nameof(TallyPreview),
                     nameof(TallyProgram), nameof(TallyUnknown), nameof(CurrentText), nameof(HasPendingEdit),
                 })
            OnPropertyChanged(name);
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        CueCommand.NotifyCanExecuteChanged();
        TakeInCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged();
        TakeOutCommand.NotifyCanExecuteChanged();
        PanicCommand.NotifyCanExecuteChanged();
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
