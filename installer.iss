#define MyAppName "MotionDesk Studio"
#define MyAppVersion "1.7.8"
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
german.UpdateDetected=Eine vorhandene Installation von {#MyAppName} in Version %1 wurde erkannt.%n%nSie wird auf Version {#MyAppVersion} aktualisiert.
french.UpdateDetected=Une installation existante de {#MyAppName} version %1 a été détectée.%n%nElle sera mise à jour vers la version {#MyAppVersion}.
spanish.UpdateDetected=Se detectó una instalación existente de {#MyAppName} versión %1.%n%nSe actualizará a la versión {#MyAppVersion}.
korean.UpdateDetected={#MyAppName} 버전 %1이(가) 이미 설치되어 있습니다.%n%n버전 {#MyAppVersion}(으)로 업데이트됩니다.
chinese.UpdateDetected=检测到已安装 {#MyAppName} 版本 %1。%n%n将更新到版本 {#MyAppVersion}。
italian.UpdateDetected=È stata rilevata un'installazione esistente di {#MyAppName} versione %1.%n%nVerrà aggiornata alla versione {#MyAppVersion}.
russian.UpdateDetected=Обнаружена существующая установка {#MyAppName} версии %1.%n%nОна будет обновлена до версии {#MyAppVersion}.
japanese.UpdateDetected={#MyAppName} バージョン %1 の既存のインストールが検出されました。%n%nバージョン {#MyAppVersion} に更新されます。
portuguese.UpdateDetected=Foi detetada uma instalação existente do {#MyAppName} versão %1.%n%nSerá atualizada para a versão {#MyAppVersion}.
turkish.UpdateDetected={#MyAppName} uygulamasının %1 sürümü zaten yüklü.%n%nSürüm {#MyAppVersion} olarak güncellenecek.
arabic.UpdateDetected=تم اكتشاف تثبيت موجود من {#MyAppName} الإصدار %1.%n%nسيتم تحديثه إلى الإصدار {#MyAppVersion}.
hindi.UpdateDetected={#MyAppName} संस्करण %1 का मौजूदा इंस्टॉलेशन मिला।%n%nइसे संस्करण {#MyAppVersion} में अपडेट किया जाएगा।
greek.KeepDataPromptText=Να διαγραφούν επίσης οι ρυθμίσεις, τα προφίλ και τα αποθηκευμένα DeskZones/DeskContainers του {#MyAppName};
english.KeepDataPromptText=Do you also want to delete {#MyAppName}'s settings, profiles and saved DeskZones/DeskContainers?
german.KeepDataPromptText=Sollen auch die Einstellungen, Profile und gespeicherten DeskZones/DeskContainers von {#MyAppName} gelöscht werden?
french.KeepDataPromptText=Voulez-vous aussi supprimer les paramètres, profils et DeskZones/DeskContainers enregistrés de {#MyAppName} ?
spanish.KeepDataPromptText=¿Desea eliminar también la configuración, los perfiles y los DeskZones/DeskContainers guardados de {#MyAppName}?
korean.KeepDataPromptText={#MyAppName}의 설정, 프로필, 저장된 DeskZones/DeskContainers도 삭제하시겠습니까?
chinese.KeepDataPromptText=是否同时删除 {#MyAppName} 的设置、配置文件以及已保存的 DeskZones/DeskContainers？
italian.KeepDataPromptText=Vuoi eliminare anche le impostazioni, i profili e i DeskZones/DeskContainers salvati di {#MyAppName}?
russian.KeepDataPromptText=Удалить также настройки, профили и сохранённые DeskZones/DeskContainers приложения {#MyAppName}?
japanese.KeepDataPromptText={#MyAppName} の設定、プロファイル、保存済みの DeskZones/DeskContainers も削除しますか？
portuguese.KeepDataPromptText=Deseja eliminar também as definições, os perfis e os DeskZones/DeskContainers guardados do {#MyAppName}?
turkish.KeepDataPromptText={#MyAppName} uygulamasının ayarları, profilleri ve kayıtlı DeskZones/DeskContainers öğeleri de silinsin mi?
arabic.KeepDataPromptText=هل تريد أيضًا حذف إعدادات {#MyAppName} وملفاته الشخصية وDeskZones/DeskContainers المحفوظة؟
hindi.KeepDataPromptText=क्या आप {#MyAppName} की सेटिंग्स, प्रोफ़ाइल और सहेजे गए DeskZones/DeskContainers भी हटाना चाहते हैं?
greek.AutostartTask=Αυτόματη εκκίνηση με την είσοδο στα Windows (System Tray)
english.AutostartTask=Start automatically when signing in to Windows (System Tray)
german.AutostartTask=Automatisch beim Anmelden bei Windows starten (Infobereich)
french.AutostartTask=Démarrer automatiquement à l'ouverture de session Windows (zone de notification)
spanish.AutostartTask=Iniciar automáticamente al iniciar sesión en Windows (bandeja del sistema)
korean.AutostartTask=Windows 로그인 시 자동으로 시작(시스템 트레이)
chinese.AutostartTask=登录 Windows 时自动启动（系统托盘）
italian.AutostartTask=Avvia automaticamente all'accesso a Windows (area di notifica)
russian.AutostartTask=Запускать автоматически при входе в Windows (область уведомлений)
japanese.AutostartTask=Windows へのサインイン時に自動的に起動する (システム トレイ)
portuguese.AutostartTask=Iniciar automaticamente ao iniciar sessão no Windows (área de notificação)
turkish.AutostartTask=Windows'ta oturum açılırken otomatik başlat (Sistem Tepsisi)
arabic.AutostartTask=البدء تلقائيًا عند تسجيل الدخول إلى Windows (علبة النظام)
hindi.AutostartTask=Windows में साइन इन करते समय अपने आप शुरू करें (सिस्टम ट्रे)
greek.StartupGroup=Ρυθμίσεις Εκκίνησης:
english.StartupGroup=Startup settings:
german.StartupGroup=Starteinstellungen:
french.StartupGroup=Paramètres de démarrage :
spanish.StartupGroup=Configuración de inicio:
korean.StartupGroup=시작 설정:
chinese.StartupGroup=启动设置：
italian.StartupGroup=Impostazioni di avvio:
russian.StartupGroup=Параметры запуска:
japanese.StartupGroup=起動設定:
portuguese.StartupGroup=Definições de arranque:
turkish.StartupGroup=Başlangıç ayarları:
arabic.StartupGroup=إعدادات بدء التشغيل:
hindi.StartupGroup=स्टार्टअप सेटिंग्स:
greek.FfmpegTask=Εγκατάσταση FFmpeg (για αναπαραγωγή βίντεο .wmv ως κινούμενο wallpaper)
english.FfmpegTask=Install FFmpeg (to play .wmv videos as an animated wallpaper)
german.FfmpegTask=FFmpeg installieren (zum Abspielen von .wmv-Videos als animierter Hintergrund)
french.FfmpegTask=Installer FFmpeg (pour lire les vidéos .wmv comme fond d'écran animé)
spanish.FfmpegTask=Instalar FFmpeg (para reproducir vídeos .wmv como fondo animado)
korean.FfmpegTask=FFmpeg 설치(.wmv 동영상을 애니메이션 배경 화면으로 재생하기 위함)
chinese.FfmpegTask=安装 FFmpeg（用于将 .wmv 视频作为动态壁纸播放）
italian.FfmpegTask=Installa FFmpeg (per riprodurre i video .wmv come sfondo animato)
russian.FfmpegTask=Установить FFmpeg (для воспроизведения видео .wmv как живых обоев)
japanese.FfmpegTask=FFmpeg をインストール (.wmv 動画をアニメーション壁紙として再生するため)
portuguese.FfmpegTask=Instalar o FFmpeg (para reproduzir vídeos .wmv como fundo animado)
turkish.FfmpegTask=FFmpeg'i yükle (.wmv videolarını animasyonlu duvar kâğıdı olarak oynatmak için)
arabic.FfmpegTask=تثبيت FFmpeg (لتشغيل فيديوهات .wmv كخلفية متحركة)
hindi.FfmpegTask=FFmpeg इंस्टॉल करें (.wmv वीडियो को एनिमेटेड वॉलपेपर के रूप में चलाने के लिए)
greek.ExtraDepsGroup=Πρόσθετες εξαρτήσεις:
english.ExtraDepsGroup=Additional dependencies:
german.ExtraDepsGroup=Zusätzliche Abhängigkeiten:
french.ExtraDepsGroup=Dépendances supplémentaires :
spanish.ExtraDepsGroup=Dependencias adicionales:
korean.ExtraDepsGroup=추가 종속성:
chinese.ExtraDepsGroup=附加依赖项：
italian.ExtraDepsGroup=Dipendenze aggiuntive:
russian.ExtraDepsGroup=Дополнительные зависимости:
japanese.ExtraDepsGroup=追加の依存関係:
portuguese.ExtraDepsGroup=Dependências adicionais:
turkish.ExtraDepsGroup=Ek bağımlılıklar:
arabic.ExtraDepsGroup=اعتماديات إضافية:
hindi.ExtraDepsGroup=अतिरिक्त निर्भरताएँ:
greek.InstallingWebView2=Εγκατάσταση Microsoft Edge WebView2 Runtime...
english.InstallingWebView2=Installing Microsoft Edge WebView2 Runtime...
german.InstallingWebView2=Microsoft Edge WebView2 Runtime wird installiert...
french.InstallingWebView2=Installation de Microsoft Edge WebView2 Runtime...
spanish.InstallingWebView2=Instalando Microsoft Edge WebView2 Runtime...
korean.InstallingWebView2=Microsoft Edge WebView2 Runtime 설치 중...
chinese.InstallingWebView2=正在安装 Microsoft Edge WebView2 Runtime...
italian.InstallingWebView2=Installazione di Microsoft Edge WebView2 Runtime...
russian.InstallingWebView2=Установка Microsoft Edge WebView2 Runtime...
japanese.InstallingWebView2=Microsoft Edge WebView2 Runtime をインストールしています...
portuguese.InstallingWebView2=A instalar o Microsoft Edge WebView2 Runtime...
turkish.InstallingWebView2=Microsoft Edge WebView2 Runtime yükleniyor...
arabic.InstallingWebView2=جارٍ تثبيت Microsoft Edge WebView2 Runtime...
hindi.InstallingWebView2=Microsoft Edge WebView2 Runtime इंस्टॉल हो रहा है...
greek.InstallingDotNet=Εγκατάσταση .NET 8.0 Desktop Runtime...
english.InstallingDotNet=Installing .NET 8.0 Desktop Runtime...
german.InstallingDotNet=.NET 8.0 Desktop Runtime wird installiert...
french.InstallingDotNet=Installation de .NET 8.0 Desktop Runtime...
spanish.InstallingDotNet=Instalando .NET 8.0 Desktop Runtime...
korean.InstallingDotNet=.NET 8.0 Desktop Runtime 설치 중...
chinese.InstallingDotNet=正在安装 .NET 8.0 Desktop Runtime...
italian.InstallingDotNet=Installazione di .NET 8.0 Desktop Runtime...
russian.InstallingDotNet=Установка .NET 8.0 Desktop Runtime...
japanese.InstallingDotNet=.NET 8.0 Desktop Runtime をインストールしています...
portuguese.InstallingDotNet=A instalar o .NET 8.0 Desktop Runtime...
turkish.InstallingDotNet=.NET 8.0 Desktop Runtime yükleniyor...
arabic.InstallingDotNet=جارٍ تثبيت .NET 8.0 Desktop Runtime...
hindi.InstallingDotNet=.NET 8.0 Desktop Runtime इंस्टॉल हो रहा है...
greek.InstallingFfmpeg=Εγκατάσταση FFmpeg...
english.InstallingFfmpeg=Installing FFmpeg...
german.InstallingFfmpeg=FFmpeg wird installiert...
french.InstallingFfmpeg=Installation de FFmpeg...
spanish.InstallingFfmpeg=Instalando FFmpeg...
korean.InstallingFfmpeg=FFmpeg 설치 중...
chinese.InstallingFfmpeg=正在安装 FFmpeg...
italian.InstallingFfmpeg=Installazione di FFmpeg...
russian.InstallingFfmpeg=Установка FFmpeg...
japanese.InstallingFfmpeg=FFmpeg をインストールしています...
portuguese.InstallingFfmpeg=A instalar o FFmpeg...
turkish.InstallingFfmpeg=FFmpeg yükleniyor...
arabic.InstallingFfmpeg=جارٍ تثبيت FFmpeg...
hindi.InstallingFfmpeg=FFmpeg इंस्टॉल हो रहा है...

; Ρητό αίτημα χρήστη: "να έχει πληροφορίες και δομή και λειτουργίες όπως του gearwin" — ίδιο μοτίβο
; με τον installer του GearWin (../installer/OptimizerWpf.iss): σελίδα "Πληροφορίες" (InfoBeforeFile)
; πριν την επιλογή φακέλου, υποχρεωτική σελίδα άδειας (LicenseFile) ανά γλώσσα, μήνυμα ανίχνευσης
; ήδη-εγκατεστημένης έκδοσης, και ερώτηση επιβεβαίωσης στην απεγκατάσταση. Από την 1.7.8 η εφαρμογή (LocalizationManager, locales/)
; υποστηρίζει 14 γλώσσες, όπως το GearWin, και ο installer τις καλύπτει όλες. Chinese.isl/Hindi.isl είναι custom
; (αντιγραφή από το GearWin installer), τα υπόλοιπα .isl έρχονται με το Inno Setup.
[Languages]
Name: "greek"; MessagesFile: "compiler:Languages\Greek.isl"; LicenseFile: "installer\license_el.txt"; InfoBeforeFile: "installer\app_description_el.txt"
Name: "english"; MessagesFile: "compiler:Default.isl"; LicenseFile: "installer\license_en.txt"; InfoBeforeFile: "installer\app_description_en.txt"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"; LicenseFile: "installer\license_de.txt"; InfoBeforeFile: "installer\app_description_de.txt"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"; LicenseFile: "installer\license_fr.txt"; InfoBeforeFile: "installer\app_description_fr.txt"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"; LicenseFile: "installer\license_es.txt"; InfoBeforeFile: "installer\app_description_es.txt"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"; LicenseFile: "installer\license_ko.txt"; InfoBeforeFile: "installer\app_description_ko.txt"
Name: "chinese"; MessagesFile: "installer\Chinese.isl"; LicenseFile: "installer\license_zh.txt"; InfoBeforeFile: "installer\app_description_zh.txt"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"; LicenseFile: "installer\license_it.txt"; InfoBeforeFile: "installer\app_description_it.txt"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"; LicenseFile: "installer\license_ru.txt"; InfoBeforeFile: "installer\app_description_ru.txt"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"; LicenseFile: "installer\license_ja.txt"; InfoBeforeFile: "installer\app_description_ja.txt"
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"; LicenseFile: "installer\license_pt.txt"; InfoBeforeFile: "installer\app_description_pt.txt"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"; LicenseFile: "installer\license_tr.txt"; InfoBeforeFile: "installer\app_description_tr.txt"
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"; LicenseFile: "installer\license_ar.txt"; InfoBeforeFile: "installer\app_description_ar.txt"
Name: "hindi"; MessagesFile: "installer\Hindi.isl"; LicenseFile: "installer\license_hi.txt"; InfoBeforeFile: "installer\app_description_hi.txt"

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
