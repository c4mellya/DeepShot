#define AppName "DeepShot"
#ifndef AppVersion
  #define AppVersion "0.2.1"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\DeepShot"
#endif

[Setup]
AppId={{6CEB8212-F8EF-436A-8B11-778EE1545D70}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=DeepShot
DefaultDirName={localappdata}\Programs\DeepShot
DefaultGroupName=DeepShot
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19045
OutputDir=..\artifacts\release
OutputBaseFilename=DeepShot-{#AppVersion}-Setup-x64
SetupIconFile=..\src\DeepShot\Assets\app.ico
UninstallDisplayIcon={app}\DeepShot.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
AppMutex=Local\DeepShot.SingleInstance

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DeepShot"; Filename: "{app}\DeepShot.exe"
Name: "{autodesktop}\DeepShot"; Filename: "{app}\DeepShot.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "DeepShot"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\DeepShot.exe"; Description: "Start DeepShot"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if not UninstallSilent then
      if MsgBox('Also delete your DeepShot history, favorites, and settings? This cannot be undone. Choose No to keep your data.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(ExpandConstant('{localappdata}\DeepShot'), True, True, True);
end;
