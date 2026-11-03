#ifndef Dist
  #error Pass /DDist=<export folder>
#endif
#ifndef Version
  #define Version "0.1.0"
#endif
#ifndef VersionName
  #define VersionName Version
#endif
#ifndef OutName
  #define OutName "KriegsspielTable-setup"
#endif

[Setup]
AppId={{6F2B3E54-8C1D-4A7E-9B0F-2D5C8E3A1F47}
AppName=Kriegsspiel Table
AppVersion={#VersionName}
VersionInfoVersion={#Version}
AppPublisher=Bruno Firme
AppPublisherURL=https://github.com/BrunoFirme/kriegsspiel-table
DefaultDirName={autopf}\Kriegsspiel Table
DefaultGroupName=Kriegsspiel Table
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile={#Dist}\LICENSE.txt
SetupIconFile={#Dist}\icon.ico
UninstallDisplayIcon={app}\icon.ico
OutputDir={#Dist}\..
OutputBaseFilename={#OutName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Tasks]
Name: desktopicon; Description: "Put a shortcut on the desktop"

[InstallDelete]
Type: filesandordirs; Name: "{app}\data_*"
Type: filesandordirs; Name: "{app}\standard"
Type: filesandordirs; Name: "{app}\docs"
Type: filesandordirs; Name: "{app}\converter"
Type: filesandordirs; Name: "{app}\native"

[Files]
Source: "{#Dist}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Kriegsspiel Table"; Filename: "{app}\KriegsspielTable.exe"; IconFilename: "{app}\icon.ico"
Name: "{autodesktop}\Kriegsspiel Table"; Filename: "{app}\KriegsspielTable.exe"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\KriegsspielTable.exe"; Description: "Open Kriegsspiel Table"; Flags: nowait postinstall skipifsilent
