# DJI정리툴 빌드: Windows에 기본으로 들어 있는 C# 컴파일러(.NET Framework 4.x)만 사용. 따로 설치할 것 없음
#   dist\DJISortTool.exe          프로그램
#   build\OrganizerTest.exe       정리 로직 테스트 (-Test 를 붙이면 빌드 후 실행)
#   -Release : 테스트 후 배포 파일까지 만듦 (버전은 src\Program.cs 의 Version)
#     release\DJISortTool-<버전>-Portable.zip   설치 없이 실행
#     release\DJISortTool-<버전>-Setup.exe      설치 프로그램 (Inno Setup 6 필요)
param([switch]$Test, [switch]$Release)
$ErrorActionPreference = 'Stop'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
Set-Location $PSScriptRoot
[Environment]::CurrentDirectory = $PSScriptRoot   # .NET 메서드의 상대 경로도 이 폴더 기준으로

if (-not (Test-Path 'assets\app.ico')) { & powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'assets\make_icon.ps1' }
$null = New-Item -ItemType Directory -Force 'dist', 'build'

$common = '/nologo', '/codepage:65001', '/optimize+', '/platform:anycpu', '/warn:4'
& $csc @common /target:winexe /out:dist\DJISortTool.exe /win32icon:assets\app.ico /win32manifest:src\app.manifest `
       /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
if ($LASTEXITCODE) { throw 'DJISortTool.exe 빌드 실패' }

& $csc @common /target:exe /out:build\OrganizerTest.exe tests\OrganizerTest.cs src\Organizer.cs
if ($LASTEXITCODE) { throw 'OrganizerTest.exe 빌드 실패' }

$exe = Get-Item 'dist\DJISortTool.exe'
"빌드 완료: $($exe.FullName)  ($([math]::Round($exe.Length / 1KB)) KB)"
if ($Test -or $Release) { & 'build\OrganizerTest.exe'; if ($LASTEXITCODE) { throw '테스트 실패' } }
if (-not $Release) { return }

# ── 배포 파일 ─────────────────────────────────────────────
$version = [regex]::Match([IO.File]::ReadAllText('src\Program.cs'), 'Version\s*=\s*"([^"]+)"').Groups[1].Value
if (-not $version) { throw 'src\Program.cs 에서 버전을 찾지 못함' }
$null = New-Item -ItemType Directory -Force 'release'

# 설치 없이 실행: 압축을 풀면 DJISortTool 폴더 하나 (파일명은 영문이라 어떤 압축 프로그램에서도 깨지지 않음)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = "release\DJISortTool-$version-Portable.zip"
if (Test-Path $zip) { [IO.File]::Delete((Resolve-Path $zip)) }
$za = [IO.Compression.ZipFile]::Open((Join-Path $PSScriptRoot $zip), 'Create')
try {
    $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $exe.FullName, 'DJISortTool/DJISortTool.exe', 'Optimal')
    $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, (Resolve-Path 'LICENSE'), 'DJISortTool/LICENSE.txt', 'Optimal')
} finally { $za.Dispose() }
"배포: $zip"

# 설치 프로그램
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe" |
        Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { Write-Warning 'Inno Setup 6이 없어 설치 프로그램은 건너뜀 (winget install JRSoftware.InnoSetup)'; return }
# Dropbox·백신이 막 만든 exe를 잡고 있으면 아이콘을 넣다 실패하므로 임시 폴더에서 만든 뒤 복사
$tmpOut = Join-Path $env:TEMP 'DJISortTool-setup'
& $iscc /Q "/DAppVersion=$version" "/O$tmpOut" 'installer\DJISortTool.iss'
if ($LASTEXITCODE) { throw '설치 프로그램 빌드 실패' }
Copy-Item (Join-Path $tmpOut "DJISortTool-$version-Setup.exe") 'release' -Force
[IO.Directory]::Delete($tmpOut, $true)
"배포: release\DJISortTool-$version-Setup.exe"

# 배포 글에 올릴 파일 지문 (받은 사람이 같은 파일인지 확인용)
Get-ChildItem 'release' -File | Where-Object { $_.Name -like "DJISortTool-$version-*" } | ForEach-Object {
    '{0}  {1}  ({2:N0} KB)' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Name, ($_.Length / 1KB)
} | Tee-Object 'release\SHA256.txt'
