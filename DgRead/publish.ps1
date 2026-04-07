$target = "win-x64"
Write-Host "🚀 DgRead 배포를 시작합니다 ($target)..." -ForegroundColor Cyan

Remove-Item -Recurse -Force bin/publish

dotnet publish -c Release -r $target --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o bin/publish

# 코드를 줄이려면 트리밍 사용
# -p:PublishTrimmed=true `

Write-Host "✅ 배포 완료!" -ForegroundColor Green
