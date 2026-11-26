using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Workbench
{
    /// <summary>
    /// Ein Blatt in der Schiene links: kleines Rechteck mit einem kaum sichtbaren Zeichen (Sanduhr, Ordner, Wolke …).
    /// Scharf ohne Effekte: Rand genau 1 Bildschirmpixel, Positionen auf ganze Pixel gerundet, kein Schatten/Weichzeichner.
    /// </summary>
    static class ReiterZeichen
    {
        public const string Sanduhr = "M7 4h10M7 20h10M8.5 4v2.5L12 12l3.5-5.5V4M8.5 20v-2.5L12 12l3.5 5.5V20";
        public const string Ordner = "M4 7.5A1.5 1.5 0 0 1 5.5 6h3.8l1.7 1.8h7.5A1.5 1.5 0 0 1 20 9.3v7.2a1.5 1.5 0 0 1-1.5 1.5h-13A1.5 1.5 0 0 1 4 16.5z";
        public const string Wolke = "M7.5 17.5h9a3.5 3.5 0 0 0 .5-6.96A5 5 0 0 0 7.3 9.6A4 4 0 0 0 7.5 17.5z";
        public const string Bildschirm = "M4.5 5.5h15v10h-15zM9.5 19h5M12 15.5V19";
        public const string Board = "M5.5 5.5h5v5h-5zM13.5 5.5h5v5h-5zM5.5 13.5h5v5h-5zM13.5 13.5h5v5h-5z";
        public const string Ausschnitt = "M5 9.5V5h4.5M14.5 5H19v4.5M19 14.5V19h-4.5M9.5 19H5v-4.5";
        public const string Plus = "M12 6.5v11M6.5 12h11";
        public const string Haus = "M4.5 11.5L12 5l7.5 6.5M6.5 10v8.5h11V10";
        public const string Notiz = "M6.5 4.5h11v15h-11zM9.5 9h5M9.5 12.5h5M9.5 16h3";
        public const string Download = "M12 4.5v10M8 11l4 4 4-4M5.5 19h13";
        public const string Nadel = "M9 4.5h6M10 4.5v5.5l-3 3.5h10l-3-3.5V4.5M12 14v5.5";
        public const string Werkzeug = "M14.5 5.5a4 4 0 0 0-5 5L4.5 15.5a1.8 1.8 0 0 0 2.5 2.5l5-5a4 4 0 0 0 5-5l-2.5 2.5-2-.5-.5-2z";
        public const string Inbox = "M12 4v7.5M9 8.5l3 3 3-3M4 13h4.5l1.5 2.5h4l1.5-2.5H20v6H4z";
        public const string Weiter = "M8.5 5.5v13l10-6.5z";
        public const string Ablage = "M4 13.5h4.5l1.5 2.5h4l1.5-2.5H20M4 13.5l2.2-6.5h11.6l2.2 6.5V18H4z";

        public static string Fuer(BlattDef b)
        {
            if (b == null) return Plus;
            switch (b.Art)
            {
                case "temp": return Sanduhr;
                case "cloud": return Wolke;
                case "board": return Board;
                case "empty": return Haus;
                case "home": return Haus;
            }
            if (!string.IsNullOrEmpty(b.Symbol)) return b.Symbol;
            return Ordner;
        }
    }

    class Reiter : Grid
    {
        public const double B = 40, H = 28;
        readonly Border kasten;
        readonly Path zeichen;
        readonly TranslateTransform schub = new TranslateTransform();
        bool an, ueber, ziel;
        double px = 1;            // Größe eines Bildschirmpixels in WPF-Einheiten

        public TranslateTransform Schub { get { return schub; } }

        public Reiter(string zeichenDaten)
        {
            Width = B; Height = H;
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            RenderTransform = schub;
            kasten = new Border { CornerRadius = new CornerRadius(8), SnapsToDevicePixels = true };
            Children.Add(kasten);
            zeichen = new Path
            {
                Data = Geometry.Parse(zeichenDaten),
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Stretch = Stretch.None
            };
            // 24er-Raster auf 11 px verkleinern, Strich bleibt ~0,75 px dick: kaum sichtbar, aber klar
            var c = new Canvas { Width = 24, Height = 24 };
            c.Children.Add(zeichen);
            Children.Add(new Viewbox { Width = 17, Height = 17, Child = c, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            Loaded += delegate { Pixel(); };
            Setzen();
        }

        void Pixel()
        {
            var src = PresentationSource.FromVisual(this);
            px = src != null && src.CompositionTarget != null ? 1 / src.CompositionTarget.TransformToDevice.M11 : 1;
            Setzen();
        }

        /// <summary>Für Bilder ohne Fenster: Skalierung vorgeben.</summary>
        public void Skalierung(double s) { px = 1 / s; Setzen(); }

        public bool An { get { return an; } set { an = value; Setzen(); } }
        public bool Ueber { get { return ueber; } set { ueber = value; Setzen(); } }
        public bool Ziel { get { return ziel; } set { ziel = value; Setzen(); } }

        /// <summary>Herausschieben um ganze Bildschirmpixel – sonst verschmiert der Rand beim Stillstand.</summary>
        public double Rund(double x) { return Math.Round(x / px) * px; }

        void Setzen()
        {
            kasten.BorderThickness = new Thickness(px);
            if (an)
            {
                kasten.Background = Farbe.B("#2EF4B860");
                kasten.BorderBrush = Farbe.B("#66F4B860");
                zeichen.Stroke = Farbe.B("#FFF4B860");
            }
            else
            {
                kasten.Background = Farbe.B(ueber ? "#14FFFFFF" : "#00FFFFFF");
                kasten.BorderBrush = Farbe.B(ziel ? "#99F4B860" : "#00FFFFFF");
                zeichen.Stroke = Farbe.B(ueber || ziel ? "#B3FFFFFF" : "#73FFFFFF");
            }
        }

    }
}
