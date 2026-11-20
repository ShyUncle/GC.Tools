; DynamicWallpaper 安装包脚本（Inno Setup 6）
; 使用方法：安装 Inno Setup（https://jrsoftware.org/isinfo.php）后，
; 用 Inno Setup Compiler 打开本文件点击 Compile，即可生成 setup 安装程序。
; 发布前先执行（在项目目录下）：
;   dotnet publish -c Release -r win-x64 --self-contained true ^
;     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
;     -p:EnableCompressionInSingleFile=true -o publish/self-contained

#define MyAppName "动态壁纸"
#define MyAppVersion "1.0.0"
#define MyAppExeName "DynamicWallpaper.exe"

[Setup]
AppId={{8E7F4C2A-9B1D-4A3E-B6F5-2C8D0A1E5F7B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\DynamicWallpaper
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=installer\output
OutputBaseFilename=DynamicWallpaper-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
; 普通权限安装（不写注册表系统区，用户级卸载干净）
PrivilegesRequired=lowest

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
; 自包含单文件发布：只带一个 exe，目标机器无需安装 .NET
Source: "publish\self-contained\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 卸载时清理运行时产生的用户数据（壁纸库与配置保留在用户目录，若需保留请删除以下两行）
Type: filesandordirs; Name: "{userappdata}\DynamicWallpaper\OnlineCache"
Type: filesandordirs; Name: "{userappdata}\DynamicWallpaper\WebView2"
