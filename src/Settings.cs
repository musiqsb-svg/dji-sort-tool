using System;
using System.IO;

namespace DJISortTool
{
    // %LOCALAPPDATA%\DJISortTool\settings.ini  (설치형·설치 없이 실행 모두 같은 위치)
    class Settings
    {
        public bool TrashMisc = true;   // 정리 후 카드 관리 파일(MISC)을 휴지통으로
        public bool AddSize   = true;   // 날짜 폴더 이름 끝에 용량 (260823_DATA_61.2G)

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DJISortTool", "settings.ini"); }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try {
                foreach (var line in File.ReadAllLines(FilePath)) {
                    var p = line.Split(new[] { '=' }, 2);
                    if (p.Length != 2) continue;
                    if (p[0].Trim() == "TrashMisc") s.TrashMisc = p[1].Trim() == "1";
                    if (p[0].Trim() == "AddSize")   s.AddSize   = p[1].Trim() == "1";
                }
            } catch (Exception) { }
            return s;
        }

        public void Save()
        {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, "TrashMisc=" + (TrashMisc ? "1" : "0") + "\r\n" +
                                            "AddSize="   + (AddSize   ? "1" : "0") + "\r\n");
            } catch (Exception) { }
        }
    }
}
