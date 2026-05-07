param (
    [Parameter(Mandatory = $false)]
    [ValidateSet("x64", "x86", "arm64")]
    [string]$Arch = "x64"
)

$ProjectDir = "lanlanlu-toolkit"
$PublishProfile = "Properties\PublishProfiles\win-$Arch.pubxml"
$OutputPath = "out_portable_$Arch"

Write-Host "正在封裝 蘭蘭露工具箱 (win-$Arch portable)..." -ForegroundColor Cyan

if (-not (Test-Path $ProjectDir)) {
    Write-Error "找不到專案目錄 $ProjectDir，請在專案根目錄執行此腳本。"
    exit
}

dotnet publish "$ProjectDir\lanlanlu-toolkit.csproj" -c Release `
    -p:PublishProfile=$PublishProfile `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishTrimmed=false `
    -o $OutputPath

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "封裝完成！" -ForegroundColor Green
    Write-Host "路徑: " -NoNewline
    Write-Host (Get-Item $OutputPath).FullName -ForegroundColor Yellow
}
else {
    Write-Host ""
    Write-Host "封裝失敗。" -ForegroundColor Red
}
