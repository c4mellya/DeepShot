#define AppName "Zeep Cut"
#ifndef AppVersion
  #define AppVersion "0.1.3"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif

[Setup]
AppId={{6CEB8212-F8EF-436A-8B11-778EE1545D70}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Zeep Cut
DefaultDirName={localappdata}\Programs\Zeep Cut
DefaultGroupName=Zeep Cut
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
OutputDir=..\artifacts\release
OutputBaseFilename=ZeepCut-{#AppVersion}-Setup-x64
SetupIconFile=..\src\ZeepCut\Assets\app.ico
UninstallDisplayIcon={app}\ZeepCut.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\ZeepCut.SingleInstance

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Zeep Cut"; Filename: "{app}\ZeepCut.exe"
Name: "{autodesktop}\Zeep Cut"; Filename: "{app}\ZeepCut.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "ZeepCut"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\ZeepCut.exe"; Description: "Start Zeep Cut"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if not UninstallSilent then
      if MsgBox('Also delete your Zeep Cut history, favorites, and settings? This cannot be undone. Choose No to keep your data.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(ExpandConstant('{localappdata}\ZeepCut'), True, True, True);
end;
