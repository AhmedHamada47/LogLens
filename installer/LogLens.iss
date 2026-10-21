; LogLens Inno Setup Script
; Download Inno Setup from https://jrsoftware.org/isdl.php
; Build: ISCC.exe installer\LogLens.iss  (optionally /DAppVersion=1.2.3)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{CA161C4B-32EB-403E-8A23-11853DA2BB3F}
AppName=LogLens
AppVersion={#AppVersion}
AppPublisher=LogLens
AppPublisherURL=https://github.com/AhmedHamada47/LogLens
DefaultDirName={autopf}\LogLens
DefaultGroupName=LogLens
OutputDir=..\installer\output
OutputBaseFilename=LogLens-Setup
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=LogLens
WizardStyle=modern
SetupIconFile=..\LogLens.ico
UninstallDisplayIcon={app}\LogLens.exe
; Ask the user to close a running LogLens before installing/uninstalling
AppMutex=LogLens_SingleInstance

[Tasks]
Name: "desktopicon"; Description: "Create a Desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startmenuicon"; Description: "Create a Start Menu shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
Source: "..\bin\Release\net9.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\LogLens"; Filename: "{app}\LogLens.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall LogLens"; Filename: "{uninstallexe}"
Name: "{autodesktop}\LogLens"; Filename: "{app}\LogLens.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\LogLens.exe"; Description: "Launch LogLens"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: dirifempty; Name: "{app}"
