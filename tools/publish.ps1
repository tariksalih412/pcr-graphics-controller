# Builds the release: one self-contained PcrGraphics.App.exe for win-x64 + the templates,
# zipped as publish\PcrGraphics-v<version>-win-x64.zip (the GitHub release asset).
# Usage (from the project folder):
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1
#   powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -SkipTests
param([switch]$SkipTests)
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
$app = "src\PcrGraphics.App\PcrGraphics.App.csproj"
$version = ([xml](Get-Content $app)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in $app" }

$out = Join-Path $root "publish\win-x64"
$zip = Join-Path $root "publish\PcrGraphics-v$version-win-x64.zip"

if (-not $SkipTests) {
    Write-Host "== tests" -ForegroundColor Cyan
    dotnet test tests\PcrGraphics.Core.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: no release" }
}

Write-Host "== publish v$version win-x64" -ForegroundColor Cyan
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish $app -c Release -r win-x64 -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# Leftovers that are not needed at runtime: IntelliSense docs of the WebView2 package and the
# Core library's pdb (the app's own symbols are embedded in the exe).
Get-ChildItem $out -File | Where-Object { $_.Extension -in ".xml", ".pdb" } | Remove-Item -Force

# The release must be exactly: the exe + templates\*.html. Anything else = something not bundled.
$extra = Get-ChildItem $out -Recurse -File | Where-Object {
    $_.Name -ne "PcrGraphics.App.exe" -and -not ($_.DirectoryName -like "*\templates" -and $_.Extension -eq ".html")
}
if ($extra) { throw "Unexpected files in the release: $($extra.Name -join ', ')" }

$exe = Join-Path $out "PcrGraphics.App.exe"
if (-not (Test-Path $exe)) { throw "Missing $exe" }
foreach ($t in "lower_third.html", "breaking_news.html") {
    if (-not (Test-Path (Join-Path $out "templates\$t"))) { throw "Missing templates\$t in the output" }
}

Write-Host "== zip" -ForegroundColor Cyan
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip      # everything publish produced

Write-Host ""
Get-ChildItem $out -Recurse -File | Select-Object @{n = "File"; e = { $_.FullName.Substring($out.Length + 1) } },
    @{n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
Write-Host "Release zip: $zip  ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)" -ForegroundColor Green
