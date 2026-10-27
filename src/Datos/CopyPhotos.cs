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
    // Copiar las fotos marcadas a la carpeta de datos.
    public partial class MainWindow
    {
        // ---------------------------------------------------------------- Copiar

        void CopyPhotos()
        {
            if (copying) return;
            if (all.Count == 0)
            {
                MessageBox.Show(this, "Primero abre una carpeta de fotos.", "Copiar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrEmpty(settings.CarpetaDatos) || !Directory.Exists(settings.CarpetaDatos))
            {
                ChooseDataFolder();
                if (string.IsNullOrEmpty(settings.CarpetaDatos)) return;
            }
            int nf = all.Count(x => x.Mark == Mark.Fav);
            int nn = all.Count(x => x.Mark == Mark.None);
            int nr = all.Count(x => x.Mark == Mark.Reject);
            var dlg = new CopyDialog(this, nf, nn, nr, settings.CarpetaDatos);
            if (dlg.ShowDialog() != true) return;
            StartCopy(dlg.Fav.IsChecked == true, dlg.None.IsChecked == true, dlg.Rej.IsChecked == true, true);
        }

        // Copia byte a byte (el EXIF y demás metadatos van dentro del archivo), con el mismo nombre,
        // y después iguala las fechas de creación/modificación/acceso y los atributos del original.
        // removeStale: null = preguntar, true = enviar a la papelera las copias que sobran, false = mantenerlas.
        void StartCopy(bool fav, bool none, bool rej, bool interactive, bool? removeStale = null)
        {
            var jobs = new List<KeyValuePair<PhotoItem, string>>();
            string baseDir = settings.CarpetaDatos;
            foreach (var it in all)
            {
                string sub = null;
                if (it.Mark == Mark.Fav && fav) sub = "seleccionadas";
                else if (it.Mark == Mark.None && none) sub = "sin_marca";
                else if (it.Mark == Mark.Reject && rej) sub = "no_deseadas";
                if (sub != null) jobs.Add(new KeyValuePair<PhotoItem, string>(it, Path.Combine(baseDir, sub)));
            }

            // Copias hechas antes que ya no están en la selección actual
            var stale = new List<string>();
            var categories = new List<KeyValuePair<string, Mark>>();
            if (fav) categories.Add(new KeyValuePair<string, Mark>("seleccionadas", Mark.Fav));
            if (none) categories.Add(new KeyValuePair<string, Mark>("sin_marca", Mark.None));
            if (rej) categories.Add(new KeyValuePair<string, Mark>("no_deseadas", Mark.Reject));
            var staleByFolder = new List<string>();
            foreach (var cat in categories)
            {
                var expected = new HashSet<string>(all.Where(x => x.Mark == cat.Value).Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
                var found = FindStaleCopies(Path.Combine(baseDir, cat.Key), expected);
                if (found.Count > 0) staleByFolder.Add("«" + cat.Key + "»: " + found.Count);
                stale.AddRange(found);
            }

            bool remove = false;
            if (stale.Count > 0)
            {
                if (removeStale.HasValue) remove = removeStale.Value;
                else
                {
                    bool recycle = HasRecycleBin(baseDir);
                    var names = stale.Take(12).Select(p => "   • " + Path.GetFileName(p)).ToList();
                    if (stale.Count > 12) names.Add("   … y " + (stale.Count - 12) + " más");
                    string msg =
                        "Hay " + stale.Count + " foto(s) copiadas antes que YA NO están en la selección actual (" +
                        string.Join(", ", staleByFolder) + "):\n\n" + string.Join("\n", names) + "\n\n" +
                        (recycle
                            ? "¿Enviarlas a la papelera de reciclaje para que la carpeta coincida con la selección?"
                            : "¿Borrarlas? ATENCIÓN: esta unidad no tiene papelera, se borrarán DEFINITIVAMENTE.") +
                        "\n\nSí: " + (recycle ? "enviarlas a la papelera" : "borrarlas") + " y copiar\n" +
                        "No: mantenerlas y copiar\nCancelar: no hacer nada\n\n" +
                        "Solo se tocan estas copias; las fotos originales nunca se modifican.";
                    var r = MessageBox.Show(this, msg, "Copias que ya no están seleccionadas", MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning, MessageBoxResult.Cancel);
                    if (r == MessageBoxResult.Cancel || r == MessageBoxResult.None) return;
                    remove = r == MessageBoxResult.Yes;
                }
            }
            if (jobs.Count == 0 && !remove) return;

            copying = true;
            btnCopiar.IsEnabled = false;
            var t = new Thread(() =>
            {
                int copied = 0, skipped = 0, renamed = 0, errors = 0, n = 0, removed = 0;
                string firstError = null;
                if (remove)
                {
                    foreach (var f in stale)
                    {
                        try
                        {
                            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f,
                                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                            removed++;
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            if (firstError == null) firstError = Path.GetFileName(f) + ": " + ex.Message;
                        }
                    }
                }
                foreach (var job in jobs)
                {
                    n++;
                    int nn2 = n;
                    Dispatcher.BeginInvoke(new Action(() => progressText.Text = "Copiando " + nn2 + " / " + jobs.Count + "…"));
                    try
                    {
                        var src = new FileInfo(job.Key.Path);
                        Directory.CreateDirectory(job.Value);
                        string dest = Path.Combine(job.Value, job.Key.Name);
                        if (File.Exists(dest))
                        {
                            if (new FileInfo(dest).Length == src.Length)
                            {
                                // Ya copiada: solo se asegura que tenga las fechas del original.
                                CopyFileTimes(src, dest);
                                skipped++;
                                continue;
                            }
                            // Otro archivo distinto con el mismo nombre: nunca se sobrescribe.
                            // Si ya hay una copia renombrada idéntica («nombre (n).jpg»), se usa esa.
                            string existing = FindRenamedCopy(dest, src);
                            if (existing != null)
                            {
                                CopyFileTimes(src, existing);
                                skipped++;
                                continue;
                            }
                            dest = UniqueName(dest);
                            renamed++;
                        }
                        File.Copy(src.FullName, dest, false);
                        CopyFileTimes(src, dest);
                        copied++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        if (firstError == null) firstError = job.Key.Name + ": " + ex.Message;
                    }
                }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    copying = false;
                    btnCopiar.IsEnabled = true;
                    progressText.Text = "";
                    string msg = "Copiadas: " + copied + "\nYa existían (omitidas): " + skipped;
                    if (removed > 0) msg += "\nCopias que ya no estaban seleccionadas, quitadas: " + removed;
                    if (renamed > 0) msg += "\nCon nombre cambiado (había otro archivo distinto con el mismo nombre): " + renamed;
                    if (errors > 0) msg += "\nErrores: " + errors + "\n" + firstError;
                    msg += "\n\nSe mantienen el nombre, las fechas y los metadatos (EXIF) originales.";
                    if (!interactive)
                    {
                        Toast("Copia terminada: " + copied + " copiadas, " + skipped + " omitidas, " + removed + " quitadas, " + errors + " errores");
                        return;
                    }
                    msg += "\n\n¿Abrir la carpeta de destino?";
                    if (MessageBox.Show(this, msg, "Copia terminada", MessageBoxButton.YesNo,
                            errors > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information) == MessageBoxResult.Yes)
                    {
                        try { Process.Start("explorer.exe", "\"" + baseDir + "\""); } catch { }
                    }
                }));
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA); // la papelera de Windows lo necesita
            t.Start();
        }

        // Imágenes en una carpeta de copias que no corresponden a ninguna foto de la selección esperada.
        // Nunca devuelve originales ni nada fuera de esa carpeta.
        List<string> FindStaleCopies(string folder, HashSet<string> expected)
        {
            var res = new List<string>();
            if (!Directory.Exists(folder) || string.IsNullOrEmpty(settings.CarpetaFotos)) return res;
            if (FolderKey(folder) == FolderKey(settings.CarpetaFotos)) return res;
            var originals = new HashSet<string>(all.Select(x => Path.GetFullPath(x.Path)), StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.EnumerateFiles(folder))
            {
                string name = Path.GetFileName(f);
                if (!Extensions.Contains(Path.GetExtension(f))) continue;
                if (expected.Contains(name) || expected.Contains(StripCopySuffix(name))) continue;
                if (originals.Contains(Path.GetFullPath(f))) continue;
                res.Add(f);
            }
            return res;
        }

        // «IMG_4 (1).jpg» → «IMG_4.jpg» (copias renombradas por conflicto de nombre)
        static string StripCopySuffix(string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(name, @"^(.*) \(\d+\)(\.[^.]+)$");
            return m.Success ? m.Groups[1].Value + m.Groups[2].Value : name;
        }

        static bool HasRecycleBin(string path)
        {
            try { return new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))).DriveType == DriveType.Fixed; }
            catch { return false; }
        }

        static void CopyFileTimes(FileInfo src, string dest)
        {
            var attrs = File.GetAttributes(dest);
            bool ro = (attrs & FileAttributes.ReadOnly) != 0;
            if (ro) File.SetAttributes(dest, attrs & ~FileAttributes.ReadOnly);
            File.SetCreationTimeUtc(dest, src.CreationTimeUtc);
            File.SetLastWriteTimeUtc(dest, src.LastWriteTimeUtc);
            File.SetLastAccessTimeUtc(dest, src.LastAccessTimeUtc);
            File.SetAttributes(dest, src.Attributes & ~FileAttributes.Compressed & ~FileAttributes.Encrypted);
        }

        static string FindRenamedCopy(string path, FileInfo src)
        {
            string dir = Path.GetDirectoryName(path), name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            for (int i = 1; ; i++)
            {
                string p = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(p)) return null;
                if (new FileInfo(p).Length == src.Length && SameContent(p, src.FullName)) return p;
            }
        }

        static bool SameContent(string a, string b)
        {
            using (var fa = File.OpenRead(a))
            using (var fb = File.OpenRead(b))
            {
                var ba = new byte[1 << 16];
                var bb = new byte[1 << 16];
                while (true)
                {
                    int na = fa.Read(ba, 0, ba.Length);
                    int nb = fb.Read(bb, 0, na > 0 ? na : bb.Length);
                    if (na != nb) return false;
                    if (na == 0) return true;
                    for (int i = 0; i < na; i++) if (ba[i] != bb[i]) return false;
                }
            }
        }

        static string UniqueName(string path)
        {
            string dir = Path.GetDirectoryName(path), name = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            for (int i = 1; ; i++)
            {
                string p = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(p)) return p;
            }
        }
    }
}
