using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Workbench
{
    /// <summary>
    /// Tiefe als semantischer Zoom (wie Fotos/Karten): Auseinanderziehen = hinein, Zusammenziehen = heraus.
    /// Die Ansicht folgt den Fingern direkt (Größe + Zahl links), loslassen rastet ein: über der Hälfte → nächste Ebene,
    /// sonst federt sie zurück. Eine Geste = höchstens eine Ebene. Am Ende nur leichter Gummiband-Widerstand.
    /// Mausrad mit Strg: jede Raste eine Ebene. Kein Extra-Fenster, kein Modus.
    /// </summary>
    partial class Hauptfenster
    {
        const double ZoomWeg = 320;      // so weit muss man ziehen für eine ganze Ebene
        double zoomSumme;
        bool zoomVerbraucht;             // diese Geste hat schon gewechselt – erst loslassen
        DispatcherTimer zoomRuhe;
        readonly List<Border> tiefeStriche = new List<Border>();
        readonly List<TextBlock> tiefeZahlen = new List<TextBlock>();

        DateTime zuletztFein;
        /// <summary>Touchpad schickt kleine Schritte; ein glatter 120er mitten in einer Touchpad-Geste zählt trotzdem als fein.</summary>
        bool IstFein(int delta)
        {
            if (delta % 120 != 0 || DateTime.UtcNow - zuletztFein < TimeSpan.FromMilliseconds(400)) { zuletztFein = DateTime.UtcNow; return true; }
            return false;
        }

        public void Zoom(int delta)
        {
            var b = Akt;
            if (b == null || !ImOrdner) return;
            if (!IstFein(delta)) { TiefeAendern(delta > 0 ? 1 : -1); return; }   // Mausrad: eine Raste, eine Ebene
            if (zoomRuhe == null)
            {
                zoomRuhe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
                zoomRuhe.Tick += delegate { zoomRuhe.Stop(); ZoomLoslassen(); };
            }
            zoomRuhe.Stop(); zoomRuhe.Start();
            if (zoomVerbraucht) return;
            zoomSumme += delta;
            int d = Math.Sign(zoomSumme), ziel;
            double p = ZoomFortschritt(b, d, out ziel);
            ZoomZeigen(d, p, ziel);
            if (ziel >= 0 && p >= 1) { zoomVerbraucht = true; zoomSumme = 0; TiefeSetzen(b, ziel); }
        }

        /// <summary>0…1 = wie weit zur nächsten Ebene; ziel = -1, wenn es in diese Richtung nicht weitergeht (dann Gummiband).</summary>
        double ZoomFortschritt(BlattDef b, int d, out int ziel)
        {
            int jetzt = Tiefe(b), max;
            if (!tiefeMax.TryGetValue(b.Id, out max)) max = 1;
            max = Math.Max(max, jetzt);
            bool geht = d > 0 ? jetzt < max : d < 0 && jetzt > 0;
            ziel = geht ? jetzt + d : -1;
            double p = Math.Abs(zoomSumme) / ZoomWeg;
            return geht ? Math.Min(1, p) : Math.Min(0.1, p * 0.2);
        }

        void ZoomLoslassen()
        {
            var b = Akt;
            bool war = zoomVerbraucht;
            zoomVerbraucht = false;
            if (war || b == null || zoomSumme == 0) { zoomSumme = 0; return; }
            int d = Math.Sign(zoomSumme), ziel;
            double p = ZoomFortschritt(b, d, out ziel);
            zoomSumme = 0;
            if (ziel >= 0 && p >= 0.45) TiefeSetzen(b, ziel);   // über die Hälfte: einrasten
            else ZoomZurueck();
        }

        /// <summary>Direkt unter den Fingern: Inhalt wächst (hinein) oder schrumpft (heraus), die Ziel-Zahl links leuchtet auf.</summary>
        void ZoomZeigen(int d, double p, int ziel)
        {
            if (bild || buehne.Children.Count == 0) return;
            var el = buehne.Children[0] as FrameworkElement;
            if (el == null) return;
            var st = el.RenderTransform as ScaleTransform;
            if (st == null || st.IsFrozen) { st = new ScaleTransform(1, 1); el.RenderTransformOrigin = new Point(0.5, 0.5); el.RenderTransform = st; }
            double s = d > 0 ? 1 + 0.08 * p : 1 - 0.06 * p;
            st.BeginAnimation(ScaleTransform.ScaleXProperty, null); st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            st.ScaleX = s; st.ScaleY = s;
            el.BeginAnimation(OpacityProperty, null);
            el.Opacity = 1 - 0.35 * p;
            for (int i = 0; i < tiefeZahlen.Count; i++)
            {
                tiefeZahlen[i].BeginAnimation(OpacityProperty, null);
                tiefeZahlen[i].Opacity = i == ziel ? 0.35 + 0.65 * p : 0.35 * Math.Min(1, p * 3);
                if (i < tiefeStriche.Count && i == ziel) tiefeStriche[i].Width = 8 + 6 * p;
            }
        }

        void ZoomZurueck()
        {
            if (bild || buehne.Children.Count == 0) return;
            var el = buehne.Children[0] as FrameworkElement;
            if (el == null) return;
            var st = el.RenderTransform as ScaleTransform;
            var ez = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
            if (st != null && !st.IsFrozen)
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ez });
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ez });
            }
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            int jetzt = Tiefe(Akt);
            for (int i = 0; i < tiefeZahlen.Count; i++)
            {
                tiefeZahlen[i].BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)));
                if (i < tiefeStriche.Count && i != jetzt) tiefeStriche[i].Width = 8;
            }
        }
    }
}
