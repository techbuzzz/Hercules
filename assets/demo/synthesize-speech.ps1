#Requires -Version 5.1
param(
    [string]$Script = "$(Get-Location)\demo-script.md",
    [string]$Output = "$(Get-Location)\demo-voice.mp3",
    [string]$Voice = "en-US-GuyNeural",
    [string]$Rate = "+10%"
)

$Script = [System.IO.Path]::GetFullPath($Script)
$Output = [System.IO.Path]::GetFullPath($Output)

$edgeTts = Get-Command edge-tts -ErrorAction SilentlyContinue
if (-not $edgeTts) {
    Write-Host "edge-tts is not installed. Install it with:" -ForegroundColor Yellow
    Write-Host "  pip install edge-tts" -ForegroundColor Cyan
    exit 1
}

# Extract the TTS block from the script
$lines = Get-Content -Path $Script
$collect = $false
$ttsLines = @()
foreach ($line in $lines) {
    if ($line -match '^```text\s*$' -and -not $collect) { $collect = $true; continue }
    if ($line -match '^```\s*$' -and $collect) { $collect = $false; continue }
    if ($collect) { $ttsLines += $line }
}

$text = ($ttsLines -join ' ').Trim()
if ([string]::IsNullOrWhiteSpace($text)) {
    Write-Host "Could not find TTS text block in $Script" -ForegroundColor Red
    exit 1
}

Write-Host "Synthesizing speech..." -ForegroundColor Green
Write-Host "Voice: $Voice" -ForegroundColor Cyan
Write-Host "Output: $Output" -ForegroundColor Cyan

& edge-tts --voice $Voice --rate $Rate --text "$text" --write-media "$Output"
if ($LASTEXITCODE -eq 0) {
    Write-Host "Saved to $Output" -ForegroundColor Green
} else {
    Write-Host "edge-tts failed." -ForegroundColor Red
}
