; DJI정리툴 설치 프로그램 (Inno Setup 6)
;  - 관리자 권한 없이 현재 사용자에게 설치:  %LOCALAPPDATA%\Programs\DJISortTool
;  - 시작 메뉴 바로가기(폴더 없이 바로), 바탕화면 아이콘(선택), 폴더 우클릭 메뉴(선택)
;  - 제거하면 설정·기록(%LOCALAPPDATA%\DJISortTool)까지 지움
;  build.ps1 -Release 가 버전을 넘겨 줌 (직접 컴파일하면 아래 기본값)

#ifndef AppVersion
  #define AppVersion "1.2"
#endif

[Setup]
AppId={{38358392-BC3B-47AA-8BA8-E612A69A7E7D}
AppName=DJI정리툴
AppVersion={#AppVersion}
AppVerName=DJI정리툴 {#AppVersion}
AppPublisher=이중필름
AppCopyright=Copyright © 2026 이중필름
VersionInfoVersion={#AppVersion}.0.0
VersionInfoDescription=DJI정리툴 설치 프로그램
DefaultDirName={autopf}\DJISortTool
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\release
OutputBaseFilename=DJISortTool-{#AppVersion}-Setup
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\DJISortTool.exe
UninstallDisplayName=DJI정리툴
WizardStyle=modern
Compression=lzma2/ultra
SolidCompression=yes
ShowLanguageDialog=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 아이콘 만들기"; GroupDescription: "추가 아이콘:"
Name: "contextmenu"; Description: "폴더 우클릭 메뉴에 'DJI정리툴로 정리' 추가"; GroupDescription: "탐색기:"

; 폴더 우클릭 · 폴더 안 빈 곳 우클릭 → 그 폴더로 바로 미리보기 (현재 사용자만, 제거하면 함께 삭제)
; Windows 11에서는 '더 많은 옵션 표시' 안에 나타남 (새 메뉴에 바로 넣으려면 코드 서명된 패키지가 필요)
[Registry]
Root: HKCU; Subkey: "Software\Classes\Directory\shell\DJISortTool"; ValueType: string; ValueName: "MUIVerb"; ValueData: "DJI정리툴로 정리"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\DJISortTool"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\DJISortTool.exe"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\DJISortTool"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Single"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\DJISortTool\command"; ValueType: string; ValueName: ""; ValueData: """{app}\DJISortTool.exe"" ""%1"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\DJISortTool"; ValueType: string; ValueName: "MUIVerb"; ValueData: "DJI정리툴로 정리"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\DJISortTool"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\DJISortTool.exe"",0"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\DJISortTool\command"; ValueType: string; ValueName: ""; ValueData: """{app}\DJISortTool.exe"" ""%V"""; Tasks: contextmenu

[Files]
Source: "..\dist\DJISortTool.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\DJI정리툴"; Filename: "{app}\DJISortTool.exe"
Name: "{autodesktop}\DJI정리툴"; Filename: "{app}\DJISortTool.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DJISortTool.exe"; Description: "DJI정리툴 실행"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\DJISortTool"
