using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SelectorDeFotos
{
    // Modo de prueba (--autotest): teclas y comandos simulados y captura de la ventana, sin tocar el teclado real.
    public partial class MainWindow
    {
        // Modo de prueba: ejecuta teclas sin tocar el teclado real y guarda una captura de la ventana.
        SlideshowWindow testSlide;
        // Medición de rendimiento (comando WaitPregen)
        DateTime perfStart = DateTime.MinValue;
        readonly List<double> perfNav = new List<double>(), perfLag = new List<double>();
        DispatcherTimer perfLagTimer;
        System.Diagnostics.Stopwatch perfLagWatch;
        System.Text.StringBuilder perfLog;

        void StartPerf(string outPng)
        {
            perfStart = DateTime.Now;
            perfLog = new System.Text.StringBuilder();
            perfLog.AppendLine("Inicio " + perfStart.ToString("HH:mm:ss") + "  fotos=" + all.Count + "  miniaturas en disco=" + (thumbCacheDir != null));
            // Retraso de la interfaz: un temporizador de 100 ms; lo que tarde de más es tiempo con la interfaz ocupada
            perfLagWatch = System.Diagnostics.Stopwatch.StartNew();
            perfLagTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(100) };
            double last = 0;
            int ticks = 0;
            perfLagTimer.Tick += (s, e) =>
            {
                double now = perfLagWatch.Elapsed.TotalMilliseconds;
                if (last > 0) perfLag.Add(Math.Max(0, now - last - 100));
                last = now;
                if (++ticks % 300 == 0) // cada 30 s
                    perfLog.AppendLine(string.Format("{0:0} s  miniaturas {1}/{2}  analizadas {3}", (DateTime.Now - perfStart).TotalSeconds, pregenDone, pregenTotal, analysis.Count));
            };
            perfLagTimer.Start();
        }

        void EndPerf(string outPng)
        {
            perfLagTimer.Stop();
            FlushAnalysis(true);
            Func<List<double>, double, double> pct = (l, p) => { if (l.Count == 0) return 0; var o = l.OrderBy(x => x).ToList(); return o[Math.Min(o.Count - 1, (int)(o.Count * p))]; };
            perfLog.AppendLine(string.Format("Fin tras {0:0.0} min  miniaturas {1}/{2}  analizadas {3}", (DateTime.Now - perfStart).TotalMinutes, pregenDone, pregenTotal, analysis.Count));
            perfLog.AppendLine(string.Format("Cambio de foto (ms) durante el proceso: n={0}  media {1:0.0}  p95 {2:0.0}  máx {3:0.0}", perfNav.Count, perfNav.Count > 0 ? perfNav.Average() : 0, pct(perfNav, 0.95), perfNav.Count > 0 ? perfNav.Max() : 0));
            perfLog.AppendLine(string.Format("Retraso de la interfaz (ms): media {0:0.0}  p95 {1:0.0}  p99 {2:0.0}  máx {3:0.0}", perfLag.Count > 0 ? perfLag.Average() : 0, pct(perfLag, 0.95), pct(perfLag, 0.99), perfLag.Count > 0 ? perfLag.Max() : 0));
            int groups = all.Where(x => x.Burst != null).Select(x => x.Burst).Distinct().Count();
            int similar = all.Where(x => x.Burst != null && x.BurstPos == 0).Count(x => (x.Burst.End - x.Burst.Start).TotalSeconds > BurstGapSeconds * (x.Burst.Items.Count - 1));
            perfLog.AppendLine("Ráfagas: " + groups + " (con fotos parecidas unidas: " + similar + ")  fotos en ráfagas: " + all.Count(x => x.Burst != null));
            File.WriteAllText(Path.ChangeExtension(outPng, null) + "_perf.txt", perfLog.ToString());
        }

        public void RunAutotest(string keys, string outPng)
        {
            var queue = new Queue<string>(keys.Split(','));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
            timer.Tick += (s, e) =>
            {
                timer.Interval = TimeSpan.FromMilliseconds(250);
                if (queue.Count > 0)
                {
                    string k = queue.Dequeue().Trim();
                    if (k == "WaitPregen")
                    {
                        // Rendimiento: espera a que terminen las miniaturas y el análisis; mientras, avanza una foto por
                        // paso y mide cuánto tarda cada cambio de foto y el retraso de la interfaz. Informe: <salida>_perf.txt
                        if (perfStart == DateTime.MinValue) StartPerf(outPng);
                        bool done = thumbCacheDir != null && pregenTotal > 0 && pregenDone >= pregenTotal;
                        if (!done && (DateTime.Now - perfStart).TotalMinutes < 40)
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            if (idx >= view.Count - 1) GoTo(0); else Go(1);
                            sw.Stop();
                            perfNav.Add(sw.Elapsed.TotalMilliseconds);
                            queue = new Queue<string>(new[] { k }.Concat(queue)); // seguir esperando
                            return;
                        }
                        EndPerf(outPng);
                        return;
                    }
                    if (k == "SortName") cmbOrden.SelectedIndex = 2;
                    else if (k == "HideRej") { chkRej.IsChecked = false; OnFilterChanged(); }
                    else if (k.StartsWith("Zoom")) ZoomCenter(double.Parse(k.Substring(4), System.Globalization.CultureInfo.InvariantCulture));
                    else if (k.StartsWith("Strip")) { stripSize = double.Parse(k.Substring(5)); ApplyStripSize(); OnStripResized(); }
                    else if (k == "Side") ToggleStripSide();
                    else if (k == "Cols") CycleColumns();
                    else if (k == "ColsBack") CycleColumns(-1);
                    else if (k.StartsWith("GoTo:")) GoToText(k.Substring(5));
                    else if (k.StartsWith("Import:")) ImportNames(k.Substring(7).Replace('+', ' '), false);
                    else if (k == "Info") ToggleInfo();
                    else if (k == "RetryRO") RetryWritable();
                    else if (k == "Grid") ToggleGrid();
                    else if (k == "Cache") ToggleThumbCache();
                    else if (k == "Reset") ResetPreferences(false);
                    else if (k == "SlidePlus" && testSlide != null) testSlide.TestKey(Key.Up);
                    else if (k.StartsWith("Drag")) { stripSize = Math.Max(StripMin, Math.Min(StripMax, double.Parse(k.Substring(4)))); ApplyStripSize(); OnStripResized(); }
                    else if (k == "Slide")
                    {
                        var favs = all.Where(x => x.Mark == Mark.Fav).ToList();
                        testSlide = new SlideshowWindow(favs, 0, 4, fullDim) { ShowActivated = false };
                        testSlide.WindowState = WindowState.Normal; testSlide.Left = -5000; testSlide.Top = 0; testSlide.Width = 1600; testSlide.Height = 1000;
                        testSlide.Show();
                    }
                    else if (k.StartsWith("CmpMode")) SetCompareMode(int.Parse(k.Substring(7)));
                    else if (k.StartsWith("Ui")) SetUiScale(int.Parse(k.Substring(2)));
                    else if (k == "CC") { OnCompareKey(); OnCompareKey(); }
                    else if (k == "CopyName" && Current != null) CopyToClipboard(Current.Name);
                    else if (k == "Dump")
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var sl in slots.Where(x => x.Item != null))
                        {
                            var clipPB = VisualTreeHelper.GetClip(sl.PhotoBox); var clipG = VisualTreeHelper.GetClip((Visual)sl.Root.Child); var clipR = VisualTreeHelper.GetClip(sl.Root);
                            sb.AppendLine(sl.Item.Name + " idx=" + sl.Index + " root=" + sl.Root.ActualWidth + "x" + sl.Root.ActualHeight + " img=" + sl.Img.ActualWidth + "x" + sl.Img.ActualHeight + " box=" + sl.PhotoBox.ActualWidth + "x" + sl.PhotoBox.ActualHeight + " clipBox=" + (clipPB == null ? "-" : clipPB.Bounds.ToString()) + " clipG=" + (clipG == null ? "-" : clipG.Bounds.ToString()) + " clipRoot=" + (clipR == null ? "-" : clipR.Bounds.ToString()) + " cur=" + sl.CurFrame.Visibility);
                        }
                        var ms = mainImage.Source as BitmapSource;
                        sb.AppendLine("MAIN source=" + (ms == null ? "null" : ms.PixelWidth + "x" + ms.PixelHeight) + " hiResItem=" + (hiResItem == null ? "-" : hiResItem.Name) + " hiResLoading=" + (hiResLoading == null ? "-" : hiResLoading.Name) + " cur=" + (Current == null ? "-" : Current.Name + " " + Current.Width + "x" + Current.Height) + " fullDim=" + fullDim + " zoom=" + zoom.Matrix.M11);
                        foreach (var pp in new[] { prevCmp, nextCmp }) { var ps = pp.Img.Source as BitmapSource; sb.AppendLine(pp.Label + " source=" + (ps == null ? "null" : ps.PixelWidth + "x" + ps.PixelHeight) + " item=" + (pp.Item == null ? "-" : pp.Item.Name)); }
                        File.WriteAllText(outPng + ".txt", sb.ToString());
                    }
                    else if (k == "Justify") { settings.CuadriculaJustificada = !settings.CuadriculaJustificada; OnStripResized(); LayoutStrip(); }
                    else if (k == "Undo") Undo();
                    else if (k == "Export") ExportList(false);
                    else if (k == "Compact") { settings.BarraCompacta = !settings.BarraCompacta; UpdateLabels(); }
                    else if (k.StartsWith("Goal")) SetGoal(int.Parse(k.Substring(4)));
                    else if (k == "CopyAll") StartCopy(true, true, true, false);
                    else if (k == "CopyFavSync") StartCopy(true, false, false, false, true);
                    else if (k.StartsWith("Scroll")) { double f = double.Parse(k.Substring(6), System.Globalization.CultureInfo.InvariantCulture); ScrollTo(sideStrip ? new Point(0, f * scrollBar.ActualHeight) : new Point(f * scrollBar.ActualWidth, 0)); }
                    else if (k == "Burst") ToggleBurst();
                    
                    
                    else if (k == "SlideCheck")
                    {
                        // Presentación: ↑ ↓ cambian el tiempo de 0,5 en 0,5; al salir, el visor va a la foto que se veía
                        var favs = all.Where(x => x.Mark == Mark.Fav).ToList();
                        var sb3 = new System.Text.StringBuilder();
                        var w = new SlideshowWindow(favs, 0, 4, fullDim) { ShowActivated = false };
                        w.WindowState = WindowState.Normal; w.Left = -5000; w.Top = 0; w.Width = 800; w.Height = 600;
                        w.Show();
                        sb3.AppendLine("inicio: " + w.Seconds + " s");
                        w.TestKey(Key.Up); sb3.AppendLine("tras ↑: " + w.Seconds + " s");
                        w.TestKey(Key.Up); sb3.AppendLine("tras ↑: " + w.Seconds + " s");
                        for (int n = 0; n < 12; n++) w.TestKey(Key.Down);
                        sb3.AppendLine("tras 12 × ↓: " + w.Seconds + " s (mínimo 0,5)");
                        w.TestKey(Key.Right); w.TestKey(Key.Right); w.TestKey(Key.Right);
                        var shown = w.CurrentItem;
                        sb3.AppendLine("foto en la presentación: " + (shown == null ? "-" : shown.Name) + "   actual en el visor antes: " + (Current == null ? "-" : Current.Name));
                        w.Close();
                        FocusNearest(shown);
                        sb3.AppendLine("actual en el visor al salir: " + (Current == null ? "-" : Current.Name) + (Current == shown ? "   OK" : "   DISTINTA"));
                        // Con «Favoritas» oculto, la más cercana que se vea
                        chkFav.IsChecked = false; OnFilterChanged();
                        FocusNearest(shown);
                        sb3.AppendLine("con favoritas ocultas: " + (Current == null ? "-" : Current.Name) + (view.Contains(Current) && Current != shown ? "   OK (la más cercana visible)" : "   REVISAR"));
                        chkFav.IsChecked = true; OnFilterChanged();
                        // Texto de ayuda abierto antes de empezar: se cierra
                        var tt = new ToolTip { Content = "IMG_prueba.jpg", PlacementTarget = strip, Placement = System.Windows.Controls.Primitives.PlacementMode.Center };
                        tt.IsOpen = true;
                        UpdateLayout();
                        bool wasOpen = tt.IsOpen;
                        CloseOpenToolTips();
                        sb3.AppendLine("texto de ayuda abierto: " + wasOpen + " → tras cerrar: " + tt.IsOpen + (wasOpen && !tt.IsOpen ? "   OK" : "   REVISAR"));
                        File.WriteAllText(Path.ChangeExtension(outPng, null) + "_slide.txt", sb3.ToString());
                    }
                    else if (k == "MiddleLoupe")
                    {
                        // Lupa con el botón de la rueda: pulsar → lupa; soltar → vuelve el zoom que había
                        var sb4 = new System.Text.StringBuilder();
                        sb4.AppendLine("antes: zoom " + zoom.Matrix.M11.ToString("0.00"));
                        center.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Middle) { RoutedEvent = Mouse.MouseDownEvent });
                        sb4.AppendLine("pulsado: lupa=" + loupeActive + " zoom " + zoom.Matrix.M11.ToString("0.00") + "  texto: " + zoomText.Text);
                        center.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Middle) { RoutedEvent = Mouse.MouseUpEvent });
                        sb4.AppendLine("soltado: lupa=" + loupeActive + " zoom " + zoom.Matrix.M11.ToString("0.00"));
                        File.WriteAllText(Path.ChangeExtension(outPng, null) + "_loupe.txt", sb4.ToString());
                    }
                    else if (k.StartsWith("BurstWheel") && burstMode && burstTileList.Count > 0)
                    {
                        // Rueda sobre la primera foto de la ráfaga (BurstWheel3 = 3 pasos)
                        int steps = k.Length > 10 ? int.Parse(k.Substring(10)) : 1;
                        for (int n = 0; n < steps; n++)
                            burstTileList[0].Root.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = UIElement.MouseWheelEvent });
                    }
                    else if (k == "BurstFrames" && burstMode)
                    {
                        // Comprueba, en la vista de ráfaga ya cargada, que cada capa de marcos está sobre su foto
                        var sb5 = new System.Text.StringBuilder();
                        UpdateLayout();
                        sb5.AppendLine("zoom " + burstZoom.Matrix.M11.ToString("0.00") + "  escala interfaz " + settings.EscalaInterfaz + " %");
                        foreach (var t in burstTileList)
                        {
                            var img = t.Img.TransformToAncestor(t.Root).TransformBounds(new Rect(0, 0, t.Img.ActualWidth, t.Img.ActualHeight));
                            img.Intersect(new Rect(0, 0, t.Clip.ActualWidth, t.Clip.ActualHeight));
                            var box = t.PhotoBox.TransformToAncestor(t.Root).TransformBounds(new Rect(0, 0, t.PhotoBox.ActualWidth, t.PhotoBox.ActualHeight));
                            bool ok = t.PhotoBox.IsVisible && Math.Abs(img.X - box.X) < 1.5 && Math.Abs(img.Y - box.Y) < 1.5
                                      && Math.Abs(img.Width - box.Width) < 1.5 && Math.Abs(img.Height - box.Height) < 1.5;
                            sb5.AppendLine(string.Format("{0} {1} foto=({2:0},{3:0} {4:0}x{5:0}) marcos=({6:0},{7:0} {8:0}x{9:0}) {10}",
                                t.Item.Name, t.Item == Current ? "[actual]" : "        ", img.X, img.Y, img.Width, img.Height, box.X, box.Y, box.Width, box.Height, ok ? "OK" : "DESCOLOCADO"));
                        }
                        File.AppendAllText(Path.ChangeExtension(outPng, null) + "_frames.txt", sb5.ToString() + "\n");
                    }
                    else if (k == "PlaceLeft") SetStripPlace(true, true);
                    else if (k == "PlaceRight") SetStripPlace(true, false);
                    else if (k == "PlaceTop") SetStripPlace(false, true);
                    else if (k == "PlaceBottom") SetStripPlace(false, false);
                    else if (k == "BarStyleTest")
                    {
                        // Muestra una lista con barra de desplazamiento sobre el visor (para ver el estilo)
                        var sp = new StackPanel();
                        for (int n = 0; n < 40; n++) sp.Children.Add(new TextBlock { Text = "Línea de prueba " + n, Foreground = Brushes.White, FontSize = 14 });
                        var sv = new ScrollViewer { Content = sp, Height = 260, Width = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Background = B("#2B2B2B") };
                        Panel.SetZIndex(sv, 200);
                        Grid.SetColumnSpan(sv, 3);
                        center.Children.Add(sv);
                    }
                    else if (k == "AutoAdv") ToggleAutoAdvance();
                    else if (k == "Loupe") StartLoupe();
                    else if (k == "Flush") FlushAnalysis(true);
                    else if (k == "AnalysisDump")
                    {
                        var sb2 = new System.Text.StringBuilder();
                        foreach (var x in all.OrderBy(x => x.SortDate))
                            sb2.AppendLine(x.Name + " " + x.SortDate.ToString("HH:mm:ss") + " nit=" + x.Sharpness.ToString("0") + " hash=" + (x.HasHash ? x.Hash.ToString("x16") : "-")
                                + " grupo=" + (x.Burst == null ? "-" : (x.BurstPos + 1) + "/" + x.Burst.Items.Count));
                        File.WriteAllText(Path.ChangeExtension(outPng, null) + "_analisis.txt", sb2.ToString());
                    }
                    else if (k == "Free" || k == "Centered") SetCenteredThumb(k == "Centered");
                    else if (k == "WheelDown" || k == "WheelUp")
                        OnWheel(strip, new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, k == "WheelUp" ? 120 : -120) { RoutedEvent = UIElement.MouseWheelEvent });
                    else if (k.StartsWith("BurstClick:") && burstMode)
                    {
                        // Clic simulado (pulsar y soltar) sobre la foto n (1 = primera) de la vista de ráfaga
                        int n = int.Parse(k.Substring(11)) - 1;
                        if (n >= 0 && n < burstTileList.Count)
                        {
                            var c = burstTileList[n].Clip;
                            c.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                            c.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                        }
                    }
                    else if (k.StartsWith("BurstCheck"))
                    {
                        // Diagnóstico: recorre las ráfagas del tamaño indicado (BurstCheck9) y anota, para cada foto,
                        // el estado del borde de la foto actual en la vista de ráfaga. Salida: <salida>_burst.txt
                        int size = int.Parse(k.Substring(10));
                        var sb = new System.Text.StringBuilder();
                        var groups = all.Where(x => x.Burst != null && x.BurstPos == 0 && x.Burst.Items.Count == size).Select(x => x.Burst).Take(6).ToList();
                        sb.AppendLine("rafagas de " + size + ": " + groups.Count + "  modo=" + settings.ModoRafaga + " borde=" + settings.BordeFoco);
                        foreach (var g in groups)
                            foreach (var it in g.Items)
                            {
                                GoToItem(it);
                                UpdateLayout();
                                var t = burstTileList.FirstOrDefault(x => x.Item == Current);
                                if (t == null) { sb.AppendLine(it.Name + "  SIN RECUADRO  burstMode=" + burstMode + " cur=" + (Current == null ? "-" : Current.Name)); continue; }
                                sb.AppendLine(string.Format("{0} pos={1} cur={2} brush={3} frameVis={4} frame={5:0}x{6:0} box={7} {8:0}x{9:0} @({10:0},{11:0}) img={12:0}x{13:0} clip={14:0}x{15:0} root={16:0}x{17:0}",
                                    it.Name, it.BurstPos, Current.Name,
                                    t.Frame.BorderBrush == null ? "null" : t.Frame.BorderBrush is SolidColorBrush ? ((SolidColorBrush)t.Frame.BorderBrush).Color.ToString() : t.Frame.BorderBrush.GetType().Name,
                                    t.Frame.Visibility, t.Frame.ActualWidth, t.Frame.ActualHeight,
                                    t.PhotoBox.Visibility, t.PhotoBox.ActualWidth, t.PhotoBox.ActualHeight, t.PhotoBox.Margin.Left, t.PhotoBox.Margin.Top,
                                    t.Img.ActualWidth, t.Img.ActualHeight, t.Clip.ActualWidth, t.Clip.ActualHeight, t.Root.ActualWidth, t.Root.ActualHeight));
                            }
                        File.WriteAllText(Path.ChangeExtension(outPng, null) + "_burst.txt", sb.ToString());
                    }
                    else if (k.StartsWith("Bar") || k.StartsWith("Ctx"))
                    {
                        // Barra de marcar: BarMain / BarNext / BarTile2 (mostrarla), BarFavNext… (clic en Favorita),
                        // BarRejTile2… (clic en No deseada). Ctx…: el clic derecho se hizo sobre esa foto (luego PhotoMenu).
                        string what = k.StartsWith("BarFav") || k.StartsWith("BarRej") ? k.Substring(6) : k.Substring(3);
                        MarkBarUi bar = what == "Main" ? mainMarkBar
                            : what == "Next" ? markBars.FirstOrDefault(b => b.Area == nextCmp.Pane)
                            : what == "Prev" ? markBars.FirstOrDefault(b => b.Area == prevCmp.Pane)
                            : what.StartsWith("Tile") ? markBars.Where(b => b.InBurst).ElementAtOrDefault(int.Parse(what.Substring(4))) : null;
                        if (bar != null)
                        {
                            if (k.StartsWith("Ctx")) contextTarget = bar.Item();
                            else
                            {
                                ShowMarkBar(bar, true);
                                var b2 = k.StartsWith("BarFav") ? bar.Fav : k.StartsWith("BarRej") ? bar.Rej : null;
                                if (b2 != null) b2.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                            }
                        }
                    }
                    else if (k == "PhotoMenu" || k == "ThumbMenu" || k.StartsWith("OptMenu"))
                    {
                        // Dibuja un menú en <salida>_menu.png: clic derecho del visor, de las miniaturas, o ⚙
                        // (OptMenu = el menú; OptMenu3 = el submenú del elemento 3, sin contar separadores)
                        ContextMenu cm;
                        if (k.StartsWith("OptMenu"))
                        {
                            cm = BuildOptionsMenu();
                            if (k.Length > 7)
                            {
                                // OptMenu1 = submenú 1; OptMenu1.0 = submenú 0 dentro del submenú 1
                                var path = k.Substring(7).Split('.').Select(int.Parse).ToList();
                                var sub = cm.Items.OfType<MenuItem>().ElementAt(path[0]);
                                for (int pi = 1; pi < path.Count; pi++) sub = sub.Items.OfType<MenuItem>().ElementAt(path[pi]);
                                var items = sub.Items.Cast<object>().ToList();
                                sub.Items.Clear();
                                cm = new ContextMenu();
                                foreach (var x in items) cm.Items.Add(x);
                            }
                        }
                        else cm = k == "PhotoMenu" ? center.ContextMenu : strip.ContextMenu;
                        cm.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, cm));
                        cm.ApplyTemplate();
                        cm.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        cm.Arrange(new Rect(cm.DesiredSize));
                        cm.UpdateLayout();
                        var mrtb = new RenderTargetBitmap((int)Math.Ceiling(cm.ActualWidth), (int)Math.Ceiling(cm.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                        mrtb.Render(cm);
                        var menc = new PngBitmapEncoder();
                        menc.Frames.Add(BitmapFrame.Create(mrtb));
                        using (var mfs = File.Create(Path.ChangeExtension(outPng, null) + "_menu.png")) menc.Save(mfs);
                    }
                    else if (k.StartsWith("Hover"))
                    {
                        // Hover / HoverFav / HoverRej: ratón simulado sobre la miniatura siguiente a la actual
                        // (con la vista de ráfaga abierta, sobre la foto siguiente de la ráfaga)
                        hoverForced = slots.FirstOrDefault(x => x.Item != null && x.Index == idx + 1);
                        if (hoverForced != null)
                        {
                            var btn = k == "HoverFav" ? hoverForced.HoverFav : k == "HoverRej" ? hoverForced.HoverRej : null;
                            if (btn != null) btn.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                            UpdateHoverMarks(hoverForced);
                        }
                    }
                    else if (k == "GridDblTest" && gridMode)
                    {
                        // Simula un doble clic sobre una foto de otra fila: el primer clic mueve la cuadrícula y el
                        // segundo cae sobre otra miniatura (en el mismo punto de la pantalla)
                        Func<Slot, RoutedEvent, bool> click = (sl, ev) =>
                        {
                            sl.Root.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = ev });
                            return true;
                        };
                        var a = slots.FirstOrDefault(x => x.Item != null && x.Index >= idx + Cols);
                        if (a != null)
                        {
                            var wanted = a.Item;
                            click(a, UIElement.MouseLeftButtonDownEvent);
                            click(a, UIElement.MouseLeftButtonUpEvent);
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                var b2 = slots.FirstOrDefault(x => x.Item != null && x.Item != wanted);
                                click(b2, UIElement.MouseLeftButtonDownEvent);
                                click(b2, UIElement.MouseLeftButtonUpEvent);
                                Toast("TEST doble clic: quería " + wanted.Name + " · abierta " + (Current == null ? "-" : Current.Name) + " · cuadrícula " + (gridMode ? "sí" : "no"));
                            }), DispatcherPriority.Background);
                        }
                    }
                    else if (k.StartsWith("Focus")) SetFocusStyle(int.Parse(k.Substring(5)));
                    else if (k == "BurstZoom" && burstTileList.Count > 0)
                    {
                        var c0 = burstTileList[0].Clip;
                        var m = Matrix.Identity; m.ScaleAt(2.5, 2.5, c0.ActualWidth * 0.3, c0.ActualHeight * 0.7);
                        burstZoom.Matrix = m;
                    }
                    else if (k.StartsWith("Win") && k.Contains("x"))
                    {
                        // Tamaño de la ventana de prueba: Win1366x768
                        var wh = k.Substring(3).Split('x');
                        Width = double.Parse(wh[0]); Height = double.Parse(wh[1]);
                    }
                    else if (k == "ErrTest") LogError(new InvalidOperationException("Error de prueba del modo autotest"), "prueba");
                    else if (k == "HelpShot" || k == "AboutShot")
                    {
                        // Dibuja la ventana de atajos (o «Acerca de») en <salida>_help.png
                        Window hw = k == "HelpShot" ? (Window)new HelpWindow(this) : new AboutDialog(this);
                        hw.ShowActivated = false; hw.WindowStartupLocation = WindowStartupLocation.Manual; hw.Left = -5000; hw.Top = 0;
                        hw.Show();
                        hw.UpdateLayout();
                        var el = (FrameworkElement)hw.Content;
                        var hb = new RenderTargetBitmap((int)Math.Ceiling(el.ActualWidth), (int)Math.Ceiling(el.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            dc.DrawRectangle(hw.Background, null, new Rect(0, 0, el.ActualWidth, el.ActualHeight));
                            dc.DrawRectangle(new VisualBrush(el), null, new Rect(0, 0, el.ActualWidth, el.ActualHeight));
                        }
                        hb.Render(dv);
                        var he = new PngBitmapEncoder();
                        he.Frames.Add(BitmapFrame.Create(hb));
                        using (var hf = File.Create(Path.ChangeExtension(outPng, null) + "_help.png")) he.Save(hf);
                        hw.Close();
                    }
                    else if (k == "BurstPanUp" && burstTileList.Count > 0)
                    {
                        // Arrastre exagerado hacia arriba (como al mover mucho la foto ampliada): debe quedar en su sitio
                        var m = burstZoom.Matrix; m.Translate(-3000, -3000);
                        burstZoom.Matrix = ClampBurstZoom(m, burstTileList[0]);
                    }
                    else if (k == "KeepThis" && burstMode) KeepOnlyThis(BurstMembers(openBurst));
                    else if (k.Length > 0)
                    {
                        var key = (Key)Enum.Parse(typeof(Key), k);
                        if (!(burstMode && HandleBurstKey(key))) HandleKey(key);
                    }
                    return;
                }
                timer.Stop();
                var shot = testSlide != null ? (Window)testSlide : this;
                var rtb = new RenderTargetBitmap((int)shot.ActualWidth, (int)shot.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render((Visual)shot.Content);
                if (testSlide != null) { File.WriteAllText(outPng + ".txt", testSlide.DebugState); testSlide.Close(); }
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = File.Create(outPng)) enc.Save(fs);
                Close();
            };
            timer.Start();
        }
    }
}
