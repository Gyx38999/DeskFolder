using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DeskFolder
{
    public class Folder
    {
        public string Name { get; set; }
        public string Path { get; set; }
    }

    public class Config
    {
        public List<Folder> Folders { get; set; }
        public bool All { get; set; }
        public List<string> Ignored { get; set; }
        public string Color { get; set; }
        public int Alpha { get; set; }
        public bool Clear { get; set; }
        public Dictionary<string, string> Icons { get; set; }
        public bool Tray { get; set; }
        public bool ShowLabels { get; set; }
        public Config() { Folders = new List<Folder>(); All = true; Ignored = new List<string>(); Color = "#12141B"; Alpha = 238; Clear = false; Icons = new Dictionary<string, string>(); Tray = true; ShowLabels = true; }
    }

    public class DeskItem
    {
        public string Name;
        public Rect R;
    }

    public static class Desk
    {
        public const int LVM_GETITEMCOUNT = 0x1004;
        public const int LVM_GETITEMTEXT = 0x1073;
        public const int LVM_GETITEMRECT = 0x100E;

        [DllImport("user32.dll")] static extern IntPtr FindWindow(string c, string t);
        [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr p, IntPtr c, string cls, string t);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int acc, bool inh, int pid);
        [DllImport("kernel32.dll")] static extern IntPtr VirtualAllocEx(IntPtr hp, IntPtr addr, uint size, uint type, uint prot);
        [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(IntPtr hp, IntPtr addr, uint size, uint type);
        [DllImport("kernel32.dll")] static extern bool WriteProcessMemory(IntPtr hp, IntPtr addr, byte[] buf, uint size, out IntPtr written);
        [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(IntPtr hp, IntPtr addr, byte[] buf, uint size, out IntPtr read);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern void SHChangeNotify(int ev, uint flags, IntPtr a, IntPtr b);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }

        public static IntPtr ListView()
        {
            IntPtr dv = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv == IntPtr.Zero)
            {
                IntPtr w = IntPtr.Zero;
                while (true)
                {
                    w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null);
                    if (w == IntPtr.Zero) break;
                    IntPtr d = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (d != IntPtr.Zero) { dv = d; break; }
                }
            }
            if (dv == IntPtr.Zero) return IntPtr.Zero;
            return FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
        }

        public static List<DeskItem> Items()
        {
            List<DeskItem> res = new List<DeskItem>();
            IntPtr lv = ListView();
            if (lv == IntPtr.Zero) return res;
            uint pid = 0;
            GetWindowThreadProcessId(lv, out pid);
            IntPtr hp = OpenProcess(0x0008 | 0x0010 | 0x0020 | 0x0400, false, (int)pid);
            if (hp == IntPtr.Zero) return res;
            IntPtr buf = VirtualAllocEx(hp, IntPtr.Zero, 4096, 0x3000, 0x04);
            if (buf == IntPtr.Zero) return res;
            try
            {
                int cnt = (int)SendMessage(lv, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
                if (cnt > 400) cnt = 400;
                for (int i = 0; i < cnt; i++)
                {
                    IntPtr txt = buf + 1024;
                    byte[] lvi = new byte[64];
                    BitConverter.GetBytes(1).CopyTo(lvi, 0);
                    BitConverter.GetBytes(i).CopyTo(lvi, 4);
                    BitConverter.GetBytes(txt.ToInt64()).CopyTo(lvi, 24);
                    BitConverter.GetBytes(260).CopyTo(lvi, 32);
                    IntPtr wr;
                    WriteProcessMemory(hp, buf, lvi, 64, out wr);
                    SendMessage(lv, LVM_GETITEMTEXT, (IntPtr)i, buf);
                    byte[] rb = new byte[520];
                    IntPtr rr;
                    ReadProcessMemory(hp, txt, rb, 520, out rr);
                    string name = Encoding.Unicode.GetString(rb).Split('\0')[0];

                    SendMessage(lv, LVM_GETITEMRECT, (IntPtr)i, buf);
                    byte[] r4 = new byte[16];
                    ReadProcessMemory(hp, buf, r4, 16, out rr);
                    POINT a = new POINT();
                    a.X = BitConverter.ToInt32(r4, 0); a.Y = BitConverter.ToInt32(r4, 4);
                    POINT b = new POINT();
                    b.X = BitConverter.ToInt32(r4, 8); b.Y = BitConverter.ToInt32(r4, 12);
                    ClientToScreen(lv, ref a);
                    ClientToScreen(lv, ref b);
                    DeskItem it = new DeskItem();
                    it.Name = name;
                    it.R = new Rect(a.X, a.Y, b.X - a.X, b.Y - a.Y);
                    res.Add(it);
                }
            }
            finally
            {
                VirtualFreeEx(hp, buf, 0, 0x8000);
            }
            return res;
        }

        public static void RefreshDesktop()
        {
            SHChangeNotify(0x8000000, 0x1000, IntPtr.Zero, IntPtr.Zero);
        }

        public static Rect HoverZone(Rect icon)
        {
            return new Rect(icon.Left - 8, icon.Top - 8, icon.Width + 16, icon.Height + 86);
        }
    }

    public static class Core
    {
        public static double Scale = 1.0;
        public static Config cfg;
        public static string cfgPath;
        public static string desktop;
        public static ManagerWindow Mgr;
        public static PanelWindow Panel;
        public static ZoomWindow Zoom;
        public static DispatcherTimer Watcher;
        public static List<DeskItem> Cache = new List<DeskItem>();
        public static DateTime CacheTime = DateTime.MinValue;
        static BitmapSource folderIcon;

        public static void RefreshPanelLabels()
        {
            if (Panel == null || !Panel.IsVisible || Panel.Opacity < 0.2) return;
            Folder f = Panel.Current();
            if (f != null) Panel.Open(f);
        }

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int i);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);

        public static double U(double v) { return v; }

        public static void Init()
        {
            IntPtr dc = GetDC(IntPtr.Zero);
            int dpi = GetDeviceCaps(dc, 88);
            ReleaseDC(IntPtr.Zero, dc);
            if (dpi <= 0) dpi = 96;
            Scale = dpi / 96.0;
            desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            cfgPath = Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskFolder"), "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(cfgPath));
            if (File.Exists(cfgPath))
            {
                try
                {
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    cfg = ser.Deserialize<Config>(File.ReadAllText(cfgPath, Encoding.UTF8));
                }
                catch { cfg = null; }
            }
            if (cfg == null) cfg = new Config();
            if (cfg.Folders == null) cfg.Folders = new List<Folder>();
            if (cfg.Ignored == null) cfg.Ignored = new List<string>();
            if (cfg.Icons == null) cfg.Icons = new Dictionary<string, string>();
            try
            {
                List<string> dead = new List<string>();
                foreach (KeyValuePair<string, string> kv in cfg.Icons)
                    if (!File.Exists(kv.Value) || !Directory.Exists(kv.Key)) dead.Add(kv.Key);
                if (dead.Count > 0)
                {
                    foreach (string k in dead)
                    {
                        string png = cfg.Icons[k];
                        cfg.Icons.Remove(k);
                        try { if (png != null && File.Exists(png)) File.Delete(png); } catch { }
                        try
                        {
                            string ico = Path.ChangeExtension(png, ".ico");
                            if (ico != null && File.Exists(ico)) File.Delete(ico);
                        }
                        catch { }
                    }
                    Save();
                }
            }
            catch { }
        }

        public static bool StartupOn()
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                if (k == null) return false;
                object v = k.GetValue("DeskFolder");
                k.Close();
                return v != null;
            }
            catch { return false; }
        }

        public static void SetStartup(bool on)
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (k == null) return;
                if (on) k.SetValue("DeskFolder", "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"");
                else k.DeleteValue("DeskFolder", false);
                k.Close();
            }
            catch (Exception ex)
            {
                MsgBox.Show("设置开机启动失败：\n" + ex.Message);
            }
        }

        public static bool IsIgnored(string name)        {
            foreach (string s in cfg.Ignored) if (string.Equals(s, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static List<Folder> Targets()
        {
            List<Folder> res = new List<Folder>();
            if (cfg.All)
            {
                try
                {
                    foreach (string d in Directory.GetDirectories(desktop))
                    {
                        string n = Path.GetFileName(d);
                        if (IsIgnored(n)) continue;
                        Folder f = new Folder();
                        f.Name = n;
                        f.Path = d;
                        res.Add(f);
                    }
                }
                catch { }
                return res;
            }
            foreach (Folder f in cfg.Folders)
            {
                if (f.Path == null || !Directory.Exists(f.Path)) continue;
                if (f.Path.StartsWith(desktop, StringComparison.OrdinalIgnoreCase)) res.Add(f);
            }
            return res;
        }

        public static void Save()
        {
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                File.WriteAllText(cfgPath, ser.Serialize(cfg), new UTF8Encoding(false));
            }
            catch { }
        }

        public static Point Cursor()
        {
            POINT p = new POINT();
            GetCursorPos(out p);
            return new Point(p.X, p.Y);
        }

        public static Rect WinRect(Window w)
        {
            RECT r = new RECT();
            GetWindowRect(new WindowInteropHelper(w).Handle, out r);
            return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }

        public static BitmapSource FolderIcon()
        {
            if (folderIcon != null) return folderIcon;
            SHSTOCKICONINFO si = new SHSTOCKICONINFO();
            si.cbSize = (uint)Marshal.SizeOf(si);
            if (SHGetStockIconInfo(3, 0x100, ref si) == 0 && si.hIcon != IntPtr.Zero)
            {
                folderIcon = Imaging.CreateBitmapSourceFromHIcon(si.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                DestroyIcon(si.hIcon);
            }
            return folderIcon;
        }

        public static string IconsDir()
        {
            string d = Path.Combine(Path.GetDirectoryName(cfgPath), "icons");
            try { Directory.CreateDirectory(d); } catch { }
            return d;
        }

        public static string IconKey(string p)
        {
            try { return Path.GetFullPath(p).TrimEnd('\\').ToLowerInvariant(); }
            catch { return (p ?? "").ToLowerInvariant(); }
        }

        public static BitmapSource CustomIconPlain(string folder)
        {
            try
            {
                if (cfg == null || cfg.Icons == null || folder == null) return null;
                string file;
                if (!cfg.Icons.TryGetValue(IconKey(folder), out file) || string.IsNullOrEmpty(file)) return null;
                if (!File.Exists(file)) return null;
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(file);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        public static BitmapSource CustomIconFor(string folder)
        {
            BitmapSource bi = CustomIconPlain(folder);
            if (bi == null) return null;
            return Normalize(bi);
        }

        public static bool SetFolderIcon(string folder, string imageFile, out string err)
        {
            err = "";
            try
            {
                BitmapImage bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(imageFile);
                bi.EndInit();
                bi.Freeze();

                string dir = IconsDir();
                string stem = DateTime.Now.Ticks.ToString("x");
                string png = Path.Combine(dir, stem + ".png");
                using (FileStream fs = new FileStream(png, FileMode.Create, FileAccess.Write))
                {
                    PngBitmapEncoder pe = new PngBitmapEncoder();
                    pe.Frames.Add(BitmapFrame.Create(bi));
                    pe.Save(fs);
                }
                string ico = Path.Combine(dir, stem + ".ico");
                File.WriteAllBytes(ico, BuildIco(bi));

                cfg.Icons[IconKey(folder)] = png;
                Save();
                WriteFolderIni(folder, ico);
                RefreshFolderIcon(folder);
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        public static void ClearFolderIcon(string folder)
        {
            try
            {
                cfg.Icons.Remove(IconKey(folder));
                Save();
                string ini = Path.Combine(folder, "desktop.ini");
                if (File.Exists(ini))
                {
                    File.SetAttributes(ini, FileAttributes.Normal);
                    File.Delete(ini);
                }
                try { File.SetAttributes(folder, FileAttributes.Directory); } catch { }
                RefreshFolderIcon(folder);
            }
            catch { }
        }

        static void WriteFolderIni(string folder, string ico)
        {
            string ini = Path.Combine(folder, "desktop.ini");
            if (File.Exists(ini)) { File.SetAttributes(ini, FileAttributes.Normal); File.Delete(ini); }
            File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=" + ico + ",0\r\n", new UTF8Encoding(false));
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
            try { File.SetAttributes(folder, FileAttributes.ReadOnly); } catch { }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern void SHChangeNotify(int ev, uint flags, string a, string b);

        static void RefreshFolderIcon(string folder)
        {
            ClearIconCache();
            try { SHChangeNotify(0x00002000, 0x0005, folder, null); } catch { }
            try { SHChangeNotify(0x08000000, 0x1000, null, null); } catch { }
        }

        static BitmapSource ScaleIcon(BitmapSource src, int s)
        {
            double sx = (double)s / src.PixelWidth;
            TransformedBitmap tb = new TransformedBitmap(src, new ScaleTransform(sx, sx));
            FormatConvertedBitmap cb = new FormatConvertedBitmap(tb, PixelFormats.Bgra32, null, 0);
            cb.Freeze();
            return cb;
        }

        static byte[] DibFor(BitmapSource b)
        {
            int s = b.PixelWidth;
            int stride = s * 4;
            byte[] px = new byte[stride * s];
            b.CopyPixels(px, stride, 0);
            int maskStride = ((s + 31) / 32) * 4;
            byte[] res = new byte[40 + stride * s + maskStride * s];
            using (MemoryStream ms = new MemoryStream(res))
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write(40); bw.Write(s); bw.Write(s * 2); bw.Write((short)1); bw.Write((short)32);
                bw.Write(0); bw.Write(stride * s); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
                for (int y = s - 1; y >= 0; y--) bw.Write(px, y * stride, stride);
                bw.Write(new byte[maskStride * s]);
            }
            return res;
        }

        static byte[] BuildIco(BitmapSource src)
        {
            int[] sizes = { 256, 128, 64, 48, 32, 16 };
            List<byte[]> imgs = new List<byte[]>();
            foreach (int s in sizes) imgs.Add(DibFor(ScaleIcon(src, s)));
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
                int off = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    bw.Write((byte)(s == 256 ? 0 : s));
                    bw.Write((byte)(s == 256 ? 0 : s));
                    bw.Write((byte)0); bw.Write((byte)0);
                    bw.Write((short)1); bw.Write((short)32);
                    bw.Write(imgs[i].Length);
                    bw.Write(off);
                    off += imgs[i].Length;
                }
                foreach (byte[] d in imgs) bw.Write(d);
                return ms.ToArray();
            }
        }

        static readonly Dictionary<string, BitmapSource> iconCache = new Dictionary<string, BitmapSource>();
        static readonly Dictionary<string, string[]> listCache = new Dictionary<string, string[]>();

        static string Stamp(string path)
        {
            try
            {
                if (File.Exists(path)) return File.GetLastWriteTimeUtc(path).Ticks.ToString();
                DirectoryInfo di = new DirectoryInfo(path);
                if (di.Exists) return di.LastWriteTimeUtc.Ticks.ToString();
            }
            catch { }
            return "0";
        }

        public static void ClearIconCache()
        {
            lock (iconCache) iconCache.Clear();
            lock (listCache) listCache.Clear();
        }

        static System.Windows.Forms.NotifyIcon tray;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string name);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string cls, string title);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        public static readonly uint MsgShow = RegisterWindowMessage("DeskFolderShowManager");

        public static void ShowManager()
        {
            try
            {
                if (Mgr == null) return;
                Mgr.Show();
                if (Mgr.WindowState == WindowState.Minimized) Mgr.WindowState = WindowState.Normal;
                Mgr.Activate();
                Mgr.RebuildList();
            }
            catch { }
        }

        public static void ExitAll()
        {
            try { if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; } }
            catch { }
            Application.Current.Shutdown();
        }

        public static void InitTray()
        {
            try
            {
                tray = new System.Windows.Forms.NotifyIcon();
                string ico = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "悬浮文件夹.ico");
                if (File.Exists(ico)) tray.Icon = new System.Drawing.Icon(ico);
                else tray.Icon = System.Drawing.SystemIcons.Application;
                tray.Text = "DeskFolder";
                System.Windows.Forms.ContextMenuStrip mn = new System.Windows.Forms.ContextMenuStrip();
                mn.BackColor = System.Drawing.Color.FromArgb(17, 22, 33);
                mn.ForeColor = System.Drawing.Color.FromArgb(236, 241, 250);
                mn.Renderer = new DarkMenuRenderer();
                mn.ShowImageMargin = false;
                try { mn.Font = new System.Drawing.Font("Microsoft YaHei UI", 9f); }
                catch { }
                System.Windows.Forms.ToolStripMenuItem mi = new System.Windows.Forms.ToolStripMenuItem("打开设置");
                mi.Click += delegate { ShowManager(); };
                System.Windows.Forms.ToolStripMenuItem mq = new System.Windows.Forms.ToolStripMenuItem("退出");
                mq.Click += delegate { ExitAll(); };
                mn.Items.Add(mi);
                mn.Items.Add(mq);
                foreach (System.Windows.Forms.ToolStripItem it in mn.Items) it.ForeColor = System.Drawing.Color.FromArgb(236, 241, 250);
                tray.ContextMenuStrip = mn;
                tray.DoubleClick += delegate { ShowManager(); };
                tray.Visible = true;
            }
            catch { }
        }

        static bool SkipEntry(string p)
        {
            string n = Path.GetFileName(p).ToLowerInvariant();
            return n == "desktop.ini" || n == "thumbs.db" || n == "ehthumbs.db" || n == ".ds_store";
        }

        static readonly Dictionary<string, string> NameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"Google Chrome","Chrome"},{"Microsoft Edge","Edge"},{"Mozilla Firefox","Firefox"},
            {"Wallpaper Engine","Wallpaper"},{"Epic Games Launcher","Epic"},{"EA App","EA"},
            {"WeChat","微信"},{"Tencent QQ","QQ"},{"Discord","Discord"},{"Spotify","Spotify"},
            {"Visual Studio Code","VS Code"},{"Adobe Photoshop","Photoshop"},{"7-Zip File Manager","7-Zip"},
            {"Windows Media Player","WMP"},{"Origin","Origin"},{"Ubisoft Connect","Ubisoft"},
            {"百度网盘","百度网盘"},{"迅雷","迅雷"},{"钉钉","钉钉"},{"企业微信","企业微信"}
        };

        public static string DisplayName(string path)
        {
            string raw = Directory.Exists(path) ? new DirectoryInfo(path).Name : Path.GetFileNameWithoutExtension(path);
            string s = raw;
            string[] tails = { " - 快捷方式", "–快捷方式", "-快捷方式", " - Shortcut", " Shortcut" };
            foreach (string t in tails)
                if (s.EndsWith(t, StringComparison.OrdinalIgnoreCase)) { s = s.Substring(0, s.Length - t.Length); break; }
            string mapped;
            if (NameMap.TryGetValue(s, out mapped)) return mapped;
            return s;
        }

        public static string[] EntriesOf(string path)
        {
            string key = path + "|" + Stamp(path);
            lock (listCache) { string[] c; if (listCache.TryGetValue(key, out c)) return c; }
            string[] all;
            try { all = Directory.GetFileSystemEntries(path); }
            catch { all = new string[0]; }
            string[] keep = new string[all.Length];
            int n = 0;
            for (int i = 0; i < all.Length; i++) if (!SkipEntry(all[i])) keep[n++] = all[i];
            string[] res = keep;
            if (n != all.Length)
            {
                res = new string[n];
                for (int i = 0; i < n; i++) res[i] = keep[i];
            }
            lock (listCache) { if (listCache.Count > 400) listCache.Clear(); listCache[key] = res; }
            return res;
        }

        public static BitmapSource IconFor(string path)
        {
            string key = path + "|" + Stamp(path);
            lock (iconCache) { BitmapSource c; if (iconCache.TryGetValue(key, out c)) return c; }
            BitmapSource b = IconForRaw(path);
            if (b != null)
            {
                lock (iconCache) { if (iconCache.Count > 800) iconCache.Clear(); iconCache[key] = b; }
            }
            return b;
        }

        public static BitmapSource PlainIconFor(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    BitmapSource ci = CustomIconPlain(path);
                    if (ci != null) return ci;
                }
            }
            catch { }
            try
            {
                int idx = 0;
                string ico = ShortcutIcon(path, out idx);
                if (ico != null)
                {
                    BitmapSource ib = IconFromFile(ico, idx);
                    if (ib != null) return ib;
                }
            }
            catch { }
            try
            {
                BitmapSource b = ShellImage(path);
                if (b != null) return b;
            }
            catch { }
            try
            {
                if (Directory.Exists(path)) return FolderIcon();
                SHFILEINFO fi = new SHFILEINFO();
                uint attr = 0x80u;
                IntPtr r = SHGetFileInfo(path, attr, ref fi, (uint)Marshal.SizeOf(fi), 0x100);
                if (r != IntPtr.Zero && fi.hIcon != IntPtr.Zero)
                {
                    BitmapSource b = Imaging.CreateBitmapSourceFromHIcon(fi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    DestroyIcon(fi.hIcon);
                    return b;
                }
            }
            catch { }
            return IconFor(path);
        }

        static BitmapSource IconForRaw(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    BitmapSource ci = CustomIconFor(path);
                    if (ci != null) return ci;
                }
            }
            catch { }
            try
            {
                int idx = 0;
                string ico = ShortcutIcon(path, out idx);
                if (ico != null)
                {
                    BitmapSource ib = IconFromFile(ico, idx);
                    if (ib != null) return Normalize(ib);
                }
            }
            catch { }
            try
            {
                BitmapSource b = ShellImage(path);
                if (b != null) return Normalize(b);
            }
            catch { }
            try
            {
                if (Directory.Exists(path)) return Normalize(FolderIcon());
                SHFILEINFO fi = new SHFILEINFO();
                uint attr = Directory.Exists(path) ? 0x10u : 0x80u;
                IntPtr r = SHGetFileInfo(path, attr, ref fi, (uint)Marshal.SizeOf(fi), 0x100 | 0x0);
                if (r != IntPtr.Zero && fi.hIcon != IntPtr.Zero)
                {
                    BitmapSource b = Imaging.CreateBitmapSourceFromHIcon(fi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    DestroyIcon(fi.hIcon);
                    return Normalize(b);
                }
            }
            catch { }
            return Normalize(FolderIcon());
        }

        static byte pa(byte[] px, int i) { return px[i * 4 + 3]; }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class CShellLink { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellLinkW
        {
            void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
            void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        }

        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint PrivateExtractIcons(string file, int index, int cx, int cy, IntPtr[] phicon, int[] piconid, uint nIcons, uint flags);

        static string ShortcutIcon(string path, out int idx)
        {
            idx = 0;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".url")
            {
                string ico = null;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string t = raw.Trim();
                    if (t.StartsWith("IconFile=", StringComparison.OrdinalIgnoreCase)) ico = t.Substring(9).Trim().Trim('"');
                    else if (t.StartsWith("IconIndex=", StringComparison.OrdinalIgnoreCase)) int.TryParse(t.Substring(10).Trim(), out idx);
                }
                if (ico != null && ico.Length > 0 && File.Exists(ico)) return ico;
                return null;
            }
            if (ext == ".lnk")
            {
                try
                {
                    IShellLinkW lnk = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046")));
                    ((IPersistFile)lnk).Load(path, 0);
                    StringBuilder sb = new StringBuilder(1024);
                    lnk.GetIconLocation(sb, sb.Capacity, out idx);
                    string ico = sb.ToString();
                    if (ico.Length > 0)
                    {
                        ico = Environment.ExpandEnvironmentVariables(ico.Trim('"'));
                        if (File.Exists(ico)) return ico;
                    }
                    StringBuilder exe = new StringBuilder(1024);
                    lnk.GetPath(exe, exe.Capacity, IntPtr.Zero, 0);
                    string target = exe.ToString();
                    if (target.Length > 0 && File.Exists(target)) { idx = 0; return target; }
                }
                catch { }
                return null;
            }
            return null;
        }

        static BitmapSource IconFromFile(string file, int index)
        {
            try
            {
                IntPtr[] h = new IntPtr[1];
                int[] id = new int[1];
                uint got = PrivateExtractIcons(file, index, 256, 256, h, id, 1, 0);
                if (got == 0 || h[0] == IntPtr.Zero) return null;
                BitmapSource b = Imaging.CreateBitmapSourceFromHIcon(h[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                DestroyIcon(h[0]);
                return b;
            }
            catch { return null; }
        }

        static BitmapSource Normalize(BitmapSource src)
        {
            try
            {                BitmapSource b = src;
                if (b.Format != PixelFormats.Bgra32)
                    b = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                int w = b.PixelWidth, h = b.PixelHeight;
                byte[] px = new byte[w * h * 4];
                b.CopyPixels(px, w * 4, 0);
                byte[] keyed = (byte[])px.Clone();
                for (int i = 0; i < w * h; i++)
                {
                    int o = i * 4;
                    if (keyed[o + 3] <= 70 && keyed[o] >= 225 && keyed[o + 1] >= 225 && keyed[o + 2] >= 225) keyed[o + 3] = 0;
                }
                int minX = w, minY = h, maxX = -1, maxY = -1;
                int[] th = new int[] { 100, 12 };
                foreach (int t in th)
                {
                    minX = w; minY = h; maxX = -1; maxY = -1;
                    for (int y = 0; y < h; y++)
                    {
                        int row = y * w * 4;
                        for (int x = 0; x < w; x++)
                        {
                            if (keyed[row + x * 4 + 3] >= t)
                            {
                                if (x < minX) minX = x;
                                if (x > maxX) maxX = x;
                                if (y < minY) minY = y;
                                if (y > maxY) maxY = y;
                            }
                        }
                    }
                    if (maxX >= 0) break;
                }
                if (maxX < 0 || minX > maxX || minY > maxY) return src;
                int cw = maxX - minX + 1, ch = maxY - minY + 1;
                if (cw < 4 || ch < 4) return src;
#if FORCE_TEST
                try
                {
                    if (Environment.GetEnvironmentVariable("DF_ICONLOG") != null)
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "df_norm.txt"), "srcdip=" + b.PixelWidth + "x" + b.PixelHeight + " src=" + minX + "," + minY + " size=" + cw + "x" + ch + "\n");
                }
                catch { }
#endif
                BitmapSource whole = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, keyed, w * 4);
                BitmapSource crop = new CroppedBitmap(whole, new Int32Rect(minX, minY, cw, ch));
                int side = cw > ch ? cw : ch;
                double r = (double)cw / ch;
                double dw = side * Math.Min(1.0, r * 1.15);
                double dh = side * Math.Min(1.0, 1.15 / r);
                DrawingVisual dv = new DrawingVisual();
                RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
                using (DrawingContext dc = dv.RenderOpen())
                {
                    dc.DrawImage(crop, new Rect((side - dw) / 2.0, (side - dh) / 2.0, dw, dh));
                }
                RenderTargetBitmap rtb = new RenderTargetBitmap(side, side, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                return rtb;
            }
            catch { return src; }
        }

        [StructLayout(LayoutKind.Sequential)] struct SZ { public int cx; public int cy; }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory { void GetImage(SZ size, int flags, out IntPtr phbm); }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pidl, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

        static BitmapSource ShellImage(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return null;            Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            IShellItemImageFactory fac;
            SHCreateItemFromParsingName(path, IntPtr.Zero, iid, out fac);
            if (fac == null) return null;
            IntPtr hbmp = IntPtr.Zero;
            try
            {
                SZ sz = new SZ();
                sz.cx = 256;
                sz.cy = 256;
                fac.GetImage(sz, 0x4, out hbmp);
                if (hbmp == IntPtr.Zero) return null;
                BitmapSource b = Imaging.CreateBitmapSourceFromHBitmap(hbmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                return b;
            }
            finally
            {
                if (hbmp != IntPtr.Zero) DeleteObject(hbmp);
                Marshal.ReleaseComObject(fac);
            }
        }

        public static void Launch(string target)
        {
            try
            {
                if (Directory.Exists(target)) Process.Start("explorer.exe", "\"" + target + "\"");
                else Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MsgBox.Show("打不开这一项：\n" + target + "\n\n" + ex.Message);
            }
        }

        public static List<DeskItem> CachedItems()
        {
            if ((DateTime.Now - CacheTime).TotalMilliseconds > 1200 || Cache.Count == 0)
            {
                Cache = Desk.Items();
                CacheTime = DateTime.Now;
            }
            return Cache;
        }

        public static string DisplayName(Folder f)
        {
            string n = Path.GetFileName(f.Path == null ? "" : f.Path.TrimEnd('\\'));
            return n == null || n.Length == 0 ? f.Name : n;
        }

        public static Rect? ZoneOf(Folder f)
        {
            string n = DisplayName(f);
            foreach (DeskItem it in CachedItems())
            {
                if (string.Equals(it.Name, n, StringComparison.OrdinalIgnoreCase))
                    return Desk.HoverZone(it.R);
            }
            return null;
        }

        public static DateTime BlankAt = DateTime.MinValue;
        public static DateTime EngagedAt = DateTime.MinValue;
        static double HotX = -9999, HotY = -9999;

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint flags);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);

        static bool IsDesktopClass(IntPtr h)
        {
            if (h == IntPtr.Zero) return false;
            StringBuilder sb = new StringBuilder(64);
            GetClassName(h, sb, 64);
            string c = sb.ToString();
            return c == "Progman" || c == "WorkerW" || c == "SysListView32" || c == "SHELLDLL_DefView";
        }

        static IntPtr OwnHwnd(Window w)
        {
            try { return w == null ? IntPtr.Zero : new WindowInteropHelper(w).Handle; }
            catch { return IntPtr.Zero; }
        }

        static bool DesktopOnTopAt(int x, int y)
        {
            POINT p = new POINT();
            p.X = x;
            p.Y = y;
            IntPtr h = WindowFromPoint(p);
            if (h == IntPtr.Zero) return true;
            IntPtr root = GetAncestor(h, 2);
            IntPtr ph = OwnHwnd(Panel);
            IntPtr zh = OwnHwnd(Zoom);
            if (h == ph || h == zh || root == ph || root == zh) return true;
            if (IsDesktopClass(h) || IsDesktopClass(root)) return true;
            return false;
        }

        static bool IconFullyVisible(Rect r)
        {
            if (r.Width <= 0 || r.Height <= 0) return false;
            if (!DesktopOnTopAt((int)(r.Left + r.Width / 2), (int)(r.Top + r.Height / 2))) return false;
            if (!DesktopOnTopAt((int)(r.Left + 4), (int)(r.Top + 4))) return false;
            if (!DesktopOnTopAt((int)(r.Right - 4), (int)(r.Top + 4))) return false;
            if (!DesktopOnTopAt((int)(r.Left + 4), (int)(r.Bottom - 4))) return false;
            if (!DesktopOnTopAt((int)(r.Right - 4), (int)(r.Bottom - 4))) return false;
            return true;
        }

        static Folder FromItem(DeskItem it)
        {
            if (it == null) return null;
            string name = it.Name;
            if (name == null) return null;
            string path = Path.Combine(desktop, name);
            if (!Directory.Exists(path)) return null;
            foreach (Folder f in Targets())
                if (string.Equals(DisplayName(f), name, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        static DeskItem ItemAt(Point p)
        {
            foreach (DeskItem it in CachedItems())
                if (Desk.HoverZone(it.R).Contains(p)) return it;
            return null;
        }

        static DeskItem CurItem(Folder f)
        {
            string n = DisplayName(f);
            foreach (DeskItem it in CachedItems())
                if (string.Equals(it.Name, n, StringComparison.OrdinalIgnoreCase)) return it;
            return null;
        }

        public static Rect? ItemRectOf(Folder f)
        {
            DeskItem it = CurItem(f);
            if (it == null) return null;
            return it.R;
        }

        static Folder HitFolder(Point p)
        {
            DeskItem it = ItemAt(p);
            if (it == null) return null;
            if (!IconFullyVisible(it.R)) return null;
            return FromItem(it);
        }

        public static BitmapSource DeskBg;
        public static bool BgStale = true;

        [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int c);

        public static bool CaptureDeskBg()
        {
            IntPtr lv = IntPtr.Zero;
            bool ok = false;
            try
            {
                if (Panel != null && Panel.IsVisible) return false;
                if (Zoom != null && Zoom.IsVisible) return false;
                lv = Desk.ListView();
                ClearHot();
                System.Threading.Thread.Sleep(130);
                if (lv != IntPtr.Zero) ShowWindow(lv, 0);
                System.Threading.Thread.Sleep(140);
                int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
                if (w <= 0 || h <= 0) return false;
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                        g.CopyFromScreen(0, 0, 0, 0, new System.Drawing.Size(w, h));
                    System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
                        System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    byte[] buf = new byte[w * h * 4];
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), buf, y * w * 4, w * 4);
                    bmp.UnlockBits(bd);
                    BitmapSource bs = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, buf, w * 4);
                    bs.Freeze();
                    DeskBg = bs;
                    ok = true;
                    if (Environment.GetEnvironmentVariable("DF_BGDUMP") != null)
                        bmp.Save(Path.Combine(Path.GetTempPath(), "df_bg.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch { }
            finally
            {
                try { if (lv != IntPtr.Zero) ShowWindow(lv, 5); } catch { }
            }
            return ok;
        }

        public static BitmapSource PatchFill(int x, int y, int w, int h)
        {
            try
            {
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                        g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
                    System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
                        System.Drawing.Imaging.ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    int stride = bd.Stride;
                    byte[] buf = new byte[stride * h];
                    Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
                    bool[] hole = new bool[w * h];
                    for (int yy = 0; yy < h; yy++)
                        for (int xx = 0; xx < w; xx++)
                        {
                            int i = yy * stride + xx * 4;
                            int b = buf[i], gg = buf[i + 1], r = buf[i + 2];
                            int mn = Math.Min(r, Math.Min(gg, b));
                            int mx = Math.Max(r, Math.Max(gg, b));
                            if (mn > 140 && mx - mn < 28) hole[yy * w + xx] = true;
                        }
                    for (int pass = 0; pass < 3; pass++)
                    {
                        bool[] nx = new bool[w * h];
                        for (int yy = 0; yy < h; yy++)
                            for (int xx = 0; xx < w; xx++)
                            {
                                bool v = hole[yy * w + xx];
                                if (!v && xx > 0 && yy > 0 && xx < w - 1 && yy < h - 1
                                    && (hole[yy * w + xx - 1] || hole[yy * w + xx + 1] || hole[(yy - 1) * w + xx] || hole[(yy + 1) * w + xx]
                                        || hole[(yy - 1) * w + xx - 1] || hole[(yy - 1) * w + xx + 1] || hole[(yy + 1) * w + xx - 1] || hole[(yy + 1) * w + xx + 1]))
                                    v = true;
                                nx[yy * w + xx] = v;
                            }
                        hole = nx;
                    }
                    float[] fr = new float[w * h], fg = new float[w * h], fb = new float[w * h];
                    float ar = 0, ag = 0, ab = 0;
                    int an = 0;
                    for (int yy = 0; yy < h; yy++)
                        for (int xx = 0; xx < w; xx++)
                        {
                            int i = yy * stride + xx * 4;
                            fr[yy * w + xx] = buf[i + 2];
                            fg[yy * w + xx] = buf[i + 1];
                                                    fb[yy * w + xx] = buf[i];
                            if (!hole[yy * w + xx]) { ar += buf[i + 2]; ag += buf[i + 1]; ab += buf[i]; an++; }
                        }
                    if (an > 0)
                    {
                        ar /= an; ag /= an; ab /= an;
                        for (int yy = 0; yy < h; yy++)
                            for (int xx = 0; xx < w; xx++)
                                if (hole[yy * w + xx])
                                {
                                    fr[yy * w + xx] = ar; fg[yy * w + xx] = ag; fb[yy * w + xx] = ab;
                                }
                        for (int it = 0; it < 200; it++)
                            for (int yy = 1; yy < h - 1; yy++)
                                for (int xx = 1; xx < w - 1; xx++)
                                {
                                    int k = yy * w + xx;
                                    if (!hole[k]) continue;
                                    fr[k] = (fr[k - 1] + fr[k + 1] + fr[k - w] + fr[k + w]) * 0.25f;
                                    fg[k] = (fg[k - 1] + fg[k + 1] + fg[k - w] + fg[k + w]) * 0.25f;
                                    fb[k] = (fb[k - 1] + fb[k + 1] + fb[k - w] + fb[k + w]) * 0.25f;
                                }
                        for (int yy = 0; yy < h; yy++)
                            for (int xx = 0; xx < w; xx++)
                            {
                                if (!hole[yy * w + xx]) continue;
                                int i = yy * stride + xx * 4;
                                buf[i] = (byte)Math.Max(0, Math.Min(255, (int)fb[yy * w + xx]));
                                buf[i + 1] = (byte)Math.Max(0, Math.Min(255, (int)fg[yy * w + xx]));
                                buf[i + 2] = (byte)Math.Max(0, Math.Min(255, (int)fr[yy * w + xx]));
                            }
                    }
                    for (int yy = 0; yy < h; yy++)
                        for (int xx = 0; xx < w; xx++)
                            buf[yy * stride + xx * 4 + 3] = 255;
                    bmp.UnlockBits(bd);
                    if (Environment.GetEnvironmentVariable("DF_PATCHDUMP") != null)
                        bmp.Save(Path.Combine(Path.GetTempPath(), "df_patch.png"), System.Drawing.Imaging.ImageFormat.Png);
                    BitmapSource bs = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, buf, stride);
                    bs.Freeze();
                    return bs;
                }
            }
            catch { return null; }
        }

        static byte[] Grab(int x, int y, int w, int h)
        {
            byte[] d = new byte[w * 4 * h];
            try
            {
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                        g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
                    System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
                        System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    for (int row = 0; row < h; row++)
                        Marshal.Copy(IntPtr.Add(bd.Scan0, row * bd.Stride), d, row * w * 4, w * 4);
                    bmp.UnlockBits(bd);
                }
            }
            catch { return null; }
            return d;
        }

        static void DumpPatch(byte[] buf, int w, int h)
        {
            if (Environment.GetEnvironmentVariable("DF_PATCHDUMP") == null) return;
            try
            {
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
                        System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    for (int row = 0; row < h; row++)
                        Marshal.Copy(buf, row * w * 4, IntPtr.Add(bd.Scan0, row * bd.Stride), w * 4);
                    bmp.UnlockBits(bd);
                    bmp.Save(Path.Combine(Path.GetTempPath(), "df_patch.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch { }
        }

        static void FillHoles(byte[] buf, int w, int h, bool[] hole)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                bool[] nx = new bool[w * h];
                for (int yy = 1; yy < h - 1; yy++)
                    for (int xx = 1; xx < w - 1; xx++)
                    {
                        bool v = hole[yy * w + xx];
                        if (!v && (hole[yy * w + xx - 1] || hole[yy * w + xx + 1]
                            || hole[(yy - 1) * w + xx] || hole[(yy + 1) * w + xx])) v = true;
                        nx[yy * w + xx] = v;
                    }
                for (int yy = 1; yy < h - 1; yy++)
                    for (int xx = 1; xx < w - 1; xx++) hole[yy * w + xx] = nx[yy * w + xx];
            }
            int n = 0;
            float sr = 0, sg = 0, sb = 0;
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                    if (!hole[yy * w + xx])
                    {
                        int i = (yy * w + xx) * 4;
                        sb += buf[i]; sg += buf[i + 1]; sr += buf[i + 2]; n++;
                    }
            if (n == 0) return;
            sr /= n; sg /= n; sb /= n;
            float[] fr = new float[w * h], fg = new float[w * h], fb = new float[w * h];
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    int k = yy * w + xx, i = k * 4;
                    if (hole[k]) { fb[k] = sb; fg[k] = sg; fr[k] = sr; }
                    else { fb[k] = buf[i]; fg[k] = buf[i + 1]; fr[k] = buf[i + 2]; }
                }
            for (int it = 0; it < 90; it++)
                for (int yy = 1; yy < h - 1; yy++)
                    for (int xx = 1; xx < w - 1; xx++)
                    {
                        int k = yy * w + xx;
                        if (!hole[k]) continue;
                        fb[k] = (fb[k - 1] + fb[k + 1] + fb[k - w] + fb[k + w]) * 0.25f;
                        fg[k] = (fg[k - 1] + fg[k + 1] + fg[k - w] + fg[k + w]) * 0.25f;
                        fr[k] = (fr[k - 1] + fr[k + 1] + fr[k - w] + fr[k + w]) * 0.25f;
                    }
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    int k = yy * w + xx, i = k * 4;
                    if (hole[k])
                    {
                        buf[i] = (byte)Math.Max(0, Math.Min(255, (int)fb[k]));
                        buf[i + 1] = (byte)Math.Max(0, Math.Min(255, (int)fg[k]));
                        buf[i + 2] = (byte)Math.Max(0, Math.Min(255, (int)fr[k]));
                    }
                    buf[i + 3] = 255;
                }
        }

        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, int flags);
        [DllImport("gdi32.dll")] static extern bool SetViewportOrgEx(IntPtr hdc, int x, int y, IntPtr prev);

        static byte[] PrintItem(IntPtr lv, int x, int y, int w, int h)
        {
            int fw = GetSystemMetrics(0), fh = GetSystemMetrics(1);
            if (fw <= 0 || fh <= 0) return null;
            byte[] full = new byte[fw * 4 * fh];
            try
            {
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(fw, fh, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        PrintWindow(lv, hdc, 2);
                        g.ReleaseHdc(hdc);
                    }
                    System.Drawing.Imaging.BitmapData bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, fw, fh),
                        System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    for (int row = 0; row < fh; row++)
                        Marshal.Copy(IntPtr.Add(bd.Scan0, row * bd.Stride), full, row * fw * 4, fw * 4);
                    bmp.UnlockBits(bd);
                }
            }
            catch { return null; }
            byte[] d = new byte[w * 4 * h];
            for (int row = 0; row < h; row++)
            {
                int sy = y + row;
                if (sy < 0 || sy >= fh) continue;
                Array.Copy(full, (sy * fw + x) * 4, d, row * w * 4, w * 4);
            }
            return d;
        }

        static bool HotScan(byte[] d, int w, int h, out int hx0, out int hy0, out int hx1, out int hy1)
        {
            hx0 = hx1 = hy0 = hy1 = 0;
            if (d == null) return false;
            int cnt = 0, bx0 = w, bxx = -1, by0 = h, byy = -1;
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    int i = (yy * w + xx) * 4;
                    int b = d[i], g2 = d[i + 1], r = d[i + 2];
                    int mx = Math.Max(r, Math.Max(g2, b)), mn = Math.Min(r, Math.Min(g2, b));
                    if (mx - mn < 6 && b > 22 && b < 96)
                    {
                        cnt++;
                        if (xx < bx0) { bx0 = xx; } if (xx > bxx) { bxx = xx; }
                        if (yy < by0) { by0 = yy; } if (yy > byy) { byy = yy; }
                    }
                }
            if (cnt < 400 || bxx < bx0 || byy < by0) return false;
            hx0 = bx0; hy0 = by0; hx1 = bxx; hy1 = byy;
            return true;
        }

        public static BitmapSource PatchFor(Folder f, Rect iconRect, out int ox, out int oy)
        {
            ox = 0; oy = 0;
            double artPx = 48.0 * Scale;
            double ax0 = iconRect.Left + (iconRect.Width - artPx) / 2;
            double ay0 = iconRect.Top;
            int gx0 = (int)Math.Floor(ax0 - 6);
            int gy0 = (int)Math.Floor(ay0 - 6);
            int gw = (int)Math.Ceiling(artPx + 12);
            int gh;
            int bandTop = -1;
            if (cfg.ShowLabels) gh = (int)Math.Ceiling(artPx + 12);
            else
            {
                gh = (int)Math.Ceiling(iconRect.Bottom + 8 - gy0);
                bandTop = (int)Math.Round(ay0 + artPx - gy0);
            }
            byte[] cap = Grab(gx0, gy0, gw, gh);
            if (cap == null) return null;
            bool[] hole = new bool[gw * gh];
            BitmapSource icon = PlainIconFor(f.Path);
            if (icon != null)
            {
                int iw = icon.PixelWidth, ih = icon.PixelHeight;
                int[] ip = new int[iw * ih];
                icon.CopyPixels(ip, iw * 4, 0);
                for (int yy = 0; yy < gh; yy++)
                    for (int xx = 0; xx < gw; xx++)
                    {
                        int u = (int)Math.Round((xx + gx0 - ax0) / artPx * iw);
                        int v = (int)Math.Round((yy + gy0 - ay0) / artPx * ih);
                        if (u < 0 || v < 0 || u >= iw || v >= ih) continue;
                        if (((ip[v * iw + u] >> 24) & 255) > 10) hole[yy * gw + xx] = true;
                    }
            }
            for (int pass = 0; pass < 3; pass++)
            {
                bool[] nx = new bool[gw * gh];
                for (int yy = 1; yy < gh - 1; yy++)
                    for (int xx = 1; xx < gw - 1; xx++)
                    {
                        bool v = hole[yy * gw + xx];
                        if (!v && (hole[yy * gw + xx - 1] || hole[yy * gw + xx + 1] || hole[(yy - 1) * gw + xx] || hole[(yy + 1) * gw + xx])) v = true;
                        nx[yy * gw + xx] = v;
                    }
                for (int yy = 1; yy < gh - 1; yy++)
                    for (int xx = 1; xx < gw - 1; xx++) hole[yy * gw + xx] = nx[yy * gw + xx];
            }
            FillHoles(cap, gw, gh, hole);
            if (bandTop >= 0 && bandTop < gh - 1)
            {
                for (int xx = 0; xx < gw; xx++)
                {
                    int ti = (bandTop - 1) * gw * 4 + xx * 4;
                    int bi = (gh - 1) * gw * 4 + xx * 4;
                    for (int yy = bandTop; yy < gh - 1; yy++)
                    {
                        double w = (double)(yy - bandTop + 1) / (gh - bandTop);
                        int k = yy * gw * 4 + xx * 4;
                        cap[k] = (byte)(cap[ti] + (cap[bi] - cap[ti]) * w);
                        cap[k + 1] = (byte)(cap[ti + 1] + (cap[bi + 1] - cap[ti + 1]) * w);
                        cap[k + 2] = (byte)(cap[ti + 2] + (cap[bi + 2] - cap[ti + 2]) * w);
                    }
                }
            }
            for (int yy = 0; yy < gh; yy++)
                for (int xx = 0; xx < gw; xx++)
                    cap[(yy * gw + xx) * 4 + 3] = 255;
            DumpPatch(cap, gw, gh);
            BitmapSource bs = BitmapSource.Create(gw, gh, 96, 96, PixelFormats.Bgra32, null, cap, gw * 4);
            bs.Freeze();
            ox = gx0; oy = gy0;
            return bs;
        }
        public static void EnsureBg()
        {
            if (DeskBg != null && !BgStale) return;
            bool ok = CaptureDeskBg();
            if (ok) BgStale = false;
        }

        public static BitmapSource BgCrop(int x, int y, int w, int h)
        {
            if (DeskBg == null || w <= 0 || h <= 0) return null;
            try
            {
                int bw = DeskBg.PixelWidth, bh = DeskBg.PixelHeight;
                if (x < 0) { w += x; x = 0; }
                if (y < 0) { h += y; y = 0; }
                if (x + w > bw) w = bw - x;
                if (y + h > bh) h = bh - y;
                if (w <= 0 || h <= 0) return null;
                CroppedBitmap cb = new CroppedBitmap(DeskBg, new Int32Rect(x, y, w, h));
                cb.Freeze();
                return cb;
            }
            catch { return null; }
        }

        public static void ClearHot()
        {
            try
            {
                IntPtr lv = Desk.ListView();
                if (lv == IntPtr.Zero) return;
                PostMessage(lv, 0x0200, IntPtr.Zero, new IntPtr(-1));
            }
            catch { }
        }

        public static void HotBurst()
        {
            ClearHot();
            try
            {
                System.Threading.Thread t = new System.Threading.Thread((System.Threading.ThreadStart)delegate
                {
                    try
                    {
                        System.Threading.Thread.Sleep(200); ClearHot();
                        System.Threading.Thread.Sleep(300); ClearHot();
                    }
                    catch { }
                });
                t.IsBackground = true;
                t.Start();
            }
            catch { }
        }

        public static void StartWatcher()
        {
            Watcher = new DispatcherTimer();
            Watcher.Interval = TimeSpan.FromMilliseconds(30);
            Watcher.Tick += delegate
            {
                try
                {
                    Point p = Cursor();
#if FORCE_TEST
                    string fp = Environment.GetEnvironmentVariable("DF_POINT");
                    if (fp != null && fp.Length > 0)
                    {
                        string[] xy = fp.Split(',');
                        if (xy.Length == 2) p = new Point(double.Parse(xy[0]), double.Parse(xy[1]));
                    }
#endif
                    if ((GetAsyncKeyState(1) & 0x8000) != 0 && Panel != null && Panel.IsVisible)
                    {
                        Rect pr0 = WinRect(Panel);
                        if (!pr0.Contains(p))
                        {
                            Panel.CloseNow();
                            BlankAt = DateTime.MinValue;
                            EngagedAt = DateTime.MinValue;
                            return;
                        }
                    }
                    Folder cur = Panel.Current();
                    bool open = cur != null && Panel.IsVisible;


                    if (open)
                    {
                        DeskItem ci = CurItem(cur);
                        if (ci == null || !IconFullyVisible(ci.R)) { Panel.CloseNow(); BlankAt = DateTime.MinValue; EngagedAt = DateTime.MinValue; return; }
                        Rect pr = WinRect(Panel);
                        Rect? cz0 = ZoneOf(cur);
                        Rect? cz = null;
                        if (cz0.HasValue)
                        {
                            Rect z = cz0.Value;
                            double pad = Core.U(26);
                            cz = new Rect(z.Left - pad, z.Top - pad, z.Width + pad * 2, z.Height + pad * 2);
                        }
                        if (pr.Contains(p) || (cz.HasValue && cz.Value.Contains(p)))
                        {
                            EngagedAt = DateTime.Now;
                            BlankAt = DateTime.MinValue;
                            return;
                        }
                        if (cz.HasValue)
                        {
                            Rect czv = cz.Value;
                            Rect corridor;
                            if (pr.Left > czv.Right - 1)
                                corridor = new Rect(czv.Right - 2, Math.Min(czv.Top, pr.Top) - Core.U(6), Math.Max(0, pr.Left - czv.Right + 4), Math.Max(czv.Height, pr.Height) + Core.U(12));
                            else if (pr.Right < czv.Left + 1)
                                corridor = new Rect(pr.Right - 2, Math.Min(czv.Top, pr.Top) - Core.U(6), Math.Max(0, czv.Left - pr.Right + 4), Math.Max(czv.Height, pr.Height) + Core.U(12));
                            else if (pr.Top > czv.Bottom - 1)
                                corridor = new Rect(Math.Min(czv.Left, pr.Left) - Core.U(6), czv.Bottom - 2, Math.Max(czv.Width, pr.Width) + Core.U(12), Math.Max(0, pr.Top - czv.Bottom + 4));
                            else
                                corridor = new Rect(Math.Min(czv.Left, pr.Left) - Core.U(6), pr.Bottom - 2, Math.Max(czv.Width, pr.Width) + Core.U(12), Math.Max(0, czv.Top - pr.Bottom + 4));
                            if (corridor.Width > 0 && corridor.Height > 0 && corridor.Contains(p))
                            {
                                EngagedAt = DateTime.Now;
                                BlankAt = DateTime.MinValue;
                                return;
                            }
                        }

                        Folder other = HitFolder(p);
                        if (other != null)
                        {
                            if ((DateTime.Now - EngagedAt).TotalMilliseconds < 30) return;
                            if (cur.Path != other.Path) Panel.Open(other);
                            EngagedAt = DateTime.Now;
                            return;
                        }

                        if (BlankAt == DateTime.MinValue) BlankAt = DateTime.Now;
                        if ((DateTime.Now - BlankAt).TotalMilliseconds > 160)
                        {
                            Panel.CloseNow();
                            BlankAt = DateTime.MinValue;
                            EngagedAt = DateTime.MinValue;
                        }
                        return;
                    }

                    Folder hit = HitFolder(p);
                    if (hit != null)
                    {
                        Panel.Open(hit);
                        EngagedAt = DateTime.Now;
                        BlankAt = DateTime.MinValue;
                    }
                }
                catch { }
            };
            Watcher.Start();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHSTOCKICONINFO
        {
            public uint cbSize;
            public IntPtr hIcon;
            public int iSysIconIndex;
            public int iIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szPath;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr h);
    }

    public class PanelWindow : Window
    {
        Folder cur;
        Border root;
        ScrollViewer sc;
        StackPanel holder;
        ScaleTransform zoom;
        int gen;
        Brush labelBrush = new SolidColorBrush(Color.FromRgb(208, 214, 226));
        bool clearMode;
        DropShadowEffect labelShadow;
        Color hovTop, hovBottom, hovBorder;

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint flags);

        struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);

        public PanelWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = Ui.Font;
            Ui.StyleTooltip(this);

            root = new Border();
            root.Background = new SolidColorBrush(Color.FromArgb(238, 18, 20, 27));
            root.CornerRadius = new CornerRadius(Core.U(16));
            root.Padding = new Thickness(Core.U(8));
            root.BorderThickness = new Thickness(1);
            root.BorderBrush = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            root.SnapsToDevicePixels = true;
            root.Effect = Shadow();

            labelShadow = new DropShadowEffect();
            labelShadow.Color = Colors.Black;
            labelShadow.BlurRadius = 6;
            labelShadow.Opacity = 1;
            labelShadow.Direction = 0;
            labelShadow.ShadowDepth = 0;

            zoom = new ScaleTransform(1, 1);
            root.RenderTransformOrigin = new Point(0, 0.5);
            root.RenderTransform = zoom;

            sc = new ScrollViewer();
            sc.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sc.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            root.Child = sc;
            Content = root;
        }

        public static Color PanelColor()
        {
            Config c = Core.cfg;
            string s = c != null && c.Color != null ? c.Color.Trim() : "";
            if (s.StartsWith("#") && (s.Length == 7 || s.Length == 9))
            {
                try
                {
                    int a = s.Length == 9 ? Convert.ToInt32(s.Substring(1, 2), 16) : (c != null ? c.Alpha : 238);
                    int r = Convert.ToInt32(s.Substring(s.Length == 9 ? 3 : 1, 2), 16);
                    int g = Convert.ToInt32(s.Substring(s.Length == 9 ? 5 : 3, 2), 16);
                    int b = Convert.ToInt32(s.Substring(s.Length == 9 ? 7 : 5, 2), 16);
                    if (c != null && s.Length == 7) a = c.Alpha;
                    return Color.FromArgb((byte)Math.Max(0, Math.Min(255, a)), (byte)r, (byte)g, (byte)b);
                }
                catch { }
            }
            return Color.FromArgb(238, 18, 20, 27);
        }

        static DropShadowEffect Shadow()
        {
            DropShadowEffect sh = new DropShadowEffect();
            sh.Color = Color.FromArgb(255, 2, 4, 10);
            sh.BlurRadius = 30;
            sh.Opacity = 0.66;
            sh.Direction = 270;
            sh.ShadowDepth = 6;
            return sh;
        }

        void ApplyStyle()
        {
            Config c = Core.cfg;
            clearMode = c != null && c.Clear;
            if (clearMode)
            {
                root.Background = Brushes.Transparent;
                root.BorderBrush = Brushes.Transparent;
                root.Effect = null;
                labelBrush = new SolidColorBrush(Color.FromRgb(243, 246, 252));
                hovTop = Color.FromArgb(34, 255, 255, 255);
                hovBottom = Color.FromArgb(10, 255, 255, 255);
                hovBorder = Color.FromArgb(48, 255, 255, 255);
            }
            else
            {
                Color col = PanelColor();
                double lum = (0.299 * col.R + 0.587 * col.G + 0.114 * col.B) / 255.0;
                bool light = lum > 0.62;
                LinearGradientBrush gb = new LinearGradientBrush(
                    Ui.Light(col, light ? 0.03 : 0.09),
                    Ui.Dark(col, light ? 0.02 : 0.07), 90);
                root.Background = gb;
                root.BorderBrush = new SolidColorBrush(light ? Color.FromArgb(26, 0, 0, 0) : Color.FromArgb(26, 255, 255, 255));
                if (root.Effect == null) root.Effect = Shadow();
                labelBrush = light
                    ? (Brush)new SolidColorBrush(Color.FromRgb(30, 35, 44))
                    : new SolidColorBrush(Color.FromRgb(218, 224, 236));
                if (light)
                {
                    hovTop = Color.FromArgb(26, 0, 0, 0);
                    hovBottom = Color.FromArgb(8, 0, 0, 0);
                    hovBorder = Color.FromArgb(34, 0, 0, 0);
                }
                else
                {
                    hovTop = Color.FromArgb(40, 255, 255, 255);
                    hovBottom = Color.FromArgb(12, 255, 255, 255);
                    hovBorder = Color.FromArgb(44, 255, 255, 255);
                }
            }
        }

        internal static DoubleAnimation Make(double from, double to, double ms, IEasingFunction ease)
        {
            DoubleAnimation a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms));
            a.EasingFunction = ease;
            return a;
        }

        static DateTime animT0 = DateTime.Now;

        static void AnimLog(string s)
        {
            if (Environment.GetEnvironmentVariable("DF_ANIMLOG") == null) return;
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "df_anim.txt"), ((int)(DateTime.Now - animT0).TotalMilliseconds) + " " + s + "\r\n", Encoding.UTF8);
            }
            catch { }
        }

        void StartSampler()
        {
            if (Environment.GetEnvironmentVariable("DF_ANIMLOG") == null) return;
            DateTime s0 = DateTime.Now;
            DispatcherTimer t = new DispatcherTimer();
            t.Interval = TimeSpan.FromMilliseconds(12);
            t.Tick += delegate
            {
                AnimLog("f op=" + Math.Round(Opacity, 2) + " sx=" + Math.Round(zoom.ScaleX, 2));
                if ((DateTime.Now - s0).TotalMilliseconds > 240) t.Stop();
            };
            t.Start();
        }

        void AnimateIn(bool soft)
        {
            gen++;
            AnimLog("in-start " + (soft ? "soft" : "full"));
            BeginAnimation(OpacityProperty, null);
            zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            if (soft)
            {
                double from = Math.Max(Opacity, 0.72);
                Opacity = from;
                BeginAnimation(OpacityProperty, Make(from, 1, 150, new CubicEase { EasingMode = EasingMode.EaseOut }));
                zoom.ScaleX = 1;
                zoom.ScaleY = 1;
                StartSampler();
                return;
            }
            Opacity = 0;
            BeginAnimation(OpacityProperty, Make(0, 1, 170, new CubicEase { EasingMode = EasingMode.EaseOut }));
            zoom.ScaleX = 1;
            zoom.ScaleY = 1;
            StartSampler();
#if FORCE_TEST
            string ac = Environment.GetEnvironmentVariable("DF_AUTOCLOSE");
            if (ac != null && ac.Length > 0)
            {
                DispatcherTimer ct = new DispatcherTimer();
                ct.Interval = TimeSpan.FromMilliseconds(int.Parse(ac));
                ct.Tick += delegate { ct.Stop(); CloseNow(); };
                ct.Start();
            }
#endif
        }

        void AnimateOut()
        {
            int g = gen;
            AnimLog("out-start");
            double from = Opacity;
            if (from < 0.05) from = 1;
            BeginAnimation(OpacityProperty, null);
            DoubleAnimation fade = Make(from, 0, 220, new CubicEase { EasingMode = EasingMode.EaseInOut });
            fade.Completed += delegate
            {
                if (g != gen) return;
                Hide();
                Opacity = 1;
                zoom.ScaleX = 1;
                zoom.ScaleY = 1;
            };
            BeginAnimation(OpacityProperty, fade);
            zoom.ScaleX = 1;
            zoom.ScaleY = 1;
            StartSampler();
        }

        public Folder Current() { return cur; }

        public void Open(Folder f)
        {
            if (Environment.GetEnvironmentVariable("DF_ZLOG") != null) { try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "df_zlog.txt"), "OPEN " + f.Path + " " + DateTime.Now.ToString("HH:mm:ss.fff") + "\r\n", Encoding.UTF8); } catch { } }
            if (cur != null && cur.Path == f.Path && IsVisible && Opacity > 0.5) return;
            cur = f;
            ApplyStyle();
            holder = new StackPanel();
            string[] all;
            try { all = Core.EntriesOf(f.Path); }
            catch { all = new string[0]; }
            int n = Math.Min(all.Length, 60);
            if (n == 0)
            {
                StackPanel empty = new StackPanel();
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                empty.VerticalAlignment = VerticalAlignment.Center;
                TextBlock eg = new TextBlock();
                eg.Text = "\uE8B7";
                eg.FontFamily = Ui.GlyphFont;
                eg.FontSize = Core.U(24);
                eg.Foreground = labelBrush;
                eg.Opacity = 0.42;
                eg.HorizontalAlignment = HorizontalAlignment.Center;
                if (clearMode) eg.Effect = labelShadow;
                empty.Children.Add(eg);
                TextBlock hint = new TextBlock();
                hint.Text = "这个文件夹还是空的\n把文件拖进来，或点「导入」";
                hint.FontSize = Core.U(11.5);
                hint.Foreground = labelBrush;
                hint.LineHeight = Core.U(18);
                if (clearMode) hint.Effect = labelShadow;
                hint.TextAlignment = TextAlignment.Center;
                hint.TextWrapping = TextWrapping.Wrap;
                hint.Width = Core.U(190);
                hint.Margin = new Thickness(0, Core.U(8), 0, 0);
                empty.Children.Add(hint);
                holder.Children.Add(empty);
                Width = Core.U(224);
                Height = Core.U(104);
            }
            else
            {
                int fit = (int)Math.Floor((SystemParameters.WorkArea.Width - Core.U(24)) / Core.U(82));
                if (fit < 1) fit = 1;
                int cols = n < fit ? n : fit;
                int rows = (n + cols - 1) / cols;
                WrapPanel wp = new WrapPanel();
                for (int i = 0; i < n; i++) wp.Children.Add(Tile(all[i], i));
                holder.Children.Add(wp);
                double maxH = SystemParameters.WorkArea.Height - Core.U(90);
                double h = Math.Min(rows * Core.U(86) + Core.U(18), maxH);
                Width = cols * Core.U(82) + Core.U(18);
                Height = h;
                sc.MaxHeight = h - Core.U(18) + 2;
            }
            sc.Content = holder;
            bool wasVisible = IsVisible && Opacity > 0.2;
            Place(f);
            if (!wasVisible) Show();
            UpdateLayout();
            Place(f);
            try
            {
                SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            }
            catch { }
            AnimateIn(wasVisible);
        }

        void Place(Folder f)
        {
            Rect? z = Core.ZoneOf(f);
            if (!z.HasValue) return;
            Rect icon = new Rect(z.Value.Left + 8, z.Value.Top + 8, Math.Max(8, z.Value.Width - 16), Math.Max(8, z.Value.Height - 86));
            double w = Width * Core.Scale;
            double h = Height * Core.Scale;
            double fx = Core.Scale, fy = Core.Scale;
            IntPtr hw = IntPtr.Zero;
            try { hw = new WindowInteropHelper(this).Handle; } catch { }
            if (hw != IntPtr.Zero)
            {
                RECT rr;
                if (GetWindowRect(hw, out rr))
                {
                    int pw = rr.Right - rr.Left, ph = rr.Bottom - rr.Top;
                    if (pw > 8 && ph > 8 && Width > 1 && Height > 1)
                    {
                        w = pw; h = ph;
                        fx = pw / Width; fy = ph / Height;
                    }
                }
            }
            double m = 4;
            double gap = 8 * Core.Scale;

            Rect wa = SystemParameters.WorkArea;
            Rect wa2 = new Rect(wa.Left * Core.Scale, wa.Top * Core.Scale, wa.Width * Core.Scale, wa.Height * Core.Scale);

            double spaceR = wa2.Right - (icon.Right + gap);
            double spaceL = icon.Left - gap - wa2.Left;
            double spaceB = wa2.Bottom - (icon.Bottom + gap);
            double spaceT = icon.Top - gap - wa2.Top;
            double yc = icon.Top + icon.Height / 2 - h / 2;
            double xc = icon.Left + icon.Width / 2 - w / 2;
            double labelInPanel = (9 + (86 - (34 + 4 + 16)) / 2 + 34 + 4 + 8) * Core.Scale;
            double yText = icon.Bottom + 2 * Core.Scale - labelInPanel;

            bool above = spaceT >= h && (spaceB < h * 1.4 || yText + h > wa2.Bottom - m);
            bool below = !above && spaceB >= h && yText < wa2.Top + m - h;
            double y;
            if (above) y = icon.Top - gap - h;
            else if (below) y = icon.Bottom + gap;
            else if (yText >= wa2.Top + m && yText + h <= wa2.Bottom - m) y = yText;
            else if (spaceT >= h) { y = icon.Top - gap - h; above = true; }
            else if (spaceB >= h) { y = icon.Bottom + gap; below = true; }
            else y = Math.Max(wa2.Top + m, Math.Min(yText, wa2.Bottom - m - h));

            bool edgeR = wa2.Right - icon.Right < icon.Width * 2;
            bool edgeL = icon.Left - wa2.Left < icon.Width * 2;
            double x;
            if (above || below) x = xc;
            else if (edgeR && spaceL >= w) x = icon.Left - gap - w;
            else if (edgeL && spaceR >= w) x = icon.Right + gap;
            else if (spaceR >= w) x = icon.Right + gap;
            else if (spaceL >= w) x = icon.Left - gap - w;
            else x = spaceR >= spaceL ? icon.Right + gap : icon.Left - gap - w;

            if (x + w > wa2.Right - m) x = wa2.Right - w - m;
            if (x < wa2.Left + m) x = wa2.Left + m;
            if (y + h > wa2.Bottom - m) y = wa2.Bottom - h - m;
            if (y < wa2.Top + m) y = wa2.Top + m;
            Left = x / fx;
            Top = y / fy;
#if FORCE_TEST
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "df_pos.txt"),
                    f.Name + (above ? " ABOVE" : (below ? " BELOW" : " SIDE")) +
                    " icon=" + (int)icon.Left + "," + (int)icon.Top + "-" + (int)icon.Right + "," + (int)icon.Bottom +
                    " panel=" + (int)x + "," + (int)y + "-" + (int)(x + w) + "," + (int)(y + h) +
                    " scale=" + Core.Scale + " fx=" + fx + " wh=" + (int)w + "x" + (int)h + " dip=" + (int)Width + "x" + (int)Height + " wa=" + (int)wa2.Right + "," + (int)wa2.Bottom + "\r\n", Encoding.UTF8);
            }
            catch { }
