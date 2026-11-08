// Star Wars: Force Commander mod installer.
// Applies any combination of the optional patches in PatchData.cs to the untouched game files.
// focom.exe and fcv1p1.rpk are rebuilt from their originals, which are kept as "focom_original.exe"
// and "forcecommand\fcv1p1_original.rpk". forcecommand.rpk is 274 MB and only 13 bytes change, so it
// is patched in place and those bytes are put back on removal. dgVoodoo.conf is saved as
// "dgVoodoo_original.conf" before its scaling mode and resolution are changed.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;
using Microsoft.Win32;

class Run {
    public readonly int Offset; public readonly byte[] New;
    public Run(int offset, byte[] nw) { Offset = offset; New = nw; }
}

class Patch {
    public readonly string Key, File; public readonly Run[] Runs;
    public Patch(string key, string file, Run[] runs) { Key = key; File = file; Runs = runs; }
    // offsets are in the original file; "shift" moves everything at or past a point (the insert)
    public bool IsIn(byte[] b, int shiftFrom, int shift) {
        foreach (var r in Runs) {
            int o = r.Offset >= shiftFrom ? r.Offset + shift : r.Offset;
            if (o + r.New.Length > b.Length) return false;
            for (int i = 0; i < r.New.Length; i++) if (b[o + i] != r.New[i]) return false;
        }
        return true;
    }
}

// one step of rebuilding the relocated script object: copy from the original archive, or new bytes
class RpkOp {
    public readonly int Src, Len; public readonly byte[] Lit;
    public RpkOp(int src, int len) { Src = src; Len = len; }
    public RpkOp(string hex) { Lit = PatchData.Hex(hex); Len = Lit.Length; }
}

class BaseRun {
    public readonly long Offset; public readonly byte[] Old, New;
    public BaseRun(long offset, string old, string nw) { Offset = offset; Old = PatchData.Hex(old); New = PatchData.Hex(nw); }
}

class Option {
    public string Key, Title, Info;
    public string[] Needs;      // "exe", "rpk", "base", "conf"
    public CheckBox Box;
    public bool Available;
}

class InstallerForm : Form {
    const string ExeName = "focom.exe", ExeBackup = "focom_original.exe";
    const string RpkName = @"forcecommand\fcv1p1.rpk", RpkBackup = @"forcecommand\fcv1p1_original.rpk";
    const string BaseName = @"forcecommand\forcecommand.rpk";
    const string ConfName = "dgVoodoo.conf", ConfBackup = "dgVoodoo_original.conf";

    static readonly int[][] Resolutions = {
        new[]{1920,1080}, new[]{2560,1440}, new[]{3840,2160}, new[]{1600,900}, new[]{1366,768}, new[]{1280,720},
        new[]{1920,1200}, new[]{1680,1050}, new[]{2560,1600}, new[]{2560,1080}, new[]{3440,1440}, new[]{3840,1600} };

    readonly TextBox folder = new TextBox();
    readonly Label status = new Label();
    readonly TextBox info = new TextBox();
    readonly ComboBox res = new ComboBox();
    readonly List<int[]> resList = new List<int[]>();
    readonly List<Option> options = new List<Option>();
    readonly Button install = new Button(), restore = new Button();
    string dir;                      // the Resource folder
    byte[] exeBase, rpkBase;         // untouched files, or null
    bool baseOk, confOk, exeClean, rpkClean, baseClean = true;

    Option Opt(string key) { return options.First(o => o.Key == key); }
    bool On(string key) { return Opt(key).Box.Checked && Opt(key).Box.Enabled; }
    void Set(string key, bool v) { Opt(key).Box.Checked = v; }
    string P(string name) { return Path.Combine(dir, name); }

