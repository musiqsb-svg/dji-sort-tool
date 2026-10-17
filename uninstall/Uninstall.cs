// DJI정리툴 제거 (배포 파일과 함께 주는 제거 프로그램)
//  설치형(Setup.exe)으로 설치한 DJI정리툴을 찾아 Windows 정식 제거 프로그램(설치 폴더의 unins000.exe)을 실행
//  = [설정 › 앱]에서 제거하는 것과 같음. 설정·기록(%LOCALAPPDATA%\DJISortTool)도 함께 지워짐
//  설치 프로그램의 AppId와 같은 값을 씀 (installer\DJISortTool.iss)
using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("DJI정리툴 제거")]
[assembly: AssemblyProduct("DJI정리툴")]
[assembly: AssemblyDescription("설치한 DJI정리툴을 제거 (DJI와 무관한 개인 제작)")]
[assembly: AssemblyCompany("이중필름")]
[assembly: AssemblyCopyright("Copyright © 2026 이중필름")]

namespace DJISortTool
{
    static class Uninstall
    {
        const string Title = "DJI정리툴 제거";
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{38358392-BC3B-47AA-8BA8-E612A69A7E7D}_is1";

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            string cmd = UninstallString(Registry.CurrentUser) ?? UninstallString(Registry.LocalMachine);
            if (cmd == null) {
                MessageBox.Show("이 PC에는 DJI정리툴이 설치되어 있지 않습니다.\n\n설치 없이 쓰는 버전(Portable)은 압축을 푼 DJISortTool 폴더를 지우면 됩니다.",
                                Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // 실행 중이면 프로그램 파일을 지우지 못하므로 먼저 닫게 함
            while (Process.GetProcessesByName("DJISortTool").Length > 0) {
                if (MessageBox.Show("DJI정리툴이 실행 중입니다. 창을 닫은 뒤 [다시 시도]를 눌러 주세요.", Title,
                                    MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning) != DialogResult.Retry) return;
            }
            // "C:\...\unins000.exe" → 제거 여부는 제거 프로그램이 직접 물어봄
            string exe = cmd.Trim(), args = "";
            if (exe.StartsWith("\"")) {
                int end = exe.IndexOf('"', 1);
                if (end > 0) { args = exe.Substring(end + 1).Trim(); exe = exe.Substring(1, end - 1); }
            }
            try { Process.Start(exe, args); }
            catch (Exception ex) {
                MessageBox.Show("제거 프로그램을 실행하지 못했습니다.\n[설정 › 앱 › 설치된 앱]에서 'DJI정리툴'을 제거해 주세요.\n\n" + ex.Message,
                                Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static string UninstallString(RegistryKey hive)
        {
            try {
                using (var k = hive.OpenSubKey(Key)) return k == null ? null : k.GetValue("UninstallString") as string;
            } catch (Exception) { return null; }
        }
    }
}
