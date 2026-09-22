# MotionDesk Studio - Local Build Script
$ErrorActionPreference = "Stop"

Write-Host "=== Έναρξη Τοπικής Διαδικασίας Build ===" -ForegroundColor Cyan

if (-not (Get-Command "python" -ErrorAction SilentlyContinue)) {
    Write-Error "Η Python δεν βρέθηκε στο σύστημα!"
    exit 1
}

Write-Host "Εκτέλεση AI Build Agent..." -ForegroundColor Yellow
python scripts/ai_build_agent.py

if ($LASTEXITCODE -ne 0) {
    Write-Error "Η διαδικασία build απέτυχε."
    exit 1
}

Write-Host "Εκτέλεση dotnet publish..." -ForegroundColor Yellow
dotnet publish MotionDeskStudio.csproj -c Release -r win-x64 --self-contained false

if ($LASTEXITCODE -ne 0) {
    Write-Error "Το dotnet publish απέτυχε."
    exit 1
}

Write-Host "=== Η διαδικασία ολοκληρώθηκε επιτυχώς! ===" -ForegroundColor Green
