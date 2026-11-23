using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using TrayIcon = System.Drawing.Icon;
using Rectangle = System.Drawing.Rectangle;
using SystemIcons = System.Drawing.SystemIcons;

namespace NailongDesktop
{
    internal sealed class PetWindow : Window
    {
        private const double FeedSeconds = 2.1;
        private const double WalkSeconds = 1.85;
        private readonly Preferences settings;
        private readonly PetView pet = new PetView();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly DispatcherTimer timer;
        private readonly LaughSound sound = new LaughSound();
        private readonly Random random = new Random();
        private readonly Forms.NotifyIcon tray;
        private readonly Forms.ContextMenuStrip trayMenu = new Forms.ContextMenuStrip();
        private readonly TrayIcon trayIcon;
        private readonly Forms.ToolStripMenuItem visibilityItem;
        private readonly Forms.ToolStripMenuItem sleepItem;
        private readonly Forms.ToolStripMenuItem soundItem;
        private readonly Forms.ToolStripMenuItem topmostItem;
        private readonly Forms.ToolStripMenuItem wanderItem;
        private readonly Forms.ToolStripMenuItem startupItem;
        private readonly Forms.ToolStripMenuItem[] sizes;

        private IntPtr handle;
        private PetAction action;
        private double actionStarted;
        private double nextWalk = 20;
        private int direction = 1;
        private int walkFrom;
        private int walkTo;
        private bool sleeping;
        private bool pointerDown;
        private bool dragging;
        private Point mouseOrigin;
        private Point dragAnchor;

        private double Now { get { return clock.Elapsed.TotalSeconds; } }

        public PetWindow(Preferences settings)
        {
            this.settings = settings;
            Title = "奶龙桌面宠物";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = settings.Topmost;
            Content = pet;
            timer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromMilliseconds(55) };
            timer.Tick += Tick;
            MouseLeftButtonDown += BeginPointer;
            MouseMove += MovePointer;
            MouseLeftButtonUp += EndPointer;
            LostMouseCapture += delegate { if (pointerDown) FinishPointer(); };
            MouseRightButtonUp += OpenMenu;
            IsVisibleChanged += delegate
            {
                if (IsVisible) timer.Start();
                else timer.Stop();
            };

            ApplySize(settings.Size);
            SourceInitialized += delegate
            {
                handle = new WindowInteropHelper(this).Handle;
                if (settings.X.HasValue && settings.Y.HasValue)
                    PlaceOnScreen(settings.X.Value, settings.Y.Value);
                else
                    ResetPosition();
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(KeepOnScreen));
            };
            SystemEvents.DisplaySettingsChanged += DisplayChanged;

