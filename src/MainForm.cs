using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DJISortTool
{
    // 폴더를 넣으면 미리보기 → [정리 시작] → 결과 · [되돌리기]
    class MainForm : Form
    {
        static readonly Color Accent    = Color.FromArgb(37, 99, 235);
        static readonly Color Warn      = Color.FromArgb(180, 83, 9);
        static readonly Color Muted     = Color.FromArgb(107, 114, 128);
        static readonly Color Ink       = Color.FromArgb(31, 41, 55);
        static readonly Color DropBack  = Color.FromArgb(243, 244, 246);
        static readonly Color DropHover = Color.FromArgb(224, 242, 254);
        static readonly Color DropLine  = Color.FromArgb(156, 163, 175);
        static readonly Color Disabled  = Color.FromArgb(209, 213, 219);

        readonly float k;                         // 화면 배율 (100% = 1)
        readonly Settings settings = Settings.Load();
        readonly string startPath;
        readonly Panel drop;
        readonly Label dropTitle, dropHint, pathLabel, notes, status;
        readonly Button pickButton, runButton, undoButton, openButton;
        readonly ListView list;
        readonly CheckBox trashBox, sizeBox;
        readonly ProgressBar progress;
        bool hover, busy;
        string currentPath;
        ScanResult scan;
        RunResult lastRun;

        public MainForm(string startPath)
        {
            this.startPath = startPath;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;

            Text = Program.Title;
            Font = new Font("Malgun Gothic", 9.75f);
            BackColor = Color.White;
            AutoScaleMode = AutoScaleMode.None;   // 크기는 S()로 직접 배율 적용
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(760), S(650));
            MinimumSize = new Size(S(640), S(560));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            // ── 머리말 ──
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.Controls.Add(new Label {
                Text = "DJI 카메라·드론 영상을 촬영일별 폴더로 정리합니다", AutoSize = true, ForeColor = Ink,
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold), Margin = new Padding(0, 0, 0, S(2)) }, 0, 0);
            var about = new LinkLabel { Text = "정보", AutoSize = true, LinkColor = Muted, ActiveLinkColor = Accent, Margin = new Padding(0, S(4), 0, 0) };
            about.LinkClicked += (s, e) => ShowAbout();
            header.Controls.Add(about, 1, 0);

            // ── 끌어다 놓는 곳 ──
            drop = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, S(10), 0, S(8)) };
            dropTitle = new Label { Text = "폴더를 여기에 끌어다 놓으세요", AutoSize = true, ForeColor = Ink, BackColor = Color.Transparent,
                                    Font = new Font(Font.FontFamily, 13f, FontStyle.Bold) };
            dropHint  = new Label { Text = "카드를 통째로 복사한 폴더 · DCIM 폴더 · 영상만 모은 폴더 모두 됩니다", AutoSize = true,
                                    ForeColor = Muted, BackColor = Color.Transparent };
            pickButton = new Button { Text = "폴더 선택…", AutoSize = true, MinimumSize = new Size(S(110), S(32)), BackColor = Color.White, UseVisualStyleBackColor = true };
            pickButton.Click += (s, e) => { var p = FolderPicker.Pick(this, "정리할 폴더 선택"); if (p != null) LoadFolder(p); };
            drop.Controls.AddRange(new Control[] { dropTitle, dropHint, pickButton });
            drop.Paint  += PaintDrop;
            drop.Layout += (s, e) => {
                int w = drop.ClientSize.Width;
                int top = (drop.ClientSize.Height - (dropTitle.Height + S(4) + dropHint.Height + S(12) + pickButton.Height)) / 2;
                dropTitle.Location  = new Point((w - dropTitle.Width) / 2, top);
                dropHint.Location   = new Point((w - dropHint.Width) / 2, dropTitle.Bottom + S(4));
                pickButton.Location = new Point((w - pickButton.Width) / 2, dropHint.Bottom + S(12));
            };

            pathLabel = new PathLabel { Dock = DockStyle.Fill, ForeColor = Muted, Margin = Padding.Empty };

            // ── 미리보기 표 ──
            list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable,
                                  MultiSelect = false, Margin = new Padding(0, S(4), 0, S(8)) };
            list.Columns.Add("촬영일");
            list.Columns.Add("만들 폴더");
            foreach (var c in new[] { "영상", "프록시", "오디오", "사진", "용량" }) list.Columns.Add(c, 60, HorizontalAlignment.Center);
            list.Resize += (s, e) => FitColumns();

            notes = new Label { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 0, 0, S(8)) };

            sizeBox = new CheckBox { Text = "정리한 날짜 폴더 이름 끝에 용량 붙이기 (예: 260823_DATA_61.2G)", AutoSize = true,
                                     Checked = settings.AddSize, Margin = new Padding(0, 0, 0, S(4)) };
            sizeBox.CheckedChanged += (s, e) => {
                settings.AddSize = sizeBox.Checked; settings.Save();
                if (lastRun == null && scan != null && scan.Error == null) FillList(scan.Items);   // 미리보기 폴더 이름 갱신
            };
            new ToolTip().SetToolTip(sizeBox, "폴더 이름이 바뀌므로 Premiere로 가져오기 전에 정리하세요.\n카드를 포맷한 뒤에도 백업본과 용량을 비교할 수 있습니다.");

            trashBox = new CheckBox { Text = "정리 후 남은 카드 관리 파일(썸네일·목록)도 휴지통으로 보내기", AutoSize = true,
                                      Checked = settings.TrashMisc, Margin = new Padding(0, 0, 0, S(10)) };
            trashBox.CheckedChanged += (s, e) => { settings.TrashMisc = trashBox.Checked; settings.Save(); };

            // ── 버튼 ──
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            runButton = new Button { Text = "정리 시작", AutoSize = true, MinimumSize = new Size(S(130), S(38)), FlatStyle = FlatStyle.Flat,
                                     ForeColor = Color.White, BackColor = Disabled, Enabled = false, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(S(8), 0, 0, 0) };
            runButton.FlatAppearance.BorderSize = 0;
            runButton.EnabledChanged += (s, e) => runButton.BackColor = runButton.Enabled ? Accent : Disabled;
            runButton.Click += RunClick;
            undoButton = new Button { Text = "되돌리기", AutoSize = true, MinimumSize = new Size(S(100), S(38)), Visible = false, Margin = new Padding(S(8), 0, 0, 0) };
            undoButton.Click += UndoClick;
            openButton = new Button { Text = "폴더 열기", AutoSize = true, MinimumSize = new Size(S(100), S(38)), Visible = false, Margin = new Padding(S(8), 0, 0, 0) };
            openButton.Click += (s, e) => { if (lastRun != null) Process.Start("explorer.exe", "\"" + lastRun.Root + "\""); };
            buttons.Controls.AddRange(new Control[] { runButton, undoButton, openButton });

            // ── 상태 줄 ──
            var statusRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, S(10), 0, 0) };
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(170)));
            status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft,
                                 Text = "폴더를 넣으면 무엇이 어디로 옮겨질지 먼저 보여 줍니다." };
            progress = new ProgressBar { Dock = DockStyle.Fill, Height = S(16), Visible = false, Margin = new Padding(S(8), S(4), 0, S(4)) };
            statusRow.Controls.Add(status, 0, 0);
            statusRow.Controls.Add(progress, 1, 0);

            // ── 전체 배치 ──
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(S(18), S(14), S(18), S(12)) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 머리말
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(150)));    // 끌어다 놓는 곳
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(22)));     // 경로
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));        // 표
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 알림
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 용량 옵션
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 휴지통 옵션
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 버튼
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 상태
            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(drop, 0, 1);
            layout.Controls.Add(pathLabel, 0, 2);
            layout.Controls.Add(list, 0, 3);
            layout.Controls.Add(notes, 0, 4);
            layout.Controls.Add(sizeBox, 0, 5);
            layout.Controls.Add(trashBox, 0, 6);
            layout.Controls.Add(buttons, 0, 7);
            layout.Controls.Add(statusRow, 0, 8);
            layout.Resize += (s, e) => notes.MaximumSize = new Size(Math.Max(S(200), layout.ClientSize.Width - layout.Padding.Horizontal), 0);
            Controls.Add(layout);

            // 창 어디에 놓아도 받음
            foreach (var c in new Control[] { this, layout, drop, dropTitle, dropHint, list, pathLabel, notes }) HookDrop(c);

            Shown += (s, e) => { if (!string.IsNullOrWhiteSpace(this.startPath)) LoadFolder(this.startPath); };
            FormClosing += (s, e) => {
                if (busy) { e.Cancel = true; MessageBox.Show(this, "파일을 옮기는 중입니다. 끝난 뒤에 닫아 주세요.", Program.Title, MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
        }

        int S(int v) { return (int)Math.Round(v * k); }

        // ── 끌어다 놓기 ──────────────────────────────────────────
        void HookDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += (s, e) => {
                bool ok = !busy && e.Data.GetDataPresent(DataFormats.FileDrop);
                e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
                SetHover(ok);
            };
            c.DragLeave += (s, e) => SetHover(false);
            c.DragDrop  += (s, e) => {
                SetHover(false);
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (busy || paths == null || paths.Length == 0) return;
                string p = paths[0];
                if (File.Exists(p)) p = Path.GetDirectoryName(p);    // 파일을 넣으면 그 파일이 있는 폴더
                BeginInvoke(new Action(() => LoadFolder(p)));         // 탐색기를 바로 놓아 주고 처리
            };
        }

        void SetHover(bool on)
        {
            if (hover == on) return;
            hover = on;
            drop.Invalidate(true);
        }

        void PaintDrop(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = drop.ClientRectangle;
            r.Inflate(-S(2), -S(2));
            using (var path = RoundRect(r, S(10)))
            using (var fill = new SolidBrush(hover ? DropHover : DropBack))
            using (var pen = new Pen(hover ? Accent : DropLine, Math.Max(1f, 1.5f * k)) { DashStyle = DashStyle.Dash }) {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
        }

        static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ── 미리보기 ──────────────────────────────────────────────
        async void LoadFolder(string path)
        {
            if (busy) return;
            currentPath = path;
            lastRun = null;
            SetBusy(true, "폴더를 살펴보는 중…", 0);
            scan = await Task.Run(() => Organizer.Scan(path));
            SetBusy(false, null, 0);
            ShowPreview();
        }

        void ShowPreview()
        {
            openButton.Visible = undoButton.Visible = false;
            pathLabel.Text = scan.Root ?? currentPath;
            if (scan.Error != null) {
                FillList(new PlanItem[0]);
                SetNotes(Warn, scan.Error);
                runButton.Text = "정리 시작";
                runButton.Enabled = false;
                status.Text = "다른 폴더를 넣어 주세요.";
                return;
            }
            FillList(scan.Items);

            var lines = new List<string>();
            if (scan.NoProxy.Count > 0)
                lines.Add(string.Format("프록시(LRF)가 없는 원본 {0}개: {1}  (슬로모션·타임랩스 등은 LRF가 안 생길 수 있음)",
                                        scan.NoProxy.Count, ShortList(scan.NoProxy)));
            if (scan.Unhandled.Count > 0)
                lines.Add(string.Format("정리 규칙이 없는 파일 {0}개는 그대로 둡니다 ({1}).", scan.Unhandled.Count, ExtSummary(scan.Unhandled)));
            if (scan.Conflicts > 0)
                lines.Add(string.Format("같은 이름의 파일이 이미 있어 {0}개는 건너뜁니다.", scan.Conflicts));
            if (lines.Count > 0) SetNotes(Warn, string.Join("\n", lines));
            else SetNotes(Muted, "LRF는 이름을 바꿔(…_Proxy.MP4) 프록시 폴더에 넣습니다. Premiere에서 바로 프록시로 연결됩니다.");

            runButton.Text = "정리 시작";
            runButton.Enabled = true;
            status.Text = string.Format("파일 {0}개({1})를 날짜 폴더 {2}개로 정리합니다. 아직 아무것도 바뀌지 않았습니다.",
                                        scan.Items.Count, Bytes(scan.Items.Sum(x => x.Size)), scan.Days.Count);
        }

        // 미리보기: 만들어질 폴더 이름(용량 옵션 반영) · 결과: 실제 폴더 이름
        string FolderName(string day)
        {
            string name;
            if (lastRun != null && lastRun.FinalNames.TryGetValue(day, out name)) return name;
            DayFolder df;
            if (scan != null && scan.Days.TryGetValue(day, out df)) return Organizer.PreviewName(df, sizeBox.Checked);
            return day + "_DATA";
        }

        static string Bytes(long b) { return Organizer.FormatSize(b) + "B"; }

        void FillList(IEnumerable<PlanItem> items)
        {
            list.BeginUpdate();
            list.Items.Clear();
            var groups = items.GroupBy(x => x.Day).OrderBy(g => g.Key).ToList();
            foreach (var g in groups) list.Items.Add(Row(FormatDay(g.Key), FolderName(g.Key), g));
            if (groups.Count > 1) {
                var total = Row("합계", "", groups.SelectMany(g => g));
                total.Font = new Font(list.Font, FontStyle.Bold);
                total.BackColor = DropBack;
                list.Items.Add(total);
            }
            list.EndUpdate();
            FitColumns();
        }

        static ListViewItem Row(string day, string folder, IEnumerable<PlanItem> items)
        {
            var a = items.ToList();
            Func<Func<PlanItem, bool>, string> n = f => { int c = a.Count(f); return c > 0 ? c.ToString() : "–"; };
            return new ListViewItem(new[] {
                day, folder,
                n(x => x.Kind == Kind.Footage), n(x => x.Kind == Kind.Proxy), n(x => x.Kind == Kind.Audio),
                n(x => x.Kind == Kind.Jpg || x.Kind == Kind.Dng), Bytes(a.Sum(x => x.Size)) });
        }

        static string FormatDay(string yymmdd) { return "20" + yymmdd.Substring(0, 2) + "-" + yymmdd.Substring(2, 2) + "-" + yymmdd.Substring(4, 2); }

        static string ShortList(List<string> items)
        {
            string s = string.Join(", ", items.Take(6));
            return items.Count > 6 ? s + string.Format(" 외 {0}개", items.Count - 6) : s;
        }

        static string ExtSummary(IEnumerable<string> files)
        {
            return string.Join(" · ", files.GroupBy(f => Path.GetExtension(f).TrimStart('.').ToUpperInvariant())
                                           .OrderByDescending(g => g.Count())
                                           .Select(g => (g.Key == "" ? "확장자 없음" : g.Key) + " " + g.Count()));
        }

        void FitColumns()
        {
            if (list.Columns.Count < 7) return;
            int w = list.ClientSize.Width, a = S(100), b = S(190);
            int rest = Math.Max(S(50), (w - a - b) / 5);
            list.Columns[0].Width = a;
            list.Columns[1].Width = b;
            for (int i = 2; i < 7; i++) list.Columns[i].Width = rest;
        }

        void SetNotes(Color color, string text) { notes.ForeColor = color; notes.Text = text; }

        // ── 정리 ──────────────────────────────────────────────────
        async void RunClick(object sender, EventArgs e)
        {
            if (busy || scan == null || scan.Error != null) return;
            var sc = scan;
            bool trash = trashBox.Checked, addSize = sizeBox.Checked;
            SetBusy(true, "정리하는 중…", sc.Items.Count);
            var report = new Progress<int>(v => progress.Value = Math.Min(v, progress.Maximum));
            lastRun = await Task.Run(() => Organizer.Run(sc, trash, addSize, report));
            SetBusy(false, null, 0);
            ShowResult(sc);
        }

        void ShowResult(ScanResult sc)
        {
            var r = lastRun;
            FillList(r.Moved);
            var lines = new List<string>();
            bool verified = r.VerifyFailed == 0 && r.Verified == r.Moved.Count;
            bool problem = r.Skipped > 0 || r.Failed > 0 || r.Kept.Count > 0 || r.RenameFailed > 0 || !verified || r.RenamedExisting.Count > 0;
            if (!verified) lines.Add(string.Format("확인 결과 {0}개 파일이 제자리에 없거나 크기가 다릅니다. [폴더 열기]로 확인해 주세요.", r.VerifyFailed));
            if (r.Skipped > 0) lines.Add(string.Format("같은 이름의 파일이 이미 있어 {0}개는 그대로 뒀습니다.", r.Skipped));
            if (r.Failed > 0)  lines.Add(string.Format("{0}개는 옮기지 못했습니다. 다른 프로그램(Premiere, 동기화 등)이 쓰고 있을 수 있습니다.", r.Failed));
            foreach (var renamed in r.RenamedExisting)
                lines.Add("기존 폴더에 합치면서 용량이 바뀌었습니다: " + renamed + "  (이미 Premiere로 가져왔다면 링크를 다시 연결해야 합니다)");
            if (r.RenameFailed > 0) lines.Add(string.Format("폴더 {0}개는 이름에 용량을 붙이지 못했습니다 (탐색기·동기화 프로그램이 사용 중이거나 같은 이름이 있음).", r.RenameFailed));
            foreach (var kept in r.Kept) lines.Add("카드 관리 파일이 아닌 파일이 섞여 있어 남겨 뒀습니다: " + kept);
            if (sc.Unhandled.Count > 0) lines.Add(string.Format("정리 규칙이 없는 파일 {0}개는 그대로 뒀습니다 ({1}).", sc.Unhandled.Count, ExtSummary(sc.Unhandled)));
            if (sc.NoProxy.Count > 0) lines.Add(string.Format("프록시(LRF)가 없는 원본 {0}개: {1}", sc.NoProxy.Count, ShortList(sc.NoProxy)));
            if (r.Trashed.Count > 0) lines.Add("남은 카드 관리 파일(썸네일·목록)은 휴지통으로 보냈습니다.");
            SetNotes(problem ? Warn : Muted, string.Join("\n", lines));

            runButton.Text = "정리 완료";
            runButton.Enabled = false;
            openButton.Visible = true;
            undoButton.Visible = r.Moved.Count > 0;
            status.ForeColor = verified ? Color.FromArgb(21, 128, 61) : Warn;
            status.Text = verified
                ? string.Format("✔ 완료 — 파일 {0}개({1})를 옮겼고, 정리 전과 개수·용량이 같은 것을 확인했습니다.", r.Verified, Bytes(r.VerifiedBytes))
                : string.Format("완료 — 파일 {0}개를 옮겼지만 {1}개는 확인되지 않았습니다.", r.Moved.Count, r.VerifyFailed);
        }

        async void UndoClick(object sender, EventArgs e)
        {
            var run = lastRun;
            if (busy || run == null) return;
            string msg = "정리하기 전 위치로 파일을 되돌릴까요?";
            if (run.Trashed.Count > 0) msg += "\n\n휴지통으로 보낸 카드 관리 파일(썸네일·목록)은 휴지통에서 직접 복원해야 합니다.";
            if (MessageBox.Show(this, msg, Program.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            SetBusy(true, "되돌리는 중…", 0);
            var u = await Task.Run(() => Organizer.Undo(run));
            scan = await Task.Run(() => Organizer.Scan(run.Root));
            lastRun = null;
            SetBusy(false, null, 0);
            ShowPreview();
            status.Text = u.Failed == 0
                ? string.Format("되돌렸습니다 — 파일 {0}개가 원래 위치로 돌아갔습니다.", u.Restored)
                : string.Format("파일 {0}개를 되돌렸고, {1}개는 되돌리지 못했습니다 (이미 다른 곳으로 옮겼거나 사용 중).", u.Restored, u.Failed);
        }

        void SetBusy(bool on, string text, int max)
        {
            busy = on;
            UseWaitCursor = on;
            pickButton.Enabled = !on;
            trashBox.Enabled = !on;
            sizeBox.Enabled = !on;
            status.ForeColor = Muted;
            if (on) { runButton.Enabled = false; undoButton.Enabled = false; openButton.Enabled = false; }
            else { undoButton.Enabled = true; openButton.Enabled = true; }
            progress.Visible = on && max > 0;
            if (max > 0) { progress.Maximum = max; progress.Value = 0; }
            if (text != null) status.Text = text;
        }

        void ShowAbout()
        {
            MessageBox.Show(this,
                Program.Title + "\n\n" +
                "DJI 카메라·드론으로 찍은 영상(MP4·LRF·AAC)과 사진을 촬영일별 폴더로 정리하는 무료 프로그램입니다.\n\n" +
                "· 파일을 지우지 않고, 넣은 폴더 안에서 옮기기만 합니다 (되돌리기 가능)\n" +
                "· 인터넷에 연결하지 않으며, 어떤 정보도 수집하지 않습니다\n\n" +
                "만든 사람: 이중필름 (비영리 무료 배포)\n" +
                "DJI와는 무관한 개인 제작 프로그램입니다.\n" +
                "DJI는 SZ DJI Technology Co., Ltd.의 상표입니다.",
                "정보", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // 긴 경로는 가운데를 줄여 폴더 이름이 보이게  (C:\Users\…\DJI 카드 복사본)
    class PathLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.PathEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}
