@echo off
chcp 65001 > nul
title MotionDesk Studio - Master Packager

:: Μετακίνηση ακριβώς στον φάκελο όπου βρίσκεται το batch script
cd /d "%~dp0"

echo [1/4] Δημιουργία δομής φακέλων...
if not exist "src\Core" mkdir "src\Core"
if not exist "src\Widgets\sysmon" mkdir "src\Widgets\sysmon"
if not exist "src\UI" mkdir "src\UI"
if not exist "locales" mkdir "locales"
if not exist "scripts" mkdir "scripts"
if not exist "redist" mkdir "redist"

echo [2/4] Δημιουργία αρχείων βάσης εάν λείπουν...
if not exist "installer.iss" (
    echo ; MotionDesk Studio Inno Setup > installer.iss
)
if not exist "build_local.ps1" (
    echo Write-Host "MotionDesk Build Script" > build_local.ps1
)

echo [3/4] Δημιουργία συμπιεσμένου αρχείου ZIP (DeskZones)...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$baseDir = '%~dp0'.TrimEnd('\'); $paths = @('src', 'locales', 'scripts', 'redist', 'installer.iss', 'build_local.ps1') | ForEach-Object { Join-Path $baseDir $_ } | Where-Object { Test-Path $_ }; Compress-Archive -Path $paths -DestinationPath (Join-Path $baseDir 'MotionDeskStudio_DeskZones.zip') -Force;"

echo [4/4] Ολοκληρώθηκε με επιτυχία!
echo Το αρχείο ZIP δημιουργήθηκε εδώ:
echo %~dp0MotionDeskStudio_DeskZones.zip
pause