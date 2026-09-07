@echo off
title START MBS SAFETY DISPLAY (KIOSK MODE)
color 0A

:: ============================================================================
:: KONFIGURASI URL DISPLAY
:: Ganti URL di bawah ini jika server dijalankan di IP atau Port lain
:: ============================================================================
set "DISPLAY_URL=https://mbssap.indexim.id/Display"

echo ============================================================================
echo   MENJALANKAN MBS SAFETY DISPLAY - KIOSK FULL SCREEN & AUTO-SOUND
echo ============================================================================
echo.
echo URL Target: %DISPLAY_URL%
echo.

:: Direktori profil sementara agar bersih dari notifikasi / bubble crash browser
set "PROFILE_DIR=%TEMP%\MBS_Display_Profile"

:: Parameter flags Chrome/Edge untuk Kiosk murni & Auto Audio
set "FLAGS=--kiosk ^
--autoplay-policy=no-user-gesture-required ^
--no-first-run ^
--no-default-browser-check ^
--disable-infobars ^
--disable-notifications ^
--disable-translate ^
--disable-features=Translate,OptimizationHints ^
--disable-session-crashed-bubble ^
--hide-crash-restore-bubble ^
--no-errdialogs ^
--user-data-dir="%PROFILE_DIR%""

:: Cek Google Chrome di berbagai lokasi standar
if exist "C:\Program Files\Google\Chrome\Application\chrome.exe" (
    echo [OK] Membuka Google Chrome (64-bit)...
    start "" "C:\Program Files\Google\Chrome\Application\chrome.exe" %FLAGS% "%DISPLAY_URL%"
    exit /b 0
)

if exist "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe" (
    echo [OK] Membuka Google Chrome (32-bit)...
    start "" "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe" %FLAGS% "%DISPLAY_URL%"
    exit /b 0
)

if exist "%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe" (
    echo [OK] Membuka Google Chrome (User AppData)...
    start "" "%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe" %FLAGS% "%DISPLAY_URL%"
    exit /b 0
)

:: Cek Microsoft Edge jika Chrome tidak terpasang
if exist "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" (
    echo [OK] Membuka Microsoft Edge (64-bit)...
    start "" "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" %FLAGS% "%DISPLAY_URL%"
    exit /b 0
)

if exist "C:\Program Files\Microsoft\Edge\Application\msedge.exe" (
    echo [OK] Membuka Microsoft Edge...
    start "" "C:\Program Files\Microsoft\Edge\Application\msedge.exe" %FLAGS% "%DISPLAY_URL%"
    exit /b 0
)

echo [ERROR] Google Chrome atau Microsoft Edge tidak ditemukan di lokasi standar.
echo Silakan install Google Chrome atau Microsoft Edge terlebih dahulu.
pause