#endif
        }

        Border Tile(string path, int idx)
        {
            Border tile = new Border();
            tile.Width = Core.U(82);
            tile.Height = Core.U(86);
            tile.CornerRadius = new CornerRadius(Core.U(11));
            tile.Background = Brushes.Transparent;
            tile.BorderThickness = new Thickness(1);
            tile.BorderBrush = Brushes.Transparent;
            tile.SnapsToDevicePixels = true;
            tile.Cursor = Cursors.Hand;
            tile.ToolTip = path;

            StackPanel sp = new StackPanel();
            sp.VerticalAlignment = VerticalAlignment.Center;
            sp.HorizontalAlignment = HorizontalAlignment.Center;

            Image img = new Image();
            img.Width = Core.U(34);
            img.Height = Core.U(34);
            img.Stretch = Stretch.Uniform;
            img.IsHitTestVisible = false;
            BitmapSource src = Core.IconFor(path);
            if (src != null) img.Source = src;
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            sp.Children.Add(img);

            string label = Core.DisplayName(path);
            if (Core.cfg.ShowLabels)
            {
            if (clearMode)
            {
                Border chip = new Border();
                chip.CornerRadius = new CornerRadius(Core.U(5));
                chip.Padding = new Thickness(Core.U(5), Core.U(1.5), Core.U(5), Core.U(1.5));
                chip.Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
                chip.HorizontalAlignment = HorizontalAlignment.Center;
                chip.MaxWidth = Core.U(76);
                chip.Margin = new Thickness(0, Core.U(4), 0, 0);
                TextBlock lb = new TextBlock();
                lb.Text = label;
                lb.FontSize = Core.U(10.5);
                lb.FontWeight = FontWeights.SemiBold;
                lb.Foreground = Brushes.White;
                lb.TextTrimming = TextTrimming.CharacterEllipsis;
                lb.IsHitTestVisible = false;
                chip.Child = lb;
                chip.IsHitTestVisible = false;
                sp.Children.Add(chip);
            }
            else
            {
                TextBlock lb = new TextBlock();
                lb.Text = label;
                lb.FontSize = Core.U(10.5);
                lb.FontWeight = FontWeights.Medium;
                lb.Foreground = labelBrush;
                lb.TextAlignment = TextAlignment.Center;
                lb.TextTrimming = TextTrimming.CharacterEllipsis;
                lb.Width = Core.U(76);
                lb.Margin = new Thickness(0, Core.U(4), 0, 0);
                lb.MaxHeight = Core.U(26);
                sp.Children.Add(lb);
            }
            }
            tile.Child = sp;

            ScaleTransform hz = new ScaleTransform(1, 1);
            img.RenderTransform = hz;
            img.RenderTransformOrigin = new Point(0.5, 0.5);
            tile.MouseEnter += delegate
            {
                LinearGradientBrush hb = new LinearGradientBrush(hovTop, hovBottom, 90);
                tile.Background = hb;
                tile.BorderBrush = new SolidColorBrush(hovBorder);
                DropShadowEffect ish = new DropShadowEffect();
                ish.Color = Colors.Black;
                ish.BlurRadius = 10;
                ish.Opacity = 0.35;
                ish.ShadowDepth = 1;
                ish.Direction = 270;
                img.Effect = ish;
                Panel.SetZIndex(tile, 20);
                DoubleAnimation a1 = new DoubleAnimation(1.15, TimeSpan.FromMilliseconds(170));
                a1.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                hz.BeginAnimation(ScaleTransform.ScaleXProperty, a1);
                hz.BeginAnimation(ScaleTransform.ScaleYProperty, a1);
            };
            tile.MouseLeave += delegate
            {
                tile.Background = Brushes.Transparent;
                tile.BorderBrush = Brushes.Transparent;
                img.Effect = null;
                Panel.SetZIndex(tile, 0);
                DoubleAnimation a1 = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(170));
                a1.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                hz.BeginAnimation(ScaleTransform.ScaleXProperty, a1);
                hz.BeginAnimation(ScaleTransform.ScaleYProperty, a1);
            };
            tile.MouseLeftButtonDown += delegate
            {
                DoubleAnimation a1 = new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(90));
                a1.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                hz.BeginAnimation(ScaleTransform.ScaleXProperty, a1);
                hz.BeginAnimation(ScaleTransform.ScaleYProperty, a1);
            };
            tile.MouseLeftButtonUp += delegate
            {
                tile.Background = Brushes.Transparent;
                tile.BorderBrush = Brushes.Transparent;
                Core.Launch(path);
                CloseNow();
            };
            TileIn(tile, idx);
            return tile;
        }

        void TileIn(Border tile, int idx)
        {
            tile.Opacity = 0;
            TranslateTransform ty = new TranslateTransform(0, Core.U(6));
            tile.RenderTransform = ty;
            TimeSpan delay = TimeSpan.FromMilliseconds(Math.Min(idx, 9) * 16);
            CubicEase ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            DoubleAnimation fa = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170));
            fa.BeginTime = delay;
            fa.EasingFunction = ease;
            tile.BeginAnimation(UIElement.OpacityProperty, fa);
            DoubleAnimation ma = new DoubleAnimation(Core.U(6), 0, TimeSpan.FromMilliseconds(200));
            ma.BeginTime = delay;
            ma.EasingFunction = ease;
            ty.BeginAnimation(TranslateTransform.YProperty, ma);
        }

        static TextBlock OutlineLabel(string text, Brush fg)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = Core.U(10.5);
            t.FontWeight = FontWeights.SemiBold;
            t.Foreground = fg;
            t.TextAlignment = TextAlignment.Center;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Top;
            t.IsHitTestVisible = false;
            return t;
        }

        public void CloseNow()
        {
            if (Environment.GetEnvironmentVariable("DF_ZLOG") != null) { try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "df_zlog.txt"), "CLOSE " + DateTime.Now.ToString("HH:mm:ss.fff") + "\r\n", Encoding.UTF8); } catch { } }
            cur = null;
            if (Core.Zoom != null) Core.Zoom.HideZoom();
            if (!IsVisible) return;
            AnimateOut();
        }
    }

    public class ZoomWindow : Window
    {
        Canvas root;
        Image img;
        Image bgImg;
        ScaleTransform scl;
        bool styled;
        int gen;

        static readonly double ArtDip = 48;
        static readonly double Grow = 1.16;

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

        public ZoomWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            root = new Canvas();
            bgImg = new Image();
            bgImg.Stretch = Stretch.Fill;
            RenderOptions.SetBitmapScalingMode(bgImg, BitmapScalingMode.NearestNeighbor);
            img = new Image();
            img.Stretch = Stretch.Fill;
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            scl = new ScaleTransform(1, 1);
            img.RenderTransform = scl;
            img.RenderTransformOrigin = new Point(0.5, 1.0);
            root.Children.Add(bgImg);
            root.Children.Add(img);
            Content = root;
        }

        void ApplyHitThrough()
        {
            if (styled) return;
            try
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                int ex = GetWindowLong(h, -20);
                SetWindowLong(h, -20, ex | 0x20 | 0x08000000 | 0x80);
                styled = true;
            }
            catch { }
        }

        BitmapSource patch;
        int pL, pT, pR, pB;
        string patchKey;
        Folder curF;
        Rect curRect;
        bool busy;
        string shownPath;
        Folder pendF;
        Rect pendR;
        DateTime patchAt = DateTime.MinValue;

        public double PatchAge { get { return (DateTime.Now - patchAt).TotalSeconds; } }

        public void RefreshPatch()
        {
            if (curF == null || patch == null) return;
            int l = 0, t = 0; BitmapSource bs = Core.PatchFor(curF, curRect, out l, out t);
            patchAt = DateTime.Now;
            if (bs == null) return;
            if (bs.PixelWidth != patch.PixelWidth || bs.PixelHeight != patch.PixelHeight)
            {
                patch = null;
                ShowFor(curF, curRect);
                return;
            }
            patch = bs;
            patchKey = null;
            bgImg.Source = patch;
        }

        public void Prepare(Folder f, Rect itemRect)
        {
            patch = null;
            double sc = Core.Scale;
            pL = (int)Math.Floor(itemRect.Left - 3 * sc);
            pT = (int)Math.Floor(itemRect.Top - 3 * sc);
            pR = (int)Math.Ceiling(itemRect.Right + 3 * sc);
            pB = (int)Math.Ceiling(itemRect.Bottom + 3 * sc);
            string key = f.Path + "|" + (int)itemRect.Left + "," + (int)itemRect.Top;
            if (patchKey != null && patchKey == key && patch != null) return;
            BitmapSource bs = Core.PatchFor(f, itemRect, out pL, out pT);
            if (bs == null) return;
            patch = bs;
            pR = pL + bs.PixelWidth;
            pB = pT + bs.PixelHeight;
            patchKey = key;
            patchAt = DateTime.Now;
        }

        public void ShowFor(Folder f, Rect itemRect)
        {
            double sc = Core.Scale;
            double artPx = ArtDip * sc;
            double growPx = artPx * Grow;
            double cx = itemRect.Left + itemRect.Width / 2;
            double bottom = itemRect.Top + artPx;
            double gL = cx - growPx / 2, gT = bottom - growPx;
            curF = f; curRect = itemRect;
            string key = f.Path + "|" + (int)itemRect.Left + "," + (int)itemRect.Top;
            if (patchKey != key) patch = null;
            if (patch == null)
            {
                if (shownPath != f.Path) { HideZoom(); shownPath = f.Path; }
                if (busy) { pendF = f; pendR = itemRect; return; }
                busy = true;
                Folder ff = f; Rect rr = itemRect;
                System.Threading.Thread th = new System.Threading.Thread((System.Threading.ThreadStart)delegate
                {
                    BitmapSource bs = null; int l = 0, t = 0;
                    try { bs = Core.PatchFor(ff, rr, out l, out t); } catch { }
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        busy = false;
                        if (bs != null)
                        {
                            patch = bs; pL = l; pT = t;
                            pR = l + bs.PixelWidth; pB = t + bs.PixelHeight;
                            patchKey = ff.Path + "|" + (int)rr.Left + "," + (int)rr.Top;
                            patchAt = DateTime.Now;
                            if (curF != null && curF.Path == ff.Path) ShowFor(ff, rr);
                        }
                        Folder pf = pendF; Rect prr = pendR;
                        pendF = null;
                        if (pf != null && curF != null && curF.Path == pf.Path) ShowFor(pf, prr);
                    });
                });
                th.IsBackground = true;
                th.Start();
                return;
            }
            shownPath = f.Path;
            bool hasPatch = patch != null;
            if (!hasPatch) { HideZoom(); return; }
            int vx0 = (int)Math.Floor(SystemParameters.VirtualScreenLeft * sc);
            int vy0 = (int)Math.Floor(SystemParameters.VirtualScreenTop * sc);
            int vw = (int)Math.Ceiling(SystemParameters.VirtualScreenWidth * sc);
            int vh = (int)Math.Ceiling(SystemParameters.VirtualScreenHeight * sc);
            int winL = vx0, winT = vy0, winR = vx0 + vw, winB = vy0 + vh;
            BitmapSource src = Core.PlainIconFor(f.Path);
            if (src == null) return;
            if (Environment.GetEnvironmentVariable("DF_ZLOG") != null)
            {
                try
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "df_zlog.txt"),
                        f.Path + " win=" + winL + "," + winT + "+" + (winR - winL) + "x" + (winB - winT)
                        + " R=" + (int)itemRect.Left + "," + (int)itemRect.Top + "+" + (int)itemRect.Width + "x" + (int)itemRect.Height
                        + " patch=" + (hasPatch ? (pL + "," + pT + "+" + (pR - pL) + "x" + (pB - pT)) : "null")
                        + " src=" + src.PixelWidth + "x" + src.PixelHeight + "\r\n", Encoding.UTF8);
                }
                catch { }
            }
            bgImg.Source = patch;
            bgImg.Visibility = hasPatch ? Visibility.Visible : Visibility.Collapsed;
            img.Source = src;
            Width = vw / sc;
            Height = vh / sc;
            Left = vx0 / sc;
            Top = vy0 / sc;
            bgImg.Width = (pR - pL) / sc;
            bgImg.Height = (pB - pT) / sc;
            Canvas.SetLeft(bgImg, (pL - winL) / sc);
            Canvas.SetTop(bgImg, (pT - winT) / sc);
            img.Width = growPx / sc;
            img.Height = growPx / sc;
            Canvas.SetLeft(img, (gL - winL) / sc);
            Canvas.SetTop(img, (gT - winT) / sc);

            gen++;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            if (!IsVisible)
            {
                Show();
                ApplyHitThrough();
            }
            CubicEase ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            double fromA = 1.0 / Grow;
