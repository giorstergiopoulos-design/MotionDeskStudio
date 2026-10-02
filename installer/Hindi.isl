; *** Inno Setup version 6.5.0+ Hindi messages ***
; Custom Hindi translation for GearWin - Complete PC Care's installer - no official Hindi.isl
; ships with Inno Setup, so this file was translated from the stock Default.isl (English) to add Hindi
; as a full installer wizard language alongside the app's own 14 supported UI languages.
;
; Note: When translating this text, do not add periods (.) to the end of
; messages that didn't have them already, because on those messages Inno
; Setup adds the periods automatically (appending a period would result in
; two periods being displayed).

[LangOptions]
LanguageName=हिन्दी
LanguageID=$0439
LanguageCodePage=0

[Messages]

; *** Application titles
SetupAppTitle=सेटअप
SetupWindowTitle=सेटअप - %1
UninstallAppTitle=अनइंस्टॉल करें
UninstallAppFullTitle=%1 अनइंस्टॉल करें

; *** Misc. common
InformationTitle=जानकारी
ConfirmTitle=पुष्टि
ErrorTitle=त्रुटि

; *** SetupLdr messages
SetupLdrStartupMessage=यह %1 को स्थापित करेगा। क्या आप जारी रखना चाहते हैं?
LdrCannotCreateTemp=एक अस्थायी फ़ाइल बनाने में असमर्थ। सेटअप निरस्त किया गया
LdrCannotExecTemp=अस्थायी निर्देशिका में फ़ाइल चलाने में असमर्थ। सेटअप निरस्त किया गया
HelpTextNote=

; *** Startup error messages
LastErrorMessage=%1.%n%nत्रुटि %2: %3
SetupFileMissing=स्थापना निर्देशिका से फ़ाइल %1 गायब है। कृपया समस्या ठीक करें या प्रोग्राम की एक नई प्रति प्राप्त करें।
SetupFileCorrupt=सेटअप फ़ाइलें दूषित हैं। कृपया प्रोग्राम की एक नई प्रति प्राप्त करें।
SetupFileCorruptOrWrongVer=सेटअप फ़ाइलें दूषित हैं, या सेटअप के इस संस्करण के साथ असंगत हैं। कृपया समस्या ठीक करें या प्रोग्राम की एक नई प्रति प्राप्त करें।
InvalidParameter=कमांड लाइन पर एक अमान्य पैरामीटर पास किया गया:%n%n%1
SetupAlreadyRunning=सेटअप पहले से ही चल रहा है।
WindowsVersionNotSupported=यह प्रोग्राम आपके कंप्यूटर पर चल रहे विंडोज़ के संस्करण का समर्थन नहीं करता है।
WindowsServicePackRequired=इस प्रोग्राम के लिए %1 Service Pack %2 या नए की आवश्यकता है।
NotOnThisPlatform=यह प्रोग्राम %1 पर नहीं चलेगा।
OnlyOnThisPlatform=यह प्रोग्राम %1 पर ही चलाया जाना चाहिए।
OnlyOnTheseArchitectures=यह प्रोग्राम केवल निम्नलिखित प्रोसेसर आर्किटेक्चर के लिए डिज़ाइन किए गए विंडोज़ संस्करणों पर स्थापित किया जा सकता है:%n%n%1
WinVersionTooLowError=इस प्रोग्राम के लिए %1 संस्करण %2 या नए की आवश्यकता है।
WinVersionTooHighError=यह प्रोग्राम %1 संस्करण %2 या नए पर स्थापित नहीं किया जा सकता।
AdminPrivilegesRequired=इस प्रोग्राम को स्थापित करते समय आपको एक व्यवस्थापक के रूप में लॉग इन होना चाहिए।
PowerUserPrivilegesRequired=इस प्रोग्राम को स्थापित करते समय आपको एक व्यवस्थापक या Power Users समूह के सदस्य के रूप में लॉग इन होना चाहिए।
SetupAppRunningError=सेटअप ने पता लगाया है कि %1 वर्तमान में चल रहा है।%n%nकृपया अभी इसके सभी उदाहरण बंद करें, फिर जारी रखने के लिए OK दबाएँ, या बाहर निकलने के लिए Cancel दबाएँ।
UninstallAppRunningError=अनइंस्टॉल ने पता लगाया है कि %1 वर्तमान में चल रहा है।%n%nकृपया अभी इसके सभी उदाहरण बंद करें, फिर जारी रखने के लिए OK दबाएँ, या बाहर निकलने के लिए Cancel दबाएँ।

