param([string]$Output = (Join-Path $PSScriptRoot 'App'))
$ErrorActionPreference = 'Stop'
dotnet publish (Join-Path $PSScriptRoot 'Source/MooVideo.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --source https://api.nuget.org/v3/index.json -o $Output
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Source/Assets/MooVideo.ico') -Destination (Join-Path $Output 'MooVideo.ico') -Force
Write-Host 'Build complete. Keep libmpv-2.dll and tools/ next to MooVideo.exe.'
