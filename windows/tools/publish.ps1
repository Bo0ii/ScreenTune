<#
.SYNOPSIS
  Builds a self-contained ScreenTune folder (no .NET or Windows App SDK install needed).

.EXAMPLE
  ./tools/publish.ps1                 # publish\win-x64\ScreenTune.exe
  ./tools/publish.ps1 -Arch arm64
  ./tools/publish.ps1 -Install        # also copy to %LOCALAPPDATA%\Programs\ScreenTune,
                                      # add a Start menu shortcut, and start it
#>
param(
    [ValidateSet("x64", "arm64")] [string] $Arch = "x64",
    [switch] $Install
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root "publish\win-$Arch"

dotnet publish (Join-Path $root "src\ScreenTune\ScreenTune.csproj") `
    -c Release -r "win-$Arch" --self-contained true `
    -p:PublishReadyToRun=true -p:PublishTrimmed=true -p:TrimMode=partial -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published to $out"

if ($Install) {
    $dest = Join-Path $env:LOCALAPPDATA "Programs\ScreenTune"

    # Quit a running copy cleanly so it restores the displays and releases its files.
    if (Get-Process ScreenTune -ErrorAction SilentlyContinue) {
        & (Join-Path $out "ScreenTune.exe") --quit
        Start-Sleep -Seconds 2
    }

    robocopy $out $dest /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy to $dest failed (robocopy $LASTEXITCODE)" }
    $global:LASTEXITCODE = 0 # robocopy uses 1-7 for success

    $shortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "ScreenTune.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    $link.TargetPath = Join-Path $dest "ScreenTune.exe"
    $link.WorkingDirectory = $dest
    $link.Description = "Per-screen color tuning"
    $link.Save()

    # Start through Explorer so it runs as a normal user even when this script is elevated.
    Start-Process explorer.exe -ArgumentList "`"$(Join-Path $dest "ScreenTune.exe")`""
    Write-Host "Installed to $dest with a Start menu shortcut."
}
