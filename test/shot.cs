// Loads a folder into the installer form (no install) and saves the window as a PNG.
// Usage: shot.exe <installer.exe> <game folder> <out.png>
using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

class Shot {
    [STAThread]
    static void Main(string[] a) {
        var t = Assembly.LoadFrom(a[0]).GetType("InstallerForm");
        var f = (Form)t.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null).Invoke(null);
        ((TextBox)t.GetField("folder", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f)).Text = a[1];
        t.GetMethod("LoadFolder", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(f, null);
        f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-3000, 0);
        f.Shown += (s, e) => {
            Application.DoEvents();
            using (var b = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height)); b.Save(a[2]); }
            f.Close();
        };
        Application.Run(f);
    }
}