; *** Startup questions
PrivilegesRequiredOverrideTitle=सेटअप इंस्टॉल मोड चुनें
PrivilegesRequiredOverrideInstruction=इंस्टॉल मोड चुनें
PrivilegesRequiredOverrideText1=%1 को सभी उपयोगकर्ताओं के लिए स्थापित किया जा सकता है (व्यवस्थापक अधिकारों की आवश्यकता है), या केवल आपके लिए।
PrivilegesRequiredOverrideText2=%1 को केवल आपके लिए स्थापित किया जा सकता है, या सभी उपयोगकर्ताओं के लिए (व्यवस्थापक अधिकारों की आवश्यकता है)।
PrivilegesRequiredOverrideAllUsers=&सभी उपयोगकर्ताओं के लिए स्थापित करें
PrivilegesRequiredOverrideAllUsersRecommended=&सभी उपयोगकर्ताओं के लिए स्थापित करें (अनुशंसित)
PrivilegesRequiredOverrideCurrentUser=&केवल मेरे लिए स्थापित करें
PrivilegesRequiredOverrideCurrentUserRecommended=&केवल मेरे लिए स्थापित करें (अनुशंसित)

; *** Misc. errors
ErrorCreatingDir=सेटअप निर्देशिका "%1" बनाने में असमर्थ था
ErrorTooManyFilesInDir=निर्देशिका "%1" में एक फ़ाइल बनाने में असमर्थ क्योंकि इसमें बहुत अधिक फ़ाइलें हैं

; *** Setup common messages
ExitSetupTitle=सेटअप से बाहर निकलें
ExitSetupMessage=सेटअप पूर्ण नहीं हुआ है। यदि आप अभी बाहर निकलते हैं, तो प्रोग्राम स्थापित नहीं होगा।%n%nआप स्थापना पूर्ण करने के लिए किसी अन्य समय सेटअप फिर से चला सकते हैं।%n%nक्या सेटअप से बाहर निकलें?
AboutSetupMenuItem=सेटअप के &बारे में...
AboutSetupTitle=सेटअप के बारे में
AboutSetupMessage=%1 संस्करण %2%n%3%n%n%1 होम पेज:%n%4
AboutSetupNote=
TranslatorNote=

; *** Buttons
ButtonBack=< &पीछे
ButtonNext=&अगला >
ButtonInstall=&स्थापित करें
ButtonOK=ठीक है
ButtonCancel=रद्द करें
ButtonYes=&हाँ
ButtonYesToAll=सभी को &हाँ
ButtonNo=&नहीं
ButtonNoToAll=सभी क&ो नहीं
ButtonFinish=&समाप्त करें
ButtonBrowse=&ब्राउज़ करें...
ButtonWizardBrowse=ब्&राउज़ करें...
ButtonNewFolder=&नया फ़ोल्डर बनाएँ

; *** "Select Language" dialog messages
SelectLanguageTitle=सेटअप भाषा चुनें
SelectLanguageLabel=स्थापना के दौरान उपयोग करने के लिए भाषा चुनें।

; *** Common wizard text
ClickNext=जारी रखने के लिए Next दबाएँ, या सेटअप से बाहर निकलने के लिए Cancel दबाएँ।
BeveledLabel=
BrowseDialogTitle=फ़ोल्डर के लिए ब्राउज़ करें
BrowseDialogLabel=नीचे दी गई सूची में एक फ़ोल्डर चुनें, फिर OK दबाएँ।
NewFolderName=नया फ़ोल्डर

; *** "Welcome" wizard page
WelcomeLabel1=[name] सेटअप विज़ार्ड में आपका स्वागत है
WelcomeLabel2=यह आपके कंप्यूटर पर [name/ver] स्थापित करेगा।%n%nजारी रखने से पहले अन्य सभी एप्लिकेशन बंद करने की सिफारिश की जाती है।

; *** "Password" wizard page
WizardPassword=पासवर्ड
PasswordLabel1=यह स्थापना पासवर्ड से सुरक्षित है।
PasswordLabel3=कृपया पासवर्ड प्रदान करें, फिर जारी रखने के लिए Next दबाएँ। पासवर्ड केस-संवेदनशील हैं।
PasswordEditLabel=&पासवर्ड:
IncorrectPassword=आपके द्वारा दर्ज किया गया पासवर्ड सही नहीं है। कृपया फिर से प्रयास करें।

