@echo off
title LAUNCH MBS SERVER & DISPLAY
color 0B

echo ============================================================================
echo   MENJALANKAN MBS SERVER & BROWSER DISPLAY KIOSK
echo ============================================================================
echo.

:: 1. Jalankan Web Server di background jika belum jalan
netstat -ano | findstr :5111 >nul
if %errorlevel% neq 0 (
    echo [INFO] Menjalankan MBS Web Server di port 5111...
    if exist "MBS_SAP.dll" (
        start "MBS_SERVER" /min dotnet MBS_SAP.dll --urls "http://0.0.0.0:5111"
    ) else (
        start "MBS_SERVER" /min dotnet run --no-build --urls "http://0.0.0.0:5111"
    )
    echo [INFO] Menunggu server siap (3 detik)...
    timeout /t 3 /nobreak >nul
) else (
    echo [OK] Web server sudah berjalan di port 5111.
)

:: 2. Buka Display Kiosk dengan Fullscreen & Auto Sound
echo [INFO] Membuka Display Kiosk...
call "%~dp0START_DISPLAY.bat"
