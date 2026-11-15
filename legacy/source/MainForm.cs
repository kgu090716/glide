using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ThreeFingerWindowDrag
{
    internal sealed class MainForm : Form
    {
        private readonly RawTouchpad input;
        private readonly WindowMover mover;
        private readonly GestureEngine engine;
        private readonly Settings settings;
        private readonly NotifyIcon tray;
        private readonly Timer timer;
        private readonly CheckBox enabled, restore, setup;
        private readonly Label deviceLabel, liveLabel, statusLabel, speedLabel, shortcutLabel;
        private readonly ToolStripMenuItem toggleItem;
        private bool exiting;

        internal MainForm(Settings configuration)
        {
            settings = configuration;
            mover = new WindowMover { RestoreMaximized = settings.RestoreMaximized };
            engine = new GestureEngine(mover) { PixelsPerMm = settings.Speed, Enabled = settings.SetupDone };
            Text = "세 손가락 창 이동";
            Font = new Font("맑은 고딕", 10);
            BackColor = Color.FromArgb(246, 248, 252);
            ForeColor = Color.FromArgb(28, 39, 59);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, 750);
            MinimumSize = new Size(580, 600);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = SystemIcons.Application;
            Padding = new Padding(26, 20, 26, 18);

            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            Add(layout, MakeLabel("세 손가락으로, 창을 자유롭게", 20, true, 46));
            Add(layout, MakeLabel("커서를 창 위에 놓으세요. 세 손가락을 대고 움직이면\n제목 표시줄을 잡지 않아도 창 전체가 따라옵니다.", 10, false, 54));

            enabled = new CheckBox { Text = "창 이동 사용", Checked = engine.Enabled, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 8, 0, 8) };
            enabled.Enabled = settings.SetupDone;
            Add(layout, enabled);

            Panel first = new Panel { BackColor = Color.FromArgb(232, 240, 255), Height = 157, Dock = DockStyle.Top, Margin = new Padding(0, 4, 0, 12), Padding = new Padding(14) };
            Label instruction = MakeLabel("처음 한 번, Windows 제스처를 설정해 주세요.\n터치패드 → 세 손가락 제스처에서\n‘살짝 밀기’와 ‘탭’을 모두 ‘아무 작업 안 함’으로 변경하세요.", 9.5f, false, 76);
            instruction.Dock = DockStyle.Top;
            Button open = MakeButton("터치패드 설정 열기");
            open.SetBounds(14, 101, 282, 36);
            open.Click += delegate { OpenTouchpadSettings(); };
            first.Controls.Add(instruction); first.Controls.Add(open);
            Add(layout, first);
            setup = new CheckBox { Text = "위의 Windows 세 손가락 설정을 변경했습니다", Checked = settings.SetupDone, AutoSize = true, Margin = new Padding(0, 0, 0, 13) };
            Add(layout, setup);

            speedLabel = MakeLabel("이동 속도 · " + settings.Speed + " px/mm", 10, true, 27);
            Add(layout, speedLabel);
            TrackBar speed = new TrackBar { Minimum = 3, Maximum = 36, Value = settings.Speed, TickFrequency = 3, Height = 44, Dock = DockStyle.Top, Margin = new Padding(0) };
            Add(layout, speed);
            restore = new CheckBox { Text = "최대화된 창은 원래 크기로 복원해서 이동", Checked = settings.RestoreMaximized, AutoSize = true, Margin = new Padding(0, 0, 0, 14) };
            Add(layout, restore);
            deviceLabel = MakeLabel("터치패드를 찾고 있습니다…", 9.5f, true, 28);
            liveLabel = MakeLabel("입력 대기 중", 9, false, 26);
            statusLabel = MakeLabel(mover.Status, 9, false, 40);
            Add(layout, deviceLabel); Add(layout, liveLabel); Add(layout, statusLabel);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Height = 44, Dock = DockStyle.Top, Margin = new Padding(0, 2, 0, 2), WrapContents = false };
            Button refresh = MakeButton("다시 검색"), diagnose = MakeButton("진단 저장"), hide = MakeButton("트레이로 숨김");
            refresh.Width = 137; diagnose.Width = 110; hide.Width = 153;
            buttons.Controls.Add(refresh); buttons.Controls.Add(diagnose); buttons.Controls.Add(hide);
            Add(layout, buttons);
            shortcutLabel = MakeLabel("Ctrl + Alt + F9  사용/일시정지   ·   Esc  현재 이동 중지\n창을 닫아도 트레이에서 실행됩니다. 종료는 트레이 메뉴를 이용하세요.", 8.5f, false, 44);
            Add(layout, shortcutLabel);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("설정 열기", null, delegate { ShowSettings(); });
            toggleItem = new ToolStripMenuItem("창 이동 사용") { Checked = engine.Enabled };
            menu.Items.Add(toggleItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, delegate { exiting = true; Close(); });
            tray = new NotifyIcon { Icon = Icon, Text = "세 손가락 창 이동", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowSettings(); };
            toggleItem.Click += delegate { ToggleEnabled(); };

            try { input = new RawTouchpad(); }
            catch { tray.Dispose(); throw; }
            if (!input.HotkeyRegistered)
                shortcutLabel.Text = "단축키를 등록하지 못했습니다. 켜기/끄기는 트레이 메뉴를 이용하세요.\nEsc로 이동 중지 · 창을 닫아도 계속 실행 · 종료는 트레이 메뉴";
            input.Frame += delegate(ContactFrame frame, long now) {
                bool cancel = (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0 || (Native.GetAsyncKeyState(1) & 0x8000) != 0 || (Native.GetAsyncKeyState(2) & 0x8000) != 0;
                engine.Process(frame, now, cancel);
            };
            input.Toggle += ToggleEnabled;
            input.DevicesChanged += delegate { engine.Cancel(); UpdateStatus(); };
            input.InputFault += engine.Cancel;
            enabled.CheckedChanged += delegate {
                engine.Enabled = enabled.Checked && setup.Checked;
                engine.Cancel(); toggleItem.Checked = engine.Enabled;
                UpdateStatus();
            };
            setup.CheckedChanged += delegate {
                settings.SetupDone = setup.Checked;
                enabled.Enabled = setup.Checked;
                enabled.Checked = setup.Checked;
                SaveSettings();
            };
            speed.ValueChanged += delegate {
                settings.Speed = speed.Value; engine.PixelsPerMm = speed.Value;
                engine.Cancel();
                speedLabel.Text = "이동 속도 · " + speed.Value + " px/mm";
                SaveSettings();
            };
            restore.CheckedChanged += delegate { settings.RestoreMaximized = restore.Checked; mover.RestoreMaximized = restore.Checked; engine.Cancel(); SaveSettings(); };
            refresh.Click += delegate { engine.Cancel(); input.RefreshDevices(); UpdateStatus(); };
            diagnose.Click += delegate { SaveDiagnostic(); };
            hide.Click += delegate { Hide(); };
            timer = new Timer { Interval = 60 };
            timer.Tick += delegate {
                if ((Native.GetAsyncKeyState(0x1B) & 0x8000) != 0) engine.Cancel();
                engine.Tick(input.Now); UpdateStatus();
            };
            timer.Start();
            FormClosing += delegate(object sender, FormClosingEventArgs args) {
                if (!exiting && args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; Hide(); }
            };
            UpdateStatus();
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Rectangle work = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, work.Width - 32), Math.Min(Height, work.Height - 32));
            Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
        }
        private static void Add(TableLayoutPanel panel, Control control)
        {
            int row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(control, 0, row);
        }
        private static Label MakeLabel(string text, float size, bool bold, int height)
        {
            return new Label { Text = text, Font = new Font("맑은 고딕", size, bold ? FontStyle.Bold : FontStyle.Regular), Height = height, Dock = DockStyle.Top, Margin = new Padding(0), TextAlign = ContentAlignment.MiddleLeft };
        }
        private static Button MakeButton(string text)
        {
            return new Button { Text = text, Height = 35, Width = 160, FlatStyle = FlatStyle.Flat, BackColor = Color.White, Margin = new Padding(0, 0, 9, 0), Cursor = Cursors.Hand };
        }
        private void ToggleEnabled()
        {
            if (!setup.Checked) { ShowSettings(); return; }
            enabled.Checked = !enabled.Checked;
        }
        internal void ShowSettings()
        {
            Show(); WindowState = FormWindowState.Normal; Activate();
        }
        private static void OpenTouchpadSettings()
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:devices-touchpad") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show("Windows 터치패드 설정을 열지 못했습니다.\n" + ex.Message); }
        }
        private void SaveSettings()
        {
            try { settings.Save(); }
            catch (Exception ex) { MessageBox.Show(this, "설정 저장 실패: " + ex.Message + "\n프로그램을 쓰기 가능한 폴더로 옮겨 주세요.", Text); }
        }
        private void UpdateStatus()
        {
            if (input == null) return;
            deviceLabel.Text = input.DeviceCount > 0 ? "정밀 터치패드 " + input.DeviceCount + "개 감지됨" : "지원되는 정밀 터치패드가 감지되지 않았습니다";
            int contacts = input.LastFrameTime >= 0 && input.Now - input.LastFrameTime < 450 ? input.ContactCount : 0;
            long reports = input.Devices.Sum(d => d.Reports);
            string state = engine.Dragging ? "창 이동 중" : engine.Enabled ? "사용 중" : "일시정지";
            liveLabel.Text = state + "  ·  인식된 손가락 " + contacts + "개  ·  수신 " + reports + "회";
            string deviceError = input.Devices.Select(d => d.LastError).FirstOrDefault(e => !string.IsNullOrEmpty(e));
            statusLabel.Text = input.LastError ?? deviceError ?? (!setup.Checked ? "Windows 제스처 설정을 마친 뒤 확인란을 선택해 주세요." : mover.Status);
            tray.Text = "세 손가락 창 이동 · " + state;
        }
        private void SaveDiagnostic()
        {
            using (SaveFileDialog dialog = new SaveFileDialog { FileName = "Touchpad-diagnostic.txt", Filter = "텍스트 파일|*.txt", InitialDirectory = AppDomain.CurrentDomain.BaseDirectory })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    try { File.WriteAllText(dialog.FileName, Diagnostics.Describe(input), new UTF8Encoding(true)); MessageBox.Show(this, "진단 정보를 저장했습니다.", Text); }
                    catch (Exception ex) { MessageBox.Show(this, "진단 저장 실패: " + ex.Message, Text); }
                }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (timer != null) timer.Dispose();
                engine.Cancel();
                if (input != null) input.Dispose();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
            }
            base.Dispose(disposing);
        }
    }
}
