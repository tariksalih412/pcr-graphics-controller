# Phase 6 probe: records CasparCG's RAW replies to INFO / CG INFO
# with the layer empty, cued, on air, after STOP and after CLEAR.
# Usage (from the project folder):
#   powershell -ExecutionPolicy Bypass -File tools\probe_info.ps1
# Output: tools\info_probe_output.txt  (send this file back)
param(
    [string]$HostName = "127.0.0.1",
    [int]$Port = 5250,
    [string]$Template = "lower_third",
    [string]$Layer = "1-30"
)
$ErrorActionPreference = "Stop"

$out   = Join-Path $PSScriptRoot "info_probe_output.txt"
$utf8  = New-Object System.Text.UTF8Encoding($false)
$dec   = $utf8.GetDecoder()
$buf   = New-Object byte[] 65536
$chars = New-Object char[] 65536
$log   = New-Object System.Collections.Generic.List[string]

$tcp = New-Object System.Net.Sockets.TcpClient
$tcp.Connect($HostName, $Port)
$stream = $tcp.GetStream()

function Note([string]$text) {
    $log.Add("")
    $log.Add("########## $text")
    Write-Host "`n## $text" -ForegroundColor Cyan
}

# Sends one command and keeps everything that arrives until 400 ms of silence
# (or 2.5 s with no reply at all). Every line is shown as received; an empty
# line is marked so we can see where a 200 reply ends.
function Send-Amcp([string]$cmd) {
    $bytes = $utf8.GetBytes($cmd + "`r`n")
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()

    $sb = New-Object System.Text.StringBuilder
    $start = [DateTime]::UtcNow
    $last = $null
    while ($true) {
        if ($stream.DataAvailable) {
            $n = $stream.Read($buf, 0, $buf.Length)
            $c = $dec.GetChars($buf, 0, $n, $chars, 0)
            [void]$sb.Append($chars, 0, $c)
            $last = [DateTime]::UtcNow
        }
        else {
            $now = [DateTime]::UtcNow
            if ($last -and ($now - $last).TotalMilliseconds -gt 400) { break }
            if (-not $last -and ($now - $start).TotalMilliseconds -gt 2500) { break }
            Start-Sleep -Milliseconds 20
        }
    }

    $log.Add(">> $cmd")
    Write-Host ">> $cmd" -ForegroundColor Yellow
    $raw = $sb.ToString()
    if ($raw.Length -eq 0) {
        $log.Add("<< (NO REPLY)")
        Write-Host "<< (NO REPLY)" -ForegroundColor Red
        return
    }
    # keep the exact line structure; mark CRLF vs LF endings
    $endsCrLf = $raw.Contains("`r`n")
    $log.Add("   [line endings: $(if ($endsCrLf) { 'CRLF' } else { 'LF only' }); total chars: $($raw.Length)]")
    $lines = $raw -split "`r?`n"
    if ($lines.Count -gt 0 -and $lines[-1] -eq "") { $lines = $lines[0..($lines.Count - 2)] }
    foreach ($l in $lines) {
        if ($l -eq "") { $log.Add("<< (empty line)") } else { $log.Add("<< $l") }
    }
    $preview = if ($lines.Count -gt 3) { "$($lines[0]) ... ($($lines.Count) lines)" } else { $lines -join " | " }
    Write-Host "<< $preview"
}

$data = '"{\"name\":\"Probe Name\",\"title\":\"Probe Title\"}"'

try {
    Note "0. server"
    Send-Amcp "VERSION"
    Send-Amcp "VERSION SERVER"
    Send-Amcp "INFO"

    Note "1. layer EMPTY"
    Send-Amcp "CLEAR $Layer"
    Start-Sleep -Milliseconds 500
    Send-Amcp "INFO 1"
    Send-Amcp "INFO $Layer"
    Send-Amcp "CG $Layer INFO"
    Send-Amcp "CG $Layer INFO 1"

    Note "2. CUED (CG ADD ... 0)"
    Send-Amcp "CG $Layer ADD 1 $Template 0 $data"
    Start-Sleep -Milliseconds 1500
    Send-Amcp "INFO $Layer"
    Send-Amcp "CG $Layer INFO"
    Send-Amcp "CG $Layer INFO 1"

    Note "3. ON AIR (CG PLAY)"
    Send-Amcp "CG $Layer PLAY 1"
    Start-Sleep -Milliseconds 1500
    Send-Amcp "INFO 1"
    Send-Amcp "INFO $Layer"
    Send-Amcp "CG $Layer INFO 1"

    Note "4. after TAKE OUT (CG STOP)"
    Send-Amcp "CG $Layer STOP 1"
    Start-Sleep -Milliseconds 1500
    Send-Amcp "INFO $Layer"
    Send-Amcp "CG $Layer INFO 1"

    Note "5. after CLEAR"
    Send-Amcp "CLEAR $Layer"
    Start-Sleep -Milliseconds 500
    Send-Amcp "INFO $Layer"

    Note "6. other forms (may be rejected - that is useful too)"
    Send-Amcp "INFO 1-31"
    Send-Amcp "INFO 1 STATE"
    Send-Amcp "INFO 1-30 FOREGROUND"
}
finally {
    $tcp.Close()
    [System.IO.File]::WriteAllLines($out, $log, $utf8)
    Write-Host "`nSaved: $out" -ForegroundColor Green
}
