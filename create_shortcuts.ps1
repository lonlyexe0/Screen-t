# Clean up any previous broken shortcuts
$startMenu = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)

Get-ChildItem -Path $startMenu -Filter "*kinci*.lnk" | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $desktop -Filter "*kinci*.lnk" | Remove-Item -Force -ErrorAction SilentlyContinue

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $scriptDir) { $scriptDir = $PSScriptRoot }
if (-not $scriptDir) { $scriptDir = (Get-Location).Path }

$targetExe = Join-Path $scriptDir "IkinciEkran\publish\IkinciEkran.exe"
$iconPath = Join-Path $scriptDir "IkinciEkran\publish\Assets\app.ico"
$workingDir = Join-Path $scriptDir "IkinciEkran\publish"

# 1. Start Menu Shortcut (Windows Arama ve Başlat Menüsü için)
$lnk1 = Join-Path $startMenu "Ikinci Ekran.lnk"
$s1 = $wshShell.CreateShortcut($lnk1)
$s1.TargetPath = $targetExe
$s1.WorkingDirectory = $workingDir
$s1.IconLocation = $iconPath
$s1.Description = "Ikinci Ekran ve Sanal Monitor Yayin Sunucusu"
$s1.Save()

# 2. Desktop Shortcut
$lnk2 = Join-Path $desktop "Ikinci Ekran.lnk"
$s2 = $wshShell.CreateShortcut($lnk2)
$s2.TargetPath = $targetExe
$s2.WorkingDirectory = $workingDir
$s2.IconLocation = $iconPath
$s2.Description = "Ikinci Ekran ve Sanal Monitor Yayin Sunucusu"
$s2.Save()

Write-Host "Kısayollar başarıyla oluşturuldu!"
Write-Host "Başlat Menüsü: $lnk1"
Write-Host "Masaüstü:      $lnk2"
