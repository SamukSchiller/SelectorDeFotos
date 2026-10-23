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
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SelectorDeFotos
{
    public partial class MainWindow : Window
    {
        const string DataFileName = "SelectorDeFotos.marcas.json";
        const double SlotGap = 14; // hueco entre miniaturas (ahí va el borde azul de la actual)
        // Margen dentro de cada casilla alrededor de la foto, donde se dibujan los marcos exteriores (azul/verde).
        // Sin él, Windows puede recortar la casilla justo en el borde de la foto y los marcos no se verían.
        const double RingPad = 6;
        // Tira de abajo: arriba solo el espacio del borde exterior; abajo también el de la barra de desplazamiento
        const double BottomPadTop = 7, BottomPadBottom = 18;
        bool sideStrip; // miniaturas en el lateral derecho en vez de abajo
        double stripSize = 128; // alto (abajo) o ancho (lateral) de la tira
        double StripMin { get { return sideStrip ? 110 : 70; } }
        // Hasta el 50 % de la ventana (ancho en el lateral, alto abajo)
        double StripMax
        {
            get
            {
                double total = uiRoot == null ? 0 : (sideStrip ? uiRoot.ActualWidth : uiRoot.ActualHeight);
                return total > 0 ? Math.Max(StripMin + 1, Math.Floor(total * 0.5)) : 5000;
            }
        }
        int Cols { get { return gridMode ? GridCols : sideStrip ? Math.Max(1, Math.Min(3, settings.ColumnasMiniaturas)) : 1; } }
        int Rows { get { return sideStrip ? 1 : Math.Max(1, Math.Min(3, settings.FilasMiniaturas)); } }
        // Carriles de la tira: columnas en el lateral, filas abajo
        int Lanes { get { return sideStrip ? Cols : Rows; } }
        double SlotW { get { return sideStrip ? Math.Floor((stripSize - 20 - (Cols - 1) * SlotGap) / Cols) : Math.Round(SlotH * 1.39); } }
        double SlotH { get { return sideStrip ? Math.Round(SlotW / 1.39) : Math.Floor((stripSize - BottomPadTop - BottomPadBottom - (Rows - 1) * SlotGap) / Rows); } }
        // Lateral con cuadrícula justificada (opción en ⚙, activada por defecto)
        // Cuadrícula justificada: en el lateral y en la tira de abajo cuando tiene 1 sola fila
        // (con 2 o 3 filas abajo, siempre casillas iguales con la actual en el centro).
        bool Justified { get { return settings.CuadriculaJustificada && (sideStrip || Rows == 1); } }
        Thumb splitter;
        Button btnSide, btnCols;
        Grid stripHost, scrollBar;
        Border scrollTrack, scrollThumb;
        bool scrollDragging, scrollbarRetryPending;
        const int ThumbCacheMax = 600;

        static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff",
            ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp"
        };

        static readonly Brush BgBrush = B("#1B1B1B");
        static readonly Brush PanelBrush = B("#2A2A2A");
        static readonly Brush StripBrush = B("#141414");
        static readonly Brush AccentBrush = B("#4FA3FF");
        static readonly Brush FocusBrush = Brushes.White; // borde de la foto con foco (actual): tira, comparar y ráfaga
        static readonly Brush FavBrush = B("#2EAD4B");
        static readonly Brush RejBrush = B("#D9443A");
        static readonly Brush TextBrush = B("#EDEDED");
        static readonly Brush DimTextBrush = B("#9A9A9A");
        static readonly Brush ScrollBrush = B("#80FFFFFF");
        static readonly Brush StatusFavBrush = B("#00C853"); // verde vivo para la barra inferior
        static readonly Brush StatusDarkText = B("#06240F");
        static readonly Brush ScrollActiveBrush = B("#C8FFFFFF");
        static readonly Brush MomentBrush = B("#FFB43C");
        static readonly Brush CompareBrush = B("#8A929C"); // gris: solo la actual lleva el azul

        // Estado
        Settings settings;
        string settingsPath;
        string dataFile;
        DataFile data;
        FolderData folderData;
        bool dirty;
        List<PhotoItem> all = new List<PhotoItem>();
        List<PhotoItem> view = new List<PhotoItem>();
        PhotoItem gridClickItem; // foto del último clic en una miniatura (para el doble clic en la cuadrícula)
        int gridClickTime;
        Point gridClickPos;      // dónde fue ese clic (en la ventana)
        bool gridSuppressUp;
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetDoubleClickTime();
        int idx = -1;
        int lastDir = 1;
        PhotoItem sticky; // foto marcada que sigue visible hasta avanzar
        bool startPending; // carpeta nueva sin tocar: al terminar de leer fechas, ir a la primera
        volatile int loadGen;
        int metaTotal, metaDone;
        bool copying;
        bool initializing = true;
        bool fullscreen;
        WindowState prevState;
        int fullDim = 2560;
        volatile int thumbDim = 240;
        double dpiScale = 1;

        // Zoom
        Grid center;
        MatrixTransform zoom = new MatrixTransform();
        TextBlock zoomText;
        bool dragging;
        Point dragStart;
        PhotoItem lastShown, hiResItem, hiResLoading;

        // Carga de imágenes
        readonly ConcurrentDictionary<string, BitmapSource> thumbs = new ConcurrentDictionary<string, BitmapSource>();
        readonly ConcurrentDictionary<string, BitmapSource> fulls = new ConcurrentDictionary<string, BitmapSource>();
        LoadQueue thumbQueue, fullQueue;

        // Interfaz
        Border toolbar;
        StackPanel statusParts;
        Brush statusFgBrush = Brushes.White;
        TextBlock lblFotos, lblDatos, progressText, emptyText, goalText, toastText, countsText, favPosText;
        Border helpText; // atajos de la barra superior
        Border statusBar, goalPill, readOnlyPill, toast;
        System.Windows.Shapes.Path markBadge;
        System.Windows.Shapes.Rectangle mainMosaic; // mosaico de la marca en el fondo del visor
        Image mainImage;
        Canvas strip;
        ComboBox cmbOrden;
        Button btnDir, btnCopiar, btnFotos, btnDatos;
        DispatcherTimer toastTimer;

        // Comparar (foto actual a la izquierda, la siguiente a la derecha)
        DockPanel uiRoot;
        double UiScale { get { return Math.Max(50, Math.Min(200, settings.EscalaInterfaz)) / 100.0; } }
        Grid mainPane;
        TextBlock mainName;
        Border mainFrame;
        System.Windows.Shapes.Rectangle mainDash;
        ComparePane prevCmp, nextCmp;

        // Panel de comparación (foto anterior o siguiente a la actual)
        class ComparePane
        {
            public int Offset;          // -1 anterior, +1 siguiente
            public string Label;
            public ColumnDefinition Col;
            public Grid Pane;
            public Image Img;
            public TextBlock Empty, Name;
            public System.Windows.Shapes.Path Badge;
            public System.Windows.Shapes.Rectangle Mosaic; // mosaico de la marca en el fondo del panel
            public PhotoItem Item;
            public PhotoItem HiResItem, HiResLoading; // foto ya cargada / cargándose a resolución completa
            public System.Windows.Shapes.Path Anim;  // animación de la marca en este panel (se crea al usarla)
        }

        // ⚙ → «Comparar con»: 0 = la siguiente, 1 = la anterior, 2 = la anterior y la siguiente
        bool CompareShowsNext { get { return settings.Comparar && settings.ModoComparar != 1; } }
        bool CompareShowsPrev { get { return settings.Comparar && settings.ModoComparar != 0; } }

        // Deshacer: foto y marca anterior
        // Deshacer: cada entrada es un grupo de (foto, marca anterior); una importación es un solo grupo.
        readonly List<List<KeyValuePair<PhotoItem, Mark>>> undoStack = new List<List<KeyValuePair<PhotoItem, Mark>>>();
        const int MomentSeconds = 30; // separación de «momentos» en la tira
        CheckBox chkFav, chkNone, chkRej;
        readonly List<Slot> slots = new List<Slot>();
        DispatcherTimer saveTimer, progressTimer;

        class Slot
        {
            public int Row, Col, Index;
            public Border Root;
            public Image Img;
            public System.Windows.Shapes.Path Badge;
            public TextBlock ErrText;
            public Border FavFrame;
            public Border GapMark;
            public TextBlock GapText;
            public Grid HoverBox;       // botones ✓ / ✕ al pasar el ratón (ver HoverMarks.cs)
            public Border HoverFav, HoverRej;
            public Border BurstMark;    // «2/5» si la foto es parte de una ráfaga
            public TextBlock BurstText;
            public Grid PhotoBox;
            public Border CurFrame; // borde de la actual / comparada, dibujado encima (no cambia el tamaño)
            public System.Windows.Shapes.Rectangle CurDash; // estilo «líneas que avanzan» (ver FocusRing.cs)
            public PhotoItem Item;
        }

        public MainWindow()
        {
            Title = "Selector de Fotos";
            Width = 1400;
            Height = 900;
            WindowState = WindowState.Maximized;
            Background = BgBrush;
            UseLayoutRounding = true;

            Icon = AppIcon();
            Theme.DarkTitleBar(this);
            LoadSettings();
            if (!Autotest) RestoreWindowPlacement();
            BuildUi();

            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            saveTimer.Tick += (s, e) => SaveNow();
            progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            progressTimer.Tick += (s, e) => UpdateProgress();

            thumbQueue = new LoadQueue(2, ThreadPriority.BelowNormal, thumbs,
                it => LoadThumb(it), // desde disco si la opción «Guardar miniaturas» está activa
                it => Dispatcher.BeginInvoke(new Action(() => OnThumbReady(it))));
            fullQueue = new LoadQueue(1, ThreadPriority.Normal, fulls,
                it => ImageLoader.Load(it, fullDim),
                it => Dispatcher.BeginInvoke(new Action(() => OnFullReady(it))));

            PreviewKeyDown += OnKey;
            PreviewKeyUp += (s, e) => { if (e.Key == Key.Z && loupeActive) { EndLoupe(); e.Handled = true; } };
            PreviewKeyUp += OnKeyUpAlt; // Alt sola: menú de opciones
            Deactivated += (s, e) => { EndLoupe(); altAlone = false; }; // al cambiar de ventana (Alt + Tab…) no queda nada a medias
            Loaded += OnLoaded;
            ContentRendered += (s, e) => contentRendered = true;
            Closing += OnClosing;
            if (Application.Current != null) Application.Current.SessionEnding += (s, e) => SaveNow();
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                if (ex != null) LogError(ex, "proceso de fondo");
                try { Dispatcher.Invoke(new Action(SaveNow), TimeSpan.FromSeconds(3)); } catch { }
            };
            instance = this;
        }

        // Icono de la ventana (el mismo icono.ico que lleva el .exe)
        public static ImageSource AppIcon()
        {
            try
            {
                var s = typeof(MainWindow).Assembly.GetManifestResourceStream("SelectorDeFotos.icono.ico");
                if (s == null) return null;
                var dec = BitmapDecoder.Create(s, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return dec.Frames.OrderByDescending(f => f.PixelWidth).First();
            }
            catch { return null; }
        }

        static Brush B(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFrom(hex);
            b.Freeze();
            return b;
        }

        PhotoItem Current { get { return idx >= 0 && idx < view.Count ? view[idx] : null; } }

        // ---------------------------------------------------------------- Interfaz

        void BuildUi()
        {
            var root = new DockPanel { Background = BgBrush };

            toolbar = BuildToolbar();
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);

            var status = new DockPanel { LastChildFill = true };
            progressText = new TextBlock { Foreground = AccentBrush, Margin = new Thickness(16, 0, 0, 0) };
            DockPanel.SetDock(progressText, Dock.Right);
            status.Children.Add(progressText);
            goalText = new TextBlock { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold };
            goalPill = new Border
            {
                Child = goalText, CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 0, 10, 1),
                Margin = new Thickness(16, 0, 0, 0), Visibility = Visibility.Collapsed,
                ToolTip = "Favoritas seleccionadas (el objetivo se define en ⚙ Opciones)"
            };
            DockPanel.SetDock(goalPill, Dock.Right);
            countsText = new TextBlock { Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Opacity = 0.85, Margin = new Thickness(16, 0, 0, 0) };
            DockPanel.SetDock(countsText, Dock.Right);
            status.Children.Add(countsText);
            status.Children.Add(goalPill);
            // En una favorita: qué número de favorita es («Favorita 5 de 10»), a la izquierda del resumen
            favPosText = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(16, 0, 0, 0), Visibility = Visibility.Collapsed };
            DockPanel.SetDock(favPosText, Dock.Right);
            status.Children.Add(favPosText);
            // Solo lectura: visible mientras esta ventana no guarda marcas (clic para volver a intentarlo)
            var lockIcon = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            var lockText = new TextBlock { Text = "Solo lectura", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            readOnlyPill = new Border
            {
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { lockIcon, lockText } },
                Background = B("#E0C77700"), CornerRadius = new CornerRadius(9), Padding = new Thickness(9, 0, 10, 1),
                Margin = new Thickness(0, 0, 16, 0), Cursor = Cursors.Hand, Visibility = Visibility.Collapsed
            };
            TextElement.SetForeground(readOnlyPill, Brushes.White);
            readOnlyPill.MouseLeftButtonUp += (s, e) => RetryWritable();
            DockPanel.SetDock(readOnlyPill, Dock.Left);
            status.Children.Add(readOnlyPill);
            // Datos de la foto: cada parte se copia al portapapeles con un clic
            statusParts = new StackPanel { Orientation = Orientation.Horizontal, ClipToBounds = true };
            status.Children.Add(statusParts);
            statusBar = new Border { Background = PanelBrush, Padding = new Thickness(12, 5, 12, 5), Child = status };
            DockPanel.SetDock(statusBar, Dock.Bottom);
            root.Children.Add(statusBar);

            strip = new Canvas { Background = StripBrush, ClipToBounds = true };
            strip.SizeChanged += (s, e) => LayoutStrip();
            strip.MouseWheel += OnWheel;

            // Barra de desplazamiento fina sobre el borde de la tira
            scrollTrack = new Border { Background = B("#26FFFFFF"), CornerRadius = new CornerRadius(3) };
            scrollThumb = new Border { Background = B("#80FFFFFF"), CornerRadius = new CornerRadius(3) };
            scrollBar = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Arrastra para moverte rápido" };
            scrollBar.Children.Add(scrollTrack);
            scrollBar.Children.Add(scrollThumb);
            scrollBar.MouseEnter += (s, e) => UpdateScrollbar();
            scrollBar.MouseLeave += (s, e) => UpdateScrollbar();
            scrollBar.MouseLeftButtonDown += (s, e) =>
            {
                scrollDragging = true;
                scrollBar.CaptureMouse();
                ScrollTo(e.GetPosition(scrollBar));
                e.Handled = true;
            };
            scrollBar.MouseMove += (s, e) => { if (scrollDragging) ScrollTo(e.GetPosition(scrollBar)); };
            scrollBar.MouseLeftButtonUp += (s, e) =>
            {
                scrollDragging = false;
                scrollBar.ReleaseMouseCapture();
                UpdateScrollbar();
            };
            scrollBar.MouseWheel += OnWheel;
            scrollBar.SizeChanged += (s, e) => UpdateScrollbar();

            stripHost = new Grid();
            stripHost.Children.Add(strip);
            stripHost.Children.Add(scrollBar);
            root.Children.Add(stripHost);

            // Barra para cambiar el tamaño de las miniaturas
            splitter = new Thumb { ToolTip = "Arrastra para cambiar el tamaño de las miniaturas" };
            splitter.DragDelta += (s, e) =>
            {
                double d = sideStrip ? e.HorizontalChange : e.VerticalChange;
                // A la derecha / abajo, arrastrar hacia el visor (izquierda / arriba) agranda la tira; al revés si está a la izquierda / arriba
                var dock = StripDock;
                if (dock == Dock.Left || dock == Dock.Top) d = -d;
                stripSize = Math.Max(StripMin, Math.Min(StripMax, stripSize - d));
                ApplyStripSize();
            };
            splitter.DragCompleted += (s, e) => OnStripResized();
            root.Children.Add(splitter);

            sideStrip = settings.MiniaturasDerecha;
            ApplyStripMode();

            // Visor: [anterior] [actual] [siguiente]. Los paneles de comparación se muestran según ⚙ → «Comparar con».
            // Margen inferior del color del fondo: la foto nunca toca la barra verde/roja de abajo
            center = new Grid { Background = BgBrush, ClipToBounds = true, Margin = new Thickness(2) }; // separación fina en todos los bordes del visor
            prevCmp = new ComparePane { Offset = -1, Label = "Anterior", Col = new ColumnDefinition { Width = new GridLength(0) } };
            nextCmp = new ComparePane { Offset = 1, Label = "Siguiente", Col = new ColumnDefinition { Width = new GridLength(0) } };
            center.ColumnDefinitions.Add(prevCmp.Col);
            center.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            center.ColumnDefinitions.Add(nextCmp.Col);

            mainPane = new Grid { Background = BgBrush, ClipToBounds = true };
            Grid.SetColumn(mainPane, 1);
            mainMosaic = MakeMosaicLayer();
            mainPane.Children.Add(mainMosaic);
            mainImage = new Image { Stretch = Stretch.Uniform, RenderTransform = zoom };
            RenderOptions.SetBitmapScalingMode(mainImage, BitmapScalingMode.HighQuality);
            mainPane.Children.Add(mainImage);
            emptyText = new TextBlock
            {
                Foreground = DimTextBrush, FontSize = 20, TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(40)
            };
            mainPane.Children.Add(emptyText);
            markBadge = MakeMarkIcon(MainCheckSize, MainCrossSize, BgBrush, new Thickness(0, 18, 18, 0));
            mainPane.Children.Add(markBadge);
            mainName = MakePaneName();
            mainName.Background = AccentBrush;
            mainName.FontWeight = FontWeights.Bold;
            mainPane.Children.Add(mainName);
            // En modo comparar, borde azul alrededor de la foto actual para distinguirla de la anterior/siguiente
            mainFrame = new Border { BorderBrush = FocusBrush, BorderThickness = new Thickness(4), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            mainPane.Children.Add(mainFrame);
            mainDash = MakeFocusDash(new Thickness(0), 0, 4);
            mainPane.Children.Add(mainDash);
            center.Children.Add(mainPane);

            BuildComparePane(prevCmp, 0, new Thickness(0, 0, 3, 0));
            BuildComparePane(nextCmp, 2, new Thickness(3, 0, 0, 0));

            zoomText = new TextBlock
            {
                Foreground = Brushes.White, Background = B("#B0000000"), Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, // centrado: no choca con el panel de información
                Margin = new Thickness(14), Visibility = Visibility.Collapsed
            };
            Grid.SetColumnSpan(zoomText, 2);
            center.Children.Add(zoomText);

            // Aviso breve (deshacer, sin marca, exportar…)
            toastText = new TextBlock { Foreground = Brushes.White, FontSize = 15 };
            toast = new Border
            {
                Child = toastText, Background = B("#E0202020"), BorderBrush = B("#555555"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(16, 8, 16, 8),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 18, 0, 0), Visibility = Visibility.Collapsed, IsHitTestVisible = false
            };
            Grid.SetColumnSpan(toast, 2);
            center.Children.Add(toast);
            toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            toastTimer.Tick += (s, e) => { toastTimer.Stop(); toast.Visibility = Visibility.Collapsed; };
            center.MouseWheel += OnImageWheel;
            center.MouseLeftButtonDown += OnImageMouseDown;
            center.MouseMove += OnImageMouseMove;
            center.MouseLeftButtonUp += OnImageMouseUp;
            // Botón de la rueda (mantener): lupa, igual que mantener Z
            center.MouseDown += (s, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                StartLoupe();
                if (loupeActive) { center.CaptureMouse(); e.Handled = true; } // seguir recibiendo el ratón aunque salga del visor
            };
            center.MouseUp += (s, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle || !loupeActive) return;
                EndLoupe();
                if (center.IsMouseCaptured) center.ReleaseMouseCapture();
                e.Handled = true;
            };
            mainPane.SizeChanged += (s, e) => ResetZoom();
            root.Children.Add(center);

            BuildExtrasUi();
            BuildMarkBar(); // barra para marcar en el visor (aparece al acercar el ratón abajo)
            root.SizeChanged += (s, e) =>
            {
                if (gridMode) { stripSize = uiRoot.ActualWidth; ApplyStripSize(); return; }
                if (stripSize > StripMax) { stripSize = StripMax; ApplyStripSize(); }
            };
            uiRoot = root;
            ApplyUiScale(false);
            Content = root;
        }

        Border BuildToolbar()
        {
            var bar = new DockPanel { Margin = new Thickness(6, 4, 6, 4) };

            // Zona derecha: acciones y opciones
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(right, Dock.Right);
            bar.Children.Add(right);

            btnCopiar = MakeButton("Copiar fotos…");
            btnCopiar.ToolTip = "Copia las fotos marcadas a la carpeta de datos, con su nombre, fecha y metadatos originales";
            btnCopiar.Click += (s, e) => CopyPhotos();
            right.Children.Add(btnCopiar);
            var btnExport = MakeButton("Exportar lista");
            btnExport.ToolTip = "Guarda los nombres de las favoritas en un .txt y los copia al portapapeles";
            btnExport.Click += (s, e) => ExportList(true);
            right.Children.Add(btnExport);
            right.Children.Add(MakeSeparator());

            btnSide = MakeButton("");
            btnSide.Padding = new Thickness(8, 4, 8, 4);
            btnSide.Click += (s, e) => ToggleStripSide();
            right.Children.Add(btnSide);
            btnCols = MakeButton("");
            btnCols.Padding = new Thickness(8, 4, 8, 4);
            btnCols.MinWidth = 40;
            // Misma altura que los botones vecinos, con el icono centrado
            btnCols.VerticalAlignment = VerticalAlignment.Stretch;
            btnSide.VerticalAlignment = VerticalAlignment.Stretch;
            btnCols.Click += (s, e) => CycleColumns();
            btnCols.MouseRightButtonUp += (s, e) => { CycleColumns(-1); e.Handled = true; }; // clic derecho: hacia atrás
            right.Children.Add(btnCols);
            btnGrid = MakeButton("");
            btnGrid.Padding = new Thickness(9, 4, 9, 4);
            btnGrid.VerticalAlignment = VerticalAlignment.Stretch;
            btnGrid.Content = GridIcon();
            btnGrid.Click += (s, e) => ToggleGrid();
            right.Children.Add(btnGrid);
            UpdateGridButton(); // texto de ayuda desde el inicio (antes solo aparecía tras usar la cuadrícula)
            right.Children.Add(MakeSeparator());

            btnOpts = MakeIconButton("", "Opciones (Alt)");
            btnOpts.Click += (s, e) => ShowOptionsMenu(btnOpts);
            right.Children.Add(btnOpts);
            var btnHelp = MakeButton("?");
            btnHelp.ToolTip = "Atajos de teclado";
            btnHelp.FontWeight = FontWeights.Bold;
            btnHelp.MinWidth = 32;
            btnHelp.Click += (s, e) => ShowHelp();
            right.Children.Add(btnHelp);

            // Zona izquierda: carpetas, orden y filtros
            var wp = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(wp, Dock.Left);
            bar.Children.Add(wp);

            btnFotos = MakeButton("Fotos…");
            btnFotos.Click += (s, e) => ChoosePhotoFolder();
            wp.Children.Add(btnFotos);
            lblFotos = MakeLabel("", 0);
            wp.Children.Add(lblFotos);

            btnDatos = MakeButton("Datos…");
            btnDatos.Click += (s, e) => ChooseDataFolder();
            wp.Children.Add(btnDatos);
            lblDatos = MakeLabel("", 0);
            wp.Children.Add(lblDatos);

            wp.Children.Add(MakeSeparator(8, 2)); // etiqueta de carpeta | combo
            cmbOrden = new ComboBox { Width = 150, Margin = new Thickness(2, 3, 2, 3), ToolTip = "Ordenar por", Focusable = false, IsTabStop = false, VerticalAlignment = VerticalAlignment.Center };
            foreach (var s in new[] { "Fecha de captura", "Fecha de modificación", "Nombre", "Tamaño", "Tipo de archivo" })
                cmbOrden.Items.Add(s);
            cmbOrden.SelectedIndex = Math.Max(0, Math.Min(4, settings.Orden));
            cmbOrden.SelectionChanged += (s, e) =>
            {
                if (initializing) return;
                settings.Orden = cmbOrden.SelectedIndex;
                Resort();
            };
            wp.Children.Add(cmbOrden);
            btnDir = MakeButton("");
            btnDir.MinWidth = 32;
            btnDir.Padding = new Thickness(8, 4, 8, 4);
            btnDir.FontWeight = FontWeights.Bold;
            btnDir.Click += (s, e) => { settings.Descendente = !settings.Descendente; UpdateDirButton(); Resort(); };
            UpdateDirButton();
            wp.Children.Add(btnDir);

            wp.Children.Add(MakeSeparator(2, 5)); // botón | casilla
            chkFav = MakeCheck(settings.VerFavoritas);
            chkNone = MakeCheck(settings.VerSinMarca);
            chkRej = MakeCheck(settings.VerNoDeseadas);
            chkFav.ToolTip = "Mostrar las favoritas (Alt + F)";
            chkNone.ToolTip = "Mostrar las fotos sin marca (Alt + N)";
            chkRej.ToolTip = "Mostrar las no deseadas (Alt + X)";
            wp.Children.Add(chkFav);
            wp.Children.Add(chkNone);
            wp.Children.Add(chkRej);

            // Atajos: cada tecla en un recuadro y su acción al lado (si no caben, se recortan por la derecha)
            var keys = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            keys.Children.Add(MakeSeparator(5, 0)); // separador antes de los atajos: casilla | tecla
            AddToolbarKey(keys, "← →", "navegar");
            AddToolbarKey(keys, "↑", "favorita");
            AddToolbarKey(keys, "↓", "no deseada");
            AddToolbarKey(keys, "F", "ir a favorita");
            AddToolbarKey(keys, "N", "sin marca");
            AddToolbarKey(keys, "X", "ir a no deseada");
            AddToolbarKey(keys, "C", "comparar");
            AddToolbarKey(keys, "B", "ráfaga");
            AddToolbarKey(keys, "G", "cuadrícula");
            AddToolbarKey(keys, "F11", "pantalla completa");
            helpText = new Border { Child = keys, ClipToBounds = true, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            helpText.ToolTip = "← → navegar · ↑ favorita · ↓ no deseada · F ir a favorita · N sin marca · X ir a no deseada · C comparar · B ráfaga · G cuadrícula · F11 pantalla completa  (todos los atajos en «?»)";
            bar.Children.Add(helpText);

            return new Border
            {
                Background = PanelBrush, Child = bar,
                BorderBrush = B("#383838"), BorderThickness = new Thickness(0, 0, 0, 1)
            };
        }

        // Un atajo de la barra superior: tecla en un recuadro pequeño + acción en gris
        static void AddToolbarKey(StackPanel p, string key, string action)
        {
            p.Children.Add(new Border
            {
                Background = B("#3A3A3A"), BorderBrush = B("#4A4A4A"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0, 5, 1), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = key, Foreground = TextBrush, FontSize = 11.5, FontWeight = FontWeights.SemiBold }
            });
            p.Children.Add(new TextBlock
            {
                Text = action, Foreground = DimTextBrush, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 14, 0)
            });
        }

        static Style darkButtonStyle;
        public static Style DarkButtonStylePublic() { return DarkButtonStyle(); }

        // Botón plano oscuro, acorde con el resto de la interfaz.
        static Style DarkButtonStyle()
        {
            if (darkButtonStyle != null) return darkButtonStyle;
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, B("#4A4A4A"), "bd"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, B("#6A6A6A"), "bd"));
            tpl.Triggers.Add(hover);
            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, B("#5A5A5A"), "bd"));
            tpl.Triggers.Add(pressed);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            tpl.Triggers.Add(disabled);

            var st = new Style(typeof(Button));
            st.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            st.Setters.Add(new Setter(Control.BackgroundProperty, B("#383838")));
            st.Setters.Add(new Setter(Control.BorderBrushProperty, B("#484848")));
            st.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
            st.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            darkButtonStyle = st;
            return st;
        }

        Button MakeButton(string text)
        {
            return new Button
            {
                Content = text, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(2, 3, 2, 3),
                Focusable = false, IsTabStop = false, VerticalAlignment = VerticalAlignment.Center,
                Style = DarkButtonStyle()
            };
        }

        Button MakeIconButton(string glyph, string tooltip)
        {
            var b = MakeButton("");
            b.Padding = new Thickness(8, 4, 8, 4);
            b.ToolTip = tooltip;
            b.Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 15 };
            return b;
        }

        TextBlock MakeLabel(string text, double maxWidth)
        {
            var t = new TextBlock
            {
                Text = text, Foreground = TextBrush, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis
            };
            if (maxWidth > 0) t.MaxWidth = maxWidth;
            return t;
        }

        CheckBox MakeCheck(bool value)
        {
            var c = new CheckBox
            {
                IsChecked = value, Foreground = TextBrush, Margin = new Thickness(5, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center, Focusable = false, IsTabStop = false
            };
            c.Click += (s, e) => OnFilterChanged();
            return c;
        }

        // Separador vertical. Los márgenes compensan el de los elementos vecinos para que el espacio visible
        // a cada lado sea el mismo (12 px): botones y combo tienen 2, casillas 5, etiquetas de carpeta 8.
        const double SeparatorGap = 12;
        UIElement MakeSeparator(double leftNeighbor = 2, double rightNeighbor = 2)
        {
            return new Border
            {
                Width = 1, Height = 24, Background = B("#444444"), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Math.Max(0, SeparatorGap - leftNeighbor), 0, Math.Max(0, SeparatorGap - rightNeighbor), 0)
            };
        }

        void UpdateDirButton()
        {
            btnDir.Content = settings.Descendente ? "↓" : "↑";
            btnDir.ToolTip = settings.Descendente ? "Orden descendente (clic para ascendente)" : "Orden ascendente (clic para descendente)";
        }

        // Abrevia la ruta por el principio: «…\Wedding Day\Todas». La ruta completa queda en el tooltip.
        static string ShortPath(string path)
        {
            const int max = 24;
            if (string.IsNullOrEmpty(path)) return "(sin elegir)";
            path = path.TrimEnd('\\');
            if (path.Length <= max) return path;
            var parts = path.Split('\\');
            string result = parts[parts.Length - 1];
            if (result.Length > max - 2) return "…" + result.Substring(result.Length - (max - 2));
            for (int i = parts.Length - 2; i >= 0; i--)
            {
                string next = parts[i] + "\\" + result;
                if (next.Length > max - 2) break;
                result = next;
            }
            return "…\\" + result;
        }

        static string FolderName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "(sin elegir)";
            string n = Path.GetFileName(path.TrimEnd('\\'));
            return string.IsNullOrEmpty(n) ? path : n;
        }

        void UpdateLabels()
        {
            bool compact = settings.BarraCompacta;
            // Compacta: solo el nombre de la carpeta dentro del botón y la ayuda en el botón «?».
            btnFotos.Content = compact ? "Fotos: " + FolderName(settings.CarpetaFotos) : "Fotos…";
            btnDatos.Content = compact ? "Datos: " + FolderName(settings.CarpetaDatos) : "Datos…";
            btnFotos.ToolTip = "Carpeta de fotos: " + (settings.CarpetaFotos ?? "(sin elegir)");
            btnDatos.ToolTip = "Carpeta de datos (marcas y copias): " + (settings.CarpetaDatos ?? "(sin elegir)");
            lblFotos.Visibility = lblDatos.Visibility = helpText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            lblFotos.Text = ShortPath(settings.CarpetaFotos);
            lblFotos.ToolTip = settings.CarpetaFotos;
            lblDatos.Text = ShortPath(settings.CarpetaDatos);
            lblDatos.ToolTip = settings.CarpetaDatos;
            Title = (string.IsNullOrEmpty(settings.CarpetaFotos)
                ? "Selector de Fotos"
                : "Selector de Fotos — " + Path.GetFileName(settings.CarpetaFotos.TrimEnd('\\')))
                + (readOnlyData ? "  (solo lectura)" : "");
            readOnlyPill.Visibility = readOnlyData ? Visibility.Visible : Visibility.Collapsed;
            readOnlyPill.ToolTip = readOnlyReason == null ? null
                : readOnlyReason + "\nLas marcas se ven, pero los cambios no se guardan.\n\nClic para volver a intentarlo (por ejemplo, después de cerrar la otra ventana).";
        }

        void UpdateCounts()
        {
            int f = 0, n = 0, r = 0;
            foreach (var it in all)
            {
                if (it.Mark == Mark.Fav) f++;
                else if (it.Mark == Mark.Reject) r++;
                else n++;
            }
            int goal = settings.ObjetivoFavoritas;
            chkFav.Content = goal > 0 ? "Favoritas (" + f + " / " + goal + ")" : "Favoritas (" + f + ")";
            chkNone.Content = "Sin marca (" + n + ")";
            chkRej.Content = "No deseadas (" + r + ")";

            // Resumen siempre visible a la derecha de la barra inferior (también en pantalla completa)
            if (goal > 0)
            {
                goalText.Text = "✓ " + f + " / " + goal + " favoritas" + (f > goal ? "  (+" + (f - goal) + ")" : f == goal ? "  ★" : "");
                goalPill.Background = f > goal ? B("#E0C77700") : f == goal ? B("#E01E7A36") : B("#66000000");
            }
            else
            {
                goalText.Text = "✓ " + f + (f == 1 ? " favorita" : " favoritas");
                goalPill.Background = B("#66000000");
            }
            goalPill.Visibility = all.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            countsText.Text = all.Count > 0 ? "✕ " + r + " no deseadas     " + n + " sin marca" : "";
        }
    }
}
