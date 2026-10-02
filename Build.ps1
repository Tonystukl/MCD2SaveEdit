$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
Expand-Archive -LiteralPath (Join-Path $projectRoot 'assets.zip') -DestinationPath (Join-Path $projectRoot 'Source') -Force
& dotnet publish (Join-Path $projectRoot 'Source/MCD2SaveEdit.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $projectRoot 'publish')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