; *** "License Agreement" wizard page
WizardLicense=लाइसेंस अनुबंध
LicenseLabel=जारी रखने से पहले कृपया निम्नलिखित महत्वपूर्ण जानकारी पढ़ें।
LicenseLabel3=कृपया निम्नलिखित लाइसेंस अनुबंध पढ़ें। स्थापना जारी रखने से पहले आपको इस अनुबंध की शर्तों को स्वीकार करना होगा।
LicenseAccepted=मैं अनुबंध को &स्वीकार करता/करती हूँ
LicenseNotAccepted=मैं अनुबंध को स्वीकार &नहीं करता/करती हूँ

; *** "Information" wizard pages
WizardInfoBefore=जानकारी
InfoBeforeLabel=जारी रखने से पहले कृपया निम्नलिखित महत्वपूर्ण जानकारी पढ़ें।
InfoBeforeClickLabel=जब आप सेटअप जारी रखने के लिए तैयार हों, तो Next दबाएँ।
WizardInfoAfter=जानकारी
InfoAfterLabel=जारी रखने से पहले कृपया निम्नलिखित महत्वपूर्ण जानकारी पढ़ें।
InfoAfterClickLabel=जब आप सेटअप जारी रखने के लिए तैयार हों, तो Next दबाएँ।

; *** "User Information" wizard page
WizardUserInfo=उपयोगकर्ता जानकारी
UserInfoDesc=कृपया अपनी जानकारी दर्ज करें।
UserInfoName=&उपयोगकर्ता नाम:
UserInfoOrg=&संगठन:
UserInfoSerial=&सीरियल नंबर:
UserInfoNameRequired=आपको एक नाम दर्ज करना होगा।

; *** "Select Destination Location" wizard page
WizardSelectDir=गंतव्य स्थान चुनें
SelectDirDesc=[name] कहाँ स्थापित किया जाना चाहिए?
SelectDirLabel3=सेटअप [name] को निम्नलिखित फ़ोल्डर में स्थापित करेगा।
SelectDirBrowseLabel=जारी रखने के लिए, Next दबाएँ। यदि आप एक अलग फ़ोल्डर चुनना चाहते हैं, तो Browse दबाएँ।
DiskSpaceGBLabel=कम से कम [gb] GB खाली डिस्क स्थान आवश्यक है।
DiskSpaceMBLabel=कम से कम [mb] MB खाली डिस्क स्थान आवश्यक है।
CannotInstallToNetworkDrive=सेटअप एक नेटवर्क ड्राइव पर स्थापित नहीं कर सकता।
CannotInstallToUNCPath=सेटअप एक UNC पथ पर स्थापित नहीं कर सकता।
InvalidPath=आपको ड्राइव अक्षर के साथ एक पूर्ण पथ दर्ज करना होगा; उदाहरण के लिए:%n%nC:\APP%n%nया इस रूप में एक UNC पथ:%n%n\\server\share
InvalidDrive=आपके द्वारा चयनित ड्राइव या UNC शेयर मौजूद नहीं है या पहुँच योग्य नहीं है। कृपया दूसरा चुनें।
DiskSpaceWarningTitle=पर्याप्त डिस्क स्थान नहीं है
DiskSpaceWarning=स्थापित करने के लिए सेटअप को कम से कम %1 KB खाली स्थान की आवश्यकता है, लेकिन चयनित ड्राइव में केवल %2 KB उपलब्ध है।%n%nक्या आप फिर भी जारी रखना चाहते हैं?
DirNameTooLong=फ़ोल्डर का नाम या पथ बहुत लंबा है।
InvalidDirName=फ़ोल्डर का नाम मान्य नहीं है।
BadDirName32=फ़ोल्डर नामों में निम्नलिखित में से कोई भी वर्ण शामिल नहीं हो सकता:%n%n%1
DirExistsTitle=फ़ोल्डर मौजूद है
DirExists=फ़ोल्डर:%n%n%1%n%nपहले से मौजूद है। क्या आप फिर भी उस फ़ोल्डर में स्थापित करना चाहेंगे?
DirDoesntExistTitle=फ़ोल्डर मौजूद नहीं है
DirDoesntExist=फ़ोल्डर:%n%n%1%n%nमौजूद नहीं है। क्या आप चाहेंगे कि फ़ोल्डर बनाया जाए?

