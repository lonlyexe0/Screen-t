@echo off
chcp 65001 >nul
title Ikinci Monitor - Yerel Yayin Sunucusu HTTP

:: 1. Yonetici Yetkisi Kontrolu
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [*] Yonetici Izni isteniyor...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"
cls

echo ====================================================
echo    Ikinci Monitor - Yerel Yayin Sunucusu HTTP
echo ====================================================
echo.

:: 2. Sanal 2. Ekrani Otomatik Ac (ac_ikinci_ekran)
echo [*] Sanal 2. Ekran baslatiliyor...
powershell -NoProfile -Command "Enable-PnpDevice -InstanceId 'ROOT\DISPLAY\0000' -Confirm:$false" >nul 2>&1
echo [OK] 2. Ekran basariyla etkinlestirildi!
echo.

:: 3. C# Yerel Yayin Sunucusunu Baslat
cd /d "%~dp0server_csharp"
echo [*] Yayin baslatiliyor - Port 8080
echo [*] Linux baglanti adresi: http://192.168.1.10:8080
echo [*] Durdurmak icin pencereyi kapatin veya Ctrl+C basin.
echo.

dotnet run 8080

:: 4. Program Kapatildiginda 2. Ekrani Otomatik Kapat (kapat_ikinci_ekran)
echo.
echo ====================================================
echo [*] Sunucu sonlandirildi. 2. Sanal ekran kapatiliyor...
powershell -NoProfile -Command "Disable-PnpDevice -InstanceId 'ROOT\DISPLAY\0000' -Confirm:$false" >nul 2>&1
echo [OK] 2. Ekran basariyla kapatildi! Windows tek ekran moduna donduruldu.
echo ====================================================
timeout /t 2 >nul
