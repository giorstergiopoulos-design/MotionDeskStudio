#define MyAppName "MotionDesk Studio"
#define MyAppVersion "1.2.9"
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
; Η εφαρμογή είναι win-x64 (framework-dependent, native WebView2Loader.dll x64) — ο installer
; πρέπει να τρέχει ΜΟΝΟ σε 64-bit-ικανά συστήματα, όχι σε καθαρό x86. Το "x64compatible" είναι
; το σύγχρονο identifier (το απλό "x64" είναι πλέον deprecated στο Inno Setup 6.7+).
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Αυτόματη εκκίνηση με την είσοδο στα Windows (System Tray)"; GroupDescription: "Ρυθμίσεις Εκκίνησης:"

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
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Εγκατάσταση Microsoft Edge WebView2 Runtime..."; Check: not IsWebView2Installed

; 2. Έλεγχος και εγκατάσταση .NET 8.0 Desktop Runtime (εάν δεν υπάρχει).
Filename: "{tmp}\windowsdesktop-runtime-8.0-win-x64.exe"; Parameters: "/quiet /norestart"; StatusMsg: "Εγκατάσταση .NET 8.0 Desktop Runtime..."; Check: not IsDotNet8Installed

; 3. Εκκίνηση της εφαρμογής μετά το τέλος της εγκατάστασης
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

// Καθαρισμός %AppData%\MotionDeskStudio στην απεγκατάσταση (ρυθμίσεις, wallpaper.json,
// αποθηκευμένα profiles/DeskZones/DeskContainers) — ζητήθηκε ρητά, αλλά με ερώτηση επιβεβαίωσης
// πριν διαγραφούν πιθανά δεδομένα που ο χρήστης θέλει να κρατήσει.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDataPath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    AppDataPath := ExpandConstant('{userappdata}\MotionDeskStudio');
    if DirExists(AppDataPath) then
    begin
      if MsgBox('Να διαγραφούν επίσης οι ρυθμίσεις, τα προφίλ και τα αποθηκευμένα DeskZones/DeskContainers του MotionDesk Studio;' + #13#10 + #13#10 + AppDataPath, mbConfirmation, MB_YESNO) = IDYES then
        DelTree(AppDataPath, True, True, True);
    end;
  end;
end;
