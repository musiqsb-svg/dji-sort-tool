using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("DJI정리툴")]
[assembly: AssemblyProduct("DJI정리툴")]
[assembly: AssemblyDescription("DJI 카메라·드론 영상을 촬영일별 폴더로 정리 (DJI와 무관한 개인 제작)")]
[assembly: AssemblyCompany("이중필름")]
[assembly: AssemblyCopyright("Copyright © 2026 이중필름")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]
[assembly: AssemblyInformationalVersion("1.2")]

namespace DJISortTool
{
    static class Program
    {
        public const string Name    = "DJI정리툴";
        public const string Version = "1.2";
        public static string Title { get { return Name + " " + Version; } }

        // 폴더 경로를 인자로 받으면 바로 미리보기 (exe 아이콘에 폴더를 끌어다 놓거나 우클릭 메뉴에서 실행)
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }

        static void ShowError(Exception ex)
        {
            MessageBox.Show("예상하지 못한 오류가 발생했습니다.\n\n" + (ex != null ? ex.Message : ""),
                            Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
