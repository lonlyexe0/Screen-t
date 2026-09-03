@echo off
chcp 65001 >nul
title 2. Sanal Ekrani Kapat

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [*] Yonetici izni isteniyor...
    powershell -NoProfile -Command "Start-Process cmd -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs"
    exit /b
)

echo.
echo [*] 2. Sanal Ekran kapatiliyor...
powershell -NoProfile -Command "Disable-PnpDevice -InstanceId 'ROOT\DISPLAY\0000' -Confirm:$false"
echo.
echo ====================================================
echo  [OK] 2. Sanal Ekran basariyla DEVRE DISI BIRAKILDI!
echo  Windows artik tek ekran modunda.
echo ====================================================
echo.
pause
