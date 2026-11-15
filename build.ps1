$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "PiPClickThrough\PiPClickThrough.csproj"
$publish = Join-Path $PSScriptRoot "publish"
$dist = Join-Path $PSScriptRoot "dist"
$zip = Join-Path $dist "PiPClickThrough-win-x64.zip"
$iconSource = Join-Path $PSScriptRoot "assets\app-icon.b64"
$iconFile = Join-Path $PSScriptRoot "PiPClickThrough\app.ico"
[IO.File]::WriteAllBytes($iconFile, [Convert]::FromBase64String((Get-Content $iconSource -Raw).Trim()))

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null

dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none `
  -p:DebugSymbols=false `
  -o $publish

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Copy-Item (Join-Path $PSScriptRoot "README.md") $publish
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip -CompressionLevel Optimal

Write-Host ""
Write-Host "Build completed successfully."
Write-Host "Executable: $publish\PiPClickThrough.exe"
Write-Host "Portable ZIP: $zip"