; *** "Select Components" wizard page
WizardSelectComponents=घटक चुनें
SelectComponentsDesc=कौन से घटक स्थापित किए जाने चाहिए?
SelectComponentsLabel2=उन घटकों को चुनें जिन्हें आप स्थापित करना चाहते हैं; उन घटकों को साफ़ करें जिन्हें आप स्थापित नहीं करना चाहते हैं। जब आप जारी रखने के लिए तैयार हों तो Next दबाएँ।
FullInstallation=पूर्ण स्थापना
CompactInstallation=संक्षिप्त स्थापना
CustomInstallation=कस्टम स्थापना
NoUninstallWarningTitle=घटक मौजूद हैं
NoUninstallWarning=सेटअप ने पता लगाया है कि निम्नलिखित घटक पहले से ही आपके कंप्यूटर पर स्थापित हैं:%n%n%1%n%nइन घटकों का चयन रद्द करने से वे अनइंस्टॉल नहीं होंगे।%n%nक्या आप फिर भी जारी रखना चाहेंगे?
ComponentSize1=%1 KB
ComponentSize2=%1 MB
ComponentsDiskSpaceGBLabel=वर्तमान चयन के लिए कम से कम [gb] GB डिस्क स्थान आवश्यक है।
ComponentsDiskSpaceMBLabel=वर्तमान चयन के लिए कम से कम [mb] MB डिस्क स्थान आवश्यक है।

; *** "Select Additional Tasks" wizard page
WizardSelectTasks=अतिरिक्त कार्य चुनें
SelectTasksDesc=कौन से अतिरिक्त कार्य किए जाने चाहिए?
SelectTasksLabel2=[name] स्थापित करते समय सेटअप को जो अतिरिक्त कार्य करने चाहिए, उन्हें चुनें, फिर Next दबाएँ।

; *** "Select Start Menu Folder" wizard page
WizardSelectProgramGroup=स्टार्ट मेनू फ़ोल्डर चुनें
SelectStartMenuFolderDesc=सेटअप को प्रोग्राम के शॉर्टकट कहाँ रखने चाहिए?
SelectStartMenuFolderLabel3=सेटअप निम्नलिखित स्टार्ट मेनू फ़ोल्डर में प्रोग्राम के शॉर्टकट बनाएगा।
SelectStartMenuFolderBrowseLabel=जारी रखने के लिए, Next दबाएँ। यदि आप एक अलग फ़ोल्डर चुनना चाहते हैं, तो Browse दबाएँ।
MustEnterGroupName=आपको एक फ़ोल्डर नाम दर्ज करना होगा।
GroupNameTooLong=फ़ोल्डर का नाम या पथ बहुत लंबा है।
InvalidGroupName=फ़ोल्डर का नाम मान्य नहीं है।
BadGroupName=फ़ोल्डर के नाम में निम्नलिखित में से कोई भी वर्ण शामिल नहीं हो सकता:%n%n%1
NoProgramGroupCheck2=&स्टार्ट मेनू फ़ोल्डर न बनाएँ

; *** "Ready to Install" wizard page
WizardReady=स्थापित करने के लिए तैयार
ReadyLabel1=सेटअप अब आपके कंप्यूटर पर [name] स्थापित करना शुरू करने के लिए तैयार है।
ReadyLabel2a=स्थापना जारी रखने के लिए Install दबाएँ, या यदि आप किसी सेटिंग की समीक्षा या परिवर्तन करना चाहते हैं तो Back दबाएँ।
ReadyLabel2b=स्थापना जारी रखने के लिए Install दबाएँ।
ReadyMemoUserInfo=उपयोगकर्ता जानकारी:
ReadyMemoDir=गंतव्य स्थान:
ReadyMemoType=सेटअप प्रकार:
ReadyMemoComponents=चयनित घटक:
ReadyMemoGroup=स्टार्ट मेनू फ़ोल्डर:
ReadyMemoTasks=अतिरिक्त कार्य:

; *** TDownloadWizardPage wizard page and DownloadTemporaryFile
DownloadingLabel2=फ़ाइलें डाउनलोड हो रही हैं...
ButtonStopDownload=डाउनलोड &रोकें
StopDownload=क्या आप वाकई डाउनलोड रोकना चाहते हैं?
ErrorDownloadAborted=डाउनलोड निरस्त किया गया
ErrorDownloadFailed=डाउनलोड विफल रहा: %1 %2
ErrorDownloadSizeFailed=आकार प्राप्त करना विफल रहा: %1 %2
ErrorProgress=अमान्य प्रगति: %2 में से %1
ErrorFileSize=अमान्य फ़ाइल आकार: अपेक्षित %1, मिला %2

