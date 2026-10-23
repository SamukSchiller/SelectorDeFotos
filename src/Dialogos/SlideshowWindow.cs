using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SelectorDeFotos
{
    // Presentación a pantalla completa con fundido entre fotos.
    public class SlideshowWindow : Window
    {
        readonly List<PhotoItem> items;
        readonly int dim;
        readonly Image imgA, imgB;
        bool showingA;
        int index;
        public double Seconds; // de 0,5 en 0,5
        public PhotoItem CurrentItem { get { return items.Count > 0 ? items[index] : null; } } // la que se está viendo
        bool paused;
        readonly DispatcherTimer timer, infoTimer, cursorTimer;
        readonly Border infoBox, posBox;
        readonly TextBlock infoText, posText;
        readonly DispatcherTimer posTimer;
        readonly Dictionary<PhotoItem, BitmapSource> cache = new Dictionary<PhotoItem, BitmapSource>();
        readonly HashSet<PhotoItem> loading = new HashSet<PhotoItem>();
        int wanted = -1;
        DateTime helpUntil = DateTime.MinValue; // hasta cuándo se muestran los atajos

        public SlideshowWindow(List<PhotoItem> items, int start, double seconds, int dim)
        {
            this.items = items;
            this.dim = dim;
            Seconds = seconds;
            index = start;
            Icon = MainWindow.AppIcon();
            Theme.DarkDialog(this);
            Title = "Presentación de favoritas";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            Background = Brushes.Black;
            ShowInTaskbar = false;

            var g = new Grid { Background = Brushes.Black };
            imgA = MakeImage();
            imgB = MakeImage();
            g.Children.Add(imgA);
            g.Children.Add(imgB);
            infoText = new TextBlock { Foreground = Brushes.White, FontSize = 15, TextAlignment = TextAlignment.Center };
            infoBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 8, 16, 8), Child = infoText,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 30), IsHitTestVisible = false
            };
            Panel.SetZIndex(infoBox, 10);
            g.Children.Add(infoBox);
            // Posición («3 / 57») en la esquina inferior derecha, con el mismo margen abajo y a la derecha
            posText = new TextBlock { Foreground = Brushes.White, FontSize = 14 };
            posBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 4, 10, 5), Child = posText,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 24, 24), IsHitTestVisible = false
            };
            Panel.SetZIndex(posBox, 10);
            g.Children.Add(posBox);
            Content = g;

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Seconds) };
            timer.Tick += (s, e) => Next(1);
            infoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            infoTimer.Tick += (s, e) => { infoTimer.Stop(); if (!paused) FadeInfo(0); };
            posTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            posTimer.Tick += (s, e) => { posTimer.Stop(); if (!paused) posBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))); };
            cursorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            cursorTimer.Tick += (s, e) => { cursorTimer.Stop(); Cursor = Cursors.None; };

            KeyDown += OnKey;
            MouseLeftButtonUp += (s, e) => Next(1);
            MouseRightButtonUp += (s, e) => Next(-1);
            MouseWheel += (s, e) => Next(e.Delta > 0 ? -1 : 1);
            MouseMove += (s, e) => { Cursor = null; cursorTimer.Stop(); cursorTimer.Start(); };
            Loaded += (s, e) =>
            {
                helpUntil = DateTime.Now.AddSeconds(3);
                Show(index, null);
                timer.Start();
                cursorTimer.Start();
            };
            Closed += (s, e) => { timer.Stop(); infoTimer.Stop(); posTimer.Stop(); cursorTimer.Stop(); };
        }

        static Image MakeImage()
        {
            var img = new Image { Stretch = Stretch.Uniform, Opacity = 0 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape: case Key.P: Close(); break;
                case Key.Space:
                    paused = !paused;
                    if (paused) timer.Stop(); else { timer.Stop(); timer.Start(); }
                    ShowInfo(paused ? "⏸  Pausa" : "▶  Reanudada (" + SecondsText + " s por foto)");
                    break;
                case Key.Right: case Key.PageDown: Next(1); break;
                case Key.Left: case Key.PageUp: Next(-1); break;
                case Key.Up: ChangeSpeed(0.5); break;    // más tiempo por foto
                case Key.Down: ChangeSpeed(-0.5); break; // menos tiempo por foto
                case Key.H: case Key.OemQuestion:
                    helpUntil = DateTime.Now.AddSeconds(3);
                    ShowPosition(index);
                    ShowHelp();
                    break;
                default: return;
            }
            e.Handled = true;
        }

        string SecondsText { get { return Seconds.ToString("0.#"); } }

        void ChangeSpeed(double delta)
        {
            Seconds = Math.Max(0.5, Math.Min(30, Math.Round((Seconds + delta) * 2) / 2));
            timer.Interval = TimeSpan.FromSeconds(Seconds);
            if (!paused) { timer.Stop(); timer.Start(); }
            ShowInfo("Velocidad: " + SecondsText + " s por foto" + (Seconds <= 0.5 || Seconds >= 30 ? "  (límite)" : ""));
        }

        void Next(int dir)
        {
            index = ((index + dir) % items.Count + items.Count) % items.Count;
            if (!paused) { timer.Stop(); timer.Start(); }
            Show(index, null);
        }

        void Show(int i, string extra)
        {
            wanted = i;
            var it = items[i];
            BitmapSource b;
            if (cache.TryGetValue(it, out b)) Display(b);
            else Load(it);
            ShowPosition(i);
            if (extra != null) ShowInfo(extra);
            else if (DateTime.Now < helpUntil) ShowHelp();
            // Precargar la siguiente y la anterior; liberar las demás
            var keep = new HashSet<PhotoItem> { it, items[(i + 1) % items.Count], items[(i - 1 + items.Count) % items.Count] };
            foreach (var k in cache.Keys.ToList()) if (!keep.Contains(k)) cache.Remove(k);
            Load(items[(i + 1) % items.Count]);
        }

        void Load(PhotoItem it)
        {
            if (cache.ContainsKey(it) || loading.Contains(it)) return;
            loading.Add(it);
            var t = new Thread(() =>
            {
                BitmapSource b = null;
                try { b = ImageLoader.Load(it, dim); } catch { }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    loading.Remove(it);
                    if (b == null) return;
                    cache[it] = b;
                    if (wanted >= 0 && items[wanted] == it) Display(b);
                }));
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        // Fundido cruzado entre las dos capas
        void Display(BitmapSource b)
        {
            var incoming = showingA ? imgB : imgA;
            var outgoing = showingA ? imgA : imgB;
            if (outgoing.Source == b) return;
            incoming.Source = b;
            Panel.SetZIndex(incoming, 1);
            Panel.SetZIndex(outgoing, 0);
            // Fundido de 0,6 s, más corto si cada foto dura menos (con 0,5 s por foto: 0,3 s)
            var fade = TimeSpan.FromMilliseconds(Math.Min(600, Seconds * 600));
            incoming.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, fade));
            outgoing.BeginAnimation(OpacityProperty, new DoubleAnimation(outgoing.Opacity, 0, fade));
            showingA = !showingA;
        }

        public string DebugState { get { return "posicion=" + posText.Text + " (opacidad " + posBox.Opacity.ToString("0.00") + ")  texto=" + infoText.Text.Replace("\n", " | ") + "  opacidad=" + infoBox.Opacity.ToString("0.00") + "  intervalo=" + infoTimer.Interval.TotalSeconds + "s activo=" + infoTimer.IsEnabled; } }

        public void TestKey(Key k) { var ev = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, k) { RoutedEvent = Keyboard.KeyDownEvent }; OnKey(this, ev); }

        // Atajos: cada tecla en un recuadro y su acción al lado (sin dos puntos ni separadores)
        void ShowHelp()
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            AddKey(p, "Espacio", "pausar");
            AddKey(p, "← →", "anterior / siguiente");
            AddKey(p, "↑ ↓", "tiempo por foto (" + SecondsText + " s)");
            AddKey(p, "H", "ayuda");
            AddKey(p, "Esc", "salir");
            ((FrameworkElement)p.Children[p.Children.Count - 1]).Margin = new Thickness(0);
            infoText.Text = "(atajos)";
            infoBox.Child = p;
            FadeInfo(1);
            infoTimer.Stop();
            infoTimer.Interval = TimeSpan.FromSeconds(Math.Max(2.5, (helpUntil - DateTime.Now).TotalSeconds));
            infoTimer.Start();
        }

        static void AddKey(StackPanel p, string key, string action)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 22, 0) };
            item.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x48, 0x48, 0x48)), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 1, 7, 2), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = key, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, FontSize = 14 }
            });
            item.Children.Add(new TextBlock
            {
                Text = action, Foreground = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)), FontSize = 14,
                Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            });
            p.Children.Add(item);
        }

        void ShowPosition(int i)
        {
            posText.Text = (i + 1) + " / " + items.Count;
            posBox.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
            posTimer.Stop();
            posTimer.Start();
        }

        void ShowInfo(string text)
        {
            infoText.Text = text;
            infoBox.Child = infoText;
            FadeInfo(1);
            infoTimer.Stop();
            double remaining = (helpUntil - DateTime.Now).TotalSeconds;
            infoTimer.Interval = TimeSpan.FromSeconds(Math.Max(2.5, remaining));
            infoTimer.Start();
        }

        void FadeInfo(double to)
        {
            infoBox.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(300)));
        }
    }
}
