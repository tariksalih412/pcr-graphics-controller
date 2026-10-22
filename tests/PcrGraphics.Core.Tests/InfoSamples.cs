namespace PcrGraphics.Core.Tests;

/// <summary>Real INFO 1 replies from CasparCG 2.5.1, captured with tools/probe_info.ps1.</summary>
public static class InfoSamples
{
    private const string Head = """
        <?xml version="1.0" encoding="utf-8"?>
        <channel>
           <format>1080i5000</format>
           <framerate>50</framerate>
           <framerate>1</framerate>
           <mixer>
              <audio>
                 <volume>0</volume>
                 <volume>0</volume>
              </audio>
           </mixer>
           <output>
              <port>
                 <port_600>
                    <consumer>screen</consumer>
                 </port_600>
              </port>
           </output>
        """;

    /// <summary>Nothing loaded on the channel (after CLEAR): no &lt;stage&gt; at all.</summary>
    public static readonly string Empty = Head + "\n</channel>";

    /// <summary>lower_third loaded on layer 30 (identical when cued, on air, or taken out).</summary>
    public static readonly string LowerThirdOn30 = Head + "\n" + """
           <stage>
              <layer>
                 <layer_30>
                    <background>
                       <producer>empty</producer>
                    </background>
                    <foreground>
                       <file>
                          <path>file://C:\Users\Tarik\Downloads\broadcast engineering\broadcast developer\casparcg-server-v2.5.1-windows\template/lower_third.html</path>
                       </file>
                       <paused>false</paused>
                       <producer>html</producer>
                    </foreground>
                 </layer_30>
              </layer>
           </stage>
        </channel>
        """;

    /// <summary>Layer 30 holds a template that is not ours; layer 31 is a video.</summary>
    public static readonly string Foreign = Head + "\n" + """
           <stage>
              <layer>
                 <layer_30>
                    <foreground>
                       <file><path>file://C:\caspar\template/scorebug.html</path></file>
                       <producer>html</producer>
                    </foreground>
                 </layer_30>
                 <layer_31>
                    <foreground>
                       <file><path>C:\caspar\media\AMB.mp4</path></file>
                       <producer>ffmpeg</producer>
                    </foreground>
                 </layer_31>
              </layer>
           </stage>
        </channel>
        """;

    /// <summary>The exact bytes CasparCG sends: status line, XML lines, empty line (CRLF).</summary>
    public static string AsReply(string xml) =>
        "201 INFO OK\r\n" + string.Join("\r\n", xml.Split('\n').Select(l => l.TrimEnd('\r'))) + "\r\n\r\n";

    public static IEnumerable<string> Lines(string xml) => xml.Split('\n').Select(l => l.TrimEnd('\r'));
}