; *** TExtractionWizardPage wizard page and ExtractArchive
ExtractingLabel=फ़ाइलें निकाली जा रही हैं...
ButtonStopExtraction=निष्कर्षण &रोकें
StopExtraction=क्या आप वाकई निष्कर्षण रोकना चाहते हैं?
ErrorExtractionAborted=निष्कर्षण निरस्त किया गया
ErrorExtractionFailed=निष्कर्षण विफल रहा: %1

; *** Archive extraction failure details
ArchiveIncorrectPassword=पासवर्ड गलत है
ArchiveIsCorrupted=आर्काइव दूषित है
ArchiveUnsupportedFormat=आर्काइव प्रारूप असमर्थित है

; *** "Preparing to Install" wizard page
WizardPreparing=स्थापना की तैयारी
PreparingDesc=सेटअप आपके कंप्यूटर पर [name] स्थापित करने की तैयारी कर रहा है।
PreviousInstallNotCompleted=पिछले प्रोग्राम की स्थापना/हटाना पूर्ण नहीं हुआ। उस स्थापना को पूर्ण करने के लिए आपको अपना कंप्यूटर पुनः आरंभ करना होगा।%n%nअपना कंप्यूटर पुनः आरंभ करने के बाद, [name] की स्थापना पूर्ण करने के लिए सेटअप फिर से चलाएँ।
CannotContinue=सेटअप जारी नहीं रह सकता। बाहर निकलने के लिए कृपया Cancel दबाएँ।
ApplicationsFound=निम्नलिखित एप्लिकेशन उन फ़ाइलों का उपयोग कर रहे हैं जिन्हें सेटअप द्वारा अपडेट करने की आवश्यकता है। यह अनुशंसित है कि आप सेटअप को इन एप्लिकेशन को स्वचालित रूप से बंद करने दें।
ApplicationsFound2=निम्नलिखित एप्लिकेशन उन फ़ाइलों का उपयोग कर रहे हैं जिन्हें सेटअप द्वारा अपडेट करने की आवश्यकता है। यह अनुशंसित है कि आप सेटअप को इन एप्लिकेशन को स्वचालित रूप से बंद करने दें। स्थापना पूर्ण होने के बाद, सेटअप एप्लिकेशन को फिर से शुरू करने का प्रयास करेगा।
CloseApplications=एप्लिकेशन को स्वचालित रूप से &बंद करें
DontCloseApplications=एप्लिकेशन को &बंद न करें
ErrorCloseApplications=सेटअप स्वचालित रूप से सभी एप्लिकेशन बंद करने में असमर्थ था। यह अनुशंसित है कि आप जारी रखने से पहले उन फ़ाइलों का उपयोग करने वाले सभी एप्लिकेशन बंद कर दें जिन्हें सेटअप द्वारा अपडेट करने की आवश्यकता है।
PrepareToInstallNeedsRestart=सेटअप को आपका कंप्यूटर पुनः आरंभ करना होगा। अपना कंप्यूटर पुनः आरंभ करने के बाद, [name] की स्थापना पूर्ण करने के लिए सेटअप फिर से चलाएँ।%n%nक्या आप अभी पुनः आरंभ करना चाहेंगे?

; *** "Installing" wizard page
WizardInstalling=स्थापित हो रहा है
InstallingLabel=कृपया प्रतीक्षा करें जब तक सेटअप आपके कंप्यूटर पर [name] स्थापित करता है।

; *** "Setup Completed" wizard page
FinishedHeadingLabel=[name] सेटअप विज़ार्ड पूर्ण हो रहा है
FinishedLabelNoIcons=सेटअप ने आपके कंप्यूटर पर [name] की स्थापना पूर्ण कर ली है।
FinishedLabel=सेटअप ने आपके कंप्यूटर पर [name] की स्थापना पूर्ण कर ली है। स्थापित शॉर्टकट का चयन करके एप्लिकेशन लॉन्च किया जा सकता है।
ClickFinish=सेटअप से बाहर निकलने के लिए Finish दबाएँ।
FinishedRestartLabel=[name] की स्थापना पूर्ण करने के लिए, सेटअप को आपका कंप्यूटर पुनः आरंभ करना होगा। क्या आप अभी पुनः आरंभ करना चाहेंगे?
FinishedRestartMessage=[name] की स्थापना पूर्ण करने के लिए, सेटअप को आपका कंप्यूटर पुनः आरंभ करना होगा।%n%nक्या आप अभी पुनः आरंभ करना चाहेंगे?
ShowReadmeCheck=हाँ, मैं README फ़ाइल देखना चाहूँगा/चाहूँगी
YesRadio=&हाँ, अभी कंप्यूटर पुनः आरंभ करें
NoRadio=&नहीं, मैं बाद में कंप्यूटर पुनः आरंभ करूँगा/करूँगी
RunEntryExec=%1 चलाएँ
RunEntryShellExec=%1 देखें

