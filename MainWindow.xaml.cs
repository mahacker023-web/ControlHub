using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using WinForms = System.Windows.Forms;

namespace MyControlApp
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID = 9000;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_C = 0x43; // Key 'C'

        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            HwndSource source = HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(HwndHook);

            // Hotkey: Win + Shift + C
            RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_WIN | MOD_SHIFT, VK_C);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                if (this.IsVisible) 
                    this.Hide();
                else 
                { 
                    this.Show(); 
                    this.Activate(); 
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void BrightnessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                using var mclass = new ManagementClass("root\\WMI", "WmiMonitorBrightnessMethods", null);
                using var instances = mclass.GetInstances();
                foreach (ManagementObject instance in instances)
                {
                    instance.InvokeMethod("WmiSetBrightness", new object[] { uint.MaxValue, (byte)e.NewValue });
                }
            }
            catch { }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Master Volume System Logic
        }

        private void TakeScreenshot_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            System.Threading.Thread.Sleep(200);

            Rectangle bounds = WinForms.Screen.PrimaryScreen.Bounds;
            using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size);
                }
                
                string picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                string filePath = Path.Combine(picturesPath, $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                bitmap.Save(filePath, ImageFormat.Png);
            }
            this.Show();
        }

        private void CloseApp_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            UnregisterHotKey(helper.Handle, HOTKEY_ID);
            base.OnClosed(e);
        }
    }
}
