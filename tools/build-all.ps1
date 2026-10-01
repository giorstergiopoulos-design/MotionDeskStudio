<#
  build-all.ps1 - pull + build + installer για MotionDeskStudio, GearWin, waveframe.
  Χρήση:   powershell -ExecutionPolicy Bypass -File build-all.ps1 [-Only MotionDesk,GearWin,Waveframe] [-SkipPull] [-NoInstaller]
  Ρύθμισε τα paths/branches στο $Projects παρακάτω. Σταματά ένα project στο πρώτο σφάλμα και συνεχίζει με το επόμενο.
  Δεν αντικαθιστά το click-through test πριν από release (βλ. CLAUDE.md).
#>
param(
    [string[]]$Only,
    [switch]$SkipPull,
    [switch]$NoInstaller
)
$ErrorActionPreference = 'Stop'
$Iscc = "C:\Users\gstrj\AppData\Local\Programs\Inno Setup 6\ISCC.exe"

# --- ΡΥΘΜΙΣΕ ΕΔΩ ---
$Projects = @(
    @{ Name='MotionDesk'; Path='C:\Projects\MotionDeskStudio'; Branch='claude/pensive-carson-sls1n6' },
    @{ Name='GearWin';    Path='C:\Projects\GearWin';          Branch='claude/full-audit' },
    @{ Name='Waveframe';  Path='C:\Projects\waveframe';        Branch='claude/full-audit' }
)

function Run($exe, $argList) {
    & $exe @argList
    if ($LASTEXITCODE -ne 0) { throw "$exe $($argList -join ' ') -> exit code $LASTEXITCODE" }
}

function Update-Repo($p) {
    if ($SkipPull) { return }
    Push-Location $p.Path
    try {
        if (git status --porcelain) { throw "Υπάρχουν τοπικές αλλαγές στο $($p.Path) - κάνε commit/stash πρώτα (δεν κάνω pull)." }
        Run git @('fetch','origin',$p.Branch)
        Run git @('checkout',$p.Branch)
        Run git @('pull','--ff-only','origin',$p.Branch)
    } finally { Pop-Location }
}

function Build-MotionDesk($p) {
    Push-Location $p.Path
    try {
        Run dotnet @('build','MotionDeskStudio.csproj','-c','Release')
        # ΧΩΡΙΣ -o: το installer.iss διαβάζει bin\Release\net8.0-windows10.0.19041.0\win-x64\publish
        Run dotnet @('publish','MotionDeskStudio.csproj','-c','Release','-r','win-x64','--self-contained','false')
        if (-not $NoInstaller) { Run $Iscc @('installer.iss'); "-> $($p.Path)\Output\MotionDeskStudioSetup.exe" }
    } finally { Pop-Location }
}

function Build-GearWin($p) {
    Push-Location $p.Path
    try {
        Run dotnet @('test','wpf\OptimizerWpf.Tests','-c','Release')
        # το OptimizerWpf.iss περιμένει wpf\OptimizerWpf\publish\win-x64
        Run dotnet @('publish','wpf\OptimizerWpf','-c','Release','-r','win-x64','--self-contained','false','-o','wpf\OptimizerWpf\publish\win-x64')
        if (-not $NoInstaller) { Run $Iscc @('installer\OptimizerWpf.iss'); "-> $($p.Path)\installer\Output\" }
    } finally { Pop-Location }
}

function Build-Waveframe($p) {
    Push-Location $p.Path
    try {
        Run npm @('ci')
        Run npm @('run','smoke-test')
        if (-not $NoInstaller) { Run npm @('run','dist'); "-> $($p.Path)\dist\" }
    } finally { Pop-Location }
}

$results = @()
foreach ($p in $Projects) {
    if ($Only -and ($Only -notcontains $p.Name)) { continue }
    Write-Host "`n=== $($p.Name) ===" -ForegroundColor Cyan
    try {
        Update-Repo $p
        & "Build-$($p.Name)" $p
        $results += "OK     $($p.Name)"
    } catch {
        Write-Host $_.Exception.Message -ForegroundColor Red
        $results += "FAILED $($p.Name): $($_.Exception.Message)"
    }
}
Write-Host "`n=== Σύνοψη ===" ; $results | ForEach-Object { Write-Host $_ }
if ($results -match '^FAILED') { exit 1 }
