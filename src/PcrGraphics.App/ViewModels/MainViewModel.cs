using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcrGraphics.Core.Amcp;
using PcrGraphics.Core.Graphics;

namespace PcrGraphics.App.ViewModels;

/// <summary>
/// The whole control panel: one connection to CasparCG, several graphic slots
/// (each on its own layer), a PANIC that clears all of them, and the AMCP log.
/// Hotkeys act on the selected slot. The rundown holds the graphics prepared before the show;
/// F5 loads the next one into its slot and cues it.
/// </summary>
public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxLogLines = 300;

    private readonly CasparClient _client;
    private readonly LayerSync _sync;
    private readonly SynchronizationContext? _ui;
    private readonly CancellationTokenSource _cts = new();
    private GraphicSlotViewModel _selectedSlot;
    private ConnectionState _connection = ConnectionState.Disconnected;
    private string _message = "Select a graphic, type the text, then CUE (F1)";
    private RundownItemViewModel? _selectedItem;
    private RundownItemViewModel? _lastLoaded;
    private string? _rundownPath;

    /// <summary>The rundown is saved here after every change, and restored at start-up.</summary>
    public static readonly string AutosavePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PcrGraphics", "autosave-rundown.json");

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

        AddToRundownCommand = new RelayCommand(AddToRundown, () => SelectedSlot.FieldsValid);
        LoadItemCommand = new RelayCommand(() => LoadItem(SelectedItem), () => SelectedItem is not null);
        NextCommand = new AsyncRelayCommand(NextAsync, () => Rundown.Count > 0);
        RemoveItemCommand = new RelayCommand(RemoveItem, () => SelectedItem is not null);
        MoveUpCommand = new RelayCommand(() => Move(-1), () => SelectedItem is not null && Rundown.IndexOf(SelectedItem) > 0);
        MoveDownCommand = new RelayCommand(() => Move(+1), () => SelectedItem is not null && Rundown.IndexOf(SelectedItem) < Rundown.Count - 1);
        NewRundownCommand = new RelayCommand(NewRundown);
        OpenRundownCommand = new RelayCommand(OpenRundown);
        SaveRundownCommand = new RelayCommand(SaveRundown);

        foreach (var slot in Slots)
        {
            slot.Refreshed += RefreshRundownStates;
            slot.PreviewChanged += () => AddToRundownCommand.NotifyCanExecuteChanged();
        }
        Rundown.CollectionChanged += (_, _) =>
        {
            RefreshNextMarker();
            Autosave();
            RefreshRundownCommands();
            OnPropertyChanged(nameof(RundownTitle));
        };
        RestoreAutosave();

        _client.StateChanged += s => OnUi(() => Connection = s);
        _client.Log += line => OnUi(() => AddLog(line));

        // On every connect: ask CasparCG what is really on layers 1-30 / 1-31 (phase 6).
        _sync = new LayerSync(_client, Slots.Select(s => s.Controller));
        _sync.Log += line => OnUi(() => AddLog(line));
        _sync.Completed += ok => OnUi(() => OnSynced(ok));
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
            if (!SetProperty(ref _selectedSlot, value)) return;
            AddToRundownCommand?.NotifyCanExecuteChanged();
            SelectedSlotChanged?.Invoke();
        }
    }

    /// <summary>Raised when another slot is selected, so the preview loads its template.</summary>
    public event Action? SelectedSlotChanged;

    public AsyncRelayCommand PanicCommand { get; }

    // ---------- rundown ----------
    public ObservableCollection<RundownItemViewModel> Rundown { get; } = new();

    public RundownItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value)) return;
            RefreshRundownCommands();
            RefreshNextMarker();
        }
    }

    public string RundownTitle =>
        $"{(_rundownPath is null ? "unsaved" : Path.GetFileNameWithoutExtension(_rundownPath))}  ·  {Rundown.Count} items";

    public RelayCommand AddToRundownCommand { get; }
    public RelayCommand LoadItemCommand { get; }
    public AsyncRelayCommand NextCommand { get; }
    public RelayCommand RemoveItemCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand NewRundownCommand { get; }
    public RelayCommand OpenRundownCommand { get; }
    public RelayCommand SaveRundownCommand { get; }

    /// <summary>Set by the window: shows a file dialog and returns the chosen path, or null.</summary>
    public Func<string?>? AskOpenPath { get; set; }
    public Func<string?>? AskSavePath { get; set; }

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

    /// <summary>After a sync: tell the operator what needs attention.</summary>
    private void OnSynced(bool ok)
    {
        foreach (var s in Slots) s.Refresh();
        var unknown = Slots.Where(s => s.State == GraphicState.Unknown).Select(s => s.Title).ToList();
        if (!ok)
            Message = "Connected, but could not read CasparCG's layers: states kept as they were (see log)";
        else if (unknown.Count > 0)
            Message = $"Connected. Not confirmed on air or off: {string.Join(", ", unknown)}  ·  PANIC (F12) to clear";
        else
            Message = "Connected. Layer states checked with CasparCG: " +
                      string.Join(", ", Slots.Select(s => $"{s.Title} {s.StateText}"));
    }

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

    /// <summary>Adds what is in the editor now, after the selected item (or at the end).</summary>
    private void AddToRundown()
    {
        var item = new RundownItemViewModel(SelectedSlot.ToItem(), SelectedSlot);
        var at = SelectedItem is null ? Rundown.Count : Rundown.IndexOf(SelectedItem) + 1;
        Rundown.Insert(at, item);
        item.RefreshState();
        RefreshNextMarker();
        Message = $"Added to rundown ({at + 1}/{Rundown.Count}): {item.Line1}";
    }

    /// <summary>Puts the item's text in its slot's editor and selects that slot. Does not cue.</summary>
    private void LoadItem(RundownItemViewModel? item)
    {
        if (item is null) return;
        _lastLoaded = item;
        SelectedItem = item;
        RefreshNextMarker();
        SelectedSlot = item.Slot;
        item.Slot.LoadValues(item.Item.Fields);
        Message = $"Loaded: {item.Line1}" + (item.Slot.State == GraphicState.OnAir
            ? "  (slot is on air: UPDATE sends it, or TAKE OUT first)" : "");
    }

    /// <summary>
    /// What F5 will load: the item the operator selected, if it is not the one already loaded;
    /// otherwise the item after the last loaded one (the first item at the start of the show).
    /// </summary>
    public RundownItemViewModel? NextItem
    {
        get
        {
            if (SelectedItem is not null && SelectedItem != _lastLoaded) return SelectedItem;
            if (_lastLoaded is null || !Rundown.Contains(_lastLoaded)) return Rundown.FirstOrDefault();
            var after = Rundown.IndexOf(_lastLoaded) + 1;
            return after < Rundown.Count ? Rundown[after] : null;
        }
    }

    /// <summary>
    /// F5: loads the NEXT item into its slot and cues it, unless that slot is on air
    /// (then it is only loaded, nothing on air changes).
    /// </summary>
    private async Task NextAsync()
    {
        var item = NextItem;
        if (item is null)
        {
            Message = "End of rundown";
            return;
        }
        var previous = _lastLoaded;
        LoadItem(item);
        if (item.Slot.CueCommand.CanExecute(null))
        {
            await item.Slot.CueCommand.ExecuteAsync(null);
            return;
        }
        // Not cued (its slot is on air): it stays NEXT, so the next F5 cues it instead of skipping it.
        _lastLoaded = previous;
        RefreshNextMarker();
        if (item.Slot.State == GraphicState.OnAir)
            Message = $"{item.Line1} is ready, but {item.Slot.Title} is on air: TAKE OUT (F3), then F5 cues it";
    }

    private void RefreshNextMarker()
    {
        var next = NextItem;
        foreach (var item in Rundown) item.IsNext = item == next;
    }

    private void RemoveItem()
    {
        if (SelectedItem is null) return;
        var index = Rundown.IndexOf(SelectedItem);
        Rundown.RemoveAt(index);
        SelectedItem = null;
        RefreshNextMarker();
    }

    private void Move(int by)
    {
        if (SelectedItem is null) return;
        var from = Rundown.IndexOf(SelectedItem);
        var to = from + by;
        if (to < 0 || to >= Rundown.Count) return;
        Rundown.Move(from, to);
        RefreshRundownCommands();
        RefreshNextMarker();
    }

    private void NewRundown()
    {
        Rundown.Clear();
        _rundownPath = null;
        _lastLoaded = null;
        SelectedItem = null;
        OnPropertyChanged(nameof(RundownTitle));
        Message = "New rundown";
    }

    private void OpenRundown()
    {
        var path = AskOpenPath?.Invoke();
        if (path is null) return;
        try
        {
            var count = LoadRundown(RundownFile.Load(path));
            _rundownPath = path;
            OnPropertyChanged(nameof(RundownTitle));
            Message = $"Opened {Path.GetFileName(path)}: {count} items";
        }
        catch (Exception ex)
        {
            Message = $"Could not open rundown: {ex.Message}";
        }
    }

    private void SaveRundown()
    {
        var path = _rundownPath ?? AskSavePath?.Invoke();
        if (path is null) return;
        try
        {
            RundownFile.Save(path, Rundown.Select(i => i.Item));
            _rundownPath = path;
            OnPropertyChanged(nameof(RundownTitle));
            Message = $"Saved {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            Message = $"Could not save rundown: {ex.Message}";
        }
    }

    /// <summary>Replaces the list. Items for templates this app has no slot for are skipped.</summary>
    private int LoadRundown(IEnumerable<GraphicItem> items)
    {
        _lastLoaded = null;
        Rundown.Clear();
        var skipped = 0;
        foreach (var item in items)
        {
            var slot = Slots.FirstOrDefault(s => s.Template == item.Template);
            if (slot is null) { skipped++; continue; }
            var vm = new RundownItemViewModel(item, slot);
            vm.RefreshState();
            Rundown.Add(vm);
        }
        SelectedItem = null;
        RefreshNextMarker();
        if (skipped > 0) Message = $"{skipped} item(s) skipped: unknown template";
        return Rundown.Count;
    }

    private void Autosave()
    {
        try { RundownFile.Save(AutosavePath, Rundown.Select(i => i.Item)); }
        catch { /* autosave must never disturb the show */ }
    }

    private void RestoreAutosave()
    {
        try
        {
            if (File.Exists(AutosavePath) && LoadRundown(RundownFile.Load(AutosavePath)) > 0)
                Message = $"Rundown restored from last session: {Rundown.Count} items  ·  F5 cues the first";
        }
        catch { /* a broken autosave just starts an empty rundown */ }
    }

    private void RefreshRundownStates()
    {
        foreach (var item in Rundown) item.RefreshState();
    }

    private void RefreshRundownCommands()
    {
        LoadItemCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        RemoveItemCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
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
