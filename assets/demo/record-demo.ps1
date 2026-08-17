#Requires -Version 5.1
param(
    [string]$OutputPath = "$(Get-Location)\demo-raw.mp4",
    [int]$Duration = 90,
    [string]$Fps = 30
)

$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)

# Try ffmpeg first for reliability
$ffmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
if ($ffmpeg) {
    Write-Host "ffmpeg found. Recording screen via ffmpeg..." -ForegroundColor Green
    # Capture primary display. Windows DXGI desktop duplication via ffmpeg dshow or gdigrab.
    # gdigrab captures the whole virtual screen.
    & ffmpeg -f gdigrab -framerate $Fps -i desktop -t $Duration -c:v libx264 -pix_fmt yuv420p -preset fast "$OutputPath"
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Saved to $OutputPath" -ForegroundColor Green
        exit 0
    }
}

# Fallback: Windows.Graphics.Capture via C# inline compilation
Write-Host "ffmpeg not available. Trying Windows.Graphics.Capture..." -ForegroundColor Yellow

Add-Type -ReferencedAssemblies System.Runtime,System.Collections,System.Threading.Tasks,System.Linq,System.IO -TypeDefinition @"
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
public class ScreenCaptureFallback {
    public static async Task RecordAsync(string path, int seconds) {
        // Minimal stub: in a real environment use WinRT capture + MediaCapture to encode MP4.
        // This placeholder creates an empty file and logs instructions.
        File.WriteAllText(path + ".txt", "Windows.Graphics.Capture recording requires WinRT UWP packages. Use ffmpeg or OBS instead.");
        await Task.Delay(seconds * 1000);
    }
}
"@ -Language CSharp -IgnoreWarnings 2>$null

if ($?) {
    [ScreenCaptureFallback]::RecordAsync($OutputPath, $Duration).Wait()
    Write-Host "Fallback stub created. Please install ffmpeg for real screen capture:" -ForegroundColor Yellow
    Write-Host "  winget install Gyan.FFmpeg" -ForegroundColor Cyan
} else {
    Write-Host "Could not load screen capture. Please install ffmpeg:" -ForegroundColor Red
    Write-Host "  winget install Gyan.FFmpeg" -ForegroundColor Cyan
    Write-Host "Then run this script again." -ForegroundColor Cyan
}