; *** "Setup Needs the Next Disk" stuff
ChangeDiskTitle=सेटअप को अगली डिस्क की आवश्यकता है
SelectDiskLabel2=कृपया डिस्क %1 डालें और OK दबाएँ।%n%nयदि इस डिस्क पर फ़ाइलें नीचे दिखाए गए फ़ोल्डर के अलावा किसी अन्य फ़ोल्डर में मिल सकती हैं, तो सही पथ दर्ज करें या Browse दबाएँ।
PathLabel=&पथ:
FileNotInDir2=फ़ाइल "%1" "%2" में नहीं मिल सकी। कृपया सही डिस्क डालें या दूसरा फ़ोल्डर चुनें।
SelectDirectoryLabel=कृपया अगली डिस्क का स्थान निर्दिष्ट करें।

; *** Installation phase messages
SetupAborted=सेटअप पूर्ण नहीं हुआ।%n%nकृपया समस्या ठीक करें और सेटअप फिर से चलाएँ।
AbortRetryIgnoreSelectAction=कार्रवाई चुनें
AbortRetryIgnoreRetry=&फिर से प्रयास करें
AbortRetryIgnoreIgnore=&त्रुटि को अनदेखा करें और जारी रखें
AbortRetryIgnoreCancel=स्थापना रद्द करें
RetryCancelSelectAction=कार्रवाई चुनें
RetryCancelRetry=&फिर से प्रयास करें
RetryCancelCancel=रद्द करें

; *** Installation status messages
StatusClosingApplications=एप्लिकेशन बंद हो रहे हैं...
StatusCreateDirs=निर्देशिकाएँ बनाई जा रही हैं...
StatusExtractFiles=फ़ाइलें निकाली जा रही हैं...
StatusDownloadFiles=फ़ाइलें डाउनलोड हो रही हैं...
StatusCreateIcons=शॉर्टकट बनाए जा रहे हैं...
StatusCreateIniEntries=INI प्रविष्टियाँ बनाई जा रही हैं...
StatusCreateRegistryEntries=रजिस्ट्री प्रविष्टियाँ बनाई जा रही हैं...
StatusRegisterFiles=फ़ाइलें पंजीकृत हो रही हैं...
StatusSavingUninstall=अनइंस्टॉल जानकारी सहेजी जा रही है...
StatusRunProgram=स्थापना पूर्ण हो रही है...
StatusRestartingApplications=एप्लिकेशन पुनः आरंभ हो रहे हैं...
StatusRollback=परिवर्तन वापस लिए जा रहे हैं...

; *** Misc. errors
ErrorInternal2=आंतरिक त्रुटि: %1
ErrorFunctionFailedNoCode=%1 विफल रहा
ErrorFunctionFailed=%1 विफल रहा; कोड %2
ErrorFunctionFailedWithMessage=%1 विफल रहा; कोड %2.%n%3
ErrorExecutingProgram=फ़ाइल चलाने में असमर्थ:%n%1

; *** Registry errors
ErrorRegOpenKey=रजिस्ट्री कुंजी खोलने में त्रुटि:%n%1\%2
ErrorRegCreateKey=रजिस्ट्री कुंजी बनाने में त्रुटि:%n%1\%2
ErrorRegWriteKey=रजिस्ट्री कुंजी में लिखने में त्रुटि:%n%1\%2

; *** INI errors
ErrorIniEntry=फ़ाइल "%1" में INI प्रविष्टि बनाने में त्रुटि।

