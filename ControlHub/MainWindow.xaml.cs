using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using Screen = System.Windows.Forms.Screen;

namespace MyControlApp
{
    public partial class MainWindow : Window
    {
        // ---------- Win32 hotkey ----------
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID = 9000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        private uint _currentModifiers = MOD_WIN | MOD_SHIFT;
        private uint _currentVk = 0x43; // 'C'
        private bool _listeningForHotkey = false;

        private readonly string _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ControlHub", "settings.txt");

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            HwndSource? source = HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(HwndHook);

            RegisterHotKey(helper.Handle, HOTKEY_ID, _currentModifiers, _currentVk);

            // Sync sliders with real current system values on startup
            VolumeSlider.Value = (int)Math.Round(AudioHelper.GetVolume() * 100);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleOverlay();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ToggleOverlay()
        {
            if (this.Visibility == Visibility.Visible)
            {
                this.Hide();
            }
            else
            {
                this.Show();
                this.Activate();
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) this.DragMove();
        }

        private void Hide_Click(object sender, RoutedEventArgs e) => this.Hide();

        // ---------- Brightness ----------
        private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (BrightnessValueText != null)
                BrightnessValueText.Text = $"{(int)e.NewValue}%";

            try
            {
                using var mclass = new ManagementClass("root\\WMI", "WmiMonitorBrightnessMethods", null);
                using var instances = mclass.GetInstances();
                foreach (ManagementObject instance in instances)
                {
                    instance.InvokeMethod("WmiSetBrightness", new object[] { uint.MaxValue, (byte)e.NewValue });
                }
            }
            catch
            {
                // Some external monitors don't support WMI brightness — safe to ignore
            }
        }

        // ---------- Volume ----------
        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VolumeValueText != null)
                VolumeValueText.Text = $"{(int)e.NewValue}%";

            try
            {
                AudioHelper.SetVolume((float)(e.NewValue / 100.0));
            }
            catch
            {
                // Ignore if no audio device is active
            }
        }

        // ---------- Screenshot ----------
        private void TakeScreenshot_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            System.Threading.Thread.Sleep(250);

            Rectangle bounds = Screen.PrimaryScreen!.Bounds;
            using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size);
                }

                string folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                string path = Path.Combine(folder, $"ControlHub_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                bitmap.Save(path, ImageFormat.Png);
            }

            this.Show();
            this.Activate();
        }

        // ---------- Custom hotkey ----------
        private void ChangeHotkey_Click(object sender, RoutedEventArgs e)
        {
            _listeningForHotkey = true;
            HotkeyDisplayText.Text = "Press new keys...";
            ChangeHotkeyButton.IsEnabled = false;
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_listeningForHotkey) return;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Ignore lone modifier presses — wait for an actual key
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return;

            uint modifiers = 0;
            var mods = Keyboard.Modifiers;
            var display = new StringBuilder();

            if (mods.HasFlag(ModifierKeys.Windows)) { modifiers |= MOD_WIN; display.Append("Win + "); }
            if (mods.HasFlag(ModifierKeys.Control)) { modifiers |= MOD_CONTROL; display.Append("Ctrl + "); }
            if (mods.HasFlag(ModifierKeys.Alt)) { modifiers |= MOD_ALT; display.Append("Alt + "); }
            if (mods.HasFlag(ModifierKeys.Shift)) { modifiers |= MOD_SHIFT; display.Append("Shift + "); }

            if (modifiers == 0)
            {
                // Require at least one modifier so the hotkey doesn't clash with normal typing
                return;
            }

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            display.Append(key.ToString());

            var helper = new WindowInteropHelper(this);
            UnregisterHotKey(helper.Handle, HOTKEY_ID);

            _currentModifiers = modifiers;
            _currentVk = vk;

            RegisterHotKey(helper.Handle, HOTKEY_ID, _currentModifiers, _currentVk);

            HotkeyDisplayText.Text = display.ToString();
            FooterHintText.Text = $"Press {display} to toggle this panel";
            ChangeHotkeyButton.IsEnabled = true;
            _listeningForHotkey = false;

            SaveSettings();
            e.Handled = true;
        }

        // ---------- Settings persistence ----------
        private void SaveSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(_settingsPath)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(_settingsPath, $"{_currentModifiers}|{_currentVk}|{HotkeyDisplayText.Text}");
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return;
                var parts = File.ReadAllText(_settingsPath).Split('|');
                if (parts.Length == 3
                    && uint.TryParse(parts[0], out uint mods)
                    && uint.TryParse(parts[1], out uint vk))
                {
                    _currentModifiers = mods;
                    _currentVk = vk;
                    HotkeyDisplayText.Text = parts[2];
                    FooterHintText.Text = $"Press {parts[2]} to toggle this panel";
                }
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            UnregisterHotKey(helper.Handle, HOTKEY_ID);
            base.OnClosed(e);
        }
    }

    // ---------- CoreAudio volume control (no NuGet needed) ----------
    internal static class AudioHelper
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        private enum EDataFlow { eRender, eCapture, eAll }
        private enum ERole { eConsole, eMultimedia, eCommunications }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(EDataFlow dataFlow, int dwStateMask, out IntPtr ppDevices);
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
            int GetDevice(string pwstrId, out IMMDevice ppDevice);
            int RegisterEndpointNotificationCallback(IntPtr pClient);
            int UnregisterEndpointNotificationCallback(IntPtr pClient);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IAudioEndpointVolume ppInterface);
            int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
            int GetId(out string ppstrId);
            int GetState(out int pdwState);
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out int pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetChannelVolumeLevel(int nChannel, float fLevelDB, ref Guid pguidEventContext);
            int SetChannelVolumeLevelScalar(int nChannel, float fLevel, ref Guid pguidEventContext);
            int GetChannelVolumeLevel(int nChannel, out float pfLevelDB);
            int GetChannelVolumeLevelScalar(int nChannel, out float pfLevel);
            int SetMute(bool bMute, ref Guid pguidEventContext);
            int GetMute(out bool pbMute);
            int GetVolumeStepInfo(out int pnStep, out int pnStepCount);
            int VolumeStepUp(ref Guid pguidEventContext);
            int VolumeStepDown(ref Guid pguidEventContext);
            int QueryHardwareSupport(out int pdwHardwareSupportMask);
            int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
        }

        private static IAudioEndpointVolume GetVolumeInterface()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IMMDevice device);
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out IAudioEndpointVolume epVolume);
            return epVolume;
        }

        public static float GetVolume()
        {
            var vol = GetVolumeInterface();
            vol.GetMasterVolumeLevelScalar(out float level);
            return level;
        }

        public static void SetVolume(float level)
        {
            level = Math.Clamp(level, 0f, 1f);
            var vol = GetVolumeInterface();
            Guid guid = Guid.Empty;
            vol.SetMasterVolumeLevelScalar(level, ref guid);
        }
    }
}
