#define MyAppName "MotionDesk Studio"
#define MyAppVersion "1.7.6"
#define MyAppPublisher "MotionDesk Team"
#define MyAppExeName "MotionDesk.exe"

[Setup]
; Μοναδικό GUID για την εφαρμογή (μην το αλλάξεις σε μελλοντικές εκδόσεις)
AppId={{A9E8F765-1234-4567-89AB-CDEF01234567}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=MotionDeskStudioSetup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
AppMutex=MotionDeskStudio.SingleInstance
; Η εφαρμογή είναι win-x64 (framework-dependent, native WebView2Loader.dll x64) — ο installer
; πρέπει να τρέχει ΜΟΝΟ σε 64-bit-ικανά συστήματα, όχι σε καθαρό x86. Το "x64compatible" είναι
; το σύγχρονο identifier (το απλό "x64" είναι πλέον deprecated στο Inno Setup 6.7+).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=assets\MotionDesk.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableWelcomePage=no

[CustomMessages]
greek.UpdateDetected=Εντοπίστηκε ήδη εγκατεστημένη έκδοση %1 του {#MyAppName}.%n%nΘα ενημερωθεί στην έκδοση {#MyAppVersion}.
english.UpdateDetected=An existing installation of {#MyAppName} version %1 was detected.%n%nIt will be updated to version {#MyAppVersion}.
greek.KeepDataPromptText=Να διαγραφούν επίσης οι ρυθμίσεις, τα προφίλ και τα αποθηκευμένα DeskZones/DeskContainers του {#MyAppName};
english.KeepDataPromptText=Do you also want to delete {#MyAppName}'s settings, profiles and saved DeskZones/DeskContainers?
greek.AutostartTask=Αυτόματη εκκίνηση με την είσοδο στα Windows (System Tray)
english.AutostartTask=Start automatically when signing in to Windows (System Tray)
greek.StartupGroup=Ρυθμίσεις Εκκίνησης:
english.StartupGroup=Startup settings:
greek.FfmpegTask=Εγκατάσταση FFmpeg (για αναπαραγωγή βίντεο .wmv ως κινούμενο wallpaper)
english.FfmpegTask=Install FFmpeg (to play .wmv videos as an animated wallpaper)
greek.ExtraDepsGroup=Πρόσθετες εξαρτήσεις:
english.ExtraDepsGroup=Additional dependencies:
greek.InstallingWebView2=Εγκατάσταση Microsoft Edge WebView2 Runtime...
english.InstallingWebView2=Installing Microsoft Edge WebView2 Runtime...
greek.InstallingDotNet=Εγκατάσταση .NET 8.0 Desktop Runtime...
english.InstallingDotNet=Installing .NET 8.0 Desktop Runtime...
greek.InstallingFfmpeg=Εγκατάσταση FFmpeg...
english.InstallingFfmpeg=Installing FFmpeg...

; Ρητό αίτημα χρήστη: "να έχει πληροφορίες και δομή και λειτουργίες όπως του gearwin" — ίδιο μοτίβο
; με τον installer του GearWin (../installer/OptimizerWpf.iss): σελίδα "Πληροφορίες" (InfoBeforeFile)
; πριν την επιλογή φακέλου, υποχρεωτική σελίδα άδειας (LicenseFile) ανά γλώσσα, μήνυμα ανίχνευσης
; ήδη-εγκατεστημένης έκδοσης, και ερώτηση επιβεβαίωσης στην απεγκατάσταση. Μόνο EL/EN εδώ γιατί η
; ίδια η εφαρμογή (LocalizationManager, locales/) υποστηρίζει προς το παρόν μόνο αυτές τις 2 γλώσσες
; — αντίθετα με το GearWin που έχει πλήρη υποστήριξη 14 γλωσσών ήδη μέσα στην ίδια την εφαρμογή.
[Languages]
Name: "greek"; MessagesFile: "compiler:Languages\Greek.isl"; LicenseFile: "installer\license_el.txt"; InfoBeforeFile: "installer\app_description_el.txt"
Name: "english"; MessagesFile: "compiler:Default.isl"; LicenseFile: "installer\license_en.txt"; InfoBeforeFile: "installer\app_description_en.txt"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:StartupGroup}"
; Προαιρετική εξάρτηση — ζητήθηκε ρητά "να εμφανιζεται η δυνατοτητα εγκαταστασης dependencies
; οπως το ffmpeg". Το FFmpeg χρειάζεται μόνο για αναπαραγωγή/μετατροπή .wmv βίντεο ως κινούμενο
; wallpaper (WmvConversionService) — δεν είναι απαραίτητο για την υπόλοιπη εφαρμογή, γι' αυτό
; είναι tickbox και όχι υποχρεωτικό όπως το WebView2/.NET Runtime. Εγκαθίσταται μέσω winget (ίδιο
; πακέτο, Gyan.FFmpeg, με αυτό που ήδη ψάχνει το WmvConversionService.FindFfmpeg()).
Name: "installffmpeg"; Description: "{cm:FfmpegTask}"; GroupDescription: "{cm:ExtraDepsGroup}"; Flags: unchecked; Check: IsWingetAvailable and not IsFfmpegInstalled

[Files]
; Κύρια αρχεία εφαρμογής (δημιουργούνται από: dotnet publish -c Release -r win-x64)
Source: "bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

; WebView2 Evergreen Bootstrapper (κατεβάζει το πλήρες runtime online αν χρειαστεί)
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

; Η εφαρμογή είναι framework-dependent (όχι self-contained) — χρειάζεται το .NET 8 Desktop
; Runtime εγκατεστημένο στο μηχάνημα του χρήστη.
Source: "redist\windowsdesktop-runtime-8.0-win-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Αυτόματη εκκίνηση μέσω Registry (αν επιλεχθεί στο wizard)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
; 1. Έλεγχος και εγκατάσταση WebView2 Runtime (εάν δεν υπάρχει).
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "{cm:InstallingWebView2}"; Check: not IsWebView2Installed

; 2. Έλεγχος και εγκατάσταση .NET 8.0 Desktop Runtime (εάν δεν υπάρχει).
Filename: "{tmp}\windowsdesktop-runtime-8.0-win-x64.exe"; Parameters: "/quiet /norestart"; StatusMsg: "{cm:InstallingDotNet}"; Check: not IsDotNet8Installed

; 3. Προαιρετική εγκατάσταση FFmpeg μέσω winget (μόνο αν ο χρήστης επέλεξε το tickbox παραπάνω).
; "& exit 0" αποτρέπει το Inno Setup από το να δείξει σφάλμα αν το winget γυρίσει μη-μηδενικό
; exit code (π.χ. "no applicable update found" όταν είναι ήδη εγκατεστημένο).
Filename: "{cmd}"; Parameters: "/C winget install --id Gyan.FFmpeg -e --silent --accept-package-agreements --accept-source-agreements & exit 0"; StatusMsg: "{cm:InstallingFfmpeg}"; Flags: runhidden; Tasks: installffmpeg

; 4. Εκκίνηση της εφαρμογής μετά το τέλος της εγκατάστασης
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Συνάρτηση ελέγχου για το WebView2 Runtime στη Registry
function IsWebView2Installed: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
            RegKeyExists(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}');
end;

// Ελέγχει αν υπάρχει εγκατεστημένη οποιαδήποτε έκδοση 8.0.x του .NET Desktop Runtime (x64),
// σαρώνοντας τα subkeys αντί να κλειδώνουμε σε συγκεκριμένο patch version.
function HasDotNet8SubKey(RootKeyPath: String): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetSubkeyNames(HKLM, RootKeyPath, Names) then
  begin
    for I := 0 to GetArrayLength(Names) - 1 do
      if Pos('8.0.', Names[I]) = 1 then
      begin
        Result := True;
        exit;
      end;
  end;
end;

function IsDotNet8Installed: Boolean;
begin
  Result := HasDotNet8SubKey('SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') or
            HasDotNet8SubKey('SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App');
end;

// Ελέγχει αν το winget (App Installer) υπάρχει στο σύστημα, ώστε το tickbox του FFmpeg να μην
// εμφανίζεται καν σε μηχανήματα χωρίς winget (θα απέτυχε σιωπηλά χωρίς αυτό τον έλεγχο).
function IsWingetAvailable: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/C where winget >nul 2>nul', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// Γρήγορος έλεγχος αν υπάρχει ήδη FFmpeg (PATH ή γνωστός φάκελος winget) — ίδια λογική με το
// WmvConversionService.FindFfmpeg() της ίδιας της εφαρμογής, ώστε να μην προτείνουμε εγκατάσταση
// αν υπάρχει ήδη.
function IsFfmpegInstalled: Boolean;
var
  ResultCode: Integer;
begin
  if Exec(ExpandConstant('{cmd}'), '/C where ffmpeg >nul 2>nul', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
  begin
    Result := True;
    exit;
  end;
  Result := FileExists(ExpandConstant('{localappdata}\Microsoft\WinGet\Links\ffmpeg.exe'));
end;

// Ίδιο μοτίβο ανίχνευσης ενημέρωσης με το GearWin installer: διαβάζει το DisplayVersion από το
// registry uninstall key που γράφει το ίδιο το Inno Setup σε κάθε εγκατάσταση, ελέγχοντας ΚΑΙ τις
// δύο όψεις του registry (64-bit πρώτα, 32-bit fallback).
function GetInstalledVersion(): String;
var
  sVersion: String;
begin
  sVersion := '';
  if not RegQueryStringValue(HKLM64, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\A9E8F765-1234-4567-89AB-CDEF01234567_is1', 'DisplayVersion', sVersion) then
    RegQueryStringValue(HKLM32, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\A9E8F765-1234-4567-89AB-CDEF01234567_is1', 'DisplayVersion', sVersion);
  Result := sVersion;
end;

procedure InitializeWizard();
var
  sPrevVersion: String;
begin
  sPrevVersion := GetInstalledVersion();
  if sPrevVersion <> '' then
    WizardForm.WelcomeLabel2.Caption := FmtMessage(CustomMessage('UpdateDetected'), [sPrevVersion]);
end;

// Καθαρισμός %AppData%\MotionDeskStudio στην απεγκατάσταση (ρυθμίσεις, wallpaper.json,
// αποθηκευμένα profiles/DeskZones/DeskContainers) — ζητήθηκε ρητά, αλλά με ερώτηση επιβεβαίωσης
// πριν διαγραφούν πιθανά δεδομένα που ο χρήστης θέλει να κρατήσει. Το prompt χρησιμοποιεί πλέον
// CustomMessage (EL/EN) αντί για hardcoded ελληνικό κείμενο, ώστε να εμφανίζεται στη γλώσσα που
// επέλεξε ο χρήστης κατά την εγκατάσταση.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataPath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AppDataPath := ExpandConstant('{userappdata}\MotionDeskStudio');
    if DirExists(AppDataPath) then
    begin
      if MsgBox(CustomMessage('KeepDataPromptText') + #13#10 + #13#10 + AppDataPath, mbConfirmation, MB_YESNO) = IDYES then
        DelTree(AppDataPath, True, True, True);
    end;
  end;
end;
