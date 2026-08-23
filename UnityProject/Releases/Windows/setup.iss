; Inno Setup Script
; 编译: ISCC.exe setup.iss 或用 Inno Setup Compiler GUI 打开编译
#define MyAppName "[软件名称]"
#define MyAppVersion "[版本号]"
#define MyAppPublisher "[发布者]"
#define MyAppExeName "[程序启动文件名].exe"
#define MyAppId "[应用ID]"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={code:GetDefaultDir}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=no
OutputDir=Output
OutputBaseFilename=Setup_{#MyAppName}_{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
DiskSpanning=yes
DiskSliceSize=2100000000
SlicesPerDisk=1
LZMANumBlockThreads=4
WizardStyle=modern
DisableWelcomePage=no
LicenseFile=License.lic
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=icon.ico
WizardImageFile=wizard.bmp, wizard_2x.bmp
PrivilegesRequired=lowest
ShowLanguageDialog=no
Password=[安装密码]
Encryption=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "开机自动启动本程序"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "icon.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; {app} 为专用安装目录(PrivilegesRequired=lowest 下创建于 D:\ 或系统盘根),
; 直接整目录删除,确保运行时生成的 Config/日志/缓存/崩溃dump 等一并清除
Type: filesandordirs; Name: "{app}"

[Messages]
WelcomeLabel2=这将安装 [软件名称] 到您的电脑中。%n%n请注意！您必须有本软件的授权硬件，以及正规授权！否则不可使用本软件。%n%n建议在继续之前关闭所有其他应用程序。%n%n点击“下一步”继续。

[Code]
var
  BrandLabel: TNewStaticText;

procedure InitializeWizard();
begin
  BrandLabel := TNewStaticText.Create(WizardForm);
  BrandLabel.Caption := '[发布者水印]';
  BrandLabel.Font.Color := clGrayText;
  BrandLabel.Parent := WizardForm;
  BrandLabel.Left := ScaleX(12);
  BrandLabel.Top := WizardForm.Bevel.Top + ScaleY(5);
end;

function GetDefaultDir(Param: string): string;
begin
  if DirExists('D:\') then
    Result := 'D:\{#MyAppPublisher}'
  else
    Result := ExpandConstant('{sd}\{#MyAppPublisher}');
end;

const
  UninstallKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1';

function GetOldUninstallExe(var ExePath: string): Boolean;
var
  S: string;
begin
  Result := False;
  if RegQueryStringValue(HKCU, UninstallKey, 'UninstallString', S) or
     RegQueryStringValue(HKLM64, UninstallKey, 'UninstallString', S) then begin
    StringChangeEx(S, '"', '', True);
    S := Trim(S);
    if FileExists(S) then begin
      ExePath := S;
      Result := True;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): string;
var
  OldUninstall: string;
  ExitCode: Integer;
  I: Integer;
begin
  Result := '';
  if GetOldUninstallExe(OldUninstall) then begin
    if MsgBox('检测到已安装旧版本，将先卸载旧版本，是否继续？', mbConfirmation, MB_YESNO) = IDYES then begin
      if not Exec(OldUninstall, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_SHOW, ewWaitUntilTerminated, ExitCode) then begin
        Result := '旧版本卸载程序启动失败，请手动卸载后重试。';
        exit;
      end;
      { 卸载器会复制自身到临时目录再执行，原进程可能提前退出，需轮询确认卸载真正完成 }
      for I := 0 to 59 do begin
        if not RegKeyExists(HKCU, UninstallKey) and not RegKeyExists(HKLM64, UninstallKey) then
          break;
        Sleep(1000);
      end;
      { 旧版本(尤其更早的安装包)可能未配置完整清理,残留 app 下文件;}
       { 新版本安装前兜底删除整个安装目录,确保干净落地 }
      if DirExists(ExpandConstant('{app}')) then
        DelTree(ExpandConstant('{app}'), True, True, True);
    end else
      Result := '您取消了旧版本卸载，安装程序将退出。';
  end;
end;