; *** File copying errors
FileAbortRetryIgnoreSkipNotRecommended=इस फ़ाइल को &छोड़ें (अनुशंसित नहीं)
FileAbortRetryIgnoreIgnoreNotRecommended=&त्रुटि को अनदेखा करें और जारी रखें (अनुशंसित नहीं)
SourceIsCorrupted=स्रोत फ़ाइल दूषित है
SourceDoesntExist=स्रोत फ़ाइल "%1" मौजूद नहीं है
SourceVerificationFailed=स्रोत फ़ाइल का सत्यापन विफल रहा: %1
VerificationSignatureDoesntExist=हस्ताक्षर फ़ाइल "%1" मौजूद नहीं है
VerificationSignatureInvalid=हस्ताक्षर फ़ाइल "%1" अमान्य है
VerificationKeyNotFound=हस्ताक्षर फ़ाइल "%1" एक अज्ञात कुंजी का उपयोग करती है
VerificationFileNameIncorrect=फ़ाइल का नाम गलत है
VerificationFileTagIncorrect=फ़ाइल का टैग गलत है
VerificationFileSizeIncorrect=फ़ाइल का आकार गलत है
VerificationFileHashIncorrect=फ़ाइल का हैश गलत है
ExistingFileReadOnly2=मौजूदा फ़ाइल को बदला नहीं जा सका क्योंकि यह रीड-ओनली के रूप में चिह्नित है।
ExistingFileReadOnlyRetry=&रीड-ओनली विशेषता हटाएँ और फिर से प्रयास करें
ExistingFileReadOnlyKeepExisting=मौजूदा फ़ाइल &रखें
ErrorReadingExistingDest=मौजूदा फ़ाइल पढ़ने का प्रयास करते समय एक त्रुटि हुई:
FileExistsSelectAction=कार्रवाई चुनें
FileExists2=फ़ाइल पहले से मौजूद है।
FileExistsOverwriteExisting=मौजूदा फ़ाइल को &अधिलेखित करें
FileExistsKeepExisting=मौजूदा फ़ाइल &रखें
FileExistsOverwriteOrKeepAll=अगले टकरावों के लिए &यही करें
ExistingFileNewerSelectAction=कार्रवाई चुनें
ExistingFileNewer2=मौजूदा फ़ाइल सेटअप द्वारा स्थापित की जा रही फ़ाइल से नई है।
ExistingFileNewerOverwriteExisting=मौजूदा फ़ाइल को &अधिलेखित करें
ExistingFileNewerKeepExisting=मौजूदा फ़ाइल &रखें (अनुशंसित)
ExistingFileNewerOverwriteOrKeepAll=अगले टकरावों के लिए &यही करें
ErrorChangingAttr=मौजूदा फ़ाइल की विशेषताएँ बदलने का प्रयास करते समय एक त्रुटि हुई:
ErrorCreatingTemp=गंतव्य निर्देशिका में एक फ़ाइल बनाने का प्रयास करते समय एक त्रुटि हुई:
ErrorReadingSource=स्रोत फ़ाइल पढ़ने का प्रयास करते समय एक त्रुटि हुई:
ErrorCopying=फ़ाइल की प्रतिलिपि बनाने का प्रयास करते समय एक त्रुटि हुई:
ErrorDownloading=फ़ाइल डाउनलोड करने का प्रयास करते समय एक त्रुटि हुई:
ErrorExtracting=एक आर्काइव निकालने का प्रयास करते समय एक त्रुटि हुई:
ErrorReplacingExistingFile=मौजूदा फ़ाइल को बदलने का प्रयास करते समय एक त्रुटि हुई:
ErrorRestartReplace=RestartReplace विफल रहा:
ErrorRenamingTemp=गंतव्य निर्देशिका में एक फ़ाइल का नाम बदलने का प्रयास करते समय एक त्रुटि हुई:
ErrorRegisterServer=DLL/OCX पंजीकृत करने में असमर्थ: %1
ErrorRegSvr32Failed=RegSvr32 निकास कोड %1 के साथ विफल रहा
ErrorRegisterTypeLib=टाइप लाइब्रेरी पंजीकृत करने में असमर्थ: %1

; *** Uninstall display name markings
UninstallDisplayNameMark=%1 (%2)
UninstallDisplayNameMarks=%1 (%2, %3)
UninstallDisplayNameMark32Bit=32-बिट
UninstallDisplayNameMark64Bit=64-बिट
UninstallDisplayNameMarkAllUsers=सभी उपयोगकर्ता
UninstallDisplayNameMarkCurrentUser=वर्तमान उपयोगकर्ता

; *** Post-installation errors
ErrorOpeningReadme=README फ़ाइल खोलने का प्रयास करते समय एक त्रुटि हुई।
ErrorRestartingComputer=सेटअप कंप्यूटर को पुनः आरंभ करने में असमर्थ था। कृपया यह मैन्युअल रूप से करें।

