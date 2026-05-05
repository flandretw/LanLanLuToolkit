param (
    [Parameter(Mandatory = $false)]
    [ValidateSet("x64", "x86", "arm64")]
    [string]$Arch = "x64"
)

$ProjectDir = "lanlanlu-toolkit"
$PublishProfile = "Properties\PublishProfiles\win-$Arch.pubxml"
$OutputPath = "out_portable_$Arch"

# 調整為更符合動作進行中的提示
Write-Host "正在打包 攔藍錄工具箱 (win-$Arch portable)……" -ForegroundColor Cyan

if (-not (Test-Path $ProjectDir)) {
    Write-Error "找不到專案目錄 $ProjectDir，請在專案根目錄執行此腳本。"
    exit
}

# 加入強制參數，確保免安裝版的穩定性
dotnet publish "$ProjectDir\lanlanlu-toolkit.csproj" -c Release `
    -p:PublishProfile=$PublishProfile `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishTrimmed=false `
    -o $OutputPath

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n打包完成！" -ForegroundColor Green
    Write-Host "路徑: " -NoNewline
    Write-Host (Get-Item $OutputPath).FullName -ForegroundColor Yellow
}
else {
    Write-Host "`n打包失敗。" -ForegroundColor Red
}