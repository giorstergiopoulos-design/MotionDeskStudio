@echo off
chcp 65001 > nul
title Opsis - Run Latest (no install)

:: Μετακίνηση στον φάκελο όπου βρίσκεται αυτό το .bat, ανεξάρτητα από πού εκτελείται.
cd /d "%~dp0"

echo [1/2] Χτίσιμο τελευταίας έκδοσης (Release)...
dotnet build MotionDeskStudio.csproj -c Release
if errorlevel 1 (
    echo.
    echo Το build απέτυχε — δείτε τα παραπάνω σφάλματα.
    pause
    exit /b 1
)

echo [2/2] Εκκίνηση Opsis...
start "" "%~dp0bin\Release\net10.0-windows10.0.19041.0\win-x64\Opsis.exe"
