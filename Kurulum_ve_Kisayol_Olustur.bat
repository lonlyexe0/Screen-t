@echo off
chcp 65001 >nul
title Ikinci Ekran - Kurulum ve Kisayol Entegrasyonu

echo ====================================================
echo    Ikinci Ekran - Windows Uygulama Entegrasyonu
echo ====================================================
echo.

cd /d "%~dp0"

echo [*] Windows Baslat Menusu ve Masaustu kisayollari olusturuluyor...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0create_shortcuts.ps1"

echo.
echo ====================================================
echo  [OK] KURULUM BASARIYLA TAMAMLANDI!
echo ====================================================
echo.
echo  1. Artik klavyeden Windows tusuna basip "Ikinci Ekran"
echo     veya "ikinci ekran" yazarak aninda baslatabilirsiniz!
echo.
echo  2. Masaustunuzde "Ikinci Ekran" simgesi olusturuldu.
echo.
echo  3. Uygulama arayuzunde:
echo     - "Yayini Baslat" tusu ile sanal ekran otomatik acilir.
echo     - Pencereyi kapattiginizda saatin yaninda (System Tray)
echo       calismaya kesintisiz devam eder.
echo     - Tepsi menusunden "Tamamen Cikis" yapildiginda sanal
echo       ekran otomatik olarak devre disi birakilir.
echo ====================================================
echo.
set /p launch="Uygulamayi simdi baslatmak ister misiniz? (E/H): "
if /i "%launch%"=="E" (
    start "" "%~dp0IkinciEkran\publish\IkinciEkran.exe"
)
