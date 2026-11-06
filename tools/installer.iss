#ifndef Dist
  #error Pass /DDist=<export folder>
#endif
#ifndef Version
  #define Version "0.1.0"
#endif
#ifndef VersionName
  #define VersionName Version
#endif
#ifndef AppGuid
  #define AppGuid "{{6F2B3E54-8C1D-4A7E-9B0F-2D5C8E3A1F47}"
#endif
#ifndef OutName
  #define OutName "KriegsspielTable-setup"
#endif

[Setup]
AppId={#AppGuid}
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
Type: filesandordirs; Name: "{app}\backdrops"
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
Filename: "{app}\KriegsspielTable.exe"; Flags: nowait; Check: RelaunchAsked

[Code]
const
  TriesWhileHeld = 40;
  WaitBetweenTries = 500;

function RelaunchAsked: Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;

function FileIsHeld(const Path: String): Boolean;
var
  Open: TFileStream;
begin
  try
    Open := TFileStream.Create(Path, fmOpenRead or fmShareExclusive);
    Open.Free;
    Result := False;
  except
    Result := True;
  end;
end;

function HeldUnder(const Path: String): String;
var
  Found: TFindRec;
  Child: String;
begin
  Result := '';
  if FileExists(Path) then
  begin
    if FileIsHeld(Path) then Result := Path;
    Exit;
  end;
  if not DirExists(Path) then Exit;
  if not FindFirst(AddBackslash(Path) + '*', Found) then Exit;
  try
    repeat
      if (Found.Name <> '.') and (Found.Name <> '..') then
      begin
        Child := AddBackslash(Path) + Found.Name;
        if Found.Attributes and FILE_ATTRIBUTE_DIRECTORY <> 0 then Result := HeldUnder(Child)
        else if FileIsHeld(Child) then Result := Child;
      end;
    until (Result <> '') or not FindNext(Found);
  finally
    FindClose(Found);
  end;
end;

function HeldInTheFolder: String;
var
  Folders: TArrayOfString;
  Found: TFindRec;
  App: String;
  I: Integer;
begin
  Result := '';
  App := AddBackslash(ExpandConstant('{app}'));
  SetArrayLength(Folders, 5);
  Folders[0] := 'converter';
  Folders[1] := 'standard';
  Folders[2] := 'backdrops';
  Folders[3] := 'docs';
  Folders[4] := 'native';
  for I := 0 to GetArrayLength(Folders) - 1 do
  begin
    Result := HeldUnder(App + Folders[I]);
    if Result <> '' then Exit;
  end;
  if FindFirst(App + 'data_*', Found) then
  try
    repeat
      Result := HeldUnder(App + Found.Name);
    until (Result <> '') or not FindNext(Found);
  finally
    FindClose(Found);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Held: String;
  I: Integer;
begin
  Held := HeldInTheFolder;
  for I := 1 to TriesWhileHeld do
  begin
    if Held = '' then Break;
    Sleep(WaitBetweenTries);
    Held := HeldInTheFolder;
  end;
  if Held = '' then Result := ''
  else Result := 'Another program is using ' + Held + '.'
    + #13#10 + 'Close it and run this installer again. Nothing has been changed, and the version you have still works.';
end;
