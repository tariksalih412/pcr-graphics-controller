// Test bench: drive CasparCG from the terminal before any UI exists.
//   dotnet run --project tools/PcrGraphics.Cli                (127.0.0.1:5250)
//   dotnet run --project tools/PcrGraphics.Cli -- 127.0.0.1 5250
// Uses the "scorebug" template from project 1, already in CasparCG's template folder.
// Since phase 2 every key goes through the state machine: try "t" before "c".
using PcrGraphics.Core.Amcp;
using PcrGraphics.Core.Graphics;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var host = args.Length > 0 ? args[0] : "127.0.0.1";
var port = args.Length > 1 ? int.Parse(args[1]) : 5250;

await using var caspar = new CasparClient(host, port);
caspar.Log += line => Console.WriteLine("   " + line);
caspar.StateChanged += state => Console.WriteLine($"   [connection] {state}");

try
{
    await caspar.ConnectAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Cannot reach CasparCG at {host}:{port}: {ex.Message}");
    return 1;
}
await caspar.SendAsync(AmcpCommands.Version());

var graphic = new GraphicController(caspar, new Layer(1, 30), "scorebug");
var sync = new LayerSync(caspar, [graphic], syncOnConnect: false);
sync.Log += line => Console.WriteLine("   " + line);
var item = new GraphicItem("Test scorebug", "scorebug", new Dictionary<string, string>
{
    ["home"] = "السد", ["away"] = "الدحيل",
    ["homeScore"] = "0", ["awayScore"] = "0", ["minute"] = "12", ["status"] = "1H",
});

Console.WriteLine();
Console.WriteLine("c = cue   t = take in   u = update   o = take out   p = panic (clear)   s = sync with INFO   q = quit");
while (true)
{
    Console.Write($"[{graphic.State}] > ");
    var key = Console.ReadLine()?.Trim().ToLowerInvariant();
    if (key is null or "q") break;

    Task<bool>? action = key switch
    {
        "c" => graphic.CueAsync(item),
        "t" => graphic.TakeInAsync(),
        "u" => graphic.UpdateAsync(new Dictionary<string, string> { ["homeScore"] = "1", ["minute"] = "23" }),
        "o" => graphic.TakeOutAsync(),
        "p" => graphic.ClearAsync(),
        "s" => sync.SyncAsync(),
        _ => null,
    };
    if (action is null) { Console.WriteLine("   unknown key"); continue; }

    var ok = await action;
    Console.WriteLine(ok ? $"   OK -> {graphic.State}" : $"   NOT DONE: {graphic.LastError}");
}
return 0;
