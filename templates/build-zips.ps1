$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$baseDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function New-TemplateZip([string]$jsonFile, [string]$mdFile, [string]$outZip) {
    $tmpDir = Join-Path $env:TEMP ("hctpl_" + [Guid]::NewGuid().ToString("N"))
    [void][System.IO.Directory]::CreateDirectory($tmpDir)
    $jsonSrc = Join-Path $baseDir $jsonFile
    $mdSrc = Join-Path $baseDir $mdFile
    # Copy template.json
    $jsonDest = Join-Path $tmpDir "template.json"
    [System.IO.File]::Copy($jsonSrc, $jsonDest)
    # Copy memory/user_profile.md
    $memDir = Join-Path $tmpDir "memory"
    [void][System.IO.Directory]::CreateDirectory($memDir)
    $mdDest = Join-Path $memDir "user_profile.md"
    [System.IO.File]::Copy($mdSrc, $mdDest)
    # Create zip
    $outPath = Join-Path $baseDir $outZip
    if ([System.IO.File]::Exists($outPath)) { [System.IO.File]::Delete($outPath) }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($tmpDir, $outPath)
    # Cleanup temp dir
    [System.IO.Directory]::Delete($tmpDir, $true)
    $size = [System.IO.FileInfo]::new($outPath).Length
    Write-Output "Created $outZip ($size bytes)"
}

Set-Location $baseDir
New-TemplateZip "greenhouse.template.json"   "greenhouse.user_profile.md"   "greenhouse.agenttemplate"
New-TemplateZip "cold-chain.template.json"   "cold-chain.user_profile.md"   "cold-chain.agenttemplate"
New-TemplateZip "server-room.template.json"  "server-room.user_profile.md"  "server-room.agenttemplate"
New-TemplateZip "vending.template.json"      "vending.user_profile.md"      "vending.agenttemplate"
Write-Output "Done."