; *** Uninstaller messages
UninstallNotFound=फ़ाइल "%1" मौजूद नहीं है। अनइंस्टॉल नहीं किया जा सकता।
UninstallOpenError=फ़ाइल "%1" खोली नहीं जा सकी। अनइंस्टॉल नहीं किया जा सकता
UninstallUnsupportedVer=अनइंस्टॉल लॉग फ़ाइल "%1" उस प्रारूप में है जिसे अनइंस्टॉलर का यह संस्करण नहीं पहचानता। अनइंस्टॉल नहीं किया जा सकता
UninstallUnknownEntry=अनइंस्टॉल लॉग में एक अज्ञात प्रविष्टि (%1) मिली
ConfirmUninstall=क्या आप वाकई %1 और इसके सभी घटकों को पूरी तरह से हटाना चाहते हैं?
UninstallOnlyOnWin64=यह स्थापना केवल 64-बिट विंडोज़ पर ही अनइंस्टॉल की जा सकती है।
OnlyAdminCanUninstall=यह स्थापना केवल व्यवस्थापक अधिकारों वाले उपयोगकर्ता द्वारा ही अनइंस्टॉल की जा सकती है।
UninstallStatusLabel=कृपया प्रतीक्षा करें जब तक %1 आपके कंप्यूटर से हटाया जाता है।
UninstalledAll=%1 आपके कंप्यूटर से सफलतापूर्वक हटा दिया गया।
UninstalledMost=%1 अनइंस्टॉल पूर्ण हुआ।%n%nकुछ तत्व हटाए नहीं जा सके। इन्हें मैन्युअल रूप से हटाया जा सकता है।
UninstalledAndNeedsRestart=%1 का अनइंस्टॉलेशन पूर्ण करने के लिए, आपके कंप्यूटर को पुनः आरंभ करना होगा।%n%nक्या आप अभी पुनः आरंभ करना चाहेंगे?
UninstallDataCorrupted="%1" फ़ाइल दूषित है। अनइंस्टॉल नहीं किया जा सकता

; *** Uninstallation phase messages
ConfirmDeleteSharedFileTitle=साझा फ़ाइल हटाएँ?
ConfirmDeleteSharedFile2=सिस्टम इंगित करता है कि निम्नलिखित साझा फ़ाइल अब किसी भी प्रोग्राम द्वारा उपयोग में नहीं है। क्या आप चाहेंगे कि Uninstall इस साझा फ़ाइल को हटा दे?%n%nयदि कोई प्रोग्राम अभी भी इस फ़ाइल का उपयोग कर रहे हैं और इसे हटा दिया जाता है, तो वे प्रोग्राम ठीक से काम नहीं कर सकते हैं। यदि आप अनिश्चित हैं, तो No चुनें। फ़ाइल को अपने सिस्टम पर छोड़ने से कोई नुकसान नहीं होगा।
SharedFileNameLabel=फ़ाइल नाम:
SharedFileLocationLabel=स्थान:
WizardUninstalling=अनइंस्टॉल स्थिति
StatusUninstalling=%1 अनइंस्टॉल हो रहा है...

; *** Shutdown block reasons
ShutdownBlockReasonInstallingApp=%1 स्थापित हो रहा है।
ShutdownBlockReasonUninstallingApp=%1 अनइंस्टॉल हो रहा है।

; The custom messages below aren't used by Setup itself, but if you make
; use of them in your scripts, you'll want to translate them.

[CustomMessages]

NameAndVersion=%1 संस्करण %2
AdditionalIcons=अतिरिक्त शॉर्टकट:
CreateDesktopIcon=&डेस्कटॉप शॉर्टकट बनाएँ
CreateQuickLaunchIcon=&Quick Launch शॉर्टकट बनाएँ
ProgramOnTheWeb=वेब पर %1
UninstallProgram=%1 अनइंस्टॉल करें
LaunchProgram=%1 लॉन्च करें
AssocFileExtension=%1 को %2 फ़ाइल एक्सटेंशन के साथ &संबद्ध करें
AssocingFileExtension=%1 को %2 फ़ाइल एक्सटेंशन के साथ संबद्ध किया जा रहा है...
AutoStartProgramGroupDescription=स्टार्टअप:
AutoStartProgram=%1 को स्वचालित रूप से शुरू करें
AddonHostProgramNotFound=%1 आपके द्वारा चयनित फ़ोल्डर में नहीं मिल सका।%n%nक्या आप फिर भी जारी रखना चाहते हैं?
