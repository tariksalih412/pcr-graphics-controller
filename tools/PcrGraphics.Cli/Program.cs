// Phase 1 test bench: drive CasparCG from the terminal before any UI exists.
//   dotnet run --project tools/PcrGraphics.Cli                (127.0.0.1:5250)
//   dotnet run --project tools/PcrGraphics.Cli -- 127.0.0.1 5250
// Uses the "scorebug" template from project 1, already in CasparCG's template folder.
using PcrGraphics.Core.Amcp;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var host = args.Length > 0 ? args[0] : "127.0.0.1";
var port = args.Length > 1 ? int.Parse(args[1]) : 5250;
var layer = new Layer(1, 30);

await using var caspar = new CasparClient(host, port);
caspar.Log += line => Console.WriteLine("   " + line);
caspar.StateChanged += state => Console.WriteLine($"   [state] {state}");

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

var data = new Dictionary<string, string>
{
    ["home"] = "السد", ["away"] = "الدحيل",
    ["homeScore"] = "0", ["awayScore"] = "0", ["minute"] = "12", ["status"] = "1H",
};

Console.WriteLine();
Console.WriteLine("c = cue   t = take in   u = update   o = take out   p = panic (clear)   q = quit");
while (true)
{
    Console.Write("> ");
    var key = Console.ReadLine()?.Trim().ToLowerInvariant();
    string? command = key switch
    {
        "c" => AmcpCommands.CgAdd(layer, "scorebug", playOnLoad: false, data),
        "t" => AmcpCommands.CgPlay(layer),
        "u" => AmcpCommands.CgUpdate(layer, new Dictionary<string, string> { ["homeScore"] = "1", ["minute"] = "23" }),
        "o" => AmcpCommands.CgStop(layer),
        "p" => AmcpCommands.Clear(layer),
        "q" or null => null,
        _ => "",
    };
    if (command is null) break;
    if (command == "") { Console.WriteLine("   unknown key"); continue; }

    try
    {
        var reply = await caspar.SendAsync(command);
        Console.WriteLine(reply.IsSuccess ? "   OK" : "   REJECTED by CasparCG");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"   FAILED: {ex.Message}");
        if (caspar.State != ConnectionState.Connected) break;
    }
}
return 0;
