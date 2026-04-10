param (
    [string]$Version = "1.0.0"
)

$target = "win-x64"
Write-Host "🚀 DgRead v$Version 배포를 시작합니다 ($target)..." -ForegroundColor Cyan

$publishDir = "bin/publish/output"
$zipPath = "bin/publish/DgRead-win-x64-v$Version.zip"

if (Test-Path -Path "bin/publish") {
    Remove-Item -Recurse -Force "bin/publish"
}

dotnet publish -c Release -r $target --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -o $publishDir

Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

Write-Host "✅ 배포 완료! ($zipPath)" -ForegroundColor Green
