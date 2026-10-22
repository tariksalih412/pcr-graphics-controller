namespace PcrGraphics.Core.Amcp;

/// <summary>
/// One AMCP reply. The first line starts with a 3-digit code:
/// 200 = data lines follow until an empty line, 201 = one data line follows,
/// 202 = OK with no data, 4xx = bad command, 5xx = server error.
/// Measured on CasparCG 2.5.1 (tools/probe_info.ps1):
///   "201 INFO OK" is followed by a multi-line XML document that ends with an empty line;
///   "400 ERROR" is followed by one line that echoes the rejected command.
/// </summary>
public sealed record AmcpReply(int Code, string Status, IReadOnlyList<string> Data)
{
    public bool IsSuccess => Code is >= 200 and < 300;

    /// <summary>How many data lines follow the status line: 0, 1, or -1 for "until an empty line".</summary>
    public static int DataLinesFor(int code) => code switch
    {
        200 => -1,
        201 => 1,
        400 => 1,       // CasparCG echoes the rejected command on the next line
        _ => 0,
    };

    /// <summary>A 201 whose data line opens an XML document (INFO): keep reading until an empty line.</summary>
    public static bool StartsXml(string firstDataLine) => firstDataLine.TrimStart().StartsWith('<');

    public static (int Code, string Status) ParseStatusLine(string line)
    {
        line = line.Trim();
        if (line.Length >= 3 && int.TryParse(line.AsSpan(0, 3), out var code))
            return (code, line.Length > 4 ? line[4..] : "");
        return (0, line);   // not an AMCP status line
    }

    public override string ToString() =>
        Data.Count == 0 ? $"{Code} {Status}" : $"{Code} {Status} | {string.Join(" | ", Data)}";
}
