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
    // Importar y exportar la lista de fotos elegidas.
    public partial class MainWindow
    {
        // ---------------------------------------------------------------- Importar lista

        void ImportList()
        {
            if (all.Count == 0)
            {
                MessageBox.Show(this, "Primero abre una carpeta de fotos.", "Importar lista", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dlg = new ImportDialog(this);
            if (dlg.ShowDialog() == true) ImportNames(dlg.ListText, true);
        }

        // Una referencia de la lista → foto: nombre exacto, sin extensión o solo el número.
        PhotoItem MatchToken(string token)
        {
            var found = FindByName(token);
            if (found.Count == 1) return found[0];
            if (found.Count > 1)
            {
                // Varias candidatas: solo vale si una coincide exactamente
                var exact = found.FirstOrDefault(x => string.Equals(x.Name, token, StringComparison.OrdinalIgnoreCase) ||
                                                      string.Equals(Path.GetFileNameWithoutExtension(x.Name), token, StringComparison.OrdinalIgnoreCase));
                return exact;
            }
            return null;
        }

        void ImportNames(string text, bool interactive)
        {
            var tokens = Regex.Split(text ?? "", @"[\s,;]+")
                .Select(x => x.Trim().Trim('"', '\'', '(', ')', '[', ']', '.'))
                .Where(x => x.Length > 0 && Regex.IsMatch(x, @"\d"))          // solo lo que parece una foto (lleva números)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var found = new List<PhotoItem>();
            var missing = new List<string>();
            foreach (var tk in tokens)
            {
                var m = MatchToken(tk);
                if (m == null) missing.Add(tk);
                else if (!found.Contains(m)) found.Add(m);
            }
            if (found.Count == 0)
            {
                if (interactive)
                    MessageBox.Show(this, "No se encontró ninguna foto de la lista en esta carpeta." +
                        (tokens.Count > 0 ? "\n\nReferencias leídas: " + string.Join(", ", tokens.Take(10)) + (tokens.Count > 10 ? "…" : "") : ""),
                        "Importar lista", MessageBoxButton.OK, MessageBoxImage.Information);
                else Toast("Importar: ninguna foto encontrada");
                return;
            }
            var toMark = found.Where(x => x.Mark != Mark.Fav).ToList();
            int already = found.Count - toMark.Count;
            if (interactive)
            {
                string msg = "Encontradas: " + found.Count + " de " + tokens.Count + " fotos de la lista." +
                             (already > 0 ? "\nYa eran favoritas: " + already : "") +
                             (missing.Count > 0 ? "\nNo encontradas (" + missing.Count + "): " + string.Join(", ", missing.Take(12)) + (missing.Count > 12 ? "…" : "") : "");
                if (toMark.Count == 0)
                {
                    MessageBox.Show(this, msg + "\n\nTodas las encontradas ya eran favoritas.", "Importar lista", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                msg += "\n\n¿Marcar " + toMark.Count + " foto(s) como favoritas?\n(Se puede deshacer de una vez con Ctrl + Z)";
                if (MessageBox.Show(this, msg, "Importar lista", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            }
            if (folderData == null)
            {
                MessageBox.Show(this, "Para guardar las marcas primero elige la carpeta de datos.", "Importar lista", MessageBoxButton.OK, MessageBoxImage.Information);
                ChooseDataFolder();
                if (folderData == null) return;
            }
            if (readOnlyData)
            {
                Toast("Solo lectura: las marcas de esta ventana no se guardan");
                return;
            }
            var group = new List<KeyValuePair<PhotoItem, Mark>>();
            foreach (var it in toMark)
            {
                var prev = it.Mark;
                ApplyMark(it, Mark.Fav, false);
                if (it.Mark == Mark.Fav) group.Add(new KeyValuePair<PhotoItem, Mark>(it, prev));
            }
            PushUndo(group);
            Toast("Importadas: " + group.Count + " foto(s) marcadas como favoritas  (Ctrl + Z para deshacer)");
        }

        // ---------------------------------------------------------------- Exportar lista

        void ExportList(bool interactive)
        {
            var favs = all.Where(x => x.Mark == Mark.Fav).ToList();
            if (favs.Count == 0)
            {
                if (interactive) MessageBox.Show(this, "Todavía no hay favoritas.", "Exportar lista", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrEmpty(settings.CarpetaDatos) || !Directory.Exists(settings.CarpetaDatos))
            {
                if (interactive) MessageBox.Show(this, "Primero elige la carpeta de datos.", "Exportar lista", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var names = favs.Select(x => x.Name).ToList();
            string file = Path.Combine(settings.CarpetaDatos, "favoritas - " + FolderName(settings.CarpetaFotos) + ".txt");
            try
            {
                File.WriteAllLines(file, names, new System.Text.UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo guardar la lista:\n" + ex.Message, "Exportar lista", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            bool clip = false;
            try { Clipboard.SetText(string.Join(", ", names)); clip = true; } catch { }

            if (!interactive)
            {
                Toast("Lista exportada: " + names.Count + " favoritas");
                return;
            }
            string msg = "Se guardaron " + names.Count + " favoritas en:\n" + file +
                (clip ? "\n\nLa lista también está copiada en el portapapeles (separada por comas), lista para pegar en un mensaje." : "") +
                "\n\n¿Abrir el archivo?";
            if (MessageBox.Show(this, msg, "Exportar lista", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                try { Process.Start(file); } catch { }
            }
        }
    }
}
