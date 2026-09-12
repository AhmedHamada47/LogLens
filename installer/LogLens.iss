; LogLens Inno Setup Script
; Download Inno Setup from https://jrsoftware.org/isdl.php

[Setup]
AppName=LogLens
AppVersion=1.0.0
AppPublisher=LogLens
AppPublisherURL=https://github.com/LogLens
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
; Uncomment the line below when you have an icon file
; SetupIconFile=..\LogLens.ico

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