    InstallerForm() {
        Text = "Star Wars: Force Commander - Mod Installer";
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(10);

        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
        Controls.Add(root);

        var fl = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
        fl.Controls.Add(new Label { Text = "Game folder:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        folder.Width = 470;
        folder.Margin = new Padding(0, 3, 4, 0);
        folder.Leave += (s, e) => LoadFolder();
        fl.Controls.Add(folder);
        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (s, e) => {
            using (var d = new FolderBrowserDialog { Description = "Select the Force Commander folder (or its Resource folder, the one containing focom.exe)" }) {
                if (Directory.Exists(folder.Text)) d.SelectedPath = folder.Text;
                if (d.ShowDialog(this) == DialogResult.OK) { folder.Text = d.SelectedPath; LoadFolder(); }
            }
        };
        fl.Controls.Add(browse);
        root.Controls.Add(fl);
        status.AutoSize = true; status.MaximumSize = new Size(640, 0); status.Margin = new Padding(0, 4, 0, 6);
        root.Controls.Add(status);

        var g1 = Group(root, "Display (needs dgVoodoo 2)");
        Check(g1, "widescreen", "Widescreen 3D view", new[] { "exe", "conf" },
            "The game only knows 4:3 screen modes, so on a widescreen monitor it's either stretched or has black bars.\r\n\r\n" +
            "dgVoodoo is told to render at your real resolution, so the picture is sharp and fills the screen, and the 3D view " +
            "is corrected so units and terrain keep their proper shape. You see more of the battlefield at the sides.\r\n\r\n" +
            "Sets ScalingMode and the [DirectX] Resolution in dgVoodoo.conf (your old file is kept as dgVoodoo_original.conf).\r\n\r\n" +
            "Known limit: the in-mission HUD is still stretched sideways. It's made of 3D meshes stored in the game's data, " +
            "so it can't be squeezed separately. The mouse and its click areas line up with it.\r\n\r\n" +
            "Status: tested at 1920x1080. Other aspect ratios use the same calculation but haven't been played.");
        Check(g1, "menus", "Keep menus in 4:3", new[] { "exe", "conf" },
            "Without this, the front-end menus and the Esc/Save/Load screens are spread across the wide screen, so text and " +
            "pictures drift out of their frames.\r\n\r\n" +
            "Everything 2D is now drawn at 4:3 unless a mission is actually running, so menus line up. Missions are unchanged. " +
            "The HUD also squeezes for the moment the Esc menu is open.\r\n\r\nNeeds Widescreen 3D view.\r\n\r\nStatus: tested at 1920x1080.");
        var rr = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        rr.Controls.Add(new Label { Text = "Your screen resolution:", AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
        res.DropDownStyle = ComboBoxStyle.DropDownList;
        res.Margin = new Padding(8, 2, 0, 0);
        res.Width = 240;
        rr.Controls.Add(res);
        Panel(g1).Controls.Add(rr);

        var g2 = Group(root, "Fixes");
        Check(g2, "mines", "Fix Rebel mines doing no damage", new[] { "rpk" },
            "The official 1.1 update broke the Rebel mine. It homes in on a target variable that nothing ever sets, so it stays " +
            "where it is and explodes out of range of whatever set it off.\r\n\r\n" +
            "The mine now homes in on the unit that triggered it, as in version 1.0.\r\n\r\nStatus: tested.");
        Check(g2, "saves", "Scrolling save list (more than 9 saves)", new[] { "exe", "rpk" },
            "The game has 32 save slots, but its Save and Load screens can only show 9, so later saves can't be reached.\r\n\r\n" +
            "The lists now scroll, with the mouse wheel, Up/Down or PgUp/PgDn, on the in-game Save and Load screens and the main " +
            "menu's Load Saved screen.\r\n\r\nStatus: tested.");
        Check(g2, "movement", "Better unit movement around buildings", new[] { "exe" },
            "Big units (tanks, torpedo launchers, AT-ATs) wobble, weave and take long detours next to buildings, or drive through them.\r\n\r\n" +
            "- Keeps the steering state between updates and stops the heading overshooting, so units stop wobbling\r\n" +
            "- Vehicles route around the real outline of most Rebel and Imperial buildings instead of a big circle (the red hover box, measured per building)\r\n" +
            "- Vehicles ordered into a building stop at its edge; infantry still walk in and out as normal, and aircraft fly over\r\n" +
            "- Vehicles no longer veer away from landers high above them\r\n" +
            "- Turrets can still be placed right next to buildings\r\n\r\n" +
            "Known limits: a big vehicle can clip a building's corner slightly. Mission-specific buildings still use circles.\r\n\r\n" +
            "Status: tested in both campaigns.");

        var g3 = Group(root, "Gameplay and balance");
        Check(g3, "ai", "Smarter computer-controlled units", new[] { "rpk" },
            "Computer-controlled units often patrol on while they're being picked off from long range, and in the early Imperial " +
            "missions Rebel infantry shoot buildings but never capture them.\r\n\r\n" +
            "- Units shot at by something now go after the attacker\r\n" +
            "- Computer units in Aggressive or Guard stance use Defensive, the stance that closes with enemies it sees, with a larger acquire range (250)\r\n" +
            "- Computer infantry capture the command centres and turret controls they would otherwise only shoot\r\n" +
            "- Rebel infantry see twice as far, so they can respond to AT-PTs. This covers all Rebel infantry, including your own in the Rebel campaign\r\n\r\n" +
            "This makes the Imperial campaign harder, so it pairs with the option below.\r\n\r\nStatus: tested in the early Imperial missions.");
        Check(g3, "bldgs", "Imperial hospital and repair bay from the first mission", new[] { "base" },
            "The Imperial hospital and repair bay normally aren't available until late in the campaign, so nothing heals your " +
            "units through the early missions.\r\n\r\n" +
            "They can now be bought in the Ruul, Sarapin and other missions before Abridon (7 missions in all).\r\n\r\n" +
            "This changes 13 bytes of the large forcecommand.rpk in place, so that file needs no backup: the installer puts the " +
            "bytes back when you untick this or restore the game.\r\n\r\nStatus: tested.");
        Opt("widescreen").Box.CheckedChanged += (s, e) => {
            if (!Opt("widescreen").Box.Checked) Set("menus", false);
            res.Enabled = Opt("widescreen").Box.Checked && Opt("widescreen").Box.Enabled;
        };
        Opt("menus").Box.CheckedChanged += (s, e) => { if (Opt("menus").Box.Checked && Opt("widescreen").Box.Enabled) Set("widescreen", true); };

        info.Multiline = true; info.ReadOnly = true; info.ScrollBars = ScrollBars.Vertical;
        info.Size = new Size(640, 190); info.BackColor = SystemColors.Info;
        info.Text = "Point at an option to see what it does.";
        root.Controls.Add(info);

        var bl = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        install.Text = "Install selected"; install.AutoSize = true;
        install.Click += (s, e) => DoInstall();
        restore.Text = "Restore original game"; restore.AutoSize = true;
        restore.Click += (s, e) => DoRestore();
        var close = new Button { Text = "Close", AutoSize = true };
        close.Click += (s, e) => Close();
        bl.Controls.AddRange(new Control[] { install, restore, close });
        root.Controls.Add(bl);

        folder.Text = FindGame();
        LoadFolder();
    }

    static string FindGame() {
        string here = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        if (ResourceDir(here) != null) return here;
        try {
            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\LucasArts Entertainment Company LLC\Force Commander\v1.0")) {
                var p = k == null ? null : k.GetValue("Install Path") as string;
                if (p != null && ResourceDir(p) != null) return p;
            }
        } catch (Exception) { }
        return "";
    }

    // accepts the game folder or its Resource folder
    static string ResourceDir(string d) {
        if (d.Length == 0) return null;
        try {
            if (File.Exists(Path.Combine(d, ExeName))) return d;
            if (File.Exists(Path.Combine(d, "Resource", ExeName))) return Path.Combine(d, "Resource");
        } catch (ArgumentException) { }
        return null;
    }

    GroupBox Group(Control parent, string title) {
        var g = new GroupBox { Text = title, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Width = 640, MinimumSize = new Size(640, 0), Padding = new Padding(8, 4, 8, 6) };
        var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Location = new Point(8, 20) };
        g.Controls.Add(p);
        parent.Controls.Add(g);
        return g;
    }
    Control Panel(Control g) { return g is GroupBox ? g.Controls[0] : g; }

    void Check(Control g, string key, string title, string[] needs, string text) {
        var c = new CheckBox { Text = title, AutoSize = true };
        Panel(g).Controls.Add(c);
        var o = new Option { Key = key, Title = title, Info = text, Needs = needs, Box = c };
        c.MouseEnter += (s, e) => info.Text = o.Title + "\r\n\r\n" + o.Info;
        c.GotFocus += (s, e) => info.Text = o.Title + "\r\n\r\n" + o.Info;
        options.Add(o);
    }

    static string Md5(byte[] b) {
        using (var m = MD5.Create()) return BitConverter.ToString(m.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
    }
    static Patch PatchFor(string key, string file) { return PatchData.Groups.First(p => p.Key == key && p.File == file); }
    static int RpkGrowth() { return PatchData.RpkInsert.Sum(op => op.Len); }

    // ---- dgVoodoo.conf

    static int FindKey(List<string> lines, string section, string key) {
        bool inSec = false;
        for (int i = 0; i < lines.Count; i++) {
            string t = lines[i].Trim();
            if (t.StartsWith("[")) { inSec = t.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!inSec || t.StartsWith(";")) continue;
            var p = t.Split(new[] { '=' }, 2);
            if (p.Length == 2 && p[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    // the [DirectX] Resolution in the conf, when it names one
    int[] ConfResolution() {
        try {
            var lines = File.ReadAllLines(P(ConfName)).ToList();
            int i = FindKey(lines, "DirectX", "Resolution");
            if (i < 0) return null;
            int w = 0, h = 0;
            foreach (var part in lines[i].Split(new[] { '=' }, 2)[1].Split(',')) {
                var kv = part.Split(':');
                if (kv.Length != 2) continue;
                if (kv[0].Trim() == "h") int.TryParse(kv[1].Trim(), out w);
                else if (kv[0].Trim() == "v") int.TryParse(kv[1].Trim(), out h);
            }
            return w > 0 && h > 0 ? new[] { w, h } : null;
        } catch (IOException) { return null; }
    }

    void WriteConf(int w, int h) {
        string f = P(ConfName), bak = P(ConfBackup);
        if (!File.Exists(bak)) File.Copy(f, bak);
        File.SetAttributes(f, File.GetAttributes(f) & ~FileAttributes.ReadOnly);
        var lines = File.ReadAllLines(f).ToList();
        int s = FindKey(lines, "General", "ScalingMode"), r = FindKey(lines, "DirectX", "Resolution");
        if (s < 0 || r < 0) throw new IOException("\"" + ConfName + "\" doesn't have the ScalingMode and [DirectX] Resolution settings.");
        lines[s] = "ScalingMode                          = stretched";
        lines[r] = "Resolution                          = h:" + w + ", v:" + h;
        File.WriteAllLines(f, lines.ToArray());
    }

    // ---- folder scan

    void FillResolutions() {
        int[] cur = ConfResolution();
        int[] pick = null;
        if (exeBase != null && File.Exists(P(ExeName))) {
            try { if (PatchFor("widescreen", "exe").IsIn(File.ReadAllBytes(P(ExeName)), int.MaxValue, 0)) pick = cur; } catch (IOException) { }
        }
        if (pick == null) pick = DesktopSize();
        res.Items.Clear(); resList.Clear();
        var list = Resolutions.ToList();
        if (!list.Any(r => r[0] == pick[0] && r[1] == pick[1])) list.Insert(0, pick);
        foreach (var r in list) {
            resList.Add(r);
            res.Items.Add(r[0] + " x " + r[1] + "  (" + AspectName(r[0], r[1]) + (r[0] == 1920 && r[1] == 1080 ? ", tested)" : ")"));
        }
        res.SelectedIndex = list.FindIndex(r => r[0] == pick[0] && r[1] == pick[1]);
    }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int index);
    // the real pixels: Screen.Bounds is scaled down when Windows display scaling is on
    static int[] DesktopSize() {
        IntPtr dc = GetDC(IntPtr.Zero);
        int w = GetDeviceCaps(dc, 118), h = GetDeviceCaps(dc, 117);   // DESKTOPHORZRES, DESKTOPVERTRES
        ReleaseDC(IntPtr.Zero, dc);
        return w > 0 && h > 0 ? new[] { w, h } : new[] { 1920, 1080 };
    }
    static string AspectName(int w, int h) {
        double a = (double)w / h;
        if (Math.Abs(a - 16.0 / 9) < 0.02) return "16:9";
        if (Math.Abs(a - 1.6) < 0.01) return "16:10";
        if (Math.Abs(a - 4.0 / 3) < 0.01) return "4:3";
        if (Math.Abs(a - 2.37) < 0.06) return "21:9";
        return a.ToString("0.00", CultureInfo.InvariantCulture) + ":1";
    }

    void LoadFolder() {
        string typed = folder.Text.Trim().Trim('"');
        install.Enabled = restore.Enabled = false;
        foreach (var o in options) { o.Box.Enabled = false; o.Available = false; }
        res.Enabled = false;
        exeBase = rpkBase = null; baseOk = confOk = exeClean = rpkClean = false; baseClean = true;
        dir = ResourceDir(typed);
        if (dir == null) {
            status.ForeColor = Color.DarkRed;
            status.Text = typed.Length == 0 ? "Choose the folder where Force Commander is installed."
                                            : "\"" + ExeName + "\" was not found in that folder or in a Resource folder inside it.";
            return;
        }
        var msgs = new List<string>();
        byte[] exeCur = null, rpkCur = null;

        try {
            exeCur = File.ReadAllBytes(P(ExeName));
            if (Md5(exeCur) == PatchData.ExeMd5) { exeBase = exeCur; exeClean = true; }
            else if (File.Exists(P(ExeBackup)) && Md5(File.ReadAllBytes(P(ExeBackup))) == PatchData.ExeMd5) exeBase = File.ReadAllBytes(P(ExeBackup));
            else msgs.Add("\"" + ExeName + "\" isn't the version 1.1 file these patches were made for (MD5 " + PatchData.ExeMd5.ToUpperInvariant() + "), and there's no matching \"" + ExeBackup + "\". Install the official 1.1 update, or put the original back.");
        } catch (Exception ex) { msgs.Add("Can't read " + ExeName + ": " + ex.Message); }

        try {
            if (!File.Exists(P(RpkName))) msgs.Add("\"" + RpkName + "\" wasn't found. It comes with the official 1.1 update; the options that need it are unavailable.");
            else {
                rpkCur = File.ReadAllBytes(P(RpkName));
                if (Md5(rpkCur) == PatchData.RpkMd5) { rpkBase = rpkCur; rpkClean = true; }
                else if (File.Exists(P(RpkBackup)) && Md5(File.ReadAllBytes(P(RpkBackup))) == PatchData.RpkMd5) rpkBase = File.ReadAllBytes(P(RpkBackup));
                else msgs.Add("\"" + RpkName + "\" isn't the version 1.1 file (MD5 " + PatchData.RpkMd5.ToUpperInvariant() + "), and there's no matching \"" + RpkBackup + "\", so the options that need it are unavailable.");
            }
        } catch (Exception ex) { msgs.Add("Can't read " + RpkName + ": " + ex.Message); }

        bool bldgsIn = false;
        try {
            string why = ScanBase(out bldgsIn);
            baseOk = why == null;
            baseClean = !bldgsIn;
            if (why != null) msgs.Add(why);
        } catch (Exception ex) { msgs.Add("Can't read " + BaseName + ": " + ex.Message); }

        confOk = File.Exists(P(ConfName)) && File.Exists(P("ddraw.dll"));
        if (confOk) {
            try {
                var l = File.ReadAllLines(P(ConfName)).ToList();
                confOk = FindKey(l, "General", "ScalingMode") >= 0 && FindKey(l, "DirectX", "Resolution") >= 0;
            } catch (IOException) { confOk = false; }
        }
        if (!confOk) msgs.Add("dgVoodoo 2 wasn't found next to " + ExeName + " (ddraw.dll and a dgVoodoo.conf with ScalingMode and [DirectX] Resolution settings), so the Display options are unavailable. See the README.");

        foreach (var o in options) {
            o.Available = o.Needs.All(n => n == "exe" ? exeBase != null : n == "rpk" ? rpkBase != null : n == "base" ? baseOk : confOk);
            o.Box.Enabled = o.Available;
        }

        bool anyFile = exeBase != null || rpkBase != null || baseOk;
        if (!anyFile) {
            foreach (var o in options) o.Box.Checked = false;
            status.ForeColor = Color.DarkRed;
            status.Text = string.Join("\r\n", msgs);
            return;
        }
        bool modified = (exeBase != null && !exeClean) || (rpkBase != null && !rpkClean) || (baseOk && !baseClean);
        // every option ticked on an untouched game, else what's installed now
        foreach (var o in options) {
            if (!o.Available) { o.Box.Checked = false; continue; }
            bool isIn = true;
            foreach (var p in PatchData.Groups.Where(p => p.Key == o.Key)) {
                if (p.File == "exe") isIn &= exeCur != null && p.IsIn(exeCur, int.MaxValue, 0);
                else {
                    bool grown = rpkCur != null && rpkCur.Length == rpkBase.Length + RpkGrowth();
                    isIn &= rpkCur != null && p.IsIn(rpkCur, PatchData.RpkInsertAt, grown ? RpkGrowth() : 0);
                }
            }
            if (o.Key == "bldgs") isIn = bldgsIn;
            o.Box.Checked = !modified || isIn;
        }

        FillResolutions();
        res.Enabled = Opt("widescreen").Box.Enabled && Opt("widescreen").Box.Checked;
        status.ForeColor = msgs.Count > 0 ? Color.DarkOrange : Color.DarkGreen;
        var backups = new List<string>();
        if (exeBase != null) backups.Add("\"" + ExeBackup + "\"");
        if (rpkBase != null) backups.Add("\"" + RpkBackup + "\"");
        status.Text = modified
            ? "Some game files are already modified. The installer rebuilds them from the backups; the ticked options show what's installed now."
            : (msgs.Count > 0 ? "Found some of the original game files." : "Found the original game files.") +
              (backups.Count > 0 ? " Backups will be made as " + string.Join(" and ", backups) + " before patching." : "");
        if (msgs.Count > 0) status.Text += "\r\n\r\n" + string.Join("\r\n\r\n", msgs);
        install.Enabled = true;
        restore.Enabled = modified;
    }

    // null when forcecommand.rpk is usable: every run is either all original or all patched, consistently
    string ScanBase(out bool patched) {
        patched = false;
        string f = P(BaseName);
        if (!File.Exists(f)) return "\"" + BaseName + "\" wasn't found (a smaller install may keep it on the CD), so the hospital and repair bay option is unavailable.";
        if (new FileInfo(f).Length != PatchData.BaseRpkSize) return "\"" + BaseName + "\" isn't the expected size, so the hospital and repair bay option is unavailable.";
        int news = 0, olds = 0;
        using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            foreach (var r in PatchData.Bldgs) {
                var b = new byte[r.New.Length];
                s.Seek(r.Offset, SeekOrigin.Begin);
                if (s.Read(b, 0, b.Length) != b.Length) return "\"" + BaseName + "\" couldn't be read.";
                if (b.SequenceEqual(r.New)) news++;
                else if (b.SequenceEqual(r.Old)) olds++;
                else return "\"" + BaseName + "\" isn't the expected version, so the hospital and repair bay option is unavailable.";
            }
        }
        if (news != 0 && olds != 0) return "\"" + BaseName + "\" is only partly patched, so the hospital and repair bay option is unavailable.";
        patched = news != 0;
        return null;
    }

    // ---- building

    byte[] BuildExe(List<string> applied) {
        bool sections = On("menus") || On("movement") || On("saves");
        int size = sections ? PatchData.ExeFinalSize : exeBase.Length;
        var img = new byte[size];
        Buffer.BlockCopy(exeBase, 0, img, 0, exeBase.Length);
        if (sections) Apply(img, PatchFor("common", "exe"));
        foreach (var k in new[] { "widescreen", "menus", "movement", "saves" }) {
            if (!On(k)) continue;
            Apply(img, PatchFor(k, "exe"));
            applied.Add(Opt(k).Title);
        }
        if (On("widescreen")) {
            var r = resList[res.SelectedIndex];
            // the game stays 4:3 inside, so its picture is squeezed back by (4/3) / your aspect ratio
            float k = (float)(4.0 / 3.0 * r[1] / r[0]);
            if (r[0] * 9 == r[1] * 16) k = 0.75f;
            Buffer.BlockCopy(BitConverter.GetBytes(k), 0, img, PatchData.Aspect3D, 4);
            if (On("menus")) Buffer.BlockCopy(BitConverter.GetBytes(2f * k), 0, img, PatchData.Aspect2D, 4);
        }
        return img;
    }

    byte[] BuildRpk(List<string> applied) {
        var flat = (byte[])rpkBase.Clone();
        foreach (var k in new[] { "mines", "saves", "ai" }) {
            if (!On(k)) continue;
            Apply(flat, PatchFor(k, "rpk"));
            if (k != "saves") applied.Add(Opt(k).Title);
        }
        if (!On("ai")) return flat;
        int grow = RpkGrowth(), at = PatchData.RpkInsertAt;
        var img = new byte[flat.Length + grow];
        Buffer.BlockCopy(flat, 0, img, 0, at);
        int w = at;
        foreach (var op in PatchData.RpkInsert) {
            if (op.Lit != null) Buffer.BlockCopy(op.Lit, 0, img, w, op.Len);
            else Buffer.BlockCopy(rpkBase, op.Src, img, w, op.Len);   // always from the untouched file
            w += op.Len;
        }
        Buffer.BlockCopy(flat, at, img, w, flat.Length - at);
        return img;
    }

    static void Apply(byte[] img, Patch p) {
        foreach (var r in p.Runs) Buffer.BlockCopy(r.New, 0, img, r.Offset, r.New.Length);
    }

    // patch or un-patch forcecommand.rpk in place, only when it needs to change
    void WriteBase(bool patched) {
        bool now;
        if (ScanBase(out now) != null || now == patched) return;
        string f = P(BaseName);
        File.SetAttributes(f, File.GetAttributes(f) & ~FileAttributes.ReadOnly);
        using (var s = new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            foreach (var r in PatchData.Bldgs) {
                var b = patched ? r.New : r.Old;
                s.Seek(r.Offset, SeekOrigin.Begin);
                s.Write(b, 0, b.Length);
            }
    }

    // the file is written next to the target, then swapped in
    void Swap(string name, string backup, byte[] original, byte[] img) {
        string f = P(name), bak = P(backup);
        // a file already using the backup name that isn't the original is moved aside, never trusted or overwritten
        if (File.Exists(bak) && Md5(File.ReadAllBytes(bak)) != Md5(original)) {
            string stem = Path.Combine(Path.GetDirectoryName(bak), Path.GetFileNameWithoutExtension(bak)), ext = Path.GetExtension(bak);
            string old = stem + "-old" + ext;
            for (int i = 2; File.Exists(old); i++) old = stem + "-old" + i + ext;
            File.Move(bak, old);
        }
        if (!File.Exists(bak)) File.WriteAllBytes(bak, original);
        if (File.Exists(f)) File.SetAttributes(f, File.GetAttributes(f) & ~FileAttributes.ReadOnly);
        File.WriteAllBytes(f + ".tmp", img);
        File.Copy(f + ".tmp", f, true);
        File.Delete(f + ".tmp");
    }

    bool Guard(Action a) {
        try { a(); return true; }
        catch (IOException ex) {
            MessageBox.Show(this, "Couldn't write the game files. Close Force Commander if it's running and try again.\r\n\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        } catch (UnauthorizedAccessException ex) {
            MessageBox.Show(this, "Windows denied access to the game folder. Right-click the installer and choose \"Run as administrator\".\r\n\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        foreach (var n in new[] { ExeName, RpkName }) try { File.Delete(P(n) + ".tmp"); } catch (Exception) { }
        return false;
    }

    void DoInstall() {
        var applied = new List<string>();
        byte[] exe = null, rpk = null;
        bool widescreen = On("widescreen");
        int[] r = widescreen ? resList[res.SelectedIndex] : null;
        if (exeBase != null) exe = BuildExe(applied);
        if (rpkBase != null) rpk = BuildRpk(applied);
        if (baseOk && On("bldgs")) applied.Add(Opt("bldgs").Title);
        bool wantBase = baseOk && On("bldgs");
        bool restoreConf = !widescreen && confOk && File.Exists(P(ConfBackup)) && exeBase != null && !exeClean && WasWidescreen();
        string msg = applied.Count == 0 ? "No options were selected, so the game files are now the originals."
                                        : "Installed:\r\n  - " + string.Join("\r\n  - ", applied);
        if (!Guard(() => {
            if (exe != null) Swap(ExeName, ExeBackup, exeBase, exe);
            if (rpk != null) Swap(RpkName, RpkBackup, rpkBase, rpk);
            if (baseOk) WriteBase(wantBase);
            if (widescreen) WriteConf(r[0], r[1]);
            else if (restoreConf) File.Copy(P(ConfBackup), P(ConfName), true);
        })) return;
        if (widescreen) msg += "\r\n\r\ndgVoodoo.conf now renders at " + r[0] + " x " + r[1] + ". Keep the game's own screen setting at 1280x960.";
        else if (restoreConf) msg += "\r\n\r\ndgVoodoo.conf was put back from \"" + ConfBackup + "\".";
        msg += "\r\n\r\nSaved games from a different set of options may not load, so finish your current mission first when you change them.";
        MessageBox.Show(this, msg, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        LoadFolder();
    }

    bool WasWidescreen() {
        try { return PatchFor("widescreen", "exe").IsIn(File.ReadAllBytes(P(ExeName)), int.MaxValue, 0); }
        catch (IOException) { return false; }
    }

    void DoRestore() {
        if (MessageBox.Show(this, "Put the original, unmodified game files back?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        bool conf = confOk && File.Exists(P(ConfBackup)) && exeBase != null && WasWidescreen();
        if (!Guard(() => {
            if (exeBase != null) Swap(ExeName, ExeBackup, exeBase, exeBase);
            if (rpkBase != null) Swap(RpkName, RpkBackup, rpkBase, rpkBase);
            if (baseOk) WriteBase(false);
            if (conf) File.Copy(P(ConfBackup), P(ConfName), true);
        })) return;
        MessageBox.Show(this, "The original game files have been restored. Every mod is removed." + (conf ? " dgVoodoo.conf was put back too." : ""), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        LoadFolder();
    }

    [STAThread]
    static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new InstallerForm());
    }
}
