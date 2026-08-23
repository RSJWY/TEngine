; Inno Setup Script
; ------------------------------------------------------------
; 模板说明：
;   1. 顶部 #define 均为可直接编译的合理默认值；填好即可用 ISCC.exe setup.iss 直接编译。
;   2. 打包工具(InnoSetupBuilder)在构建时会按窗口参数回写以下 #define 值（保持双引号格式）：
;        MyAppName / MyAppVersion / MyAppPublisher / MyAppExeName / MyAppId / MyAppPassword / BrandWatermark
;      回写基于"键名+双引号值"的正则匹配，故这些 #define 的值必须始终用双引号包裹。
;   3. LicenseFile 为用户协议文件，固定为 License.lic，不参与传参。
; ------------------------------------------------------------
; 编译: ISCC.exe setup.iss 或用 Inno Setup Compiler GUI 打开编译
#define MyAppName "我的软件"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "我的公司"
#define MyAppExeName "hotUnitydemo.exe"
#define MyAppId "MyAppId"
; 安装密码：留空字符串表示无密码(Encryption 仍保留，空密码等同于普通加密占位)
#define MyAppPassword "11"
; 安装向导左下角发布者水印文字
#define BrandWatermark "我的公司"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={code:GetDefaultDir}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=no
OutputDir=setup
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
; 安装密码为空时不加密；窗口回写 MyAppPassword 为非空字符串后自动启用加密
#if MyAppPassword != ""
Password={#MyAppPassword}
Encryption=yes
#endif

[Languages]
; 使用与 setup.iss 同目录下的本地简体中文语言文件,避免依赖 ISCC 安装目录的 Languages
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "开机自动启动本程序"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "icon.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "build\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

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
WelcomeLabel2=这将安装 {#MyAppName} 到您的电脑中。%n%n请注意！您必须有本软件的授权硬件，以及正规授权！否则不可使用本软件。%n%n建议在继续之前关闭所有其他应用程序。%n%n点击“下一步”继续。

[Code]
var
  BrandLabel: TNewStaticText;

procedure InitializeWizard();
begin
  BrandLabel := TNewStaticText.Create(WizardForm);
  BrandLabel.Caption := '{#BrandWatermark}';
  BrandLabel.Font.Color := clGrayText;
  BrandLabel.Parent := WizardForm;
  BrandLabel.Left := ScaleX(12);
  BrandLabel.Top := WizardForm.Bevel.Top + ScaleY(5);
end;

function GetDefaultDir(Param: string): string;
begin
  { 默认安装目录用软件名而非发布者名;发布者仅用于向导显示 }
  if DirExists('D:\') then
    Result := 'D:\{#MyAppName}'
  else
    Result := ExpandConstant('{sd}\{#MyAppName}');
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
