// 정리 로직 테스트 (화면 없이 Organizer만)
//   OrganizerTest.exe                    가짜 폴더를 만들어 여러 상황 확인
//   OrganizerTest.exe --replica <폴더>   실제 폴더의 이름·날짜만 본뜬 1바이트 복제본으로 확인 (원본은 건드리지 않음)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DJISortTool
{
    static class OrganizerTest
    {
        static int failures;
        static string temp, trash;

        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            temp  = Path.Combine(Path.GetTempPath(), "DJISortToolTest_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            trash = Path.Combine(temp, "_trash");
            Directory.CreateDirectory(trash);
            // 실제 휴지통 대신 테스트 폴더로 옮김
            Organizer.Recycler = p => { Directory.Move(p, Path.Combine(trash, Guid.NewGuid().ToString("N"))); return true; };
            try {
                if (args.Length == 2 && args[0] == "--replica") Replica(args[1]);
                else { CardCopy(); MidnightAndLeftovers(); LooseFiles(); DcimOnly(); MergeSameDay(); SizeFormat(); Guards(); }
            } finally {
                try { Directory.Delete(temp, true); } catch (Exception) { }
            }
            Console.WriteLine(failures == 0 ? "\n모든 테스트 통과" : "\n실패 " + failures + "개");
            return failures;
        }

        // ── 상황별 테스트 ─────────────────────────────────────────

        // 카드를 통째로 복사한 폴더: 3일치, LRF 없는 원본 1개, 사진, MISC, 원래 있던 빈 폴더
        static void CardCopy()
        {
            Console.WriteLine("[카드 통째 복사]");
            string root = Dir("card");
            string clips = Path.Combine(root, @"DCIM\DJI_001");
            Clip(clips, "DJI_20260823135545_0039_D", lrf: true, aac: true);
            Clip(clips, "DJI_20260823140000_0040_D", lrf: true, aac: true);
            Clip(clips, "DJI_20260825101010_0041_D", lrf: true, aac: false);
            Clip(clips, "DJI_20260829090000_0042_D", lrf: false, aac: true);   // 슬로모션 등: LRF 없음
            Touch(clips, "DJI_20260829091500_0043_D.JPG");
            Touch(clips, "DJI_20260829091500_0043_D.DNG");
            Touch(Path.Combine(root, @"MISC\THM\DJI_001"), "DJI_20260823135545_0039_D.THM");
            Touch(Path.Combine(root, @"MISC\THM\DJI_001"), "DJI_20260823135545_0039_D.SCR");
            Touch(Path.Combine(root, "MISC"), "PP-041.db");
            Directory.CreateDirectory(Path.Combine(root, @"MISC\IDX"));
            Touch(Path.Combine(root, @"DCIM\MISC"), ".dropbox.device");
            Directory.CreateDirectory(Path.Combine(root, "내가 만든 빈 폴더"));
            var before = Snapshot(root);

            var s = Organizer.Scan(root);
            Check(s.Error == null, "미리보기 오류 없음");
            Check(s.Items.Count == 12, "옮길 파일 12개 (영상 4 · 프록시 3 · 오디오 3 · 사진 2): " + s.Items.Count);
            Check(s.NoProxy.SequenceEqual(new[] { "0042" }), "LRF 없는 원본 = 0042: " + string.Join(",", s.NoProxy));
            Check(s.Unhandled.Count == 0, "썸네일은 정리 규칙 없는 파일로 세지 않음");

            Check(Organizer.PreviewName(s.Days["260823"], true) == "260823_DATA_1M", "미리보기에 용량 붙은 폴더 이름");
            var r = Organizer.Run(s, true, true, null);
            Check(r.Moved.Count == 12 && r.Failed == 0 && r.Skipped == 0, "12개 옮김");
            Check(r.Verified == 12 && r.VerifyFailed == 0 && r.VerifiedBytes == 12, "정리 후 확인: 12개 · 12바이트 그대로");
            Check(r.FinalNames["260823"] == "260823_DATA_1M" && r.Renamed.Count == 3 && r.RenamedExisting.Count == 0, "날짜 폴더 3개에 용량 붙임");
            Check(Exists(root, @"260823_DATA_1M\260823_Footage\DJI_20260823135545_0039_D.MP4"), "원본 → Footage");
            Check(Exists(root, @"260823_DATA_1M\260823_Proxy\DJI_20260823135545_0039_D_Proxy.MP4"), "LRF → Proxy (_Proxy.MP4)");
            Check(Exists(root, @"260823_DATA_1M\260823_Audio\DJI_20260823135545_0039_D.AAC"), "AAC → Audio");
            Check(Exists(root, @"260829_DATA_1M\260829_Photo\jpg\DJI_20260829091500_0043_D.JPG"), "JPG → Photo\\jpg");
            Check(Exists(root, @"260829_DATA_1M\260829_Photo\dng\DJI_20260829091500_0043_D.DNG"), "DNG → Photo\\dng");
            Check(r.Trashed.Count == 2 && !Directory.Exists(Path.Combine(root, "MISC")), "MISC · DCIM\\MISC 휴지통으로");
            Check(!Directory.Exists(Path.Combine(root, "DCIM")), "비게 된 DCIM 정리");
            Check(Directory.Exists(Path.Combine(root, "내가 만든 빈 폴더")), "원래 비어 있던 폴더는 그대로");
            Check(Organizer.Scan(root).Error != null, "정리한 폴더를 다시 넣으면 할 일 없음");

            var u = Organizer.Undo(r);
            Check(u.Restored == 12 && u.Failed == 0, "되돌리기 12개");
            var after = Snapshot(root);
            var missing = before.Except(after).ToList();
            Check(missing.All(p => p.StartsWith(@"MISC") || p.StartsWith(@"DCIM\MISC")) && after.Except(before).Count() == 0,
                  "되돌린 뒤 원래 구조 (휴지통으로 보낸 MISC만 빠짐)");
        }

        // 옛 이름(시각 없음)의 자정 무렵 촬영, 정리 규칙 없는 파일, 이미 같은 이름, 사용자의 MISC 폴더
        static void MidnightAndLeftovers()
        {
            Console.WriteLine("[예외 상황]");
            string root = Dir("edge");
            string media = Path.Combine(root, @"DCIM\100MEDIA");
            Touch(media, "DJI_0001.MP4", new DateTime(2026, 9, 1, 23, 59, 50));
            Touch(media, "DJI_0001.LRF", new DateTime(2026, 9, 2, 0, 0, 5));     // 원본을 따라 260901
            Touch(media, "DJI_0001.SRT", new DateTime(2026, 9, 2, 0, 0, 5));
            Touch(media, "DJI_20260905103000_0007_D.OSV");
            Touch(media, "DJI_20260905104000_0008_D.MP4");
            Touch(Path.Combine(root, @"260905_DATA\260905_Footage"), "DJI_20260905104000_0008_D.MP4");
            Touch(Path.Combine(root, "MISC"), "메모.txt");                       // 사용자 파일이 든 MISC

            var s = Organizer.Scan(root);
            Check(s.Items.Count == 3, "옮길 파일 3개: " + s.Items.Count);
            Check(s.Items.Where(x => x.Source.Contains("DJI_0001")).All(x => x.Day == "260901"), "LRF가 원본 날짜(260901)를 따라감");
            Check(s.Unhandled.Count == 2, "SRT·OSV는 그대로 둠: " + s.Unhandled.Count);
            Check(s.Conflicts == 1, "같은 이름 1개 미리 알림");

            var r = Organizer.Run(s, true, true, null);
            Check(r.Moved.Count == 2 && r.Skipped == 1, "2개 옮기고 1개 건너뜀");
            Check(r.Kept.Count == 1 && File.Exists(Path.Combine(root, @"MISC\메모.txt")), "사용자 파일이 든 MISC는 남김");
            Check(Exists(root, @"DCIM\100MEDIA\DJI_0001.SRT") && Exists(root, @"DCIM\100MEDIA\DJI_20260905104000_0008_D.MP4"),
                  "정리 못 한 파일이 남은 폴더는 그대로");
        }

        // DCIM 없이 영상 파일만 복사한 폴더
        static void LooseFiles()
        {
            Console.WriteLine("[파일만 복사한 폴더]");
            string root = Dir("loose");
            Clip(root, "DJI_20260907120000_0100_D", lrf: true, aac: false);
            var s = Organizer.Scan(root);
            var r = Organizer.Run(s, true, true, null);
            Check(r.Moved.Count == 2, "2개 옮김");
            Check(Exists(root, @"260907_DATA_1M\260907_Proxy\DJI_20260907120000_0100_D_Proxy.MP4"), "넣은 폴더 안에 날짜 폴더");
            Check(Directory.Exists(root), "넣은 폴더 자체는 그대로");
        }

        // DCIM 폴더만 넣은 경우
        static void DcimOnly()
        {
            Console.WriteLine("[DCIM 폴더를 넣음]");
            string root = Path.Combine(Dir("dcimonly"), "DCIM");
            Clip(Path.Combine(root, "DJI_001"), "DJI_20260912080000_0200_D", lrf: true, aac: true);
            Touch(Path.Combine(root, "MISC"), ".dropbox.device");
            var r = Organizer.Run(Organizer.Scan(root), true, true, null);
            Check(r.Moved.Count == 3 && r.Trashed.Count == 1, "3개 옮기고 DCIM\\MISC 휴지통으로");
            Check(Exists(root, @"260912_DATA_1M\260912_Footage\DJI_20260912080000_0200_D.MP4"), "DCIM 안에 날짜 폴더");
            Check(!Directory.Exists(Path.Combine(root, "DJI_001")), "비게 된 DJI_001 정리");
        }

        // 같은 날짜 카드를 한 폴더에 두 번 정리: 기존 260910_DATA_2M에 합치고 용량 갱신, 되돌리면 이름도 원래대로
        static void MergeSameDay()
        {
            Console.WriteLine("[같은 날짜 추가 정리]");
            string root = Dir("merge");
            Sized(Path.Combine(root, @"DCIM\DJI_001"), "DJI_20260910100000_0300_D.MP4", 2);
            var r1 = Organizer.Run(Organizer.Scan(root), true, true, null);
            Check(r1.FinalNames["260910"] == "260910_DATA_2M", "첫 카드: 260910_DATA_2M");

            Sized(Path.Combine(root, "두번째 카드"), "DJI_20260910110000_0301_D.MP4", 3);
            var s2 = Organizer.Scan(root);
            Check(s2.Days["260910"].Existed && s2.Days["260910"].Name == "260910_DATA_2M", "기존 폴더를 찾아 합칠 준비");
            Check(Organizer.PreviewName(s2.Days["260910"], true) == "260910_DATA_5M", "미리보기: 합친 뒤 260910_DATA_5M");
            var r2 = Organizer.Run(s2, true, true, null);
            Check(r2.FinalNames["260910"] == "260910_DATA_5M" && r2.RenamedExisting.Count == 1, "합치고 용량 갱신 (기존 폴더 이름 바뀜 알림)");
            Check(Exists(root, @"260910_DATA_5M\260910_Footage\DJI_20260910100000_0300_D.MP4") &&
                  Exists(root, @"260910_DATA_5M\260910_Footage\DJI_20260910110000_0301_D.MP4"), "두 카드 파일이 한 폴더에");
            Check(r2.Verified == 1 && r2.VerifiedBytes == 3 * 1024 * 1024, "정리 후 확인: 3MB 그대로");

            var u = Organizer.Undo(r2);
            Check(u.Restored == 1 && Directory.Exists(Path.Combine(root, "260910_DATA_2M")) &&
                  Exists(root, @"두번째 카드\DJI_20260910110000_0301_D.MP4"), "되돌리면 260910_DATA_2M과 두 번째 카드 파일이 원래대로");
        }

        static void SizeFormat()
        {
            Console.WriteLine("[용량 표기]");
            const long MB = 1024L * 1024, GB = MB * 1024, TB = GB * 1024;
            Check(Organizer.FormatSize(1) == "1M", "1바이트 → 1M");
            Check(Organizer.FormatSize(850 * MB) == "850M", "850MB → 850M");
            Check(Organizer.FormatSize(GB) == "1G", "1GB → 1G");
            Check(Organizer.FormatSize(GB * 219 / 100) == "2.1G", "2.19GB → 2.1G (내림)");
            Check(Organizer.FormatSize(GB * 123 / 2) == "61.5G", "61.5GB → 61.5G");
            Check(Organizer.FormatSize(GB * 245 / 4) == "61.2G", "61.25GB → 61.2G (내림)");
            Check(Organizer.FormatSize(TB * 3 / 2) == "1.5T", "1.5TB → 1.5T");
        }

        static void Guards()
        {
            Console.WriteLine("[막아야 하는 경우]");
            Check(Organizer.Scan(@"C:\").Error.Contains("맨 위"), "드라이브 맨 위는 막음");
            Check(Organizer.Scan(Path.Combine(temp, "없는 폴더")).Error != null, "없는 폴더");
            string sony = Dir("sony");
            Touch(Path.Combine(sony, @"M4ROOT\CLIP"), "C0001.MP4");
            Check(Organizer.Scan(sony).Error != null, "DJI 파일 없는 폴더(소니 카드)는 할 일 없음");
        }

        // ── 실제 폴더 복제본 ──────────────────────────────────────
        static void Replica(string src)
        {
            src = Path.GetFullPath(src).TrimEnd('\\');
            string root = Dir("replica");
            int n = 0;
            foreach (var f in new DirectoryInfo(src).EnumerateFiles("*", SearchOption.AllDirectories)) {
                string dst = Path.Combine(root, f.FullName.Substring(src.Length + 1));
                Touch(Path.GetDirectoryName(dst), Path.GetFileName(dst), f.LastWriteTime);
                n++;
            }
            Console.WriteLine("[복제본] " + src + "  파일 " + n + "개");
            var before = Snapshot(root);
            var s = Organizer.Scan(root);
            Check(s.Error == null, "미리보기 오류 없음 " + (s.Error ?? ""));
            foreach (var g in s.Items.GroupBy(x => x.Day).OrderBy(g => g.Key))
                Console.WriteLine(string.Format("    {0}_DATA  영상 {1} · 프록시 {2} · 오디오 {3} · 사진 {4}", g.Key,
                    g.Count(x => x.Kind == Kind.Footage), g.Count(x => x.Kind == Kind.Proxy), g.Count(x => x.Kind == Kind.Audio),
                    g.Count(x => x.Kind == Kind.Jpg || x.Kind == Kind.Dng)));
            Console.WriteLine("    LRF 없는 원본: " + string.Join(", ", s.NoProxy) + "   그대로 둘 파일: " + s.Unhandled.Count + "   같은 이름: " + s.Conflicts);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var r = Organizer.Run(s, true, true, null);
            Console.WriteLine(string.Format("    정리 {0:N2}초: 옮김 {1} · 건너뜀 {2} · 실패 {3} · 휴지통 {4}", sw.Elapsed.TotalSeconds, r.Moved.Count, r.Skipped, r.Failed, r.Trashed.Count));
            Check(r.Moved.Count == s.Items.Count && r.Failed == 0, "전부 옮김");
            Check(r.Verified == r.Moved.Count && r.VerifyFailed == 0, "정리 후 확인: " + r.Verified + "개 제자리·같은 크기");
            Console.WriteLine("    정리 후 맨 위: " + string.Join(", ", Directory.GetFileSystemEntries(root).Select(Path.GetFileName)));

            var u = Organizer.Undo(r);
            var after = Snapshot(root);
            Check(u.Failed == 0 && after.Except(before).Count() == 0 &&
                  before.Except(after).All(p => p.StartsWith("MISC") || p.StartsWith(@"DCIM\MISC")), "되돌리면 원래 구조 (MISC만 휴지통에)");
        }

        // ── 도우미 ────────────────────────────────────────────────
        static void Check(bool ok, string name)
        {
            Console.WriteLine((ok ? "  ✔ " : "  ✖ ") + name);
            if (!ok) failures++;
        }

        static string Dir(string name) { string d = Path.Combine(temp, name); Directory.CreateDirectory(d); return d; }

        static void Clip(string dir, string baseName, bool lrf, bool aac)
        {
            Touch(dir, baseName + ".MP4");
            if (lrf) Touch(dir, baseName + ".LRF");
            if (aac) Touch(dir, baseName + ".AAC");
        }

        static void Touch(string dir, string name, DateTime? time = null)
        {
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, name);
            File.WriteAllBytes(p, new byte[] { 0 });
            File.SetLastWriteTime(p, time ?? new DateTime(2026, 1, 1));   // 파일명에 시각이 있으면 이 날짜는 쓰이지 않아야 함
        }

        static void Sized(string dir, string name, int megabytes)
        {
            Directory.CreateDirectory(dir);
            using (var fs = File.Create(Path.Combine(dir, name))) fs.SetLength(megabytes * 1024L * 1024);
        }

        static bool Exists(string root, string rel) { return File.Exists(Path.Combine(root, rel)); }

        static HashSet<string> Snapshot(string root)
        {
            return new HashSet<string>(Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                                                .Select(p => p.Substring(root.Length + 1)), StringComparer.OrdinalIgnoreCase);
        }
    }
}
