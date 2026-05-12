param (
    [Parameter(Mandatory = $false)]
    [ValidateSet("x64", "x86", "arm64")]
    [string]$Arch = "x64"
)

$ProjectDir = "lanlanlu-toolkit"
$PublishProfile = "Properties\PublishProfiles\win-$Arch.pubxml"
$OutputPath = "out_portable_$Arch"

Write-Host "Packaging LanLanLu Toolkit (win-$Arch portable)..." -ForegroundColor Cyan

if (-not (Test-Path $ProjectDir)) {
    Write-Error "Cannot find project directory $ProjectDir. Please run this script in the project root directory."
    exit
}

dotnet publish "$ProjectDir\lanlanlu-toolkit.csproj" -c Release `
    -p:PublishProfile=$PublishProfile `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishTrimmed=false `
    -o $OutputPath

if ($LASTEXITCODE -eq 0) {
    Write-Host "Cleaning up unused language folders..." -ForegroundColor Cyan
    $KeepDirs = @("Assets", "Microsoft.UI.Xaml", "runtimes", "zh-TW", "en-us")
    Get-ChildItem -Path $OutputPath -Directory | Where-Object { $_.Name -notin $KeepDirs -and $_.Name -match "^[a-z]{2,3}(-[A-Za-z]+)+$" } | Remove-Item -Recurse -Force

    # Clean up diagnostic, debugging DLLs, and symbols to further reduce size
    Write-Host "Cleaning up diagnostic/debugging DLLs and symbols..." -ForegroundColor Cyan
    Get-ChildItem -Path $OutputPath -Include "Microsoft.DiaSymReader.Native.*.dll", "mscordaccore*.dll", "mscordbi.dll", "*.pdb", "*.xml" -File -Recurse | Remove-Item -Force


    Write-Host ""
    Write-Host "Packaging completed!" -ForegroundColor Green
    Write-Host "Path: " -NoNewline
    Write-Host (Get-Item $OutputPath).FullName -ForegroundColor Yellow
}
else {
    Write-Host ""
    Write-Host "Packaging failed." -ForegroundColor Red
}
