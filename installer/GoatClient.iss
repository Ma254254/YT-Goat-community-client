; ------------------------------------------------------------------
;  GOAT CLIENT – Windows installer (Inno Setup 6)
;  Built by GitHub Actions:  iscc /DAppVersion=x.y.z /DSourceDir=..\publish installer\GoatClient.iss
; ------------------------------------------------------------------

#define AppName "GOAT CLIENT"
#define AppExeName "GoatClient.exe"
#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
; Fixed AppId: lets Setup detect and update an existing installation. Never change it.
AppId={{8F3C2A71-5D4E-4B6A-9C1F-2E7D3A9B6C40}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=GOAT CLIENT
AppPublisherURL=https://discord.gg/N8mPvMTPhz
AppSupportURL=https://discord.gg/N8mPvMTPhz
DefaultDirName={autopf}\GoatClient
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=GoatClient-Setup
SetupIconFile=..\GoatClient\Assets\Logo\GoatClient.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=assets\wizard-large-100.bmp,assets\wizard-large-150.bmp,assets\wizard-large-200.bmp
WizardSmallImageFile=assets\wizard-small-100.bmp,assets\wizard-small-150.bmp,assets\wizard-small-200.bmp
WizardImageStretch=no
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
; The launcher holds this mutex while running (see App.xaml.cs).
AppMutex=Local\GoatClient.Launcher
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// User data (Minecraft installations, instances/worlds, settings, logs) lives in %APPDATA%\GoatClient.
// It is only removed if the user explicitly agrees – the default answer is "No".
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
  ResultCode: Integer;
  I: Integer;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\GoatClient');
    if DirExists(DataDir) and not UninstallSilent then
    begin
      if MsgBox('Do you also want to delete all GOAT CLIENT user data?' + #13#10#13#10 +
                DataDir + #13#10#13#10 +
                'This includes installed Minecraft versions, your instances and WORLDS, settings and logs. ' +
                'This cannot be undone.' + #13#10#13#10 +
                'Choose "No" to keep your data.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
        // Remove the stored Microsoft sign-in from the Windows Credential Manager.
        Exec(ExpandConstant('{sys}\cmdkey.exe'), '/delete:GoatClient/msa-refresh-token/count', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
        for I := 0 to 15 do
          Exec(ExpandConstant('{sys}\cmdkey.exe'), '/delete:GoatClient/msa-refresh-token/' + IntToStr(I), '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      end;
    end;
  end;
end;
