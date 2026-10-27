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
    // Carpetas de fotos y datos, protección del archivo de marcas y guardado.
    public partial class MainWindow
    {
        // ---------------------------------------------------------------- Carpetas

        void ChoosePhotoFolder()
        {
            string p = FolderPicker.Pick(this, "Elige la carpeta con las fotos", settings.CarpetaFotos);
            if (p == null) return;
            if (string.IsNullOrEmpty(settings.CarpetaDatos) || !Directory.Exists(settings.CarpetaDatos))
            {
                MessageBox.Show(this, "Ahora elige la carpeta donde se guardarán las marcas (y donde se copiarán las fotos seleccionadas).",
                    "Carpeta de datos", MessageBoxButton.OK, MessageBoxImage.Information);
                string d = FolderPicker.Pick(this, "Elige la carpeta de datos", p);
                if (d != null) settings.CarpetaDatos = d;
            }
            OpenPhotoFolder(p);
        }

        void ChooseDataFolder()
        {
            string p = FolderPicker.Pick(this, "Elige la carpeta de datos (marcas y copias)", settings.CarpetaDatos ?? settings.CarpetaFotos);
            if (p == null) return;
            SaveNow();
            undoStack.Clear();
            settings.CarpetaDatos = p;
            SaveSettings();
            var cur = Current;
            LoadData();
            CheckMissingMarks();
            StoreMetaCache();
            RefreshThumbCache();
            UpdateLabels();
            UpdateCounts();
            sticky = null;
            RebuildView(cur);
            ShowCurrent();
        }

        void OpenPhotoFolder(string path)
        {
            SaveNow();
            loadGen++;
            int gen = loadGen;
            settings.CarpetaFotos = path;
            SaveSettings();
            all = new List<PhotoItem>();
            view = new List<PhotoItem>();
            undoStack.Clear();
            idx = -1;
            sticky = null;
            thumbs.Clear();
            fulls.Clear();

            try
            {
                foreach (var f in new DirectoryInfo(path).EnumerateFiles())
                {
                    if (!Extensions.Contains(f.Extension) || f.Name.StartsWith("._")) continue;
                    if ((f.Attributes & FileAttributes.Hidden) != 0) continue;
                    all.Add(new PhotoItem
                    {
                        Path = f.FullName, Name = f.Name, Ext = f.Extension.ToLowerInvariant(),
                        Size = f.Length, Modified = f.LastWriteTime
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo leer la carpeta:\n" + ex.Message, "Selector de Fotos", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            LoadData();
            CheckMissingMarks();
            UpdateLabels();
            UpdateCounts();
            SortAll();
            PhotoItem start = null;
            if (folderData != null && folderData.ultima != null)
                start = all.FirstOrDefault(x => string.Equals(x.Name, folderData.ultima, StringComparison.OrdinalIgnoreCase));
            startPending = start == null;
            RebuildView(start);
            ShowCurrent();
            StartMetaReading(gen);
            RefreshThumbCache();
        }

        static string FolderKey(string path)
        {
            return Path.GetFullPath(path).TrimEnd('\\').ToLowerInvariant();
        }

        // ---------------------------------------------------------------- Protección del archivo de marcas
        //  · Estructura de la carpeta de datos: en la raíz, el archivo de marcas y las carpetas de copias de fotos;
        //    todo lo interno de la app (copia anterior, copias de seguridad, miniaturas, bloqueo) va en «data».
        //  · Guardado seguro (ver Json.Save): temporal + verificación + reemplazo atómico, y la versión anterior en data\marcas.bak.
        //  · Copia de seguridad diaria (y antes de quitar marcas) en «copias_de_seguridad_marcas».
        //  · Si el archivo no se puede LEER (bloqueado por OneDrive, antivirus…): solo lectura, nunca se sobrescribe.
        //  · Si está DAÑADO: se aparta y se recupera automáticamente de la copia más reciente.
        //  · Si otra ventana usa las mismas marcas: esta ventana no guarda, para no pisarlas.

        const string AppDataFolderName = "data";
        const string BackupFolderName = "copias_de_seguridad";
        string AppDataDir { get { return Path.Combine(settings.CarpetaDatos, AppDataFolderName); } }
        string BakFile { get { return Path.Combine(AppDataDir, "marcas.bak"); } }
        string TmpFile { get { return Path.Combine(AppDataDir, "marcas.tmp"); } }
        const int BackupsToKeep = 30;
        bool readOnlyData;
        string readOnlyReason; // por qué esta ventana no guarda (texto del indicador «Solo lectura»)
        string lastSaveError;
        FileStream dataLock;
        public static bool Autotest;

        void Alert(string text, MessageBoxImage icon)
        {
            if (Autotest) { Toast(text.Split('\n')[0]); return; }
            MessageBox.Show(this, text, "Selector de Fotos", MessageBoxButton.OK, icon);
        }

        // Al abrir la app el aviso se muestra cuando la ventana ya está a la vista (si no, puede quedar detrás).
        void AlertWhenVisible(string text, MessageBoxImage icon)
        {
            if (retryingWritable) return; // al reintentar basta con el aviso breve
            if (Autotest || contentRendered) { Alert(text, icon); return; }
            EventHandler shown = null;
            shown = (s, e) =>
            {
                ContentRendered -= shown;
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => { Activate(); Alert(text, icon); }));
            };
            ContentRendered += shown;
        }

        bool contentRendered, retryingWritable;

        // Clic en «Solo lectura»: si la otra ventana ya se cerró (o el archivo se desbloqueó), se vuelven a leer las
        // marcas del disco y esta ventana empieza a guardar.
        void RetryWritable()
        {
            if (!readOnlyData) return;
            var cur = Current;
            undoStack.Clear();
            retryingWritable = true;
            try { LoadData(); } finally { retryingWritable = false; }
            CheckMissingMarks();
            UpdateLabels();
            UpdateCounts();
            sticky = null;
            RebuildView(cur);
            ShowCurrent();
            Toast(readOnlyData ? "Sigue en solo lectura: " + Char.ToLower(readOnlyReason[0]) + readOnlyReason.Substring(1)
                               : "Listo: las marcas se volvieron a leer y esta ventana ya guarda los cambios");
        }

        // Estructura antigua → nueva: mueve a «data» la copia anterior, las copias de seguridad, las miniaturas y
        // los archivos dañados que estaban sueltos en la raíz. No borra nada que no sea de la app.
        void MigrateDataLayout(string folder)
        {
            try
            {
                string d = Path.Combine(folder, AppDataFolderName);
                Directory.CreateDirectory(d);
                string oldBak = Path.Combine(folder, DataFileName + ".bak");
                string newBak = Path.Combine(d, "marcas.bak");
                if (File.Exists(oldBak)) { if (!File.Exists(newBak)) File.Move(oldBak, newBak); else File.Delete(oldBak); }
                string oldTmp = Path.Combine(folder, DataFileName + ".tmp");
                string newTmp = Path.Combine(d, "marcas.tmp");
                if (File.Exists(oldTmp)) { if (!File.Exists(newTmp)) File.Move(oldTmp, newTmp); else File.Delete(oldTmp); }
                foreach (var dam in Directory.GetFiles(folder, DataFileName + ".danado-*"))
                {
                    string target = Path.Combine(d, "marcas.danado-" + Path.GetFileName(dam).Substring((DataFileName + ".danado-").Length) + ".json");
                    if (!File.Exists(target)) File.Move(dam, target);
                }
                string oldLock = Path.Combine(folder, "SelectorDeFotos.marcas.lock");
                if (File.Exists(oldLock)) { try { File.Delete(oldLock); } catch { } } // si otra ventana antigua lo usa, se deja

                string oldBackups = Path.Combine(folder, "copias_de_seguridad_marcas");
                string newBackups = Path.Combine(d, BackupFolderName);
                if (Directory.Exists(oldBackups))
                {
                    if (!Directory.Exists(newBackups)) Directory.Move(oldBackups, newBackups);
                    else
                    {
                        foreach (var fb in Directory.GetFiles(oldBackups))
                        {
                            string target = Path.Combine(newBackups, Path.GetFileName(fb));
                            if (!File.Exists(target)) File.Move(fb, target);
                        }
                        if (!Directory.EnumerateFileSystemEntries(oldBackups).Any()) Directory.Delete(oldBackups);
                    }
                }

                // Solo si parece nuestra carpeta de miniaturas (tiene las subcarpetas 400 u 800)
                string oldThumbs = Path.Combine(folder, ThumbFolderName);
                string newThumbs = Path.Combine(d, ThumbFolderName);
                if (Directory.Exists(oldThumbs) && (Directory.Exists(Path.Combine(oldThumbs, "400")) || Directory.Exists(Path.Combine(oldThumbs, "800"))))
                {
                    if (!Directory.Exists(newThumbs)) Directory.Move(oldThumbs, newThumbs);
                    else DeleteThumbCache(oldThumbs);
                }
                // Carpeta de miniaturas antigua que quedó vacía
                if (Directory.Exists(oldThumbs) && !Directory.EnumerateFileSystemEntries(oldThumbs).Any()) Directory.Delete(oldThumbs);
            }
            catch { }
        }

        bool AcquireDataLock(string folder)
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, AppDataFolderName));
                dataLock = new FileStream(Path.Combine(folder, AppDataFolderName, "marcas.lock"), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                return true;
            }
            catch (IOException) { return false; }      // otra ventana lo tiene abierto
            catch { return true; }                    // sin permisos para el candado: se sigue igual
        }

        void ReleaseDataLock()
        {
            if (dataLock == null) return;
            try { dataLock.Dispose(); } catch { }
            dataLock = null;
        }

        string BackupDir { get { return Path.Combine(AppDataDir, BackupFolderName); } }

        // Copia el archivo de marcas a la carpeta de copias de seguridad y conserva solo las más recientes.
        string Snapshot(string tag)
        {
            try
            {
                if (dataFile == null || !File.Exists(dataFile)) return null;
                Directory.CreateDirectory(BackupDir);
                string dest = Path.Combine(BackupDir, "marcas " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + tag + ".json");
                File.Copy(dataFile, dest, true);
                foreach (var old in new DirectoryInfo(BackupDir).GetFiles("marcas *.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(BackupsToKeep))
                {
                    try { old.Delete(); } catch { }
                }
                return dest;
            }
            catch { return null; }
        }

        void DailySnapshot()
        {
            try
            {
                string today = "marcas " + DateTime.Now.ToString("yyyy-MM-dd");
                if (Directory.Exists(BackupDir) && Directory.GetFiles(BackupDir, today + "*.json").Length > 0) return;
                Snapshot("");
            }
            catch { }
        }

        // Busca la copia válida más reciente: «.bak» (versión anterior), «.tmp» (guardado interrumpido) y las copias diarias.
        DataFile TryRecover(string file, out string from)
        {
            from = null;
            var candidates = new List<string> { BakFile, TmpFile };
            try
            {
                if (Directory.Exists(BackupDir))
                    candidates.AddRange(new DirectoryInfo(BackupDir).GetFiles("marcas *.json")
                        .OrderByDescending(f => f.LastWriteTimeUtc).Select(f => f.FullName));
            }
            catch { }
            foreach (var c in candidates)
            {
                DataFile d;
                if (Json.TryRead(c, out d) == Json.ReadResult.Ok && d != null)
                {
                    from = c;
                    return d;
                }
            }
            return null;
        }

        void LoadData()
        {
            data = null;
            folderData = null;
            dataFile = null;
            readOnlyData = false;
            readOnlyReason = null;
            ReleaseDataLock();
            foreach (var it in all) it.Mark = Mark.None;
            if (string.IsNullOrEmpty(settings.CarpetaDatos) || !Directory.Exists(settings.CarpetaDatos)) return;

            MigrateDataLayout(settings.CarpetaDatos);
            string file = Path.Combine(settings.CarpetaDatos, DataFileName);
            dataFile = file;

            if (!AcquireDataLock(settings.CarpetaDatos))
            {
                readOnlyData = true;
                readOnlyReason = "Las marcas de esta carpeta de datos ya están abiertas en otra ventana de Selector de Fotos.";
                AlertWhenVisible(readOnlyReason + "\n\n" +
                      "Esta ventana las muestra pero NO guardará cambios, para no pisar las de la otra ventana.\n" +
                      "Abajo a la izquierda se ve «Solo lectura»; al cerrar la otra ventana, haz clic ahí para empezar a guardar.",
                      MessageBoxImage.Warning);
            }

            DataFile d;
            var result = Json.TryRead(file, out d);
            if (result == Json.ReadResult.Missing)
            {
                // Sin archivo principal: quizá un guardado se interrumpió; se intenta recuperar.
                string from;
                d = TryRecover(file, out from);
                if (d != null) Alert("No se encontró el archivo de marcas, pero se recuperaron desde:\n" + from, MessageBoxImage.Information);
            }
            else if (result == Json.ReadResult.Unreadable)
            {
                readOnlyData = true;
                readOnlyReason = "No se pudo leer el archivo de marcas (puede estar bloqueado por OneDrive, el antivirus u otro programa).";
                AlertWhenVisible("No se pudo leer el archivo de marcas (puede estar bloqueado por OneDrive, el antivirus u otro programa):\n" + file +
                      "\n\nPara no perder nada, esta sesión NO guardará cambios.\nCierra la app y vuelve a abrirla en unos segundos.",
                      MessageBoxImage.Error);
            }
            else if (result == Json.ReadResult.Corrupt)
            {
                string damaged = Path.Combine(AppDataDir, "marcas.danado-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                try { File.Copy(file, damaged, true); } catch { }
                string from;
                d = TryRecover(file, out from);
                if (d != null)
                    Alert("El archivo de marcas estaba dañado. Se recuperaron automáticamente desde la copia de seguridad:\n" + from +
                          "\n\nEl archivo dañado se apartó como:\n" + damaged, MessageBoxImage.Warning);
                else
                    Alert("El archivo de marcas estaba dañado y no hay ninguna copia de seguridad válida.\n" +
                          "Se apartó como:\n" + damaged + "\n\nSe empieza con las marcas vacías.", MessageBoxImage.Error);
            }
            else if (!readOnlyData)
            {
                DailySnapshot();
            }
            data = d;
            if (data == null) data = new DataFile { version = 1 };
            if (data.carpetas == null) data.carpetas = new Dictionary<string, FolderData>();
            if (string.IsNullOrEmpty(settings.CarpetaFotos)) return;

            string key = FolderKey(settings.CarpetaFotos);
            if (!data.carpetas.TryGetValue(key, out folderData))
            {
                folderData = new FolderData();
                data.carpetas[key] = folderData;
            }
            folderData.marcas = new Dictionary<string, string>(folderData.marcas ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            folderData.meta = new Dictionary<string, string>(folderData.meta ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);

            foreach (var it in all)
            {
                string v;
                if (folderData.marcas.TryGetValue(it.Name, out v))
                    it.Mark = v == "fav" ? Mark.Fav : v == "no" ? Mark.Reject : Mark.None;
                if (!it.MetaRead && folderData.meta.TryGetValue(it.Name, out v)) ApplyMetaCache(it, v);
            }
        }

        static void ApplyMetaCache(PhotoItem it, string v)
        {
            try
            {
                var p = v.Split('|');
                if (p.Length < 6 || long.Parse(p[0]) != it.Size || long.Parse(p[1]) != it.Modified.Ticks) return;
                long taken = long.Parse(p[2]);
                it.Taken = taken > 0 ? new DateTime(taken) : (DateTime?)null;
                it.Orientation = int.Parse(p[3]);
                it.Width = int.Parse(p[4]);
                it.Height = int.Parse(p[5]);
                it.MetaRead = true;
            }
            catch { }
        }

        void StoreMetaCache()
        {
            if (folderData == null) return;
            var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in all)
            {
                if (!it.MetaRead) continue;
                m[it.Name] = string.Join("|", it.Size, it.Modified.Ticks, it.Taken.HasValue ? it.Taken.Value.Ticks : 0,
                    it.Orientation, it.Width, it.Height);
            }
            folderData.meta = m;
            ScheduleSave();
        }

        void StartMetaReading(int gen)
        {
            var pending = new ConcurrentQueue<PhotoItem>(all.Where(x => !x.MetaRead));
            metaTotal = pending.Count;
            metaDone = 0;
            if (metaTotal == 0) { progressText.Text = ""; ComputeBursts(); UpdateStrip(); return; } // todas las fechas ya estaban guardadas
            progressTimer.Start();
            UpdateProgress();
            int remaining = 3;
            for (int i = 0; i < 3; i++)
            {
                var t = new Thread(() =>
                {
                    PhotoItem it;
                    while (gen == loadGen && pending.TryDequeue(out it))
                    {
                        MetaReader.Read(it);
                        Interlocked.Increment(ref metaDone);
                    }
                    if (Interlocked.Decrement(ref remaining) == 0)
                        Dispatcher.BeginInvoke(new Action(() => OnMetaDone(gen)));
                });
                t.IsBackground = true;
                t.Priority = ThreadPriority.BelowNormal;
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            }
        }

        void UpdateProgress()
        {
            if (copying) return;
            if (metaTotal > 0 && metaDone < metaTotal)
                progressText.Text = "Leyendo fechas… " + (metaDone * 100 / metaTotal) + "%";
        }

        void OnMetaDone(int gen)
        {
            if (gen != loadGen) return;
            progressTimer.Stop();
            if (!copying) progressText.Text = "";
            StoreMetaCache();
            ComputeBursts();
            if (settings.Orden == 0)
            {
                var cur = startPending ? null : Current;
                SortAll();
                RebuildView(cur);
                ShowCurrent();
            }
            else
            {
                UpdateStatus();
                UpdateStrip();
            }
        }


        // ---------------------------------------------------------------- Guardado

        void ScheduleSave()
        {
            dirty = true;
            saveTimer.Stop();
            saveTimer.Start();
        }

        void SaveNow()
        {
            saveTimer.Stop();
            if (!dirty || data == null || dataFile == null || readOnlyData) return;
            try
            {
                Directory.CreateDirectory(AppDataDir);
                Json.Save(dataFile, data, BakFile, TmpFile);
                dirty = false;
                if (lastSaveError != null)
                {
                    lastSaveError = null;
                    progressText.Text = "";
                    Toast("Marcas guardadas de nuevo correctamente");
                }
            }
            catch (Exception ex)
            {
                // El archivo bueno no se toca; se reintenta en el próximo cambio y al cerrar.
                if (lastSaveError == null) Toast("No se pudieron guardar las marcas: " + ex.Message);
                lastSaveError = ex.Message;
                progressText.Text = "⚠ Marcas sin guardar: " + ex.Message;
            }
        }

        // Posición, tamaño y estado de la ventana (se restauran al abrir)
        void RestoreWindowPlacement()
        {
            if (settings.VentanaAncho > 300 && settings.VentanaAlto > 200)
            {
                var r = new Rect(settings.VentanaX, settings.VentanaY, settings.VentanaAncho, settings.VentanaAlto);
                var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                      SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                if (screen.IntersectsWith(r)) // solo si sigue en una pantalla conectada
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = r.Left; Top = r.Top; Width = r.Width; Height = r.Height;
                }
            }
            WindowState = settings.VentanaAncho > 0 && !settings.VentanaMaximizada ? WindowState.Normal : WindowState.Maximized;
        }

        void SaveWindowPlacement()
        {
            var rb = RestoreBounds;
            if (!rb.IsEmpty && rb.Width > 0 && rb.Height > 0)
            {
                settings.VentanaX = rb.Left; settings.VentanaY = rb.Top;
                settings.VentanaAncho = rb.Width; settings.VentanaAlto = rb.Height;
            }
            settings.VentanaMaximizada = fullscreen ? prevState == WindowState.Maximized : WindowState == WindowState.Maximized;
            settings.PantallaCompleta = fullscreen;
        }

        // Al cerrar: si el último guardado falló, avisar antes de perder nada.
        void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveNow();
            if (dirty && !readOnlyData && data != null && dataFile != null && !Autotest)
            {
                var r = MessageBox.Show(this,
                    "No se pudieron guardar las últimas marcas:\n" + lastSaveError +
                    "\n\n¿Cerrar igualmente? Se perderían los cambios desde el último guardado.\n" +
                    "(Pulsa «No» para seguir en la app y volver a intentarlo, por ejemplo reconectando el disco.)",
                    "Selector de Fotos", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
            }
            if (!Autotest) SaveWindowPlacement();
            SaveSettings();
            ReleaseDataLock();
        }

        // Quita las marcas de fotos que ya no están en la carpeta (borradas, movidas o renombradas), avisando antes.
        void CheckMissingMarks()
        {
            if (folderData == null || readOnlyData || all.Count == 0) return; // carpeta vacía o no disponible: no se toca nada
            var names = new HashSet<string>(all.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            var missing = folderData.marcas.Keys.Where(k => !names.Contains(k)).ToList();
            if (missing.Count == 0) return;

            SaveNow();
            string backup = Snapshot(" antes de quitar marcas");
            int favs = missing.Count(k => folderData.marcas[k] == "fav");
            var list = missing.Take(12).Select(k => "   • " + k + (folderData.marcas[k] == "fav" ? "  (favorita)" : "  (no deseada)")).ToList();
            if (missing.Count > 12) list.Add("   … y " + (missing.Count - 12) + " más");
            Alert("Hay " + missing.Count + " foto(s) marcadas que ya no están en la carpeta (" + favs + " favoritas, " +
                  (missing.Count - favs) + " no deseadas). Puede que se hayan borrado, movido o renombrado:\n\n" +
                  string.Join("\n", list) + "\n\nSe quitarán de las marcas." +
                  (backup != null ? "\n\nAntes se guardó una copia de seguridad de las marcas en:\n" + backup : ""),
                  MessageBoxImage.Warning);
            foreach (var k in missing) folderData.marcas.Remove(k);
            if (folderData.ultima != null && !names.Contains(folderData.ultima)) folderData.ultima = null;
            dirty = true;
            SaveNow();
        }

        void LoadSettings()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            settingsPath = Path.Combine(exeDir, "SelectorDeFotos.ajustes.json");
            settings = Json.Load<Settings>(settingsPath);
            if (settings == null)
            {
                // Sin ajustes junto al .exe: los de AppData solo se usan si en la carpeta del programa no se puede
                // escribir (es donde se guardan en ese caso). Una copia nueva en una carpeta normal empieza limpia,
                // sin heredar las carpetas de otra copia.
                string alt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SelectorDeFotos", "ajustes.json");
                settings = (CanWriteIn(exeDir) ? null : Json.Load<Settings>(alt)) ?? new Settings();
            }
        }

        static bool CanWriteIn(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".prueba_escritura_" + Guid.NewGuid().ToString("N") + ".tmp");
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch { return false; }
        }

        void SaveSettings()
        {
            if (initializing) return;
            try
            {
                Json.Save(settingsPath, settings);
            }
            catch
            {
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SelectorDeFotos");
                    Directory.CreateDirectory(dir);
                    settingsPath = Path.Combine(dir, "ajustes.json");
                    Json.Save(settingsPath, settings);
                }
                catch { }
            }
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            double maxScreen = Math.Max(SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight) * dpi.DpiScaleX;
            fullDim = Math.Max(1920, (int)maxScreen);
            dpiScale = dpi.DpiScaleX;
            thumbDim = ThumbDimFor();

            initializing = false;
            if (!Autotest && settings.PantallaCompleta) ToggleFullscreen();
            UpdateLabels();
            UpdateCounts();
            if (!string.IsNullOrEmpty(settings.CarpetaFotos) && Directory.Exists(settings.CarpetaFotos))
                OpenPhotoFolder(settings.CarpetaFotos);
            else
                ShowCurrent();
        }
    }
}
