using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Workbench
{
    static class Farbe
    {
        public static readonly Brush Text = B("#ECEEF2");
        public static readonly Brush Text2 = B("#A3AAB6");
        public static readonly Brush Text3 = B("#8A92A0");
        public static readonly Brush Akzent = B("#F4B860");
        public static readonly Brush AkzentText = B("#1C1405");
        public static readonly Brush AkzentWeich = B("#24F4B860");
        public static readonly Brush AkzentRand = B("#80F4B860");
        public static readonly Brush Linie = B("#10FFFFFF");
        public static readonly Brush Flaeche = B("#08FFFFFF");
        public static readonly Brush Flaeche2 = B("#0DFFFFFF");
        public static readonly Brush Hover = B("#0FFFFFFF");
        public static readonly Brush Fenster = B("#1B1E25");
        public static readonly Brush FensterMica = B("#C8181B22");
        public static readonly Brush Karte = B("#F8212430");
        public static readonly Brush Bald = B("#F4B860");
        public static readonly Brush BaldWeich = B("#24F4B860");
        public static readonly Brush Woche = B("#E6FFFFFF");
        public static readonly Brush WocheWeich = B("#1AFFFFFF");
        public static readonly Brush Spaeter = B("#B4BBC7");
        public static readonly Brush SpaeterWeich = B("#12FFFFFF");
        public static readonly Brush Gefahr = B("#FF9C8F");
        public static readonly Brush GefahrWeich = B("#1FFF9C8F");
        public static readonly Brush Gut = B("#7FD8A8");
        public static readonly Brush Transparent = Brushes.Transparent;

        public static SolidColorBrush B(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        /// <summary>Farbe des Datei-Platzhalters nach Endung (bis das echte Vorschaubild da ist).</summary>
        public static Brush Endung(string ext)
        {
            switch (ext.ToLowerInvariant())
            {
                case ".pdf": return B("#F08A7E");
                case ".zip": case ".rar": case ".7z": case ".gz": case ".tar": return B("#E9C46A");
                case ".exe": case ".msi": case ".msix": case ".appx": return B("#9DB4FF");
                case ".png": case ".jpg": case ".jpeg": case ".gif": case ".webp": case ".heic": case ".bmp": case ".svg": return B("#6CD3A6");
                case ".mp4": case ".mov": case ".mkv": case ".avi": case ".webm": case ".mp3": case ".wav": return B("#C3A6F5");
                case ".doc": case ".docx": case ".odt": case ".rtf": return B("#9DB4FF");
                case ".xls": case ".xlsx": case ".csv": case ".ods": return B("#7ED39B");
                case ".ppt": case ".pptx": case ".odp": return B("#F4A261");
                case ".psd": case ".ai": return B("#7FB6F0");
            }
            return B("#C9CED8");
        }
    }

    static class Ikon
    {
        public const string Suche = "M11 4a7 7 0 1 1 0 14a7 7 0 1 1 0-14zM20 20l-3.5-3.5";
        public const string Muell = "M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3";
        public const string Regler = "M4 6h9M17 6h3M4 12h3M11 12h9M4 18h11M19 18h1M15 4a2 2 0 1 1 0 4a2 2 0 1 1 0-4zM9 10a2 2 0 1 1 0 4a2 2 0 1 1 0-4zM17 16a2 2 0 1 1 0 4a2 2 0 1 1 0-4z";
        public const string Runter = "M6 9l6 6 6-6";
        public const string Behalten = "M12 19V8M7 13l5-5 5 5M5 4h14";
        public const string Kalender = "M5 5h14a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2zM3 10h18M8 3v4M16 3v4";
        public const string Unendlich = "M12 12c-2-2.5-3.5-4-5.5-4a4 4 0 0 0 0 8c2 0 3.5-1.5 5.5-4zm0 0c2 2.5 3.5 4 5.5 4a4 4 0 0 0 0-8c-2 0-3.5 1.5-5.5 4z";
        public const string Ordner = "M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z";
        public const string Plus = "M12 5v14M5 12h14";
        public const string Zurueck = "M4 12a8 8 0 1 0 2.5-5.8M4 4v4h4";
        public const string X = "M6 6l12 12M18 6L6 18";
        public const string Links = "M15 6l-6 6 6 6";
        public const string Rechts = "M9 6l6 6-6 6";
        public const string Haken = "M5 12.5l4.5 4.5L19 7.5";
        public const string Oeffnen = "M14 4h6v6M20 4l-9 9M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5";
        public const string Uhr = "M12 3a9 9 0 1 1 0 18a9 9 0 1 1 0-18zM12 7v5l3 2";
        public const string Minus = "M5 12h14";
        public const string Stift = "M4 20h4L19 9l-4-4L4 16v4zM13.5 6.5l4 4";
        public const string Wolke = "M7 18h10.5a4 4 0 0 0 .6-7.96A6 6 0 0 0 6.3 9.1A4.5 4.5 0 0 0 7 18z";
        public const string Quadrat = "M6 6h12v12H6z";
    }

    static class UI
    {
        public static readonly FontFamily Schrift = new FontFamily("Segoe UI Variable Text, Segoe UI, Inter");
        public static readonly FontFamily SchriftTitel = new FontFamily("Segoe UI Variable Display, Segoe UI Semibold, Segoe UI");

        public static void ThemaLaden(ResourceDictionary ziel)
        {
            ziel.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Xaml));
        }

        public static TextBlock T(string text, double groesse, Brush farbe, FontWeight gewicht)
        {
            return new TextBlock { Text = text, FontSize = groesse, Foreground = farbe, FontWeight = gewicht, TextTrimming = TextTrimming.CharacterEllipsis };
        }
        public static TextBlock T(string text, double groesse, Brush farbe) { return T(text, groesse, farbe, FontWeights.Normal); }

        public static FrameworkElement Icon(string daten, double groesse, Brush farbe, double dicke)
        {
            var p = new Path
            {
                Data = Geometry.Parse(daten),
                Stroke = farbe,
                StrokeThickness = dicke,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            };
            var c = new Canvas { Width = 24, Height = 24 };
            c.Children.Add(p);
            return new Viewbox { Width = groesse, Height = groesse, Child = c, IsHitTestVisible = false };
        }
        public static FrameworkElement Icon(string daten, double groesse, Brush farbe) { return Icon(daten, groesse, farbe, 1.8); }

        /// <summary>Das Workbench-Zeichen: zwei Ebenen übereinander.</summary>
        public static FrameworkElement Logo(double groesse)
        {
            var c = new Canvas { Width = 24, Height = 24 };
            var hinten = new Rectangle { Width = 14, Height = 11, RadiusX = 3, RadiusY = 3, Fill = Farbe.Akzent, Opacity = 0.45 };
            Canvas.SetLeft(hinten, 7); Canvas.SetTop(hinten, 3);
            var vorne = new Rectangle { Width = 14, Height = 11, RadiusX = 3, RadiusY = 3, Fill = Farbe.Akzent };
            Canvas.SetLeft(vorne, 3); Canvas.SetTop(vorne, 9);
            c.Children.Add(hinten); c.Children.Add(vorne);
            return new Viewbox { Width = groesse, Height = groesse, Child = c, IsHitTestVisible = false };
        }

        public static Button Knopf(string text, string icon, string stil)
        {
            var b = new Button { Style = (Style)Application.Current.FindResource(stil ?? "Btn") };
            if (icon == null) b.Content = text;
            else
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                Brush f = stil == "BtnPri" ? Farbe.AkzentText : (stil == "BtnGefahr" ? Farbe.Gefahr : Farbe.Text);
                sp.Children.Add(Icon(icon, 14, f, 2));
                if (!string.IsNullOrEmpty(text))
                {
                    var t = new TextBlock { Text = text, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    sp.Children.Add(t);
                }
                b.Content = sp;
            }
            return b;
        }
        public static Button Knopf(string text) { return Knopf(text, null, "Btn"); }

        public static Button IconKnopf(string icon, string hilfe, Brush farbe)
        {
            var b = new Button { Style = (Style)Application.Current.FindResource("BtnIcon"), Content = Icon(icon, 16, farbe), ToolTip = hilfe };
            System.Windows.Automation.AutomationProperties.SetName(b, hilfe);
            return b;
        }

        public static Border Pill(string text, Brush fg, Brush bg)
        {
            return new Border
            {
                Background = bg,
                CornerRadius = new CornerRadius(11),
                Height = 22,
                Padding = new Thickness(8, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock { Text = text, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = fg, VerticalAlignment = VerticalAlignment.Center }
            };
        }

        /// <summary>Platzhalter im Stil einer Datei, bis das echte Vorschaubild geladen ist.</summary>
        public static Border DateiPlatzhalter(string ext, double b, double h)
        {
            string e = ext.TrimStart('.').ToUpperInvariant();
            if (e.Length > 4) e = e.Substring(0, 4);
            return new Border
            {
                Width = b,
                Height = h,
                Background = Farbe.Endung(ext),
                CornerRadius = new CornerRadius(b * 0.13, b * 0.32, b * 0.13, b * 0.13),
                Child = new TextBlock
                {
                    Text = e,
                    FontSize = Math.Max(7, b * 0.22),
                    FontWeight = FontWeights.Bold,
                    Foreground = Farbe.AkzentText,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, h * 0.11)
                }
            };
        }

        public static Grid OrdnerBild(double b)
        {
            var g = new Grid { Width = b, Height = b * 0.85 };
            var hinten = new Path { Data = Geometry.Parse("M2 8a4 4 0 0 1 4-4h12l5 5h23a4 4 0 0 1 4 4v25a4 4 0 0 1-4 4H6a4 4 0 0 1-4-4z"), Fill = Farbe.B("#D9B45A"), Stretch = Stretch.Uniform };
            var vorne = new Path { Data = Geometry.Parse("M2 15h48v23a4 4 0 0 1-4 4H6a4 4 0 0 1-4-4z"), Fill = Farbe.B("#F0CF7E"), Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Bottom };
            vorne.Height = b * 0.85 * (27.0 / 38.0);
            g.Children.Add(hinten); g.Children.Add(vorne);
            return g;
        }

        // ---------- Bewegung ----------

        public static readonly IEasingFunction Weich = new CubicEase { EasingMode = EasingMode.EaseOut };

        public static void Einblenden(UIElement el, double vonY, int ms)
        {
            if (Vorschau.Sofort) return;   // Render-Modus: ohne Bewegung
            var tt = new TranslateTransform(0, vonY);
            el.RenderTransform = tt;
            el.Opacity = 0;
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Weich });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(vonY, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Weich });
        }

        public static void Ausblenden(UIElement el, int ms, Action danach)
        {
            var a = new DoubleAnimation(el.Opacity, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Weich };
            if (danach != null) a.Completed += delegate { danach(); };
            el.BeginAnimation(UIElement.OpacityProperty, a);
        }

        /// <summary>Kleines Menü, das unter einem Element aufklappt.</summary>
        public static Popup Menue(UIElement ziel, IList<KeyValuePair<string, Action>> eintraege) { return Menue(ziel, eintraege, false, false); }
        public static Popup Menue(UIElement ziel, IList<KeyValuePair<string, Action>> eintraege, bool amMauszeiger) { return Menue(ziel, eintraege, amMauszeiger, false); }

        public static Popup Menue(UIElement ziel, IList<KeyValuePair<string, Action>> eintraege, bool amMauszeiger, bool oben)
        {
            var pop = new Popup { PlacementTarget = ziel, Placement = amMauszeiger ? PlacementMode.MousePoint : (oben ? PlacementMode.Top : PlacementMode.Bottom), StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, VerticalOffset = amMauszeiger ? 0 : (oben ? -8 : 6) };
            var sp = new StackPanel();
            foreach (var kv in eintraege)
            {
                if (kv.Key == "-") { sp.Children.Add(new Border { Height = 1, Background = Farbe.Linie, Margin = new Thickness(6, 4, 6, 4) }); continue; }
                var b = new Button { Style = (Style)Application.Current.FindResource("BtnMenue"), Content = kv.Key };
                var aktion = kv.Value;
                b.Click += delegate { pop.IsOpen = false; aktion(); };
                sp.Children.Add(b);
            }
            pop.Child = new Border
            {
                Background = Farbe.B("#FF262A33"),
                BorderBrush = Farbe.B("#1FFFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                MinWidth = 180,
                Margin = new Thickness(0, 0, 12, 12),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.5, Color = Colors.Black },
                Child = sp
            };
            pop.IsOpen = true;
            return pop;
        }

        const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <Style x:Key='Btn' TargetType='Button'>
    <Setter Property='Foreground' Value='#ECEEF2'/>
    <Setter Property='Background' Value='#0DFFFFFF'/>
    <Setter Property='BorderBrush' Value='#1AFFFFFF'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='14,0'/>
    <Setter Property='Height' Value='36'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid>
            <Border x:Name='b' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8' SnapsToDevicePixels='True'/>
            <Border x:Name='h' Background='#14FFFFFF' CornerRadius='8' Opacity='0'/>
            <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='h' Property='Opacity' Value='1'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='h' Property='Opacity' Value='0.5'/></Trigger>
            <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='#F4B860'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='BtnPri' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Background' Value='#F4B860'/>
    <Setter Property='BorderBrush' Value='#F4B860'/>
    <Setter Property='Foreground' Value='#1C1405'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
  </Style>

  <Style x:Key='BtnGefahr' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Foreground' Value='#FF9C8F'/>
    <Setter Property='BorderBrush' Value='#4DFF9C8F'/>
  </Style>

  <Style x:Key='BtnIcon' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Width' Value='36'/>
    <Setter Property='Padding' Value='0'/>
  </Style>

  <Style x:Key='BtnFlach' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderBrush' Value='Transparent'/>
  </Style>

  <Style x:Key='BtnMenue' TargetType='Button' BasedOn='{StaticResource Btn}'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderBrush' Value='Transparent'/>
    <Setter Property='HorizontalContentAlignment' Value='Left'/>
    <Setter Property='Padding' Value='12,0'/>
    <Setter Property='Height' Value='36'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid Background='Transparent'>
            <Border Background='{TemplateBinding Background}' CornerRadius='8'/>
            <Border x:Name='h' Background='#14FFFFFF' CornerRadius='8' Opacity='0'/>
            <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Stretch' VerticalAlignment='Center'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='h' Property='Opacity' Value='1'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Leer' TargetType='Button'>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border Background='{TemplateBinding Background}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Rund' TargetType='Button'>
    <Setter Property='Width' Value='38'/>
    <Setter Property='Height' Value='38'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Grid>
            <Border Background='{TemplateBinding Background}' CornerRadius='19'/>
            <Border x:Name='h' Background='#1FFFFFFF' CornerRadius='19' Opacity='0'/>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='h' Property='Opacity' Value='1'/></Trigger>
            <Trigger Property='IsPressed' Value='True'><Setter TargetName='h' Property='Opacity' Value='0.6'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Fensterknopf' TargetType='Button'>
    <Setter Property='Width' Value='46'/>
    <Setter Property='Height' Value='44'/>
    <Setter Property='Foreground' Value='#C9CED8'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' Background='{TemplateBinding Background}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#14FFFFFF'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key='FensterknopfX' TargetType='Button' BasedOn='{StaticResource Fensterknopf}'>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' Background='Transparent'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#C42B1C'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Chip' TargetType='ToggleButton'>
    <Setter Property='Foreground' Value='#C9CED8'/>
    <Setter Property='Height' Value='32'/>
    <Setter Property='Padding' Value='12,0'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToggleButton'>
          <Border x:Name='b' Background='Transparent' BorderBrush='#1AFFFFFF' BorderThickness='1' CornerRadius='16' Padding='{TemplateBinding Padding}'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#0FFFFFFF'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='b' Property='Background' Value='#1FF4B860'/>
              <Setter TargetName='b' Property='BorderBrush' Value='#99F4B860'/>
              <Setter Property='Foreground' Value='#ECEEF2'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- Wahlknopf in der Download-Frage: groß, ein Klick genügt -->
  <Style x:Key='Wahl' TargetType='ToggleButton'>
    <Setter Property='Foreground' Value='#ECEEF2'/>
    <Setter Property='Height' Value='46'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='FontWeight' Value='Medium'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToggleButton'>
          <Grid>
            <Border x:Name='b' Background='#0AFFFFFF' BorderBrush='#1AFFFFFF' BorderThickness='1' CornerRadius='10'/>
            <Border x:Name='h' Background='#12FFFFFF' CornerRadius='10' Opacity='0'/>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='h' Property='Opacity' Value='1'/></Trigger>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='b' Property='Background' Value='#F4B860'/>
              <Setter TargetName='b' Property='BorderBrush' Value='#F4B860'/>
              <Setter Property='Foreground' Value='#1C1405'/>
              <Setter Property='FontWeight' Value='SemiBold'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Tag' TargetType='Button'>
    <Setter Property='Foreground' Value='#ECEEF2'/>
    <Setter Property='Height' Value='36'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='b' Background='{TemplateBinding Background}' CornerRadius='18'>
            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#14FFFFFF'/></Trigger>
            <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.3'/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Schalter' TargetType='CheckBox'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='CheckBox'>
          <Grid Width='44' Height='24' Background='Transparent'>
            <Border x:Name='bahn' CornerRadius='12' Background='#14FFFFFF' BorderBrush='#33FFFFFF' BorderThickness='1'/>
            <Ellipse x:Name='knopf' Width='14' Height='14' Fill='#C9CED8' HorizontalAlignment='Left' Margin='5,0,0,0'/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property='IsChecked' Value='True'>
              <Setter TargetName='bahn' Property='Background' Value='#F4B860'/>
              <Setter TargetName='bahn' Property='BorderBrush' Value='#F4B860'/>
              <Setter TargetName='knopf' Property='Fill' Value='#1C1405'/>
              <Setter TargetName='knopf' Property='HorizontalAlignment' Value='Right'/>
              <Setter TargetName='knopf' Property='Margin' Value='0,0,5,0'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key='Suchfeld' TargetType='TextBox'>
    <Setter Property='Foreground' Value='#ECEEF2'/>
    <Setter Property='CaretBrush' Value='#ECEEF2'/>
    <Setter Property='SelectionBrush' Value='#F4B860'/>
    <Setter Property='FontSize' Value='13'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='0'/>
    <Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='TextBox'>
          <ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='Center' Focusable='False' HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Hidden'/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='10'/>
    <Setter Property='MinWidth' Value='10'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Track x:Name='PART_Track' IsDirectionReversed='True'>
            <Track.Thumb>
              <Thumb>
                <Thumb.Template>
                  <ControlTemplate TargetType='Thumb'>
                    <Border x:Name='t' CornerRadius='3' Background='#26FFFFFF' Margin='3,2'/>
                    <ControlTemplate.Triggers>
                      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='t' Property='Background' Value='#40FFFFFF'/></Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType='ToolTip'>
    <Setter Property='Foreground' Value='#ECEEF2'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ToolTip'>
          <Border Background='#FF2A2E37' BorderBrush='#1FFFFFFF' BorderThickness='1' CornerRadius='6' Padding='8,5'>
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
</ResourceDictionary>";
    }
}
