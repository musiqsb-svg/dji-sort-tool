// 정리 로직 (화면과 분리: 테스트 프로그램도 이 파일을 그대로 씀)
//
//  넣은 폴더\260823_DATA\260823_Footage        원본 영상  DJI_20260823135545_0039_D.MP4
//                       \260823_Proxy          프록시     .LRF → DJI_20260823135545_0039_D_Proxy.MP4
//                       \260823_Audio          오디오     .AAC
//                       \260823_Photo\jpg, dng 사진
//
//  - 촬영일: 파일명의 녹화 시작 시각(DJI_20260823135545_…). 복사해도 바뀌지 않음.
//    시각이 없는 옛 이름(DJI_0001)만 수정한 날짜를 씀
//  - LRF·AAC는 같은 폴더·같은 이름의 원본과 같은 날로
//  - 파일은 지우지 않고 넣은 폴더 안에서 옮기기만 함 → 되돌리기 가능
//  - 같은 이름이 이미 있으면 덮어쓰지 않고 건너뜀
//  - (옵션) 정리 후 날짜 폴더 이름 끝에 용량:  260823_DATA_61.2G
//    같은 날짜 폴더가 이미 있으면 거기에 합치고 용량을 갱신
//  - 정리 후 옮긴 파일이 모두 제자리에 같은 크기로 있는지 확인
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DJISortTool
{
    enum Kind { Footage, Proxy, Audio, Jpg, Dng }

    class PlanItem
    {
        public string Source;
        public string Dest;    // 옮길 위치 (용량을 붙이기 전 폴더 이름 기준)
        public string Final;   // 정리가 끝난 뒤 실제 위치 (용량을 붙였으면 바뀐 폴더 이름 기준)
        public string Day;     // yyMMdd
        public Kind Kind;
        public long Size;
    }

    // 날짜 폴더 하나 (yymmdd_DATA 또는 이미 용량을 붙인 yymmdd_DATA_61.2G)
    class DayFolder
    {
        public string Day;
        public string Name;            // 지금 이름. 없으면 새로 만들 yymmdd_DATA
        public bool Existed;           // 정리 전부터 있던 폴더인지 (이번 파일을 합침)
        public long ExistingBytes;     // 정리 전부터 있던 용량
        public long PlannedBytes;      // 이번에 옮겨 넣을 용량 (같은 이름이라 건너뛸 파일 제외)
    }

    class ScanResult
    {
        public string Root;
        public string Error;                                  // 정리할 수 없는 폴더면 그 이유
        public List<PlanItem> Items = new List<PlanItem>();
        public Dictionary<string, DayFolder> Days = new Dictionary<string, DayFolder>();
        public List<string> NoProxy = new List<string>();     // LRF가 없는 원본 (알림용 번호)
        public List<string> Unhandled = new List<string>();   // 정리 규칙이 없는 DJI 파일 (그대로 둠)
        public int Conflicts;                                 // 같은 이름이 이미 있어 건너뛸 파일
    }

    class RunResult
    {
        public string Root;
        public List<PlanItem> Moved = new List<PlanItem>();
        public int Skipped, Failed;
        public List<string> Trashed = new List<string>();    // 휴지통으로 보낸 카드 관리 폴더
        public List<string> Kept = new List<string>();       // 다른 파일이 섞여 있어 남겨둔 관리 폴더
        public Dictionary<string, string> FinalNames = new Dictionary<string, string>();   // 날짜 → 최종 폴더 이름
        public List<KeyValuePair<string, string>> Renamed = new List<KeyValuePair<string, string>>();   // 바꾼 폴더 경로 (전, 후)
        public List<string> RenamedExisting = new List<string>();   // 정리 전부터 있던 폴더의 이름이 바뀐 것 ("전 → 후")
        public int RenameFailed;
        public int Verified, VerifyFailed;                   // 정리 후 확인: 제자리에 같은 크기로 있는 파일 / 없거나 크기가 다른 파일
        public long VerifiedBytes, MovedBytes;
    }

    class UndoResult
    {
        public int Restored, Failed;
    }

    static class Organizer
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly Regex NameTime   = new Regex(@"^DJI_(\d{14})", RegexOptions.IgnoreCase);
        static readonly Regex NameNumber = new Regex(@"^DJI_\d{14}_(\d{4})", RegexOptions.IgnoreCase);
        static readonly Regex DataDir    = new Regex(@"^(\d{6})_DATA(_\d+(\.\d+)?[MGT])?$", RegexOptions.IgnoreCase);

        static readonly Dictionary<string, Kind> Kinds = new Dictionary<string, Kind>(StringComparer.OrdinalIgnoreCase) {
            { ".mp4", Kind.Footage }, { ".mov", Kind.Footage }, { ".lrf", Kind.Proxy }, { ".aac", Kind.Audio },
            { ".jpg", Kind.Jpg }, { ".jpeg", Kind.Jpg }, { ".dng", Kind.Dng } };

        // 카메라가 자기 화면에 보여주려고 만드는 썸네일 (정리 대상 아님)
        static readonly HashSet<string> ThumbExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".thm", ".scr" };

        // MISC(카드 관리 폴더)가 이 파일들로만 되어 있을 때만 휴지통으로 보냄
        static readonly HashSet<string> MiscExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".thm", ".scr", ".db", ".device", "" };

        public static string SubFolder(Kind k)
        {
            switch (k) {
                case Kind.Footage: return "Footage";
                case Kind.Proxy:   return "Proxy";
                case Kind.Audio:   return "Audio";
                case Kind.Jpg:     return @"Photo\jpg";
                default:           return @"Photo\dng";
            }
        }

        // ── 미리보기 ──────────────────────────────────────────────
        public static ScanResult Scan(string path)
        {
            var r = new ScanResult();
            DirectoryInfo dir;
            try { dir = new DirectoryInfo(path.Trim().Trim('"')); }
            catch (Exception) { r.Error = "폴더 경로를 읽을 수 없습니다."; return r; }
            if (!dir.Exists) { r.Error = "폴더를 찾을 수 없습니다."; return r; }

            r.Error = CheckLocation(dir);
            if (r.Error != null) return r;
            r.Root = dir.FullName.TrimEnd('\\');

            var dji = Walk(dir).Where(f => f.Name.StartsWith("DJI_", StringComparison.OrdinalIgnoreCase)).ToList();
            if (dji.Count == 0) {
                r.Error = "DJI 파일(DJI_로 시작하는 파일)이 없습니다.\nDJI 카드에서 복사한 폴더인지 확인해 주세요. 이미 정리한 폴더일 수도 있습니다.";
                return r;
            }

            var known = new List<FileInfo>();
            foreach (var f in dji) {
                if (Kinds.ContainsKey(f.Extension)) known.Add(f);
                else if (!ThumbExt.Contains(f.Extension)) r.Unhandled.Add(f.FullName);
            }

            // 원본 촬영일. 같은 폴더·같은 이름의 LRF·AAC가 따라감
            var days = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in known) if (Kinds[f.Extension] == Kind.Footage) days[Key(f)] = ShotDay(f);

            // 이미 있는 날짜 폴더 (전에 정리한 결과). 같은 날짜면 거기에 합침
            var existing = new Dictionary<string, string>();
            foreach (var d in dir.GetDirectories().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) {
                var m = DataDir.Match(d.Name);
                if (m.Success && !existing.ContainsKey(m.Groups[1].Value)) existing[m.Groups[1].Value] = d.Name;
            }

            var lrf   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in known) {
                Kind k = Kinds[f.Extension];
                string day;
                if (!days.TryGetValue(Key(f), out day)) day = ShotDay(f);
                DayFolder df;
                if (!r.Days.TryGetValue(day, out df)) {
                    string name0;
                    df = existing.TryGetValue(day, out name0)
                        ? new DayFolder { Day = day, Name = name0, Existed = true, ExistingBytes = FolderBytes(Path.Combine(r.Root, name0)) }
                        : new DayFolder { Day = day, Name = day + "_DATA" };
                    r.Days[day] = df;
                }
                string name = k == Kind.Proxy ? Path.GetFileNameWithoutExtension(f.Name) + "_Proxy.MP4" : f.Name;
                string dest = Path.Combine(r.Root, df.Name, day + "_" + SubFolder(k), name);
                if (File.Exists(dest) || !dests.Add(dest)) r.Conflicts++;
                else df.PlannedBytes += f.Length;
                if (k == Kind.Proxy) lrf.Add(Key(f));
                r.Items.Add(new PlanItem { Source = f.FullName, Dest = dest, Day = day, Kind = k, Size = f.Length });
            }

            // LRF를 만드는 카드인데 LRF가 없는 원본 (슬로모션·타임랩스 등에서는 안 생기는 것으로 보임)
            if (lrf.Count > 0) {
                foreach (var f in known.Where(x => Kinds[x.Extension] == Kind.Footage && !lrf.Contains(Key(x))).OrderBy(x => x.Name)) {
                    var m = NameNumber.Match(f.Name);
                    r.NoProxy.Add(m.Success ? m.Groups[1].Value : Path.GetFileNameWithoutExtension(f.Name));
                }
            }
            if (r.Items.Count == 0) r.Error = "정리할 수 있는 영상·사진이 없습니다.";
            return r;
        }

        // SD카드에서 바로 정리하면 복사본 없이 카드 파일을 옮기게 되므로 막음:
        // 드라이브 맨 위, 또는 맨 위에 DCIM이 있는 드라이브의 DCIM 안
        static string CheckLocation(DirectoryInfo dir)
        {
            string root = dir.Root.FullName;
            if (string.Equals(dir.FullName.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return "드라이브(SD카드) 맨 위에서는 정리하지 않습니다.\n카드 내용을 PC 폴더로 복사한 뒤 그 폴더를 넣어 주세요.";
            string dcim = Path.Combine(root, "DCIM");
            if (Directory.Exists(dcim) && IsUnder(dir.FullName, dcim))
                return "SD카드 안의 폴더로 보입니다.\n카드 내용을 PC 폴더로 복사한 뒤 그 폴더를 넣어 주세요.";
            return null;
        }

        // 하위 폴더까지 파일 나열. 이미 정리한 결과(yymmdd_DATA)와 카드 관리 폴더(MISC)는 건너뜀
        static IEnumerable<FileInfo> Walk(DirectoryInfo root)
        {
            var stack = new Stack<DirectoryInfo>();
            stack.Push(root);
            while (stack.Count > 0) {
                var d = stack.Pop();
                FileInfo[] files; DirectoryInfo[] subs;
                try { files = d.GetFiles(); subs = d.GetDirectories(); }
                catch (Exception) { continue; }   // 권한 없는 폴더는 건너뜀
                foreach (var f in files) yield return f;
                foreach (var s in subs) {
                    if (DataDir.IsMatch(s.Name) || s.Name.Equals("MISC", StringComparison.OrdinalIgnoreCase)) continue;
                    stack.Push(s);
                }
            }
        }

        static string ShotDay(FileInfo f)
        {
            var m = NameTime.Match(f.Name);
            DateTime d;
            if (!(m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMddHHmmss", Inv, DateTimeStyles.None, out d)))
                d = f.LastWriteTime;
            return d.ToString("yyMMdd", Inv);
        }

        static string Key(FileInfo f) { return Path.Combine(f.DirectoryName, Path.GetFileNameWithoutExtension(f.Name)); }

        static bool IsUnder(string path, string parent)
        {
            return (path.TrimEnd('\\') + "\\").StartsWith(parent.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        // ── 정리 ──────────────────────────────────────────────────
        public static RunResult Run(ScanResult scan, bool trashMisc, bool addSize, IProgress<int> progress)
        {
            var r = new RunResult { Root = scan.Root };
            var emptied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            foreach (var it in scan.Items) {
                if (progress != null) progress.Report(++i);
                if (File.Exists(it.Dest)) { r.Skipped++; continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(it.Dest));
                if (TryMove(it.Source, it.Dest)) { r.Moved.Add(it); r.MovedBytes += it.Size; emptied.Add(Path.GetDirectoryName(it.Source)); }
                else r.Failed++;
            }

            if (trashMisc) {
                foreach (var misc in FindMisc(scan.Root)) {
                    if (!OnlyCardFiles(misc)) { r.Kept.Add(misc); continue; }
                    if (Recycler(misc)) { r.Trashed.Add(misc); emptied.Add(Path.GetDirectoryName(misc)); }
                    else r.Kept.Add(misc);
                }
            }

            // 파일을 옮겨서 비게 된 폴더만 정리 (원래 비어 있던 폴더는 건드리지 않음)
            foreach (var d in emptied.OrderByDescending(x => x.Length)) RemoveEmptyUp(d, scan.Root);

            // 날짜 폴더 이름 끝에 용량 (이번에 파일을 넣은 폴더만)
            foreach (var df in scan.Days.Values) {
                r.FinalNames[df.Day] = df.Name;
                if (!addSize || !r.Moved.Any(x => x.Day == df.Day)) continue;
                string from = Path.Combine(scan.Root, df.Name);
                string name = SizedName(df.Day, FolderBytes(from));
                if (name.Equals(df.Name, StringComparison.OrdinalIgnoreCase)) continue;
                string to = Path.Combine(scan.Root, name);
                if (Directory.Exists(to) || !TryRenameDir(from, to)) { r.RenameFailed++; continue; }
                r.Renamed.Add(new KeyValuePair<string, string>(from, to));
                r.FinalNames[df.Day] = name;
                if (df.Existed) r.RenamedExisting.Add(df.Name + " → " + name);
            }

            // 확인: 옮긴 파일이 모두 제자리에 같은 크기로 있는지
            foreach (var it in r.Moved) {
                string oldDir = Path.Combine(scan.Root, scan.Days[it.Day].Name);
                it.Final = Path.Combine(scan.Root, r.FinalNames[it.Day], it.Dest.Substring(oldDir.Length + 1));
                var fi = new FileInfo(it.Final);
                if (fi.Exists && fi.Length == it.Size) { r.Verified++; r.VerifiedBytes += it.Size; }
                else r.VerifyFailed++;
            }
            WriteLog(r);
            return r;
        }

        // 폴더 안 모든 파일 용량 합계 (내용은 읽지 않음)
        static long FolderBytes(string dir)
        {
            try { return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
            catch (Exception) { return 0; }
        }

        public static string SizedName(string day, long bytes) { return day + "_DATA_" + FormatSize(bytes); }

        // 미리보기에서 보여줄 최종 폴더 이름
        public static string PreviewName(DayFolder df, bool addSize)
        {
            return addSize ? SizedName(df.Day, df.ExistingBytes + df.PlannedBytes) : df.Name;
        }

        // 탐색기 속성 창과 같은 1024 기준, 소수점 1자리 내림:  2.19G → 2.1G, 1GB 미만은 850M, 1TB 이상은 T
        public static string FormatSize(long bytes)
        {
            const decimal MB = 1024m * 1024, GB = MB * 1024, TB = GB * 1024;
            if (bytes >= TB) return (Math.Floor(bytes * 10m / TB) / 10).ToString("0.#", Inv) + "T";
            if (bytes >= GB) return (Math.Floor(bytes * 10m / GB) / 10).ToString("0.#", Inv) + "G";
            return Math.Max(1m, Math.Floor(bytes / MB)).ToString("0", Inv) + "M";
        }

        // 탐색기 창·동기화 프로그램이 폴더를 잠깐 잡고 있는 경우가 많아 몇 번 재시도
        static bool TryRenameDir(string from, string to)
        {
            for (int i = 0; i < 5; i++) {
                try { Directory.Move(from, to); return true; }
                catch (Exception) { Thread.Sleep(700); }
            }
            return false;
        }

        // 탐색기 미리보기·동기화 프로그램이 잠깐 잡고 있는 경우가 있어 몇 번 재시도
        static bool TryMove(string from, string to)
        {
            for (int i = 0; i < 3; i++) {
                try { File.Move(from, to); return true; }
                catch (FileNotFoundException) { return false; }
                catch (DirectoryNotFoundException) { return false; }
                catch (Exception) { Thread.Sleep(500); }
            }
            return false;
        }

        // 카드 관리 폴더: 넣은 폴더 바로 안이나 그 안 DCIM 바로 안의 MISC (DCIM을 넣었으면 첫 번째가 DCIM\MISC)
        static List<string> FindMisc(string root)
        {
            return new[] { Path.Combine(root, "MISC"), Path.Combine(root, "DCIM", "MISC") }
                   .Where(Directory.Exists).ToList();
        }

        static bool OnlyCardFiles(string dir)
        {
            try {
                return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                                .All(f => MiscExt.Contains(Path.GetExtension(f)));
            } catch (Exception) { return false; }
        }

        static void RemoveEmptyUp(string dir, string root)
        {
            while (!string.IsNullOrEmpty(dir) && IsUnder(dir, root) && !dir.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) {
                try {
                    if (!Directory.Exists(dir)) { dir = Path.GetDirectoryName(dir); continue; }
                    if (Directory.EnumerateFileSystemEntries(dir).Any()) return;
                    Directory.Delete(dir);
                } catch (Exception) { return; }
                dir = Path.GetDirectoryName(dir);
            }
        }

        // ── 되돌리기 ──────────────────────────────────────────────
        public static UndoResult Undo(RunResult run)
        {
            var u = new UndoResult();
            var emptied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 용량을 붙인 폴더 이름부터 원래대로
            for (int i = run.Renamed.Count - 1; i >= 0; i--) TryRenameDir(run.Renamed[i].Value, run.Renamed[i].Key);
            for (int i = run.Moved.Count - 1; i >= 0; i--) {
                var m = run.Moved[i];
                // 폴더 이름을 되돌리지 못했으면 바뀐 이름 쪽에서 찾음
                string at = File.Exists(m.Dest) ? m.Dest : (m.Final != null && File.Exists(m.Final) ? m.Final : null);
                try {
                    if (at != null && !File.Exists(m.Source)) {
                        Directory.CreateDirectory(Path.GetDirectoryName(m.Source));
                        File.Move(at, m.Source);
                        u.Restored++;
                        emptied.Add(Path.GetDirectoryName(at));
                    } else u.Failed++;
                } catch (Exception) { u.Failed++; }
            }
            foreach (var d in emptied.OrderByDescending(x => x.Length)) RemoveEmptyUp(d, run.Root);
            return u;
        }

        // 옮긴 기록 (프로그램을 닫은 뒤에도 원래 위치를 알 수 있게)
        //  %LOCALAPPDATA%\DJISortTool\logs\yyyyMMdd_HHmmss.txt  "원래 위치<TAB>옮긴 위치"
        static void WriteLog(RunResult r)
        {
            if (r.Moved.Count == 0) return;
            try {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DJISortTool", "logs");
                Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                foreach (var m in r.Moved) sb.Append(m.Source).Append('\t').Append(m.Final ?? m.Dest).Append("\r\n");
                File.WriteAllText(Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv) + ".txt"), sb.ToString(), new UTF8Encoding(true));
            } catch (Exception) { }
        }

        // ── 휴지통 ────────────────────────────────────────────────
        // 테스트에서는 실제 휴지통 대신 다른 동작으로 바꿔 끼움
        internal static Func<string, bool> Recycler = Recycle;

        // 휴지통이 없는 위치(NAS 등)라 영구 삭제가 되는 경우엔 Windows가 경고를 먼저 띄움
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo;
            public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        static bool Recycle(string path)
        {
            var op = new SHFILEOPSTRUCT {
                wFunc  = 3,                                        // FO_DELETE
                pFrom  = path + "\0\0",
                fFlags = 0x40 | 0x10 | 0x4 | 0x400 | 0x4000        // 휴지통, 확인 생략, 진행창 없음, 오류창 없음, 영구삭제 경고
            };
            SHFileOperation(ref op);
            return !Directory.Exists(path);
        }
    }
}
