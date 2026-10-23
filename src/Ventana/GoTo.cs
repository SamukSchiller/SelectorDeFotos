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
    // Ir a una foto por número o nombre (Ctrl + G).
    public partial class MainWindow
    {
        void ShowGoTo()
        {
            if (all.Count == 0) return;
            var dlg = new GoToDialog(this, view.Count);
            if (dlg.ShowDialog() == true) GoToText(dlg.Value);
        }

        // «IMG_8410», «8410» o «IMG_8410.jpg» → por nombre; «#1156» → por posición.
        void GoToText(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            int n;
            if (text.StartsWith("#"))
            {
                if (int.TryParse(text.Substring(1).Trim(), out n) && n >= 1 && n <= view.Count) { GoTo(n - 1); return; }
                Toast("Posición fuera de rango (1 – " + view.Count + ")");
                return;
            }
            var found = FindByName(text);
            if (found.Count == 0)
            {
                if (int.TryParse(text, out n) && n >= 1 && n <= view.Count)
                {
                    GoTo(n - 1);
                    Toast("Ninguna foto tiene «" + text + "» en el nombre: se fue a la posición " + n);
                    return;
                }
                Toast("No se encontró ninguna foto con «" + text + "»");
                return;
            }
            JumpToItem(found[0]);
            if (found.Count > 1) Toast(found.Count + " fotos coinciden con «" + text + "»: se muestra " + found[0].Name);
        }

        List<PhotoItem> FindByName(string text)
        {
            string t = text.Trim().ToLowerInvariant();
            var exact = all.Where(x => x.Name.ToLowerInvariant() == t ||
                                       Path.GetFileNameWithoutExtension(x.Name).ToLowerInvariant() == t).ToList();
            if (exact.Count > 0) return exact;
            // Solo el número: «8410» encuentra IMG_8410 (pero no IMG_84100)
            if (Regex.IsMatch(t, @"^\d+$"))
                return all.Where(x => Regex.IsMatch(Path.GetFileNameWithoutExtension(x.Name), @"(^|\D)0*" + t.TrimStart('0') + @"(\D|$)")).ToList();
            return all.Where(x => x.Name.ToLowerInvariant().Contains(t)).ToList();
        }

        void JumpToItem(PhotoItem it)
        {
            // Si el filtro la oculta, se muestra igualmente hasta que avances.
            sticky = Pass(it) ? null : it;
            startPending = false;
            RebuildView(it);
            ShowCurrent();
        }
    }
}
