param (
    [string]$Project = "Froststrap/Froststrap.csproj",
    [string]$BuildDir = "build",
    [string]$Config = "Release"
)

$ErrorActionPreference = "Stop"

if (Test-Path -Path "./$BuildDir") { Remove-Item -Recurse -Force "./$BuildDir" }
New-Item -ItemType Directory -Path "./$BuildDir" | Out-Null

dotnet publish "$Project" /p:PublishProfile=Publish-x64 -c "$Config" --configfile "$PSScriptRoot\..\..\nuget.config"

$PublishPath = "./Froststrap/bin/$Config/net10.0/publish/Spectre.exe"
if (-not (Test-Path $PublishPath)) {
    throw "Expected publish output at $PublishPath"
}

Copy-Item $PublishPath -Destination "./$BuildDir/Spectre.exe" -Force
Write-Host "Windows build complete: $BuildDir/Spectre.exe" -ForegroundColor Green