using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Workbench
{
    /// <summary>
    /// Läuft unsichtbar im Hintergrund (kein Fenster, kein Symbol im Infobereich). Das Fenster wird nur bei Bedarf gebaut und
    /// beim Schließen ganz freigegeben – so bleibt im Leerlauf fast nichts übrig.
    /// </summary>
    class WbApp : Application
    {
        readonly bool hintergrund;
        Engine E;
        Hauptfenster fenster;
        Frage frage;
        HwndSource nachrichten;
        bool kuerzelAn;

        public WbApp(bool imHintergrund)
        {
            hintergrund = imHintergrund;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += (s, e) => { Log.Fehler("UI", e.Exception); e.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Fehler("App", e.ExceptionObject as Exception);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            UI.ThemaLaden(Resources);
            var cfg = Einstellungen.Laden();
            E = new Engine(Dispatcher, cfg);
            E.DownloadsFertig += NeueDownloads;
            E.OrdnerGespeichert += Gespeichert;
            E.Geaendert += () => { if (fenster != null) fenster.Auffrischen(); };
            E.Start();
            Install.Pruefen(cfg);

            nachrichten = new HwndSource(new HwndSourceParameters("Workbench.Nachrichten") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
            nachrichten.AddHook(Nachricht);
            Tastenkuerzel(cfg.Tastenkuerzel);

            Warten(Program.ZeigenName, () => Zeigen(-1));
            Warten(Program.BeendenName, Beenden);

            Log.Info("Gestartet " + Program.Version + (hintergrund ? " (Hintergrund)" : ""));
            if (!hintergrund) Zeigen(Hauptfenster.Befristet);
            else
            {
                // Fenster schon still vorbereiten, wenn der PC zur Ruhe gekommen ist – dann öffnet es später sofort.
                var vor = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(20) };
                vor.Tick += delegate { vor.Stop(); if (fenster == null) Bauen(Hauptfenster.Befristet); Sparen(); };
                vor.Start();
            }
            // Update: höchstens einmal am Tag still nachsehen
            var upd = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(45) };
            upd.Tick += delegate { upd.Interval = TimeSpan.FromHours(6); Update.Pruefen(E.Cfg); };
            upd.Start();
        }

        void Bauen(int ansicht)
        {
            fenster = new Hauptfenster(E, ansicht, false);
            fenster.Versteckt += Sparen;
        }

        /// <summary>Nach dem Verstecken: Speicher an Windows zurückgeben (holt ihn beim Öffnen blitzschnell wieder).</summary>
        void Sparen()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, (IntPtr)(-1), (IntPtr)(-1));
            }), DispatcherPriority.ApplicationIdle);
        }

        void Warten(string name, Action a)
        {
            var ev = new EventWaitHandle(false, EventResetMode.AutoReset, name);
            var t = new Thread(() => { while (true) { ev.WaitOne(); Dispatcher.BeginInvoke(a); } });
            t.IsBackground = true;
            t.Start();
        }

        void NeueDownloads(List<Eintrag> neu)
        {
            if (!E.Cfg.Fragen) return;                         // still: Vorschlag gilt
            if (frage != null && frage.Offen && !frage.IstInfo) { frage.Hinzufuegen(neu); return; }
            NeueFrage(neu, false);
        }

        void Gespeichert(Eintrag e)
        {
            if (!E.Cfg.HinweisSpeichern) return;
            if (frage != null && frage.Offen && !frage.IstInfo) return;   // eine Frage läuft gerade – nicht drängeln
            NeueFrage(new List<Eintrag> { e }, true);
        }

        void NeueFrage(List<Eintrag> l, bool info)
        {
            if (frage != null && frage.IsVisible) frage.Close();
            frage = new Frage(E, l, info);
            frage.Closed += (s, a) => { if (frage == s) frage = null; };
            frage.Show();
        }

        public void Zeigen(int ansicht)
        {
            if (fenster == null) Bauen(ansicht < 0 ? Hauptfenster.Befristet : ansicht);
            else if (ansicht >= 0) fenster.Zeige(ansicht);
            if (fenster.WindowState == WindowState.Minimized) fenster.WindowState = WindowState.Normal;
            fenster.Hereinholen();
            fenster.Activate();
            fenster.Topmost = true; fenster.Topmost = false;   // sicher nach vorn
            Native.SetForegroundWindow(new WindowInteropHelper(fenster).Handle);
        }

        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr p, IntPtr min, IntPtr max);

        public void Tastenkuerzel(bool an)
        {
            if (nachrichten == null) return;
            if (kuerzelAn) { Native.UnregisterHotKey(nachrichten.Handle, 1); kuerzelAn = false; }
            if (an) kuerzelAn = Native.RegisterHotKey(nachrichten.Handle, 1, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x57 /* W */);
            if (an && !kuerzelAn) Log.Info("Tastenkürzel Strg+Alt+W ist schon belegt");
        }

        IntPtr Nachricht(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x0312 && w.ToInt32() == 1) { Zeigen(-1); handled = true; }   // WM_HOTKEY
            return IntPtr.Zero;
        }

        void Beenden()
        {
            Log.Info("Beendet");
            if (fenster != null) { fenster.Endgueltig = true; fenster.Close(); }
            if (E != null) E.Stop();
            Shutdown();
        }
    }
}
