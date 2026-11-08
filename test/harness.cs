// Drives the installer form without showing it, on a throwaway copy of the game files.
// Usage: harness.exe <installer.exe> <testdir\Resource> <real game Resource folder>
// The real game folder is only ever read; its hashes are compared before and after.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;

class Harness {
    static Type T; static object F; static Assembly Asm;
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static object Get(string n) { return T.GetField(n, NP).GetValue(F); }
    static object Call(string n, params object[] a) { return T.GetMethod(n, NP).Invoke(F, a); }
    static void Opt(string key, bool v) { Call("Set", key, v); }
    static bool On(string key) { return (bool)Call("On", key); }
    static CheckBox Box(string key) { var o = Call("Opt", key); return (CheckBox)o.GetType().GetField("Box").GetValue(o); }
    static void Res(int w, int h) {
        var list = (List<int[]>)Get("resList");
        ((ComboBox)Get("res")).SelectedIndex = list.FindIndex(r => r[0] == w && r[1] == h);
    }
    static int fails, passes;
    static void Check(string what, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (ok) passes++; else fails++; }
    static string Md5(byte[] b) { using (var m = MD5.Create()) return BitConverter.ToString(m.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }
    static string Md5File(string f) { using (var m = MD5.Create()) using (var s = File.OpenRead(f)) return BitConverter.ToString(m.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
    static readonly string[] All = { "widescreen", "menus", "movement", "saves", "mines", "ai", "bldgs" };
    static void Load(string d) { ((TextBox)Get("folder")).Text = d; Call("LoadFolder"); }
    static void Tick(params string[] on) {
        foreach (var k in All) if (!on.Contains(k)) Opt(k, false);
        foreach (var k in on) Opt(k, true);
    }
    static string TD, Real;
    static void Install() { if (!((string)Get("dir")).StartsWith(TD)) throw new Exception("refusing to install outside the test folder"); Call("DoInstall"); }
    static void Restore() { if (!((string)Get("dir")).StartsWith(TD)) throw new Exception("refusing to restore outside the test folder"); Call("DoRestore"); }
    static string TP(string n) { return Path.Combine(TD, n); }
    static string RP(string n) { return Path.Combine(Real, n); }
    static byte[] BuildExe() { return (byte[])Call("BuildExe", new List<string>()); }
    static byte[] BuildRpk() { return (byte[])Call("BuildRpk", new List<string>()); }

    static IEnumerable<object> Groups() { return ((System.Collections.IEnumerable)Asm.GetType("PatchData").GetField("Groups").GetValue(null)).Cast<object>(); }
    static string PF(object p, string f) { return (string)p.GetType().GetField(f).GetValue(p); }
    static bool Matches(byte[] img, string key, string file, byte[] stock) {   // every run of a group is in (true) / all bytes still original or beyond EOF (false)
        foreach (var p in Groups().Where(p => PF(p, "Key") == key && PF(p, "File") == file))
            foreach (var r in (Array)p.GetType().GetField("Runs").GetValue(p)) {
                int o = (int)r.GetType().GetField("Offset").GetValue(r);
                var nw = (byte[])r.GetType().GetField("New").GetValue(r);
                for (int i = 0; i < nw.Length; i++) if (img[o + i] != nw[i]) return false;
            }
        return true;
    }
    static bool Untouched(byte[] img, string key, string file, byte[] stock) {
        foreach (var p in Groups().Where(p => PF(p, "Key") == key && PF(p, "File") == file))
            foreach (var r in (Array)p.GetType().GetField("Runs").GetValue(p)) {
                int o = (int)r.GetType().GetField("Offset").GetValue(r);
                var nw = (byte[])r.GetType().GetField("New").GetValue(r);
                for (int i = 0; i < nw.Length; i++) {
                    if (o + i >= img.Length) break;
                    byte want = o + i < stock.Length ? stock[o + i] : (byte)0;
                    if (img[o + i] != want) return false;
                }
            }
        return true;
    }

    static bool Same(byte[] a, byte[] b, int at, int n) {
        if (a.Length != b.Length) return false;
        for (int i = at; i < at + n; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // ---- does Windows accept the exe as an image? Started suspended and killed: no game code runs.
    [StructLayout(LayoutKind.Sequential)] struct PI { public IntPtr hP, hT; public int pid, tid; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SI { public int cb; public string r, d, t; public int x, y, w, h, xc, yc, fa, fl; public short sw, c2; public IntPtr r2, i, o, e; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd, ref SI si, out PI pi);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr h, uint code);
    [DllImport("kernel32.dll")] static extern int WaitForSingleObject(IntPtr h, int ms);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    static bool LoaderAccepts(byte[] img, string tmpDir) {
        string f = Path.Combine(tmpDir, "loadtest.exe");
        File.WriteAllBytes(f, img);
        var si = new SI { cb = Marshal.SizeOf(typeof(SI)) };
        PI pi;
        bool ok = CreateProcess(f, null, IntPtr.Zero, IntPtr.Zero, false, 4 /*CREATE_SUSPENDED*/, IntPtr.Zero, tmpDir, ref si, out pi);
        if (ok) { TerminateProcess(pi.hP, 0); WaitForSingleObject(pi.hP, 5000); CloseHandle(pi.hT); CloseHandle(pi.hP); }
        try { File.Delete(f); } catch (IOException) { System.Threading.Thread.Sleep(300); File.Delete(f); }
        return ok;
    }

    // ---- message boxes
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(int tid, EnumProc f, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr c, string cls, string t);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr h, int id);
    [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
    static string lastDialog = "";
    static IntPtr lastHandle = IntPtr.Zero;
    static int tries;
    static void CloseDialogs() {
        EnumThreadWindows(GetCurrentThreadId(), (h, l) => {
            var sb = new System.Text.StringBuilder(256);
            GetClassName(h, sb, 256);
            if (sb.ToString() != "#32770") return true;
            var txt = FindWindowEx(h, IntPtr.Zero, "Static", null);
            txt = FindWindowEx(h, txt, "Static", null);
            var m = new System.Text.StringBuilder(4096);
            GetWindowText(txt, m, 4096);
            if (m.Length == 0) { txt = FindWindowEx(h, IntPtr.Zero, "Static", null); GetWindowText(txt, m, 4096); }
            lastDialog = m.ToString();
            if (h != lastHandle) { Console.WriteLine("  [dialog] " + lastDialog.Replace("\r\n", " / ")); lastHandle = h; tries = 0; }
            if (++tries > 40) { Console.WriteLine("FAIL a dialog would not close"); Environment.Exit(3); }
            if (GetDlgItem(h, 6) != IntPtr.Zero) PostMessage(h, 0x111, (IntPtr)6, IntPtr.Zero);   // Yes
            else {                                                                                  // a lone OK button: its control id varies
                PostMessage(h, 0x111, (IntPtr)1, IntPtr.Zero);
                PostMessage(h, 0x111, (IntPtr)2, IntPtr.Zero);
                PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero);
            }
            return true;
        }, IntPtr.Zero);
    }

    [STAThread]
    static int Main(string[] a) {
        TD = a[1]; Real = a[2];
        var realFiles = new[] { "focom.exe", "focom_original.exe", "dgVoodoo.conf", "dgVoodoo_original.conf", @"forcecommand\fcv1p1.rpk", @"forcecommand\fcv1p1_original.rpk", @"forcecommand\forcecommand.rpk" };
        var before = realFiles.ToDictionary(n => n, n => Md5File(RP(n)));

        Asm = Assembly.LoadFrom(a[0]);
        T = Asm.GetType("InstallerForm");
        F = T.GetConstructor(NP, null, Type.EmptyTypes, null).Invoke(null);
        var PD = Asm.GetType("PatchData");
        string exeTested = (string)PD.GetField("ExeTestedMd5").GetValue(null), rpkTested = (string)PD.GetField("RpkTestedMd5").GetValue(null);
        string baseStock = (string)PD.GetField("BaseRpkMd5").GetValue(null);
        string baseTested = before[@"forcecommand\forcecommand.rpk"];
        byte[] exeStock = File.ReadAllBytes(RP("focom_original.exe")), rpkStock = File.ReadAllBytes(RP(@"forcecommand\fcv1p1_original.rpk"));

        // ---- 1. the player's real, already patched install (read only)
        Console.WriteLine("--- real install, read only");
        Load(Real);
        Console.WriteLine("status: " + ((Label)Get("status")).Text);
        Check("real: every option detected as installed", All.All(On));
        Check("real: resolution read from the conf", ((ComboBox)Get("res")).Text.StartsWith("1920 x 1080"));
        Check("real: restore available", ((Button)Get("restore")).Enabled);
        Check("real: exe rebuild = installed exe", Md5(BuildExe()) == before["focom.exe"]);
        Check("real: rpk rebuild = installed rpk", Md5(BuildRpk()) == before[@"forcecommand\fcv1p1.rpk"]);

        // ---- 2. clean game
        Console.WriteLine("--- clean install");
        Load(TD);
        Console.WriteLine("status: " + ((Label)Get("status")).Text);
        Check("clean: all options ticked by default", All.All(On));
        Check("clean: restore disabled", !((Button)Get("restore")).Enabled);
        Check("clean: base archive is the stock file", Md5File(TP(@"forcecommand\forcecommand.rpk")) == baseStock);
        Res(1920, 1080);
        var exeAll = BuildExe(); var rpkAll = BuildRpk();
        Check("all options = tested exe, byte for byte", Md5(exeAll) == exeTested);
        Check("all options = tested rpk, byte for byte", Md5(rpkAll) == rpkTested);

        var timer = new Timer { Interval = 200 };
        timer.Tick += (s, e) => CloseDialogs();
        timer.Start();

        Install();
        Check("install all: focom.exe on disk = tested", Md5File(TP("focom.exe")) == exeTested);
        Check("install all: fcv1p1.rpk on disk = tested", Md5File(TP(@"forcecommand\fcv1p1.rpk")) == rpkTested);
        Check("install all: forcecommand.rpk on disk = tested", Md5File(TP(@"forcecommand\forcecommand.rpk")) == baseTested);
        Check("install all: exe backup = original", Md5File(TP("focom_original.exe")) == Md5(exeStock));
        Check("install all: rpk backup = original", Md5File(TP(@"forcecommand\fcv1p1_original.rpk")) == Md5(rpkStock));
        Check("install all: conf lines = the tested widescreen conf", File.ReadAllLines(TP("dgVoodoo.conf")).SequenceEqual(File.ReadAllLines(RP("dgVoodoo_widescreen.conf"))));
        Check("install all: conf backup = the original conf", Md5File(TP("dgVoodoo_original.conf")) == Md5File(RP("dgVoodoo_original.conf")));
        Check("no temp files left", !File.Exists(TP("focom.exe.tmp")) && !File.Exists(TP(@"forcecommand\fcv1p1.rpk.tmp")));
        Check("after install: every option detected", All.All(On));
        Check("after install: restore enabled", ((Button)Get("restore")).Enabled);
        Check("install again changes nothing", ReinstallSame(exeTested, rpkTested, baseTested));

        // ---- 3. each option alone and combined, built in memory
        Console.WriteLine("--- combinations");
        Load(TD);
        Res(1920, 1080);
        int combos = 0, badCombos = 0, loaderOk = 0, loaderBad = 0;
        var seen = new HashSet<string>();
        string tmp = Path.Combine(Path.GetDirectoryName(TD), "loader"); Directory.CreateDirectory(tmp);
        for (int m = 0; m < 1 << All.Length; m++) {
            var on = All.Where((k, i) => (m >> i & 1) != 0).ToArray();
            if (on.Contains("menus") && !on.Contains("widescreen")) continue;
            Tick(on);
            var ex = BuildExe(); var rp = BuildRpk();
            combos++;
            bool ok = true;
            foreach (var k in new[] { "widescreen", "menus", "movement", "saves" }) {
                bool want = on.Contains(k);
                ok &= want ? Matches(ex, k, "exe", exeStock) : Untouched(ex, k, "exe", exeStock);
            }
            foreach (var k in new[] { "mines", "saves" }) {
                bool want = on.Contains(k);
                ok &= want ? Matches(rp, k, "rpk", rpkStock) : Untouched(rp, k, "rpk", rpkStock);
            }
            ok &= on.Contains("ai") ? rp.Length == rpkAll.Length : rp.Length == rpkStock.Length;
            if (on.Contains("ai")) {
                int at = (int)PD.GetField("RpkInsertAt").GetValue(null), g = rpkAll.Length - rpkStock.Length;
                ok &= Same(rp, rpkAll, at, rpkAll.Length - at);                          // relocated object, name tree and directory are the same whatever else is on
                ok &= BitConverter.ToUInt32(rp, 0x10) == BitConverter.ToUInt32(rpkAll, 0x10);
            }
            // nothing ticked for a file = that file is exactly the original
            if (!on.Any(k => k == "widescreen" || k == "menus" || k == "movement" || k == "saves")) ok &= Md5(ex) == Md5(exeStock);
            if (!on.Any(k => k == "saves" || k == "mines" || k == "ai")) ok &= Md5(rp) == Md5(rpkStock);
            if (!ok) { badCombos++; Console.WriteLine("  BAD: " + string.Join("+", on)); }
            if (seen.Add(Md5(ex))) { if (LoaderAccepts(ex, tmp)) loaderOk++; else { loaderBad++; Console.WriteLine("  LOADER REFUSED: " + string.Join("+", on)); } }
        }
        Check("all " + combos + " option combinations build as expected", badCombos == 0);
        Check("Windows accepts all " + seen.Count + " different exes (" + loaderOk + " ok)", loaderBad == 0 && seen.Count == 12);

        // ---- 4. changing options on disk
        Console.WriteLine("--- changing options");
        Load(TD);
        Tick("widescreen", "menus", "movement", "saves", "mines", "bldgs");   // AI off
        Install();
        Check("AI removed: rpk is back to stock size", new FileInfo(TP(@"forcecommand\fcv1p1.rpk")).Length == rpkStock.Length);
        Check("AI removed: rpk = tested mines + saves build", Md5File(TP(@"forcecommand\fcv1p1.rpk")) == Md5File(RP(@"forcecommand\fcv1p1_pre_ai.rpk")));
        Check("detected: all but AI", All.All(k => On(k) == (k != "ai")));

        Tick("movement", "ai");
        Install();
        Check("widescreen off: conf put back", Md5File(TP("dgVoodoo.conf")) == Md5File(RP("dgVoodoo_original.conf")));
        Check("bldgs off: base archive stock again", Md5File(TP(@"forcecommand\forcecommand.rpk")) == baseStock);
        Check("detected: movement + AI only", All.All(k => On(k) == (k == "movement" || k == "ai")));
        var mv = File.ReadAllBytes(TP("focom.exe"));
        Check("widescreen off: its code is gone", Untouched(mv, "widescreen", "exe", exeStock) && Untouched(mv, "menus", "exe", exeStock) && Untouched(mv, "saves", "exe", exeStock));

        Console.WriteLine("--- other screen shapes");
        Tick(All);
        Res(2560, 1080);
        Install();
        var ul = File.ReadAllBytes(TP("focom.exe"));
        int a3 = (int)PD.GetField("Aspect3D").GetValue(null), a2 = (int)PD.GetField("Aspect2D").GetValue(null);
        float k3 = BitConverter.ToSingle(ul, a3), k2 = BitConverter.ToSingle(ul, a2);
        Check("2560x1080: 3D constant is (4/3)/aspect", Math.Abs(k3 - 0.5625f) < 1e-5);
        Check("2560x1080: 2D constant is twice that", Math.Abs(k2 - 1.125f) < 1e-5);
        Check("2560x1080: conf resolution written", File.ReadAllLines(TP("dgVoodoo.conf")).Any(l => l.Replace(" ", "") == "Resolution=h:2560,v:1080"));
        Check("2560x1080: Glide resolution left alone", File.ReadAllLines(TP("dgVoodoo.conf")).Count(l => l.Replace(" ", "").StartsWith("Resolution=h:2560")) == 1);
        Check("2560x1080: remembered on reload", ((ComboBox)Get("res")).Text.StartsWith("2560 x 1080"));
        Check("2560x1080: all options still detected", All.All(On));
        Res(1920, 1200);
        var w10 = BuildExe();
        Check("1920x1200: 3D constant 0.8333", Math.Abs(BitConverter.ToSingle(w10, a3) - 0.83333f) < 1e-4);

        // ---- 5. restore
        Console.WriteLine("--- restore");
        Restore();
        Check("restore: focom.exe original", Md5File(TP("focom.exe")) == Md5(exeStock));
        Check("restore: fcv1p1.rpk original", Md5File(TP(@"forcecommand\fcv1p1.rpk")) == Md5(rpkStock));
        Check("restore: forcecommand.rpk original", Md5File(TP(@"forcecommand\forcecommand.rpk")) == baseStock);
        Check("restore: conf original", Md5File(TP("dgVoodoo.conf")) == Md5File(RP("dgVoodoo_original.conf")));
        Check("restore: looks clean, all ticked again", All.All(On) && !((Button)Get("restore")).Enabled);

        // ---- 6. awkward folders
        Console.WriteLine("--- awkward folders");
        Load(Path.GetDirectoryName(TD));
        Check("parent folder is accepted", ((string)Get("dir")) == TD);
        Load(@"C:\Windows");
        Check("wrong folder: nothing can be installed", !((Button)Get("install")).Enabled);

        string conf = TP("dgVoodoo.conf"), ddraw = TP("ddraw.dll");
        File.Move(ddraw, ddraw + ".x");
        Load(TD);
        Check("no dgVoodoo: Display options off, others on", !On("widescreen") && !On("menus") && On("movement") && On("mines") && ((Button)Get("install")).Enabled);
        Console.WriteLine("status: " + ((Label)Get("status")).Text);
        var noDg = BuildExe();
        Check("no dgVoodoo: exe has no widescreen", Untouched(noDg, "widescreen", "exe", exeStock));
        File.Move(ddraw + ".x", ddraw);

        string rk = TP(@"forcecommand\fcv1p1.rpk"), bk = TP(@"forcecommand\fcv1p1_original.rpk");
        File.Move(rk, rk + ".x"); File.Move(bk, bk + ".x");
        Load(TD);
        Check("no 1.1 archive: its options off, exe options on", !On("mines") && !On("ai") && !On("saves") && On("movement") && On("widescreen"));
        Console.WriteLine("status: " + ((Label)Get("status")).Text);
        File.Move(rk + ".x", rk); File.Move(bk + ".x", bk);

        string bs = TP(@"forcecommand\forcecommand.rpk");
        File.Move(bs, bs + ".x");
        Load(TD);
        Check("no base archive: only that option off", !On("bldgs") && On("ai") && On("movement"));
        File.Move(bs + ".x", bs);

        string ex2 = TP("focom.exe"), ex2b = TP("focom_original.exe");
        var keep = File.ReadAllBytes(ex2);
        var bad = (byte[])keep.Clone(); bad[0x2000] ^= 0xFF;
        File.WriteAllBytes(ex2, bad); File.Move(ex2b, ex2b + ".x");
        Load(TD);
        Check("unknown exe, no backup: exe options off, archive options on", !On("widescreen") && !On("movement") && !On("saves") && On("mines") && On("ai") && On("bldgs"));
        Console.WriteLine("status: " + ((Label)Get("status")).Text);
        Install();
        Check("unknown exe is left alone by an install", Md5File(ex2) == Md5(bad));
        File.WriteAllBytes(ex2, keep); File.Move(ex2b + ".x", ex2b);

        // a wrong file sitting under the backup name is kept under another name
        Load(TD);
        File.WriteAllBytes(ex2b, new byte[] { 1, 2, 3 });
        Load(TD);
        Check("wrong backup with a clean game: still usable", Box("movement").Enabled);
        Opt("movement", true);
        Install();
        Check("wrong backup: moved aside, real backup written", File.Exists(TP("focom_original-old.exe")) && Md5File(ex2b) == Md5(exeStock));

        // ---- the real game must not have changed
        timer.Stop();
        Check("real game files unchanged by all of this", realFiles.All(n => Md5File(RP(n)) == before[n]));
        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "ALL PASS (" + passes + ")" : fails + " FAILED, " + passes + " passed");
        return fails == 0 ? 0 : 1;
    }

    static bool ReinstallSame(string exeMd5, string rpkMd5, string baseMd5) {
        Install();
        return Md5File(TP("focom.exe")) == exeMd5 && Md5File(TP(@"forcecommand\fcv1p1.rpk")) == rpkMd5 && Md5File(TP(@"forcecommand\forcecommand.rpk")) == baseMd5;
    }
}