#if FORCE_TEST
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "df_zoom.txt"),
                    f.Path + " rect=" + itemRect.Left + "," + itemRect.Top + "-" + itemRect.Right + "," + itemRect.Bottom
                    + " left=" + Math.Round(Left, 1) + " top=" + Math.Round(Top, 1) + " w=" + Math.Round(Width, 1) + "\r\n", Encoding.UTF8);
            }
            catch { }
#endif
            DoubleAnimation sx = PanelWindow.Make(fromA, 1, 180, ease);
            scl.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            scl.BeginAnimation(ScaleTransform.ScaleYProperty, sx);
            try
            {
                if (Environment.GetEnvironmentVariable("DF_ZLOG") != null)
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "df_zlog.txt"),
                        "shown op=" + Opacity + " vis=" + IsVisible + " scl=" + scl.ScaleX + " iw=" + img.Width + " ih=" + img.Height
                        + " left=" + Left + " top=" + Top + " w=" + Width + " h=" + Height + "\r\n", Encoding.UTF8);
            }
            catch { }
        }

        public void HideZoom()
        {
            if (Environment.GetEnvironmentVariable("DF_ZLOG") != null) { try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "df_zlog.txt"), "HIDE " + DateTime.Now.ToString("HH:mm:ss.fff") + "\r\n", Encoding.UTF8); } catch { } }
            try { if (Environment.GetEnvironmentVariable("DF_ZLOG") != null) File.AppendAllText(Path.Combine(Path.GetTempPath(), "df_zlog.txt"), "hide\r\n", Encoding.UTF8); } catch { }
            gen++;
            int g = gen;
            BeginAnimation(OpacityProperty, null);
            if (!IsVisible) return;
            DoubleAnimation a = PanelWindow.Make(Opacity < 0.05 ? 1 : Opacity, 0, 140, new CubicEase { EasingMode = EasingMode.EaseInOut });
            a.Completed += delegate
            {
                if (g != gen) return;
                Hide();
                Opacity = 1;
                scl.ScaleX = 1;
                scl.ScaleY = 1;
            };
            BeginAnimation(OpacityProperty, a);
        }
    }

    // ============================ 统一的深色界面工具 ============================
    public static class Ui
    {
        public static readonly Color Accent = Color.FromRgb(0x5B, 0x8C, 0xFF);
        public static readonly Color Accent2 = Color.FromRgb(0x93, 0x74, 0xFF);
        public static readonly Color Ink = Color.FromRgb(0xEC, 0xF1, 0xFA);
        public static readonly Color Ink2 = Color.FromRgb(0x97, 0xA2, 0xB8);
        public static readonly Color Ink3 = Color.FromRgb(0x63, 0x6E, 0x85);
        public static readonly Color WinBg = Color.FromRgb(0x0A, 0x0D, 0x14);
        public static readonly Color CardBg = Color.FromRgb(0x11, 0x16, 0x21);
        public static readonly Color CardBd = Color.FromRgb(0x1D, 0x24, 0x33);
        public static readonly Color Line = Color.FromRgb(0x1A, 0x20, 0x2D);
        public static readonly Color Danger = Color.FromRgb(0xFF, 0x70, 0x70);
        public static readonly Color Good = Color.FromRgb(0x4A, 0xDE, 0x80);
        public static readonly FontFamily Font = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
        public static readonly FontFamily GlyphFont = new FontFamily("Segoe MDL2 Assets");

        public static Color Hex(string s)
        {
            try { return (Color)ColorConverter.ConvertFromString(s); }
            catch { return Color.FromRgb(0x12, 0x14, 0x1B); }
        }

        public static SolidColorBrush Br(Color c)
        {
            SolidColorBrush b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static SolidColorBrush Br(string s) { return Br(Hex(s)); }

        public static Color Alpha(Color c, byte a) { return Color.FromArgb(a, c.R, c.G, c.B); }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromRgb(
                (byte)Math.Max(0, Math.Min(255, a.R + (b.R - a.R) * t)),
                (byte)Math.Max(0, Math.Min(255, a.G + (b.G - a.G) * t)),
                (byte)Math.Max(0, Math.Min(255, a.B + (b.B - a.B) * t)));
        }

        public static Color Light(Color c, double t)
        {
            Color m = Mix(c, Colors.White, t);
            return Color.FromArgb(c.A, m.R, m.G, m.B);
        }

        public static Color Dark(Color c, double t)
        {
            Color m = Mix(c, Colors.Black, t);
            return Color.FromArgb(c.A, m.R, m.G, m.B);
        }
        public static double Lum(Color c) { return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0; }

        public static TextBlock T(string s, double size, Color c, bool bold)
        {
            TextBlock t = new TextBlock();
            t.Text = s;
            t.FontSize = size;
            t.Foreground = Br(c);
            t.FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal;
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        public static TextBlock T(string s, double size, Color c) { return T(s, size, c, false); }

        public static TextBlock Glyph(string g, double size, Color c)
        {
            TextBlock t = new TextBlock();
            t.Text = g;
            t.FontFamily = GlyphFont;
            t.FontSize = size;
            t.Foreground = Br(c);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            return t;
        }

        public static Border Card(double r)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(r);
            b.Background = Br(CardBg);
            b.BorderBrush = Br(CardBd);
            b.BorderThickness = new Thickness(1);
            b.SnapsToDevicePixels = true;
            return b;
        }

        public static void Anim(SolidColorBrush b, Color to, double ms)
        {
            if (b == null || b.IsFrozen) return;
            ColorAnimation a = new ColorAnimation(to, TimeSpan.FromMilliseconds(ms));
            a.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            b.BeginAnimation(SolidColorBrush.ColorProperty, a);
        }

        public enum BtnKind { Primary, Ghost, Danger, Subtle }

        public static Border Btn(string text, BtnKind kind, Action click) { return Btn(text, kind, 12, click); }

        public static Border Btn(string text, BtnKind kind, double fontSize, Action click)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(9);
            b.Padding = new Thickness(kind == BtnKind.Primary ? 16 : 11, kind == BtnKind.Primary ? 8 : 5,
                                      kind == BtnKind.Primary ? 16 : 11, kind == BtnKind.Primary ? 8 : 5);
            b.Cursor = Cursors.Hand;
            b.SnapsToDevicePixels = true;
            b.RenderTransformOrigin = new Point(0.5, 0.5);
            ScaleTransform sc = new ScaleTransform(1, 1);
            b.RenderTransform = sc;

            Color bg0, bg1, fg0, fg1, bd0, bd1;
            if (kind == BtnKind.Primary)
            {
                bg0 = Color.FromRgb(0x4B, 0x7C, 0xF6); bg1 = Color.FromRgb(0x6E, 0x98, 0xFF);
                fg0 = Colors.White; fg1 = Colors.White;
                bd0 = Colors.Transparent; bd1 = Colors.Transparent;
                b.Effect = new DropShadowEffect { Color = Color.FromArgb(255, 0x36, 0x63, 0xE0), BlurRadius = 16, ShadowDepth = 0, Opacity = 0.55 };
            }
            else if (kind == BtnKind.Danger)
            {
                bg0 = Color.FromArgb(0, 0, 0, 0); bg1 = Color.FromArgb(30, 0xFF, 0x70, 0x70);
                fg0 = Color.FromRgb(0xCE, 0x94, 0x9A); fg1 = Danger;
                bd0 = Color.FromArgb(54, 0xFF, 0x70, 0x70); bd1 = Color.FromArgb(120, 0xFF, 0x70, 0x70);
                b.BorderThickness = new Thickness(1);
            }
            else if (kind == BtnKind.Ghost)
            {
                bg0 = Color.FromArgb(0, 0, 0, 0); bg1 = Color.FromRgb(0x1A, 0x21, 0x2E);
                fg0 = Color.FromRgb(0xD2, 0xDA, 0xE8); fg1 = Colors.White;
                bd0 = Color.FromRgb(0x25, 0x2D, 0x3E); bd1 = Color.FromRgb(0x37, 0x43, 0x5B);
                b.BorderThickness = new Thickness(1);
            }
            else
            {
                bg0 = Color.FromArgb(0, 0, 0, 0); bg1 = Color.FromRgb(0x1B, 0x22, 0x30);
                fg0 = Ink2; fg1 = Ink;
                bd0 = Color.FromArgb(0, 0, 0, 0); bd1 = Color.FromRgb(0x2A, 0x34, 0x49);
                b.BorderThickness = new Thickness(1);
            }

            TextBlock t = T(text, fontSize, fg0, false);
            SolidColorBrush bg = new SolidColorBrush(bg0);
            SolidColorBrush fg = new SolidColorBrush(fg0);
            SolidColorBrush bd = new SolidColorBrush(bd0);
            b.Background = bg;
            if (b.BorderThickness.Left > 0) b.BorderBrush = bd;
            b.Child = t;

            bool over = false;
            b.MouseEnter += delegate { over = true; Anim(bg, bg1, 130); Anim(fg, fg1, 130); Anim(bd, bd1, 130); };
            b.MouseLeave += delegate
            {
                over = false;
                Anim(bg, bg0, 160); Anim(fg, fg0, 160); Anim(bd, bd0, 160);
                DoubleAnimation a = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(110));
                a.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            };
            b.MouseLeftButtonDown += delegate
            {
                DoubleAnimation a = new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(90));
                a.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            };
            b.MouseLeftButtonUp += delegate
            {
                DoubleAnimation a = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120));
                a.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                if (over && click != null) click();
            };
            return b;
        }

        public static Border WinBtn(string glyph, bool danger, Action click)
        {
            Border b = new Border();
            b.Width = 34;
            b.Height = 26;
            b.CornerRadius = new CornerRadius(7);
            b.Cursor = Cursors.Hand;
            b.Margin = new Thickness(3, 0, 0, 0);
            TextBlock t = Glyph(glyph, 10, Ink2);
            SolidColorBrush bg = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            SolidColorBrush fg = new SolidColorBrush(Ink2);
            t.Foreground = fg;
            b.Background = bg;
            b.Child = t;
            b.MouseEnter += delegate
            {
                Anim(bg, danger ? Color.FromRgb(0xE5, 0x48, 0x4D) : Color.FromRgb(0x1C, 0x24, 0x33), 120);
                Anim(fg, Colors.White, 120);
            };
            b.MouseLeave += delegate
            {
                Anim(bg, Color.FromArgb(0, 0, 0, 0), 160);
                Anim(fg, Ink2, 160);
            };
            b.MouseLeftButtonUp += delegate { if (click != null) click(); };
            return b;
        }

        public static Border Chip(TextBlock text)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(9);
            b.Padding = new Thickness(10, 4, 10, 4);
            b.Margin = new Thickness(8, 0, 0, 0);
            b.Background = Br(Alpha(Accent, 24));
            b.BorderBrush = Br(Alpha(Accent, 60));
            b.BorderThickness = new Thickness(1);
            b.Child = text;
            return b;
        }

        public static void StyleTooltip(FrameworkElement fe)
        {
            try
            {
                Style st = new Style(typeof(ToolTip));
                st.Setters.Add(new Setter(Control.BackgroundProperty, Br("#1A2231")));
                st.Setters.Add(new Setter(Control.ForegroundProperty, Br("#E7ECF5")));
                st.Setters.Add(new Setter(Control.BorderBrushProperty, Br("#2E3A51")));
                st.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
                st.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
                st.Setters.Add(new Setter(Control.FontSizeProperty, 11.0));
                st.Setters.Add(new Setter(Control.FontFamilyProperty, Font));
                fe.Resources[typeof(ToolTip)] = st;
            }
            catch { }
        }

        public static void StyleScroll(ScrollViewer sv)
        {
            try
            {
                string xaml =
                    "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                    "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>" +
                    "<Grid Width='10' Background='Transparent'>" +
                    "<Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True' " +
                    "Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' " +
                    "Value='{TemplateBinding Value}' ViewportSize='{TemplateBinding ViewportSize}'>" +
                    "<Track.Thumb><Thumb><Thumb.Template>" +
                    "<ControlTemplate TargetType='{x:Type Thumb}'>" +
                    "<Border x:Name='b' CornerRadius='3' Margin='2,2,2,2' Background='#2B3447'/>" +
                    "<ControlTemplate.Triggers>" +
                    "<Trigger Property='IsMouseOver' Value='True'>" +
                    "<Setter TargetName='b' Property='Background' Value='#4A5A7A'/>" +
                    "</Trigger></ControlTemplate.Triggers>" +
                    "</ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
                    "</Track></Grid></ControlTemplate>";
                ControlTemplate tpl = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
                Style st = new Style(typeof(ScrollBar));
                st.Setters.Add(new Setter(Control.TemplateProperty, tpl));
                st.Setters.Add(new Setter(FrameworkElement.WidthProperty, 10.0));
                sv.Resources.Add(typeof(ScrollBar), st);
            }
            catch { }
        }
    }

    // ============================ 迷你开关 ============================
    public class SwitchView : Grid
    {
        Border track;
        Border knob;
        TranslateTransform tr;
        Action<bool> changed;
        bool state;

        public SwitchView(bool on, Action<bool> onChange)
        {
            changed = onChange;
            state = on;
            Width = 44;
            Height = 24;
            Cursor = Cursors.Hand;
            Background = Brushes.Transparent;

            track = new Border();
            track.Width = 44;
            track.Height = 24;
            track.CornerRadius = new CornerRadius(12);
            track.Background = new SolidColorBrush(on ? Ui.Accent : Ui.Hex("#2A3345"));

            knob = new Border();
            knob.Width = 18;
            knob.Height = 18;
            knob.CornerRadius = new CornerRadius(9);
            knob.Background = Ui.Br("#FFFFFF");
            knob.HorizontalAlignment = HorizontalAlignment.Left;
            knob.VerticalAlignment = VerticalAlignment.Center;
            knob.Margin = new Thickness(0);
            knob.Effect = new DropShadowEffect { BlurRadius = 7, ShadowDepth = 1, Opacity = 0.4, Color = Colors.Black };
            tr = new TranslateTransform(on ? 23 : 3, 0);
            knob.RenderTransform = tr;

            Children.Add(track);
            Children.Add(knob);

            MouseLeftButtonUp += delegate { Set(!state, true); };
        }

        public bool State { get { return state; } }

        public void Set(bool on, bool notify)
        {
            state = on;
            Ui.Anim((SolidColorBrush)track.Background, on ? Ui.Accent : Ui.Hex("#2A3345"), 150);
            DoubleAnimation a = new DoubleAnimation(on ? 23 : 3, TimeSpan.FromMilliseconds(170));
            a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            tr.BeginAnimation(TranslateTransform.XProperty, a);
            if (notify && changed != null) changed(on);
        }
    }

    // ============================ 自定义滑杆 ============================
    public class FancySlider : Grid
    {
        double min, max, val;
        Border fill, thumb;
        bool drag;
        public event Action<double> Changed;

        public FancySlider(double min, double max, double value)
        {
            this.min = min;
            this.max = max;
            this.val = Math.Max(min, Math.Min(max, value));
            Height = 26;
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;

            Border track = new Border();
            track.Height = 6;
            track.CornerRadius = new CornerRadius(3);
            track.Background = Ui.Br("#222B3B");
            track.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(track);

            LinearGradientBrush fb = new LinearGradientBrush();
            fb.StartPoint = new Point(0, 0);
            fb.EndPoint = new Point(1, 0);
            fb.GradientStops.Add(new GradientStop(Ui.Accent, 0));
            fb.GradientStops.Add(new GradientStop(Ui.Accent2, 1));
            fill = new Border();
            fill.Height = 6;
            fill.CornerRadius = new CornerRadius(3);
            fill.Background = fb;
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            fill.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(fill);

            thumb = new Border();
            thumb.Width = 16;
            thumb.Height = 16;
            thumb.CornerRadius = new CornerRadius(8);
            thumb.Background = Ui.Br("#FFFFFF");
            thumb.HorizontalAlignment = HorizontalAlignment.Left;
            thumb.VerticalAlignment = VerticalAlignment.Center;
            thumb.Effect = new DropShadowEffect { BlurRadius = 9, ShadowDepth = 0, Opacity = 0.5, Color = Color.FromArgb(255, 0x31, 0x5A, 0xC8) };
            Children.Add(thumb);

            SizeChanged += delegate { Layout(); };
            PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                drag = true;
                CaptureMouse();
                SetFromPoint(e.GetPosition(this).X);
                e.Handled = true;
            };
            MouseMove += delegate(object s, MouseEventArgs e) { if (drag) SetFromPoint(e.GetPosition(this).X); };
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (drag) { drag = false; ReleaseMouseCapture(); e.Handled = true; }
            };
            MouseLeave += delegate { if (drag) { drag = false; ReleaseMouseCapture(); } };
            Layout();
        }

        public double Value { get { return val; } }

        void Layout()
        {
            double w = ActualWidth;
            if (w <= 1) w = 200;
            double span = Math.Max(1, w - 16);
            double x = (val - min) / (max - min) * span;
            thumb.Margin = new Thickness(x, 0, 0, 0);
            fill.Width = x + 8;
        }

        void SetFromPoint(double px)
        {
            double w = ActualWidth;
            if (w <= 20) return;
            double t = (px - 8) / Math.Max(1, w - 16);
            t = Math.Max(0, Math.Min(1, t));
            double nv = min + t * (max - min);
            if (Math.Abs(nv - val) < 0.01) return;
            val = nv;
            Layout();
            if (Changed != null) Changed(val);
        }
    }

    // ============================ 管理窗口 ============================
    public class ManagerWindow : Window
    {
        StackPanel list;
        StackPanel body;
        ScrollViewer svBody;
        TextBlock statFolders, statItems, autoHint;
        readonly List<Border> swatches = new List<Border>();
        readonly List<Border> miniIcons = new List<Border>();
        readonly List<Border> miniBars = new List<Border>();
        System.Windows.Shapes.Rectangle previewBg;
        FancySlider opacity;
        TextBlock opacityVal;
        SwitchView clearSwitch;

        static readonly string[] SwatchColors = { "#12141B", "#000000", "#2A2F3A", "#1B2340", "#38262B", "#1D3324", "#F2F0EA", "#E9EEF6" };

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);

        public ManagerWindow()
        {
            Title = "DeskFolder";
            WindowStyle = WindowStyle.None;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 48,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
            Width = 660;
            Height = 830;
            MinWidth = 600;
            MinHeight = 540;
            Rect wa0 = SystemParameters.WorkArea;
            if (Height > wa0.Height - 40) Height = Math.Max(MinHeight, wa0.Height - 40);
            if (Width > wa0.Width - 40) Width = Math.Max(MinWidth, wa0.Width - 40);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Ui.Br(Ui.WinBg);
            FontFamily = Ui.Font;
            Foreground = Ui.Br(Ui.Ink);
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            Ui.StyleTooltip(this);
            try
            {
                string ip = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), "悬浮文件夹.ico");
                if (File.Exists(ip)) Icon = BitmapFrame.Create(new Uri(ip));
            }
            catch { }

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            // 顶部渐变细线
            LinearGradientBrush al = new LinearGradientBrush();
            al.StartPoint = new Point(0, 0);
            al.EndPoint = new Point(1, 0);
            al.GradientStops.Add(new GradientStop(Ui.Accent, 0));
            al.GradientStops.Add(new GradientStop(Ui.Accent2, 0.45));
            al.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x93, 0x74, 0xFF), 1));
            Border accentLine = new Border();
            accentLine.Background = al;
            Grid.SetRow(accentLine, 0);
            Panel.SetZIndex(accentLine, 5);
            root.Children.Add(accentLine);

            // ---------- 标题栏 ----------
            DockPanel bar = new DockPanel();
            bar.Margin = new Thickness(14, 0, 10, 0);
            Grid.SetRow(bar, 1);
            root.Children.Add(bar);

            StackPanel winBtns = new StackPanel();
            winBtns.Orientation = Orientation.Horizontal;
            winBtns.VerticalAlignment = VerticalAlignment.Center;
            winBtns.HorizontalAlignment = HorizontalAlignment.Right;
            Border minBtn = Ui.WinBtn("\uE921", false, delegate { WindowState = WindowState.Minimized; });
            Border closeBtn = Ui.WinBtn("\uE8BB", true, delegate { Close(); });
            WindowChrome.SetIsHitTestVisibleInChrome(minBtn, true);
            WindowChrome.SetIsHitTestVisibleInChrome(closeBtn, true);
            winBtns.Children.Add(minBtn);
            winBtns.Children.Add(closeBtn);
            DockPanel.SetDock(winBtns, Dock.Right);
            bar.Children.Add(winBtns);

            StackPanel leftArea = new StackPanel();
            leftArea.Orientation = Orientation.Horizontal;
            leftArea.VerticalAlignment = VerticalAlignment.Center;

            Border logo = new Border();
            logo.Width = 24;
            logo.Height = 24;
            logo.CornerRadius = new CornerRadius(7);
            LinearGradientBrush lg = new LinearGradientBrush();
            lg.StartPoint = new Point(0, 0);
            lg.EndPoint = new Point(1, 1);
            lg.GradientStops.Add(new GradientStop(Ui.Accent, 0));
            lg.GradientStops.Add(new GradientStop(Ui.Accent2, 1));
            logo.Background = lg;
            logo.Child = Ui.Glyph("\uE8B7", 11, Colors.White);
            leftArea.Children.Add(logo);

            TextBlock appName = Ui.T("DeskFolder", 12.5, Ui.Ink, true);
            appName.Margin = new Thickness(9, 0, 0, 0);
            leftArea.Children.Add(appName);
            TextBlock verTag = Ui.T("悬浮文件夹", 9.5, Ui.Ink3, false);
            verTag.Margin = new Thickness(8, 1, 0, 0);
            leftArea.Children.Add(verTag);
            leftArea.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
            };
            bar.Children.Add(leftArea);

            // ---------- 内容 ----------
            svBody = new ScrollViewer();
            svBody.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            svBody.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            svBody.Padding = new Thickness(20, 2, 12, 10);
            svBody.Focusable = false;
            Ui.StyleScroll(svBody);
            Grid.SetRow(svBody, 2);
            root.Children.Add(svBody);

            body = new StackPanel();
            svBody.Content = body;

            // ---------- 头部横幅 ----------
            Border hero = new Border();
            hero.CornerRadius = new CornerRadius(16);
            hero.Padding = new Thickness(18, 14, 16, 14);
            hero.Margin = new Thickness(0, 2, 4, 12);
            hero.BorderThickness = new Thickness(1);
            hero.BorderBrush = Ui.Br(Ui.Alpha(Ui.Accent, 42));
            LinearGradientBrush hbg = new LinearGradientBrush();
            hbg.StartPoint = new Point(0, 0);
            hbg.EndPoint = new Point(1, 1);
            hbg.GradientStops.Add(new GradientStop(Color.FromRgb(0x17, 0x20, 0x38), 0));
            hbg.GradientStops.Add(new GradientStop(Color.FromRgb(0x10, 0x14, 0x20), 1));
            hero.Background = hbg;
            body.Children.Add(hero);

            Grid heroGrid = new Grid();
            RadialGradientBrush glow = new RadialGradientBrush();
            glow.Center = new Point(0.86, 0.18);
            glow.GradientOrigin = new Point(0.86, 0.18);
            glow.RadiusX = 0.55;
            glow.RadiusY = 0.95;
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(66, 0x5B, 0x8C, 0xFF), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x5B, 0x8C, 0xFF), 1));
            Border glowLayer = new Border();
            glowLayer.Background = glow;
            glowLayer.IsHitTestVisible = false;
            heroGrid.Children.Add(glowLayer);

            DockPanel heroRow = new DockPanel();
            heroGrid.Children.Add(heroRow);

            StackPanel heroText = new StackPanel();
            heroText.VerticalAlignment = VerticalAlignment.Center;
            TextBlock heroTitle = Ui.T("DeskFolder", 19, Ui.Ink, true);
            heroText.Children.Add(heroTitle);
            TextBlock heroSub = Ui.T("桌面文件夹悬停预览 · 外观与开关", 11, Ui.Ink2, false);
            heroSub.Margin = new Thickness(0, 4, 0, 0);
            heroText.Children.Add(heroSub);
            heroRow.Children.Add(heroText);

            StackPanel chips = new StackPanel();
            chips.Orientation = Orientation.Horizontal;
            chips.VerticalAlignment = VerticalAlignment.Center;
            chips.HorizontalAlignment = HorizontalAlignment.Right;
            statFolders = Ui.T("0 个文件夹", 11, Ui.Accent, false);
            statItems = Ui.T("0 个项目", 11, Ui.Good, false);
            Border chipA = Ui.Chip(statFolders);
            Border chipB = Ui.Chip(statItems);
            chipB.Background = Ui.Br(Ui.Alpha(Ui.Good, 20));
            chipB.BorderBrush = Ui.Br(Ui.Alpha(Ui.Good, 56));
            chips.Children.Add(chipA);
            chips.Children.Add(chipB);
            heroRow.Children.Add(chips);
            hero.Child = heroGrid;

            // ---------- 面板外观 ----------
            Border cApp = Ui.Card(16);
            cApp.Padding = new Thickness(18, 15, 18, 16);
            cApp.Margin = new Thickness(0, 0, 4, 12);
            body.Children.Add(cApp);
            StackPanel sApp = new StackPanel();
            cApp.Child = sApp;

            DockPanel ah = new DockPanel();
            ah.Children.Add(Ui.T("面板外观", 13, Ui.Ink, true));
            TextBlock ahHint = Ui.T("点色块即时换色 · 改动自动保存", 10.5, Ui.Ink3, false);
            ahHint.HorizontalAlignment = HorizontalAlignment.Right;
            ah.Children.Add(ahHint);
            sApp.Children.Add(ah);

            // 预览（带一张“壁纸”底色，能看出透明度）
            Grid pv = new Grid();
            pv.Height = 74;
            pv.Margin = new Thickness(0, 11, 0, 0);
            pv.ClipToBounds = true;
            LinearGradientBrush wall = new LinearGradientBrush();
            wall.StartPoint = new Point(0, 0);
            wall.EndPoint = new Point(1, 1);
            wall.GradientStops.Add(new GradientStop(Color.FromRgb(0x26, 0x3A, 0x57), 0));
            wall.GradientStops.Add(new GradientStop(Color.FromRgb(0x3C, 0x37, 0x65), 0.55));
            wall.GradientStops.Add(new GradientStop(Color.FromRgb(0x53, 0x40, 0x55), 1));
            pv.Background = wall;
            previewBg = new System.Windows.Shapes.Rectangle();
            previewBg.RadiusX = 12;
            previewBg.RadiusY = 12;
            previewBg.Margin = new Thickness(14, 8, 14, 8);
            previewBg.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.55, Color = Colors.Black };
            pv.Children.Add(previewBg);

            StackPanel pvContent = new StackPanel();
            pvContent.Orientation = Orientation.Horizontal;
            pvContent.HorizontalAlignment = HorizontalAlignment.Center;
            pvContent.VerticalAlignment = VerticalAlignment.Center;
            for (int i = 0; i < 3; i++) pvContent.Children.Add(MiniTile());
            pv.Children.Add(pvContent);
            sApp.Children.Add(pv);

            // 色块
            WrapPanel wp2 = new WrapPanel();
            wp2.Margin = new Thickness(0, 11, 0, 0);
            foreach (string hex in SwatchColors)
            {
                wp2.Children.Add(MakeSwatch(hex));
            }
            sApp.Children.Add(wp2);

            // 不透明度
            Grid opRow = new Grid();
            opRow.Margin = new Thickness(0, 4, 0, 0);
            opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            opRow.ColumnDefinitions.Add(new ColumnDefinition());
            opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock opLabel = Ui.T("不透明度", 11, Ui.Ink3, false);
            opLabel.Width = 56;
            Grid.SetColumn(opLabel, 0);
            opRow.Children.Add(opLabel);
            opacity = new FancySlider(15, 100, (Core.cfg.Alpha <= 0 ? 238 : Core.cfg.Alpha) / 2.55);
            opacity.Margin = new Thickness(6, 0, 12, 0);
            opacity.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(opacity, 1);
            opRow.Children.Add(opacity);
            opacityVal = Ui.T("100%", 11, Ui.Ink2, false);
            opacityVal.Width = 38;
            opacityVal.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(opacityVal, 2);
            opRow.Children.Add(opacityVal);
            opacityVal.Text = ((int)Math.Round(opacity.Value)) + "%";
            opacity.Changed += delegate(double v)
            {
                Core.cfg.Alpha = (int)(v * 2.55);
                opacityVal.Text = ((int)Math.Round(v)) + "%";
                Core.Save();
                RefreshPreview();
            };
            sApp.Children.Add(opRow);

            // 纯透明
            Border clearRow = new Border();
            clearRow.Margin = new Thickness(0, 8, 0, 0);
            sApp.Children.Add(clearRow);
            Grid cr = new Grid();
            cr.ColumnDefinitions.Add(new ColumnDefinition());
            cr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel crText = new StackPanel();
            crText.VerticalAlignment = VerticalAlignment.Center;
            crText.Children.Add(Ui.T("纯透明", 12, Ui.Ink, false));
            TextBlock crSub = Ui.T("不显示底板，只浮出图标与名字（任何壁纸上都看得清）", 10.5, Ui.Ink3, false);
            crSub.Margin = new Thickness(0, 3, 0, 0);
            crSub.TextWrapping = TextWrapping.Wrap;
            crText.Children.Add(crSub);
            Grid.SetColumn(crText, 0);
            cr.Children.Add(crText);
            clearSwitch = new SwitchView(Core.cfg.Clear, delegate(bool v)
            {
                Core.cfg.Clear = v;
                Core.Save();
                if (Core.Panel != null) Core.Panel.CloseNow();
                RefreshPreview();
                UpdateSwatchSelection();
            });
            clearSwitch.Margin = new Thickness(12, 0, 0, 0);
            clearSwitch.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(clearSwitch, 1);
            cr.Children.Add(clearSwitch);
            clearRow.Child = cr;

            // ---------- 常规开关 ----------
            Border cGen = Ui.Card(16);
            cGen.Padding = new Thickness(18, 15, 18, 8);
            cGen.Margin = new Thickness(0, 0, 4, 12);
            body.Children.Add(cGen);
            StackPanel sGen = new StackPanel();
            cGen.Child = sGen;
            sGen.Children.Add(Ui.T("常规", 13, Ui.Ink, true));

            Grid gg = new Grid();
            gg.Margin = new Thickness(0, 10, 0, 0);
            gg.ColumnDefinitions.Add(new ColumnDefinition());
            gg.ColumnDefinitions.Add(new ColumnDefinition());
            gg.RowDefinitions.Add(new RowDefinition());
            gg.RowDefinitions.Add(new RowDefinition());

            TextBlock allDesc = Ui.T(Core.cfg.All ? "桌面上的每个文件夹都会悬停展开" : "只展开下面列出的文件夹", 10.5, Ui.Ink3, false);
            autoHint = allDesc;
            allDesc.TextWrapping = TextWrapping.Wrap;
            allDesc.Margin = new Thickness(0, 3, 0, 0);
            SwitchView swAll = new SwitchView(Core.cfg.All, delegate(bool v)
            {
                Core.cfg.All = v;
                Core.Save();
                if (Core.Panel != null) Core.Panel.CloseNow();
                RebuildList();
            });
            FrameworkElement cellAll = ToggleCell("自动接管所有文件夹", allDesc, swAll, new Thickness(0, 0, 14, 12));
            Grid.SetColumn(cellAll, 0); Grid.SetRow(cellAll, 0);
            gg.Children.Add(cellAll);

            SwitchView swStart = null;
            swStart = new SwitchView(Core.StartupOn(), delegate(bool v)
            {
                Core.SetStartup(v);
                bool now = Core.StartupOn();
                if (now != v && swStart != null) swStart.Set(now, false);
            });
            TextBlock startDesc = Ui.T("登录 Windows 后自动在后台运行", 10.5, Ui.Ink3, false);
            startDesc.TextWrapping = TextWrapping.Wrap;
            startDesc.Margin = new Thickness(0, 3, 0, 0);
            FrameworkElement cellStart = ToggleCell("开机自动启动", startDesc, swStart, new Thickness(16, 0, 0, 12));
            Grid.SetColumn(cellStart, 1); Grid.SetRow(cellStart, 0);
            gg.Children.Add(cellStart);

            SwitchView swTray = new SwitchView(Core.cfg.Tray, delegate(bool v)
            {
                Core.cfg.Tray = v;
                Core.Save();
            });
            TextBlock trayDesc = Ui.T("关窗不退出，留在右下角托盘", 10.5, Ui.Ink3, false);
            trayDesc.TextWrapping = TextWrapping.Wrap;
            trayDesc.Margin = new Thickness(0, 3, 0, 0);
            FrameworkElement cellTray = ToggleCell("关闭时隐藏到托盘", trayDesc, swTray, new Thickness(0, 0, 14, 0));
            Grid.SetColumn(cellTray, 0); Grid.SetRow(cellTray, 1);
            gg.Children.Add(cellTray);

            SwitchView swLabels = new SwitchView(Core.cfg.ShowLabels, delegate(bool v)
            {
                Core.cfg.ShowLabels = v;
                Core.Save();
                Core.RefreshPanelLabels();
            });
            TextBlock lblDesc = Ui.T("面板里图标下方显示文件名", 10.5, Ui.Ink3, false);
            lblDesc.TextWrapping = TextWrapping.Wrap;
            lblDesc.Margin = new Thickness(0, 3, 0, 0);
            FrameworkElement cellLbl = ToggleCell("显示图标名字", lblDesc, swLabels, new Thickness(16, 0, 0, 0));
            Grid.SetColumn(cellLbl, 1); Grid.SetRow(cellLbl, 1);
            gg.Children.Add(cellLbl);

            Border vline = new Border();
            vline.Width = 1;
            vline.Background = Ui.Br(Color.FromRgb(0x1B, 0x22, 0x31));
            vline.HorizontalAlignment = HorizontalAlignment.Right;
            vline.Margin = new Thickness(0, 4, 0, 8);
            Grid.SetColumn(vline, 0);
            Grid.SetRowSpan(vline, 2);
            gg.Children.Add(vline);
            sGen.Children.Add(gg);

            // ---------- 文件夹列表 ----------
            DockPanel secHead = new DockPanel();
            secHead.Margin = new Thickness(3, 4, 5, 9);
            secHead.Children.Add(Ui.T("生效的文件夹", 13, Ui.Ink, true));
            TextBlock secHint = Ui.T("可以把文件直接拖到下面每一行", 10.5, Ui.Ink3, false);
            secHint.HorizontalAlignment = HorizontalAlignment.Right;
            secHead.Children.Add(secHint);
            body.Children.Add(secHead);

            list = new StackPanel();
            body.Children.Add(list);

            // ---------- 底部操作栏 ----------
            Border foot = new Border();
            foot.Background = Ui.Br(Color.FromRgb(0x0C, 0x10, 0x18));
            foot.BorderBrush = Ui.Br(Ui.Line);
            foot.BorderThickness = new Thickness(0, 1, 0, 0);
            foot.Padding = new Thickness(20, 12, 20, 12);
            Grid.SetRow(foot, 3);
            root.Children.Add(foot);

            DockPanel fd = new DockPanel();
            StackPanel fbtns = new StackPanel();
            fbtns.Orientation = Orientation.Horizontal;
            Border mk = Ui.Btn("＋ 创建悬浮窗文件夹", Ui.BtnKind.Primary, delegate { CreateFolder(); });
            Border sd = Ui.Btn("把文件夹放回桌面", Ui.BtnKind.Ghost, delegate { EnsureOnDesktop(); });
            sd.Margin = new Thickness(9, 0, 0, 0);
            Border q = Ui.Btn("退出程序", Ui.BtnKind.Danger, delegate { Core.ExitAll(); });
            q.Margin = new Thickness(9, 0, 0, 0);
            fbtns.Children.Add(mk);
            fbtns.Children.Add(sd);
            fbtns.Children.Add(q);
            fd.Children.Add(fbtns);
            TextBlock fh = Ui.T("关闭窗口 = 收进托盘", 10, Ui.Ink3, false);
            fh.HorizontalAlignment = HorizontalAlignment.Right;
            fh.VerticalAlignment = VerticalAlignment.Center;
            fd.Children.Add(fh);
            foot.Child = fd;

            // 窗口外框
            Border outline = new Border();
            outline.BorderBrush = Ui.Br(Color.FromRgb(0x1B, 0x22, 0x31));
            outline.BorderThickness = new Thickness(1);
            outline.IsHitTestVisible = false;
            Grid.SetRowSpan(outline, 4);
            root.Children.Add(outline);

            RefreshPreview();
            UpdateSwatchSelection();
            RebuildList();

            SourceInitialized += delegate
            {
                try
                {
                    IntPtr self = new WindowInteropHelper(this).Handle;
                    int style = GetWindowLong(self, -16);
                    SetWindowLong(self, -16, style & ~0x00010000); // 去掉最大化，避免无边框窗口最大化后盖住任务栏
                    HwndSource src = (HwndSource)PresentationSource.FromVisual(this);
                    if (src == null) return;
                    src.AddHook(new HwndSourceHook(delegate(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
                    {
                        if ((uint)msg == Core.MsgShow)
                        {
                            handled = true;
                            Dispatcher.BeginInvoke(new Action(delegate { Core.ShowManager(); }));
                        }
                        return IntPtr.Zero;
                    }));
                }
                catch { }
            };
            Closing += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (Core.cfg.Tray)
                {
                    e.Cancel = true;
                    Hide();
                }
                else
                {
                    Core.ExitAll();
                }
            };
        }

        static FrameworkElement ToggleCell(string title, TextBlock desc, SwitchView sw, Thickness margin)
        {
            Border cell = new Border();
            cell.Margin = margin;
            Grid inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition());
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel sp = new StackPanel();
            sp.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(Ui.T(title, 12, Ui.Ink, false));
            sp.Children.Add(desc);
            Grid.SetColumn(sp, 0);
            inner.Children.Add(sp);
            sw.Margin = new Thickness(12, 0, 0, 0);
            sw.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(sw, 1);
            inner.Children.Add(sw);
            cell.Child = inner;
            return cell;
        }

        StackPanel MiniTile()
        {
            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(0, 0, 16, 0);
            sp.VerticalAlignment = VerticalAlignment.Center;
            Border icon = new Border();
            icon.Width = 28;
            icon.Height = 28;
            icon.CornerRadius = new CornerRadius(8);
            icon.Background = Ui.Br(Color.FromArgb(32, 255, 255, 255));
            Image mi = new Image();
            mi.Width = 16;
            mi.Height = 16;
            mi.Stretch = Stretch.Uniform;
            RenderOptions.SetBitmapScalingMode(mi, BitmapScalingMode.HighQuality);
            BitmapSource mf = Core.FolderIcon();
            if (mf != null) mi.Source = mf;
            icon.Child = mi;
            sp.Children.Add(icon);
            Border bar = new Border();
            bar.Width = 26;
            bar.Height = 4;
            bar.CornerRadius = new CornerRadius(2);
            bar.Margin = new Thickness(1, 6, 0, 0);
            bar.HorizontalAlignment = HorizontalAlignment.Center;
            bar.Background = Ui.Br(Color.FromArgb(110, 255, 255, 255));
            sp.Children.Add(bar);
            miniIcons.Add(icon);
            miniBars.Add(bar);
            return sp;
        }

        Border MakeSwatch(string hex)
        {
            Color c = Ui.Hex(hex);
            Border b = new Border();
            b.Width = 28;
            b.Height = 28;
            b.CornerRadius = new CornerRadius(9);
            b.Margin = new Thickness(0, 0, 9, 9);
            b.Cursor = Cursors.Hand;
            b.SnapsToDevicePixels = true;
            b.Background = new SolidColorBrush(c);
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = Ui.Br(Color.FromArgb(34, 255, 255, 255));
            b.Tag = hex;
            TextBlock ck = Ui.Glyph("\uE73E", 10, Color.FromArgb(235, 255, 255, 255));
            ck.Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.9, Color = Colors.Black };
            ck.Visibility = Visibility.Collapsed;
            b.Child = ck;
            b.MouseEnter += delegate
            {
                if (b.BorderBrush is SolidColorBrush && !((SolidColorBrush)b.BorderBrush).IsFrozen)
                    Ui.Anim((SolidColorBrush)b.BorderBrush, Color.FromArgb(90, 255, 255, 255), 120);
            };
            b.MouseLeave += delegate { UpdateSwatchSelection(); };
            b.MouseLeftButtonUp += delegate
            {
                Core.cfg.Color = (string)b.Tag;
                Core.cfg.Clear = false;
                Core.Save();
                if (Core.Panel != null) Core.Panel.CloseNow();
                clearSwitch.Set(false, false);
                UpdateSwatchSelection();
                RefreshPreview();
            };
            swatches.Add(b);
            return b;
        }

        void UpdateSwatchSelection()
        {
            foreach (Border b in swatches)
            {
                bool sel = string.Equals((string)b.Tag, Core.cfg.Color, StringComparison.OrdinalIgnoreCase) && !Core.cfg.Clear;
                b.BorderThickness = new Thickness(sel ? 2 : 1);
                SolidColorBrush bd = new SolidColorBrush(sel ? Ui.Accent : Color.FromArgb(34, 255, 255, 255));
                b.BorderBrush = bd;
                TextBlock ck = b.Child as TextBlock;
                if (ck != null) ck.Visibility = sel ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        void RefreshPreview()
        {
            Config c = Core.cfg;
            Color col = PanelWindow.PanelColor();
            if (c.Clear)
            {
                previewBg.Fill = Brushes.Transparent;
                previewBg.Stroke = Ui.Br(Color.FromArgb(120, 255, 255, 255));
                previewBg.StrokeThickness = 1;
                previewBg.StrokeDashArray = new DoubleCollection { 4, 4 };
                previewBg.Effect = null;
            }
            else
            {
                previewBg.Fill = new SolidColorBrush(col);
                previewBg.Stroke = Ui.Br(Color.FromArgb(30, 255, 255, 255));
                previewBg.StrokeThickness = 1;
                previewBg.StrokeDashArray = null;
                previewBg.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Direction = 270, Opacity = 0.55, Color = Colors.Black };
            }
            bool onDark = c.Clear || Ui.Lum(col) <= 0.62;
            Color iconBg = onDark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);
            Color barFg = onDark ? Color.FromArgb(96, 255, 255, 255) : Color.FromArgb(70, 20, 24, 32);
            for (int i = 0; i < miniIcons.Count; i++)
            {
                miniIcons[i].Background = new SolidColorBrush(iconBg);
                miniBars[i].Background = new SolidColorBrush(barFg);
            }
        }

        public void RebuildList()
        {
            List<Folder> ts = Core.Targets();
            int items = 0;
            foreach (Folder f in ts)
            {
                try { items += Core.EntriesOf(f.Path).Length; }
                catch { }
            }
            statFolders.Text = ts.Count + " 个文件夹";
            statItems.Text = items + " 个项目";
            if (autoHint != null)
                autoHint.Text = Core.cfg.All ? "桌面上的每个文件夹都会悬停展开" : "只展开下面列出的文件夹";

            list.Children.Clear();
            if (ts.Count == 0)
            {
                list.Children.Add(EmptyState());
                return;
            }
            foreach (Folder f in ts) list.Children.Add(MakeRow(f));
        }

        FrameworkElement EmptyState()
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 2, 4, 0);
            System.Windows.Shapes.Rectangle r = new System.Windows.Shapes.Rectangle();
            r.RadiusX = 14;
            r.RadiusY = 14;
            r.Stroke = Ui.Br(Color.FromRgb(0x25, 0x2E, 0x40));
            r.StrokeThickness = 1;
            r.StrokeDashArray = new DoubleCollection { 5, 4 };
            r.Fill = Ui.Br(Color.FromRgb(0x0E, 0x12, 0x1B));
            g.Children.Add(r);

            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(20, 26, 20, 26);
            sp.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(Ui.Glyph("\uE8B7", 26, Ui.Ink3));
            TextBlock t = Ui.T(Core.cfg.All ? "桌面上还没有文件夹" : "还没有生效的文件夹", 12, Ui.Ink2, false);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(t);
            TextBlock t2 = Ui.T("点下面的「＋ 创建悬浮窗文件夹」新建一个", 10.5, Ui.Ink3, false);
            t2.HorizontalAlignment = HorizontalAlignment.Center;
            t2.Margin = new Thickness(0, 4, 0, 0);
            t2.TextWrapping = TextWrapping.Wrap;
            t2.TextAlignment = TextAlignment.Center;
            sp.Children.Add(t2);
            g.Children.Add(sp);
            return g;
        }

        static void RowHover(Border row, bool on)
        {
            SolidColorBrush bg = row.Background as SolidColorBrush;
            SolidColorBrush bd = row.BorderBrush as SolidColorBrush;
            if (bg != null) Ui.Anim(bg, on ? Ui.Hex("#151C2A") : Ui.CardBg, 120);
            if (bd != null) Ui.Anim(bd, on ? Ui.Hex("#2A3550") : Ui.CardBd, 120);
        }

        Border MakeRow(Folder f)
        {
            Border row = new Border();
            row.CornerRadius = new CornerRadius(13);
            row.Background = new SolidColorBrush(Ui.CardBg);
            row.BorderBrush = new SolidColorBrush(Ui.CardBd);
            row.BorderThickness = new Thickness(1);
            row.Padding = new Thickness(12, 10, 12, 10);
            row.Margin = new Thickness(0, 0, 4, 8);
            row.AllowDrop = true;
            row.Drop += delegate(object s, DragEventArgs e)
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null) Import(f, files);
                e.Handled = true;
            };
            row.DragOver += delegate(object s, DragEventArgs e)
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
            };
            row.MouseEnter += delegate { RowHover(row, true); };
            row.MouseLeave += delegate { RowHover(row, false); };

            int count = 0;
            try { if (Directory.Exists(f.Path)) count = Core.EntriesOf(f.Path).Length; }
            catch { }

            bool exists = Directory.Exists(f.Path);
            bool onDesk = exists && f.Path.StartsWith(Core.desktop, StringComparison.OrdinalIgnoreCase);

            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid info = new Grid();
            info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            info.ColumnDefinitions.Add(new ColumnDefinition());

            Border ip = new Border();
            ip.Width = 42;
            ip.Height = 42;
            ip.CornerRadius = new CornerRadius(11);
            ip.Background = Ui.Br(Color.FromRgb(0x18, 0x20, 0x2F));
            ip.BorderBrush = Ui.Br(Color.FromRgb(0x21, 0x2B, 0x3D));
            ip.BorderThickness = new Thickness(1);
            Image pic = new Image();
            pic.Width = 28;
            pic.Height = 28;
            pic.Stretch = Stretch.Uniform;
            RenderOptions.SetBitmapScalingMode(pic, BitmapScalingMode.HighQuality);
            BitmapSource src = Core.IconFor(f.Path);
            if (src != null) pic.Source = src;
            ScaleTransform picScale = new ScaleTransform(1, 1);
            pic.RenderTransform = picScale;
            pic.RenderTransformOrigin = new Point(0.5, 0.5);
            pic.MouseEnter += delegate
            {
                DoubleAnimation a = new DoubleAnimation(1.14, TimeSpan.FromMilliseconds(170));
                a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                picScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                picScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            };
            pic.MouseLeave += delegate
            {
                DoubleAnimation a = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(170));
                a.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                picScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                picScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            };
            ip.Child = pic;
            Grid.SetColumn(ip, 0);
            info.Children.Add(ip);

            StackPanel txt = new StackPanel();
            txt.VerticalAlignment = VerticalAlignment.Center;
            txt.Margin = new Thickness(12, 0, 8, 0);
            TextBlock n1 = Ui.T(Core.DisplayName(f), 13, Ui.Ink, true);
            n1.TextTrimming = TextTrimming.CharacterEllipsis;
            txt.Children.Add(n1);
            StackPanel meta = new StackPanel();
            meta.Orientation = Orientation.Horizontal;
            meta.Margin = new Thickness(0, 4, 0, 0);
            Border dot = new Border();
            dot.Width = 6;
            dot.Height = 6;
            dot.CornerRadius = new CornerRadius(3);
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.Background = Ui.Br(!exists ? Color.FromRgb(0xFF, 0x70, 0x70) : (onDesk ? Ui.Good : Color.FromRgb(0xF5, 0xB6, 0x5C)));
            meta.Children.Add(dot);
            TextBlock n2 = Ui.T("  " + count + " 个项目 · " + (exists ? (onDesk ? "在桌面，悬停展开" : "不在桌面") : "文件夹已不存在"), 10.5, Ui.Ink3, false);
            n2.TextTrimming = TextTrimming.CharacterEllipsis;
            meta.Children.Add(n2);
            txt.Children.Add(meta);
            Grid.SetColumn(txt, 1);
            info.Children.Add(txt);
            info.ColumnDefinitions[0].Width = new GridLength(42);
            Grid.SetColumn(info, 0);
            g.Children.Add(info);

            StackPanel acts = new StackPanel();
            acts.Orientation = Orientation.Horizontal;
            acts.VerticalAlignment = VerticalAlignment.Center;

            Border op = Ui.Btn("打开", Ui.BtnKind.Subtle, 11, delegate { if (Directory.Exists(f.Path)) Core.Launch(f.Path); });
            acts.Children.Add(op);

            Border ci = Ui.Btn("换图标", Ui.BtnKind.Subtle, 11, delegate
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Title = "选择「" + f.Name + "」的图标图片";
                dlg.Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico|所有文件|*.*";
                if (dlg.ShowDialog() != true) return;
                string err;
                if (!Core.SetFolderIcon(f.Path, dlg.FileName, out err))
                {
                    MsgBox.Show("设置图标失败：\n" + err);
                    return;
                }
                if (Core.Panel != null) Core.Panel.CloseNow();
                RebuildList();
                Desk.RefreshDesktop();
            });
            ci.Margin = new Thickness(6, 0, 0, 0);
            ci.ToolTip = "导入一张图片，作为这个文件夹的图标（桌面上的图标也会跟着变）";
            acts.Children.Add(ci);

            Border imp = Ui.Btn("导入", Ui.BtnKind.Subtle, 11, delegate
            {
                OpenFileDialog dlg = new OpenFileDialog();
                dlg.Multiselect = true;
                dlg.Title = "选择要放进「" + f.Name + "」的文件（会移动，不复制）";
                dlg.Filter = "所有文件|*.*";
                if (dlg.ShowDialog() == true) Import(f, dlg.FileNames);
            });
            imp.Margin = new Thickness(6, 0, 0, 0);
            imp.ToolTip = "把选中文件移动到该文件夹里";
            acts.Children.Add(imp);

            Border rm = Ui.Btn("不再预览", Ui.BtnKind.Subtle, 11, delegate
            {
                if (Core.cfg.All)
                {
                    if (!Core.IsIgnored(Core.DisplayName(f))) Core.cfg.Ignored.Add(Core.DisplayName(f));
                }
                else
                {
                    for (int i = Core.cfg.Folders.Count - 1; i >= 0; i--)
                        if (Core.cfg.Folders[i].Path == f.Path) Core.cfg.Folders.RemoveAt(i);
                }
                Core.Save();
                if (Core.Panel != null) Core.Panel.CloseNow();
                RebuildList();
            });
            rm.Margin = new Thickness(6, 0, 0, 0);
            rm.ToolTip = "只是取消它的悬停预览，文件夹本身保留";
            acts.Children.Add(rm);
            Grid.SetColumn(acts, 1);
            g.Children.Add(acts);

            row.Child = g;
            return row;
        }

        void CreateFolder()
        {
            string name = TextPrompt.Show(this, "新建一个悬浮窗文件夹（会真的在桌面生成这个文件夹）", "常用工具");
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0) return;
            foreach (char c in name)
            {
                if ("\\/:*?\"<>|".IndexOf(c) >= 0)
                {
                    MsgBox.Show("名字里不能有 \\ / : * ? \" < > | 这些字符。");
                    return;
                }
            }
            string path = System.IO.Path.Combine(Core.desktop, name);
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                MsgBox.Show("创建文件夹失败：\n" + ex.Message);
                return;
            }
            foreach (Folder f in Core.cfg.Folders)
            {
                if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    f.Path = path;
                    Core.Save();
                    Desk.RefreshDesktop();
                    RebuildList();
                    return;
                }
            }
            Folder nf = new Folder();
            nf.Name = name;
            nf.Path = path;
            Core.cfg.Folders.Add(nf);
            Core.Save();
            Desk.RefreshDesktop();
            System.Threading.Thread.Sleep(300);
            RebuildList();
        }

        void EnsureOnDesktop()
        {
            int fixedN = 0;
            foreach (Folder f in Core.cfg.Folders)
            {
                if (!Directory.Exists(f.Path)) continue;
                string target = System.IO.Path.Combine(Core.desktop, f.Name);
                if (Directory.Exists(target)) continue;
                try
                {
                    Directory.CreateDirectory(target);
                    foreach (string e in Directory.GetFileSystemEntries(f.Path))
                    {
                        string dest = System.IO.Path.Combine(target, System.IO.Path.GetFileName(e));
                        if (!File.Exists(dest) && !Directory.Exists(dest))
                        {
                            if (Directory.Exists(e)) Directory.Move(e, dest);
                            else File.Move(e, dest);
                        }
                    }
                    f.Path = target;
                    fixedN++;
                }
                catch { }
            }
            Core.Save();
            Desk.RefreshDesktop();
            RebuildList();
            MsgBox.Show("已经把 " + fixedN + " 个悬浮窗文件夹放到桌面。\n如果本来就是桌面上的文件夹，不用再点这个按钮。");
        }

        void Import(Folder f, string[] paths)
        {
            if (!Directory.Exists(f.Path)) { MsgBox.Show("这个文件夹已经不存在了。"); return; }
            int moved = 0, failed = 0;
            foreach (string p in paths)
            {
                try
                {
                    string dest = System.IO.Path.Combine(f.Path, System.IO.Path.GetFileName(p));
                    if (File.Exists(dest) || Directory.Exists(dest)) continue;
                    if (Directory.Exists(p)) Directory.Move(p, dest);
                    else File.Move(p, dest);
                    moved++;
                }
                catch { failed++; }
            }
            if (Core.Panel != null) Core.Panel.CloseNow();
            Core.Save();
            RebuildList();
            if (failed > 0) MsgBox.Show("移动了 " + moved + " 项，" + failed + " 项失败（可能被占用或跨盘）。");
        }
    }

    // ============================ 输入框对话框 ============================
    public class TextPrompt : Window
    {
        TextBox tb;
        string result;

        TextPrompt(string title, string def)
        {
            Title = "DeskFolder 输入";
            WindowStyle = WindowStyle.None;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Ui.Br(Ui.WinBg);
            FontFamily = Ui.Font;
            Foreground = Ui.Br(Ui.Ink);

            Border shell = new Border();
            shell.Background = Ui.Br(Color.FromRgb(0x11, 0x16, 0x21));
            shell.BorderBrush = Ui.Br(Color.FromRgb(0x25, 0x2E, 0x40));
            shell.BorderThickness = new Thickness(1);

            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(22, 20, 22, 18);

            TextBlock t = Ui.T(title, 12, Ui.Ink2, false);
            t.TextWrapping = TextWrapping.Wrap;
            t.LineHeight = 20;
            sp.Children.Add(t);

            Border box = new Border();
            box.Margin = new Thickness(0, 14, 0, 0);
            box.CornerRadius = new CornerRadius(9);
            box.Background = Ui.Br(Color.FromRgb(0x0B, 0x0F, 0x18));
            box.BorderThickness = new Thickness(1);
            box.BorderBrush = new SolidColorBrush(Color.FromRgb(0x23, 0x2C, 0x3D));
            box.Padding = new Thickness(11, 7, 11, 7);
            tb = new TextBox();
            tb.Text = def;
            tb.FontSize = 14;
            tb.Foreground = Ui.Br(Ui.Ink);
            tb.Background = Brushes.Transparent;
            tb.BorderThickness = new Thickness(0);
            tb.CaretBrush = Ui.Br(Ui.Accent);
            tb.SelectionBrush = Ui.Br(Ui.Alpha(Ui.Accent, 90));
            tb.SelectionOpacity = 0.9;
            tb.Padding = new Thickness(0);
            box.Child = tb;
            sp.Children.Add(box);
            SolidColorBrush boxBd = (SolidColorBrush)box.BorderBrush;
            tb.GotFocus += delegate { Ui.Anim(boxBd, Ui.Accent, 140); };
            tb.LostFocus += delegate { Ui.Anim(boxBd, Color.FromRgb(0x23, 0x2C, 0x3D), 160); };

            StackPanel btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.HorizontalAlignment = HorizontalAlignment.Right;
            btns.Margin = new Thickness(0, 18, 0, 0);
            Border ok = Ui.Btn("确定", Ui.BtnKind.Primary, delegate { Accept(); });
            Border no = Ui.Btn("取消", Ui.BtnKind.Ghost, delegate { Close(); });
            no.Margin = new Thickness(9, 0, 0, 0);
            btns.Children.Add(ok);
            btns.Children.Add(no);
            sp.Children.Add(btns);

            shell.Child = sp;
            Content = shell;

            shell.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
            };
            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { result = null; Close(); }
                else if (e.Key == Key.Enter) { Accept(); }
            };
            Loaded += delegate { tb.Focus(); tb.SelectAll(); };
        }

        void Accept()
        {
            result = tb.Text;
            Close();
        }

        public static string Show(Window owner, string title, string def)
        {
            TextPrompt p = new TextPrompt(title, def);
            if (owner != null) p.Owner = owner;
            p.ShowDialog();
            return p.result;
        }
    }

    // ============================ 提示对话框 ============================
    public class MsgBox : Window
    {
        public MsgBox(string text, string title)
        {
            string head = title == null || title.Length == 0 ? "DeskFolder" : title;
            Title = "DeskFolder 提示";
            WindowStyle = WindowStyle.None;
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false
            });
            Width = 430;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = Ui.Br(Ui.WinBg);
            FontFamily = Ui.Font;

            Border shell = new Border();
            shell.Background = Ui.Br(Color.FromRgb(0x11, 0x16, 0x21));
            shell.BorderBrush = Ui.Br(Color.FromRgb(0x25, 0x2E, 0x40));
            shell.BorderThickness = new Thickness(1);

            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(22, 20, 22, 18);

            DockPanel head = new DockPanel();
            Border icon = new Border();
            icon.Width = 30;
            icon.Height = 30;
            icon.CornerRadius = new CornerRadius(15);
            icon.Background = Ui.Br(Ui.Alpha(Ui.Accent, 38));
            icon.Child = Ui.Glyph("\uE946", 13, Ui.Accent);
            head.Children.Add(icon);
            TextBlock ti = Ui.T(head, 13, Ui.Ink, true);
            ti.Margin = new Thickness(12, 0, 0, 0);
            ti.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(ti);
            sp.Children.Add(head);

            TextBlock msg = Ui.T(text, 12, Ui.Ink2, false);
            msg.TextWrapping = TextWrapping.Wrap;
            msg.Margin = new Thickness(0, 14, 0, 0);
            msg.LineHeight = 20;
            sp.Children.Add(msg);

            StackPanel btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.HorizontalAlignment = HorizontalAlignment.Right;
            btns.Margin = new Thickness(0, 20, 0, 0);
            btns.Children.Add(Ui.Btn("知道了", Ui.BtnKind.Primary, delegate { Close(); }));
            sp.Children.Add(btns);

            shell.Child = sp;
            Content = shell;

            shell.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
            };
            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape || e.Key == Key.Enter) Close();
            };
        }

        public static void Show(string text) { Show(text, "DeskFolder"); }

        public static void Show(string text, string title)
        {
            MsgBox b = new MsgBox(text, title);
            try { if (Core.Mgr != null && Core.Mgr.IsVisible) b.Owner = Core.Mgr; }
            catch { }
            b.ShowDialog();
        }
    }

    // ============================ 托盘菜单深色渲染 ============================
    public class DarkMenuRenderer : System.Windows.Forms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderToolStripBorder(System.Windows.Forms.ToolStripRenderEventArgs e)
        {
            using (System.Drawing.Pen p = new System.Drawing.Pen(System.Drawing.Color.FromArgb(44, 54, 72)))
            {
                e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
        }

        protected override void OnRenderItemText(System.Windows.Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected
                ? System.Drawing.Color.White
                : System.Drawing.Color.FromArgb(226, 232, 243);
            base.OnRenderItemText(e);
        }
    }

    public class DarkColors : System.Windows.Forms.ProfessionalColorTable
    {
        static readonly System.Drawing.Color Bg = System.Drawing.Color.FromArgb(17, 22, 33);
        static readonly System.Drawing.Color Sel = System.Drawing.Color.FromArgb(40, 50, 70);
        public override System.Drawing.Color ToolStripDropDownBackground { get { return Bg; } }
        public override System.Drawing.Color MenuItemSelected { get { return Sel; } }
        public override System.Drawing.Color MenuItemBorder { get { return Sel; } }
        public override System.Drawing.Color MenuBorder { get { return System.Drawing.Color.FromArgb(38, 48, 66); } }
        public override System.Drawing.Color ImageMarginGradientBegin { get { return Bg; } }
        public override System.Drawing.Color ImageMarginGradientMiddle { get { return Bg; } }
        public override System.Drawing.Color ImageMarginGradientEnd { get { return Bg; } }
        public override System.Drawing.Color MenuItemSelectedGradientBegin { get { return Sel; } }
        public override System.Drawing.Color MenuItemSelectedGradientEnd { get { return Sel; } }
        public override System.Drawing.Color MenuItemPressedGradientBegin { get { return Sel; } }
        public override System.Drawing.Color MenuItemPressedGradientEnd { get { return Sel; } }
        public override System.Drawing.Color SeparatorDark { get { return System.Drawing.Color.FromArgb(44, 54, 72); } }
        public override System.Drawing.Color SeparatorLight { get { return System.Drawing.Color.FromArgb(44, 54, 72); } }
    }

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "df_err.txt"), DateTime.Now + "\n" + e.ExceptionObject + "\n\n"); }
                catch { }
            };
            Application app = new Application();
            IntPtr exist = IntPtr.Zero;
            try { exist = Core.FindWindow(null, "DeskFolder"); }
            catch { }
            if (exist != IntPtr.Zero)
            {
                try { Core.PostMessage(exist, Core.MsgShow, IntPtr.Zero, IntPtr.Zero); } catch { }
                return;
            }
            app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "df_err.txt"), DateTime.Now + "\n" + e.Exception + "\n\n"); }
                catch { }
                e.Handled = true;
            };
            app.Startup += delegate
            {
                Core.Init();
                try
                {
                    Microsoft.Win32.SystemEvents.UserPreferenceChanged += delegate
                    {
                        try { Core.BgStale = true; }
                        catch { }
                    };
                }
                catch { }
#if FORCE_TEST
                string si = Environment.GetEnvironmentVariable("DF_SETICON");
                if (si != null && si.Length > 0)
                {
                    string[] parts = si.Split('|');
                    string err = "";
                    bool ok = Core.SetFolderIcon(parts[0], parts[1], out err);
                    try
                    {
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "df_seticon.txt"),
                            (ok ? "OK " : "FAIL ") + parts[0] + " " + err + "\n");
                    }
                    catch { }
                }
#endif
                Core.Panel = new PanelWindow();
                Core.Mgr = new ManagerWindow();
                Core.InitTray();
                Core.Mgr.Show();
                Core.Mgr.RebuildList();
                Core.StartWatcher();
#if FORCE_TEST
                string dumpDir = Environment.GetEnvironmentVariable("DF_ICONDUMP");
                if (dumpDir != null && dumpDir.Length > 0)
                {
                    try
                    {
                        Directory.CreateDirectory(dumpDir);
                        foreach (string fp in Directory.GetFiles(dumpDir))
                        {
                            string nm = Path.GetFileNameWithoutExtension(fp);
                            if (nm == "raw") continue;
                        }
                    }
                    catch { }
                }
                string fc = Environment.GetEnvironmentVariable("DF_FORCE");
                if (fc != null && fc.Length > 0)
                    foreach (Folder f in Core.Targets()) if (Core.DisplayName(f) == fc) { Core.Watcher.Stop(); Core.Panel.Open(f); break; }
#endif
            };
            app.Run();
        }
    }
}
