using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Workbench
{
    /// <summary>
    /// Browser-Erweiterung „Download with Workbench“ (Chrome, Edge, Brave …).
    /// Steckt in der exe, wird nach %LOCALAPPDATA%\Workbench\Browser ausgepackt. Verbindung über Native Messaging:
    /// der Browser startet Workbench.exe mit „chrome-extension://…“, schickt eine Nachricht, Workbench stellt sie als Auftrag ein.
    /// Fremde Erweiterungen darf kein Programm selbst installieren – Workbench öffnet die Seite, kopiert den Pfad
    /// und trägt ihn ins Ordner-Fenster des Browsers ein; den Rest klickt man selbst.
    /// </summary>
    static class Browser
    {
        public const string Host = "com.workbench.host";
        public const string Id = "mffnoocgaogljpgadglahpfhiaodlgab";   // aus dem festen Schlüssel im manifest.json

        static readonly string[] HostSchluessel =
        {
            @"Software\Google\Chrome\NativeMessagingHosts\",
            @"Software\Microsoft\Edge\NativeMessagingHosts\",
            @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\",
            @"Software\Chromium\NativeMessagingHosts\",
            @"Software\Vivaldi\NativeMessagingHosts\"
        };

        public static string Ordner { get { return Path.Combine(Paths.Daten, "Browser"); } }
        static string HostDatei { get { return Path.Combine(Paths.Daten, "browser-host.json"); } }

        public static string Exe
        {
            get { return Install.IstInstallierteKopie() || !File.Exists(Install.Ziel) ? System.Reflection.Assembly.GetExecutingAssembly().Location : Install.Ziel; }
        }

        // ---------- Auspacken + Anmelden ----------

        /// <summary>Erweiterung aus der exe in den Ordner schreiben (nur was sich geändert hat).</summary>
        public static void Auspacken(string ziel)
        {
            Directory.CreateDirectory(ziel);
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (!name.StartsWith("browser/")) continue;
                byte[] daten;
                using (var s = asm.GetManifestResourceStream(name)) using (var ms = new MemoryStream()) { s.CopyTo(ms); daten = ms.ToArray(); }
                string datei = Path.Combine(ziel, name.Substring(8));
                if (name.EndsWith(".json") || name.EndsWith(".js"))
                    daten = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(daten).Replace("__VERSION__", Program.Version));
                if (File.Exists(datei) && File.ReadAllBytes(datei).SequenceEqual(daten)) continue;
                File.WriteAllBytes(datei, daten);
            }
        }

        /// <summary>Native-Messaging-Host für alle Chromium-Browser eintragen (HKCU, keine Adminrechte).</summary>
        public static void Anmelden()
        {
            try
            {
                Auspacken(Ordner);
                var host = new Dictionary<string, object>
                {
                    { "name", Host },
                    { "description", "Workbench" },
                    { "path", Exe },
                    { "type", "stdio" },
                    { "allowed_origins", new[] { "chrome-extension://" + Id + "/" } }
                };
                File.WriteAllText(HostDatei, Store.Json().Serialize(host), new UTF8Encoding(false));
                foreach (var k in HostSchluessel)
                    using (var r = Registry.CurrentUser.CreateSubKey(k + Host)) r.SetValue("", HostDatei);
            }
            catch (Exception ex) { Log.Fehler("Browser.Anmelden", ex); }
        }

        public static void Abmelden()
        {
            foreach (var k in HostSchluessel)
                try { Registry.CurrentUser.DeleteSubKeyTree(k + Host, false); } catch { }
            try { if (File.Exists(HostDatei)) File.Delete(HostDatei); } catch { }
            try { if (Directory.Exists(Ordner)) Directory.Delete(Ordner, true); } catch { }
        }

        // ---------- Native Messaging (Workbench.exe als Host) ----------

        /// <summary>Eine Nachricht lesen (4 Byte Länge + JSON) und in eine Auftragszeile übersetzen; null = nichts zu tun.</summary>
        public static string NachrichtLesen(Stream ein)
        {
            var len = new byte[4];
            if (Lesen(ein, len) < 4) return null;
            int n = BitConverter.ToInt32(len, 0);
            if (n <= 0 || n > 1 << 20) return null;
            var buf = new byte[n];
            if (Lesen(ein, buf) < n) return null;
            var d = Store.Json().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(buf));
            object op, zeit, pfad;
            d.TryGetValue("op", out op);
            d.TryGetValue("time", out zeit);
            d.TryGetValue("path", out pfad);
            string o = op as string, z = zeit as string, p = pfad as string;
            if (o == "hello") return "browser\thallo";
            if ((o == "plan" || o == "done") && !string.IsNullOrEmpty(p) && (ZeitUI.Stationen.Contains(z)))
            {
                if (p.IndexOf('\t') >= 0 || p.IndexOf('\n') >= 0 || !Path.IsPathRooted(p)) return null;
                return (o == "plan" ? "vormerken\t" : "fertig\t") + z + "\t" + Path.GetFullPath(p);
            }
            return null;
        }

        static int Lesen(Stream s, byte[] b)
        {
            int ges = 0;
            while (ges < b.Length) { int r = s.Read(b, ges, b.Length - ges); if (r <= 0) break; ges += r; }
            return ges;
        }

        public static void Antworten(Stream aus)
        {
            var j = Encoding.UTF8.GetBytes("{\"ok\":true}");
            aus.Write(BitConverter.GetBytes(j.Length), 0, 4);
            aus.Write(j, 0, j.Length);
            aus.Flush();
        }

        // ---------- Einrichten ----------

        class Ziel { public string Name, Exe, Seite; }

        /// <summary>Standardbrowser – oder, falls der keine Chrome-Erweiterungen kann (Firefox …), Chrome bzw. Edge.</summary>
        static Ziel Finden()
        {
            string progId = null;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice"))
                    if (k != null) progId = k.GetValue("ProgId") as string;
            }
            catch { }
            var kandidaten = new List<string>();
            if (progId != null) kandidaten.Add(progId);
            kandidaten.AddRange(new[] { "ChromeHTML", "MSEdgeHTM" });
            foreach (var id in kandidaten)
            {
                string name = null, seite = "chrome://extensions";
                if (id.StartsWith("ChromeHTML")) name = "Chrome";
                else if (id.StartsWith("MSEdgeHTM")) { name = "Edge"; seite = "edge://extensions"; }
                else if (id.StartsWith("BraveHTML")) { name = "Brave"; seite = "brave://extensions"; }
                else if (id.StartsWith("VivaldiHTM")) { name = "Vivaldi"; seite = "vivaldi://extensions"; }
                else if (id.StartsWith("ChromiumHTM")) name = "Chromium";
                if (name == null) continue;
                string exe = ExeVon(id);
                if (exe != null) return new Ziel { Name = name, Exe = exe, Seite = seite };
            }
            return null;
        }

        static string ExeVon(string progId)
        {
            try
            {
                using (var k = Registry.ClassesRoot.OpenSubKey(progId + @"\shell\open\command"))
                {
                    string c = k == null ? null : k.GetValue("") as string;
                    if (string.IsNullOrEmpty(c)) return null;
                    string exe = c.StartsWith("\"") ? c.Substring(1, c.IndexOf('"', 1) - 1) : c.Split(' ')[0];
                    return File.Exists(exe) ? exe : null;
                }
            }
            catch { return null; }
        }

        static BrowserKarte karte;

        /// <summary>Alles vorbereiten, Erweiterungsseite öffnen, Hilfekarte zeigen.</summary>
        public static void Einrichten(Einstellungen cfg)
        {
            cfg.Browser = true;
            cfg.Speichern();
            Anmelden();
            var z = Finden();
            try { Clipboard.SetText(Ordner); } catch { }
            if (z != null)
                try { Process.Start(new ProcessStartInfo(z.Exe, z.Seite) { UseShellExecute = false }); } catch (Exception ex) { Log.Fehler("Browser öffnen", ex); }
            if (karte != null) karte.Schliessen();
            karte = new BrowserKarte(z == null ? "your browser" : z.Name);
            karte.Show();
        }

        /// <summary>Die Erweiterung hat sich gemeldet.</summary>
        public static void Verbunden(Einstellungen cfg)
        {
            if (!cfg.BrowserVerbunden || !cfg.Browser) { cfg.BrowserVerbunden = true; cfg.Browser = true; cfg.Speichern(); }
            if (karte != null) karte.Fertig();
        }

        // ---------- Pfad ins Ordner-Fenster des Browsers eintragen ----------

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc f, IntPtr l);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        const int WM_SETTEXT = 0x000C, EM_SETSEL = 0x00B1;

        static string Klasse(IntPtr h) { var sb = new StringBuilder(64); GetClassName(h, sb, 64); return sb.ToString(); }

        static readonly string[] browserProzesse = { "chrome", "msedge", "brave", "vivaldi", "chromium" };

        /// <summary>Offenes Ordner-Fenster eines Browsers suchen und den Pfad eintragen. true = eingetragen.</summary>
        public static bool PfadEintragen(HashSet<IntPtr> schon)
        {
            bool ok = false;
            EnumWindows((h, l) =>
            {
                if (ok || schon.Contains(h) || !IsWindowVisible(h) || Klasse(h) != "#32770") return true;
                uint pid; GetWindowThreadProcessId(h, out pid);
                string prozess;
                try { prozess = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); } catch { return true; }
                if (!browserProzesse.Contains(prozess)) return true;
                schon.Add(h);
                IntPtr feld = IntPtr.Zero, ersatz = IntPtr.Zero;
                EnumChildWindows(h, (c, l2) =>
                {
                    if (Klasse(c) != "Edit" || !IsWindowVisible(c)) return true;
                    IntPtr p = GetParent(c);
                    if (Klasse(p) == "ComboBox" && GetDlgCtrlID(p) == 1148) { feld = c; return false; }   // „Ordner:“/„Dateiname:“
                    if (ersatz == IntPtr.Zero && Klasse(p) == "ComboBox") ersatz = c;
                    return true;
                }, IntPtr.Zero);
                if (feld == IntPtr.Zero) feld = ersatz;
                if (feld != IntPtr.Zero)
                {
                    SendMessage(feld, WM_SETTEXT, IntPtr.Zero, Ordner);
                    SendMessage(feld, EM_SETSEL, IntPtr.Zero, new IntPtr(-1));
                    ok = true;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return ok;
        }
    }

    /// <summary>Kleine Karte unten rechts, solange man im Browser klickt: drei Schritte, der aktuelle leuchtet.</summary>
    class BrowserKarte : Window
    {
        readonly List<Border> punkte = new List<Border>();
        readonly List<TextBlock> texte = new List<TextBlock>();
        readonly DispatcherTimer suche;
        readonly HashSet<IntPtr> gesehen = new HashSet<IntPtr>();
        readonly TextBlock kopf;
        readonly Grid huelle;
        readonly TranslateTransform schiebe = new TranslateTransform(0, 24);
        readonly DateTime seit = DateTime.UtcNow;
        bool zu;

        public BrowserKarte(string browserName)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 330;
            FontFamily = UI.Schrift;
            Title = "Workbench";

            var sp = new StackPanel();
            var k = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            var titel = new StackPanel { Orientation = Orientation.Horizontal };
            titel.Children.Add(UI.Logo(18));
            kopf = new TextBlock { Text = "Add to " + browserName, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Farbe.Text, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titel.Children.Add(kopf);
            k.Children.Add(titel);
            var x = UI.IconKnopf("M6 6l12 12M18 6L6 18", "Close", Farbe.Text3);
            x.HorizontalAlignment = HorizontalAlignment.Right;
            x.Click += delegate { Schliessen(); };
            k.Children.Add(x);
            sp.Children.Add(k);

            string[] schritte = { "Turn on Developer mode", "Click Load unpacked", "Click Select Folder" };
            string[] hilfe = { "A switch on the extensions page", "Appears once Developer mode is on", "Workbench fills in the folder for you" };
            for (int i = 0; i < schritte.Length; i++)
            {
                var z = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 3, 0, 3), ToolTip = hilfe[i], Background = Brushes.Transparent };
                var p = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center };
                p.Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var t = new TextBlock { Text = schritte[i], FontSize = 13, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                z.Children.Add(p); z.Children.Add(t);
                punkte.Add(p); texte.Add(t);
                sp.Children.Add(z);
            }
            var pfad = new TextBlock { Text = Browser.Ordner, FontSize = 11, Foreground = Farbe.Text3, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(30, 2, 0, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Copy" };
            pfad.MouseLeftButtonUp += delegate { try { Clipboard.SetText(Browser.Ordner); } catch { } };
            sp.Children.Add(pfad);

            var karte = new Border
            {
                Background = Farbe.B("#F5191C23"), BorderBrush = Farbe.B("#1FFFFFFF"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 14, 12, 14), Margin = new Thickness(0, 10, 0, 0),
                Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Opacity = 0.5, Direction = 270 }, Child = sp
            };
            huelle = new Grid { Margin = new Thickness(20), RenderTransform = schiebe, Opacity = 0 };
            huelle.Children.Add(new Border { Height = 10, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(36, 0, 36, 0), CornerRadius = new CornerRadius(14, 14, 0, 0), Background = Farbe.B("#C02A2E38"), BorderBrush = Farbe.B("#24FFFFFF"), BorderThickness = new Thickness(1, 1, 1, 0) });
            huelle.Children.Add(new Border { Height = 10, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(18, 5, 18, 0), CornerRadius = new CornerRadius(16, 16, 0, 0), Background = Farbe.B("#E0242833"), BorderBrush = Farbe.B("#2EFFFFFF"), BorderThickness = new Thickness(1, 1, 1, 0) });
            huelle.Children.Add(karte);
            Content = huelle;
            Schritt(0);

            SourceInitialized += delegate { Native.NichtAktivieren(new WindowInteropHelper(this).Handle); };
            SizeChanged += delegate { Platzieren(); };
            Loaded += delegate
            {
                Platzieren();
                huelle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = UI.Weich });
                schiebe.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = UI.Weich });
            };

            // Sobald der Browser sein Ordner-Fenster öffnet: Pfad eintragen, Schritt 3 leuchtet
            suche = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
            suche.Tick += delegate
            {
                if ((DateTime.UtcNow - seit).TotalMinutes > 15) { suche.Stop(); return; }
                if (Browser.PfadEintragen(gesehen)) Schritt(2);
            };
            suche.Start();
        }

        void Schritt(int aktiv)
        {
            for (int i = 0; i < punkte.Count; i++)
            {
                bool fertig = i < aktiv, jetzt = i == aktiv;
                punkte[i].BorderBrush = fertig ? Farbe.Gut : jetzt ? Farbe.Akzent : Farbe.B("#40FFFFFF");
                punkte[i].Background = fertig ? Farbe.B("#227FD8A8") : jetzt ? Farbe.AkzentWeich : Brushes.Transparent;
                var t = (TextBlock)punkte[i].Child;
                t.Text = fertig ? "✓" : (i + 1).ToString();
                t.Foreground = fertig ? Farbe.Gut : jetzt ? Farbe.Akzent : Farbe.Text3;
                texte[i].Foreground = jetzt ? Farbe.Text : fertig ? Farbe.Text2 : Farbe.Text3;
            }
        }

        /// <summary>Nur für --render: Inhalt ohne Fenster, Schritt gesetzt.</summary>
        public FrameworkElement FuerBild(int schritt)
        {
            suche.Stop();
            Schritt(schritt);
            Content = null;
            huelle.Opacity = 1;
            schiebe.Y = 0;
            return huelle;
        }

        public void Fertig()
        {
            Schritt(3);
            kopf.Text = "Connected";
            suche.Stop();
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            t.Tick += delegate { t.Stop(); Schliessen(); };
            t.Start();
        }

        public void Schliessen()
        {
            if (zu) return;
            zu = true;
            suche.Stop();
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
            a.Completed += delegate { Close(); };
            huelle.BeginAnimation(OpacityProperty, a);
        }

        void Platzieren()
        {
            var src = PresentationSource.FromVisual(this);
            var wa = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Matrix m = src != null && src.CompositionTarget != null ? src.CompositionTarget.TransformFromDevice : Matrix.Identity;
            var unten = m.Transform(new Point(wa.Right, wa.Bottom));
            var oben = m.Transform(new Point(wa.Left, wa.Top));
            Left = Math.Max(oben.X, unten.X - Width + 6);
            Top = Math.Max(oben.Y, unten.Y - ActualHeight + 6);
        }
    }
}