            trayIcon = TrayIcon.ExtractAssociatedIcon(Forms.Application.ExecutablePath);
            tray = new Forms.NotifyIcon { Text = "奶龙桌面宠物", Icon = trayIcon ?? SystemIcons.Application, ContextMenuStrip = trayMenu, Visible = true };
            tray.MouseDoubleClick += delegate(object sender, Forms.MouseEventArgs e) { if (e.Button == Forms.MouseButtons.Left) ToggleVisibility(); };
            visibilityItem = AddItem("隐藏奶龙", ToggleVisibility);
            AddItem("捧腹大笑", delegate { ShowPet(); Laugh(); });
            AddItem("喂一口饼干", delegate { ShowPet(); Feed(); });
            AddItem("转个身", delegate { ShowPet(); Turn(); });
            sleepItem = AddItem("睡一会儿", ToggleSleep);
            AddSeparator();
            soundItem = AddItem("播放笑声", ToggleSound);
            topmostItem = AddItem("始终置顶", ToggleTopmost);
            wanderItem = AddItem("偶尔散步", ToggleWander);
            var sizeMenu = new Forms.ToolStripMenuItem("桌宠大小");
            sizes = new[] { SizeItem(75), SizeItem(100), SizeItem(125), SizeItem(150) };
            sizeMenu.DropDownItems.AddRange(sizes);
            trayMenu.Items.Add(sizeMenu);
            startupItem = AddItem("登录 Windows 时启动", ToggleStartup);
            AddItem("回到主屏幕右下角", ResetPosition);
            AddSeparator();
            AddItem("退出奶龙", Close);
            trayMenu.Opening += RefreshMenu;
        }

        public void ShowPet()
        {
            if (!IsVisible) Show();
            KeepOnScreen();
        }

        protected override void OnClosed(EventArgs e)
        {
            RememberPosition();
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            timer.Stop();
            sound.Dispose();
            tray.Visible = false;
            tray.Dispose();
            trayMenu.Dispose();
            if (trayIcon != null) trayIcon.Dispose();
            base.OnClosed(e);
        }

        private void BeginPointer(object sender, MouseButtonEventArgs e)
        {
            Point cursor = MessagePoint();
            if (pointerDown || !Mouse.Capture(pet)) return;
            if (action == PetAction.Walk) SetAction(PetAction.Idle);
            WindowRect rect = GetBounds();
            mouseOrigin = cursor;
            dragAnchor = new Point((mouseOrigin.X - rect.Left) / Math.Max(1, rect.Width),
                (mouseOrigin.Y - rect.Top) / Math.Max(1, rect.Height));
            pointerDown = true;
            e.Handled = true;
        }

        private void MovePointer(object sender, MouseEventArgs e)
        {
            if (pointerDown) MovePointerTo(MessagePoint());
        }

        private void MovePointerTo(Point cursor)
        {
            if (!dragging)
            {
                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                if ((cursor - mouseOrigin).Length < 5 * scale) return;
                dragging = true;
            }
            WindowRect rect = GetBounds();
            MovePixels((int)Math.Round(cursor.X - dragAnchor.X * rect.Width), (int)Math.Round(cursor.Y - dragAnchor.Y * rect.Height));
        }

        private void EndPointer(object sender, MouseButtonEventArgs e)
        {
            if (!pointerDown) return;
            MovePointerTo(MessagePoint());
            bool wasDrag = dragging;
            FinishPointer();
            if (!wasDrag) Laugh();
            e.Handled = true;
        }

        private void FinishPointer()
        {
            bool wasDrag = dragging;
            pointerDown = false;
            dragging = false;
            if (Mouse.Captured == pet) Mouse.Capture(null);
            if (!wasDrag) return;
            KeepOnScreen();
            RememberPosition();
            nextWalk = Now + 20;
        }

        private void OpenMenu(object sender, MouseButtonEventArgs e)
        {
            if (pointerDown) return;
            trayMenu.Show(Forms.Cursor.Position);
            e.Handled = true;
        }

        private void Tick(object sender, EventArgs args)
        {
            double now = Now;
            double elapsed = now - actionStarted;
            if ((action == PetAction.Laugh && elapsed >= PetArt.LaughSeconds) || (action == PetAction.Feed && elapsed >= FeedSeconds)
                || (action == PetAction.Turn && elapsed >= PetArt.TurnSeconds))
                SetAction(PetAction.Idle);
            if (action == PetAction.Walk)
            {
                WindowRect rect = GetBounds();
                MovePixels((int)Math.Round(walkFrom + (walkTo - walkFrom) * Math.Min(elapsed / WalkSeconds, 1)), rect.Top);
                if (elapsed >= WalkSeconds)
                {
                    RememberPosition();
                    SetAction(PetAction.Idle);
                }
            }
            if (!pointerDown && action == PetAction.Idle && settings.Wander && now >= nextWalk)
                StartWalk();

            pet.Frame(action, Math.Max(0, now - actionStarted), direction);
        }

        private void SetAction(PetAction next)
        {
            action = next;
            actionStarted = Now;
            timer.Interval = TimeSpan.FromMilliseconds(next == PetAction.Idle || next == PetAction.Sleep ? 55 : 33);
            pet.Frame(next, 0, direction);
        }

        private void Laugh()
        {
            if (action == PetAction.Laugh) return;
            sleeping = false;
            SetAction(PetAction.Laugh);
            nextWalk = Now + 20;
            if (settings.Sound) sound.Play();
        }

        private void Feed()
        {
            sleeping = false;
            sound.Stop();
            SetAction(PetAction.Feed);
            nextWalk = Now + 20;
        }

        private void Turn()
        {
            sleeping = false;
            sound.Stop();
            SetAction(PetAction.Turn);
            nextWalk = Now + 20;
        }

        private void ToggleSleep()
        {
            ShowPet();
            sleeping = !sleeping;
            sound.Stop();
            SetAction(sleeping ? PetAction.Sleep : PetAction.Idle);
            nextWalk = Now + 20;
        }

        private void StartWalk()
        {
            nextWalk = Now + random.Next(22, 49);
            WindowRect rect = GetBounds();
            Rectangle area = WorkArea(rect);
            int stride = (int)Math.Round(5 * rect.Width / 14.0);
            direction = random.Next(2) == 0 ? -1 : 1;
            walkFrom = rect.Left;
            walkTo = Fit(walkFrom + stride * direction, area.Left, area.Right, rect.Width);
            if (Math.Abs(walkTo - walkFrom) < stride / 3)
            {
                direction = -direction;
                walkTo = Fit(walkFrom + stride * direction, area.Left, area.Right, rect.Width);
            }
            if (walkTo != walkFrom) SetAction(PetAction.Walk);
        }

        private void ToggleVisibility()
        {
            if (!IsVisible) { ShowPet(); return; }
            sound.Stop();
            SetAction(sleeping ? PetAction.Sleep : PetAction.Idle);
            nextWalk = Now + 20;
            Hide();
        }

        private void ToggleSound()
        {
            settings.Sound = !settings.Sound;
            if (!settings.Sound) sound.Stop();
            settings.Save();
        }

        private void ToggleTopmost()
        {
            settings.Topmost = !settings.Topmost;
            Topmost = settings.Topmost;
            settings.Save();
        }

        private void ToggleWander()
        {
            settings.Wander = !settings.Wander;
            nextWalk = Now + 5;
            if (!settings.Wander && action == PetAction.Walk) SetAction(PetAction.Idle);
            RememberPosition();
        }

        private void ToggleStartup()
        {
            TryStartup(delegate { Preferences.StartsWithWindows = !Preferences.StartsWithWindows; }, true);
        }

        private void ApplySize(int percent)
        {
            settings.Size = percent;
            Width = PetArt.Width * percent / 100;
            Height = PetArt.Height * percent / 100;
            if (handle == IntPtr.Zero) return;
            if (action == PetAction.Walk) SetAction(PetAction.Idle);
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate { KeepOnScreen(); RememberPosition(); }));
        }

        private Forms.ToolStripMenuItem SizeItem(int percent)
        {
            return new Forms.ToolStripMenuItem(percent + " %", null, delegate { ApplySize(percent); }) { Tag = percent };
        }

        private Forms.ToolStripMenuItem AddItem(string label, Action handler)
        {
            var item = new Forms.ToolStripMenuItem(label, null, delegate { handler(); });
            trayMenu.Items.Add(item);
            return item;
        }

        private void AddSeparator() { trayMenu.Items.Add(new Forms.ToolStripSeparator()); }

        private void RefreshMenu(object sender, CancelEventArgs args)
        {
            visibilityItem.Text = IsVisible ? "隐藏奶龙" : "显示奶龙";
            sleepItem.Text = sleeping ? "叫醒奶龙" : "睡一会儿";
            soundItem.Checked = settings.Sound;
            topmostItem.Checked = settings.Topmost;
            wanderItem.Checked = settings.Wander;
            startupItem.Enabled = TryStartup(delegate { startupItem.Checked = Preferences.StartsWithWindows; }, false);
            foreach (Forms.ToolStripMenuItem item in sizes) item.Checked = (int)item.Tag == settings.Size;
        }

        private bool TryStartup(Action change, bool showError)
        {
            try { change(); return true; }
            catch (Exception e)
            {
                if (!(e is IOException || e is UnauthorizedAccessException || e is SecurityException)) throw;
                if (showError) MessageBox.Show(this, "Windows 拒绝修改登录启动设置。", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private void ResetPosition()
        {
            if (handle == IntPtr.Zero) return;
            if (action == PetAction.Walk) SetAction(PetAction.Idle);
            Rectangle area = Forms.Screen.PrimaryScreen.WorkingArea;
            WindowRect rect = GetBounds();
            MovePixels(Math.Max(area.Left, area.Right - rect.Width - 12), Math.Max(area.Top, area.Bottom - rect.Height - 6));
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate { KeepOnScreen(); RememberPosition(); }));
        }

        private void DisplayChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (action == PetAction.Walk) SetAction(PetAction.Idle);
                KeepOnScreen();
                RememberPosition();
            }));
        }

        private void KeepOnScreen()
        {
            if (handle == IntPtr.Zero || dragging) return;
            WindowRect rect = GetBounds();
            PlaceOnScreen(rect.Left, rect.Top);
        }

        private void PlaceOnScreen(int x, int y)
        {
            WindowRect rect = GetBounds();
            Rectangle area = Forms.Screen.FromPoint(new System.Drawing.Point(x, y)).WorkingArea;
            MovePixels(Fit(x, area.Left, area.Right, rect.Width), Fit(y, area.Top, area.Bottom, rect.Height));
        }

        private void RememberPosition()
        {
            WindowRect rect = GetBounds();
            if (rect.Width > 0)
            {
                settings.X = rect.Left;
                settings.Y = rect.Top;
            }
            settings.Save();
        }

        private static Rectangle WorkArea(WindowRect rect)
        {
            return Forms.Screen.FromRectangle(new Rectangle(rect.Left, rect.Top, rect.Width, rect.Height)).WorkingArea;
        }

        private static int Fit(int value, int start, int end, int extent)
        {
            return Math.Max(start, Math.Min(value, end - extent));
        }

        private static Point MessagePoint()
        {
            // Read the coordinates of this input message even if the cursor has already moved.
            uint packed = GetMessagePos();
            return new Point(unchecked((short)packed), unchecked((short)(packed >> 16)));
        }

        private WindowRect GetBounds()
        {
            WindowRect rect;
            GetWindowRect(handle, out rect);
            return rect;
        }

        private void MovePixels(int x, int y)
        {
            const uint flags = 0x0001 | 0x0004 | 0x0010; // Preserve size, stacking order and activation.
            SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, flags);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowRect
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);

        [DllImport("user32.dll")]
        private static extern uint GetMessagePos();

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
