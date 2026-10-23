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
    // Marcas, deshacer, navegación, filtros y teclado.
    public partial class MainWindow
    {
        // ---------------------------------------------------------------- Deshacer y sin marca

        void Undo()
        {
            if (undoStack.Count == 0)
            {
                Toast("No hay nada que deshacer");
                return;
            }
            var group = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            var it = group[0].Key;
            // Volver a la foto (aunque el filtro la oculte) y restaurar su marca.
            sticky = it;
            RebuildView(it);
            ShowCurrent();
            foreach (var g in group) ApplyMark(g.Key, g.Value, false);
            if (group.Count > 1)
            {
                Toast("Deshecha la importación: " + group.Count + " fotos vuelven a su marca anterior");
                return;
            }
            var last = group[0];
            string what = last.Value == Mark.Fav ? "favorita" : last.Value == Mark.Reject ? "no deseada" : "sin marca";
            Toast("Deshecho: " + it.Name + " vuelve a «" + what + "»");
        }

        void PushUndo(List<KeyValuePair<PhotoItem, Mark>> group)
        {
            if (group == null || group.Count == 0) return;
            undoStack.Add(group);
            if (undoStack.Count > 1000) undoStack.RemoveAt(0);
        }

        // Salta a la siguiente (dir = 1) o anterior (dir = -1) foto con esa marca; al llegar al final da la vuelta.
        void GoToMark(Mark mark, int dir)
        {
            if (view.Count == 0 || idx < 0) return;
            string what = mark == Mark.Fav ? "favorita" : mark == Mark.Reject ? "no deseada" : "sin marca";
            for (int k = 1; k < view.Count; k++)
            {
                int j = ((idx + dir * k) % view.Count + view.Count) % view.Count;
                if (view[j].Mark != mark) continue;
                if (dir > 0 && j < idx) Toast("Siguiente " + what + ": volviendo al principio");
                if (dir < 0 && j > idx) Toast("Anterior " + what + ": volviendo al final");
                GoTo(j);
                return;
            }
            bool visible = mark == Mark.Fav ? settings.VerFavoritas : mark == Mark.Reject ? settings.VerNoDeseadas : settings.VerSinMarca;
            if (!visible) Toast("Activa «" + (mark == Mark.Fav ? "Favoritas" : mark == Mark.Reject ? "No deseadas" : "Sin marca") + "» en Mostrar para usar este atajo");
            else Toast(mark == Mark.Fav ? "No hay otras favoritas" : mark == Mark.Reject ? "No hay otras no deseadas" : "No quedan fotos sin marca");
        }

        void UpdateStatus()
        {
            var it = Current;
            if (it == null) { favPosText.Visibility = Visibility.Collapsed; return; }
            // Cada parte: { texto que se muestra, valor que se copia (null = no se copia) }
            var parts = new List<string[]>();
            // posición y porcentaje recorrido (la última foto es el 100 %)
            string pct = ((idx + 1) * 100.0 / view.Count).ToString("0.0");
            parts.Add(new[] { (idx + 1) + " / " + view.Count + "   (" + pct + " %)", (idx + 1) + " / " + view.Count });
            parts.Add(new[] { it.Name, it.Name });
            string date = (it.Taken.HasValue ? it.Taken.Value : it.Modified).ToString("dd/MM/yyyy HH:mm:ss");
            parts.Add(new[] { (it.Taken.HasValue ? "Captura: " : "Modificada: ") + date, date });
            parts.Add(new[] { FormatSize(it.Size), FormatSize(it.Size) });
            if (it.Width > 0 && it.Height > 0)
            {
                bool rot = it.Orientation >= 5 && it.Orientation <= 8;
                string dims = rot ? it.Height + " × " + it.Width : it.Width + " × " + it.Height;
                parts.Add(new[] { dims, dims.Replace(" × ", "x") });
            }
            if (it.Mark == Mark.Fav)
            {
                parts.Add(new[] { "✓ Favorita", null });
                // qué número de favorita es, en el orden actual (cuenta todas, aunque el filtro oculte otras fotos)
                int pos = 0, total = 0;
                foreach (var x in all)
                {
                    if (x.Mark != Mark.Fav) continue;
                    total++;
                    if (x == it) pos = total;
                }
                favPosText.Text = "Favorita " + pos + " de " + total;
            }
            else if (it.Mark == Mark.Reject) parts.Add(new[] { "✕ No deseada", null });
            favPosText.Foreground = statusFgBrush;
            favPosText.Visibility = it.Mark == Mark.Fav ? Visibility.Visible : Visibility.Collapsed;
            SetStatusParts(parts);
        }

        void SetStatusParts(List<string[]> parts)
        {
            statusParts.Children.Clear();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) statusParts.Children.Add(new TextBlock { Text = "     ·     ", Foreground = statusFgBrush, FontWeight = FontWeights.SemiBold });
                string shown = parts[i][0], copy = parts[i][1];
                var tb = new TextBlock { Text = shown, Foreground = statusFgBrush, FontWeight = FontWeights.SemiBold };
                if (copy != null)
                {
                    tb.Cursor = Cursors.Hand;
                    tb.ToolTip = "Clic para copiar: " + copy;
                    tb.MouseEnter += (s, e) => tb.TextDecorations = TextDecorations.Underline;
                    tb.MouseLeave += (s, e) => tb.TextDecorations = null;
                    tb.MouseLeftButtonUp += (s, e) => CopyToClipboard(copy);
                }
                statusParts.Children.Add(tb);
            }
        }

        void CopyToClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
                Toast("Copiado: " + text);
            }
            catch
            {
                Toast("No se pudo copiar al portapapeles");
            }
        }

        static string FormatSize(long bytes)
        {
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("N0") + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("N1") + " MB";
        }

        // ---------------------------------------------------------------- Navegación y filtros

        void Go(int delta)
        {
            if (view.Count == 0 || idx < 0) return;
            GoTo(idx + delta);
        }

        void GoTo(int target)
        {
            if (view.Count == 0) return;
            target = Math.Max(0, Math.Min(view.Count - 1, target));
            if (target == idx && sticky == null) return;
            if (target != idx) lastDir = target > idx ? 1 : -1;
            var item = view[target];
            startPending = false;
            sticky = null; // al avanzar, la foto marcada deja de verse si no cumple el filtro
            RebuildView(item);
            ShowCurrent();
        }

        bool Pass(PhotoItem it)
        {
            if (it.Mark == Mark.Fav) return settings.VerFavoritas;
            if (it.Mark == Mark.Reject) return settings.VerNoDeseadas;
            return settings.VerSinMarca;
        }

        void RebuildView(PhotoItem keep)
        {
            view = all.Where(x => Pass(x) || x == sticky).ToList();
            idx = -1;
            if (view.Count == 0) return;
            if (keep == null) { idx = 0; return; }
            int i = view.IndexOf(keep);
            if (i >= 0) { idx = i; return; }
            // La foto ya no se muestra: ir a la más cercana que sí se muestre.
            int p = all.IndexOf(keep);
            if (p >= 0)
            {
                for (int j = p + 1; j < all.Count; j++)
                    if (Pass(all[j])) { idx = view.IndexOf(all[j]); return; }
                for (int j = p - 1; j >= 0; j--)
                    if (Pass(all[j])) { idx = view.IndexOf(all[j]); return; }
            }
            idx = 0;
        }

        void OnFilterChanged()
        {
            settings.VerFavoritas = chkFav.IsChecked == true;
            settings.VerSinMarca = chkNone.IsChecked == true;
            settings.VerNoDeseadas = chkRej.IsChecked == true;
            var cur = Current;
            sticky = null;
            RebuildView(cur);
            ShowCurrent();
            SaveSettings();
        }

        void SortAll()
        {
            Comparison<PhotoItem> byName = (a, b) => Native.StrCmpLogicalW(a.Name, b.Name);
            Comparison<PhotoItem> c;
            switch (settings.Orden)
            {
                case 1: c = (a, b) => { int r = a.Modified.CompareTo(b.Modified); return r != 0 ? r : byName(a, b); }; break;
                case 2: c = byName; break;
                case 3: c = (a, b) => { int r = a.Size.CompareTo(b.Size); return r != 0 ? r : byName(a, b); }; break;
                case 4: c = (a, b) => { int r = string.Compare(a.Ext, b.Ext, StringComparison.OrdinalIgnoreCase); return r != 0 ? r : byName(a, b); }; break;
                default: c = (a, b) => { int r = a.SortDate.CompareTo(b.SortDate); return r != 0 ? r : byName(a, b); }; break;
            }
            if (settings.Descendente) all.Sort((a, b) => c(b, a));
            else all.Sort(c);
        }

        void Resort()
        {
            var cur = Current;
            SortAll();
            RebuildView(cur);
            ShowCurrent();
            SaveSettings();
        }

        // ---------------------------------------------------------------- Marcas

        // Alt sola (pulsar y soltar sin otra tecla): abre el menú de opciones ⚙, como la barra de menús de Windows
        bool altAlone;
        Button btnOpts;

        void OnKeyUpAlt(object sender, KeyEventArgs e)
        {
            var k = e.Key == Key.System ? e.SystemKey : e.Key;
            if ((k == Key.LeftAlt || k == Key.RightAlt) && altAlone)
            {
                altAlone = false;
                e.Handled = true;
                if (btnOpts != null && btnOpts.IsVisible) ShowOptionsMenu(btnOpts);
            }
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            var sys = e.Key == Key.System ? e.SystemKey : e.Key;
            if (sys == Key.LeftAlt || sys == Key.RightAlt)
            {
                if (!e.IsRepeat) altAlone = true;
                e.Handled = true; // que Windows no active su menú de la ventana
                return;
            }
            altAlone = false; // Alt + otra tecla (Alt + F…) no abre el menú
            if (cmbOrden.IsDropDownOpen) return;
            // Vista de ráfaga abierta: sus propias teclas; ↑ ↓ y Ctrl + Z siguen como siempre
            if (burstMode && HandleBurstKey(e.Key == Key.System ? e.SystemKey : e.Key)) { e.Handled = true; return; }
            // Z sola (mantenida): lupa al 100 % donde está el ratón
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (!e.IsRepeat) StartLoupe();
                e.Handled = true;
                return;
            }
            // Alt + F / N / X: activar o desactivar el filtro «Mostrar» de favoritas, sin marca o no deseadas
            // (con Alt, WPF entrega la tecla en SystemKey)
            if (e.Key == Key.System && Keyboard.Modifiers == ModifierKeys.Alt)
            {
                CheckBox chk = e.SystemKey == Key.F ? chkFav : e.SystemKey == Key.N ? chkNone : e.SystemKey == Key.X ? chkRej : null;
                if (chk != null)
                {
                    chk.IsChecked = chk.IsChecked != true;
                    OnFilterChanged();
                    string name = chk == chkFav ? "Favoritas" : chk == chkNone ? "Sin marca" : "No deseadas";
                    Toast("Mostrar «" + name + "»: " + (chk.IsChecked == true ? "sí" : "no"));
                    e.Handled = true;
                    return;
                }
            }
            if (HandleKey(e.Key)) e.Handled = true;
        }

        // W A S D funcionan como las flechas (sin Ctrl ni Alt; con Shift sí, por Shift + ↑ en la ráfaga),
        // así la mano izquierda queda cerca de las demás teclas (F, N, X, C, B, G…)
        static Key WasdToArrow(Key key)
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return key;
            switch (key)
            {
                case Key.W: return Key.Up;
                case Key.A: return Key.Left;
                case Key.S: return Key.Down;
                case Key.D: return Key.Right;
                default: return key;
            }
        }

        bool HandleKey(Key key)
        {
            key = WasdToArrow(key);
            switch (key)
            {
                case Key.Left: Go(-1); break;
                case Key.Right: Go(1); break;
                case Key.Up:
                    if (Current != null) SetMark(Current.Mark == Mark.Reject ? Mark.None : Mark.Fav);
                    break;
                case Key.Down:
                    if (Current != null) SetMark(Current.Mark == Mark.Fav ? Mark.None : Mark.Reject);
                    break;
                case Key.D0: case Key.NumPad0:
                    if (CtrlDown) SetUiScale(100); else ResetZoom();
                    break;
                case Key.M: ToggleStripSide(); break;
                case Key.G:
                    if (CtrlDown) ShowGoTo(); else ToggleGrid();
                    break;
                case Key.Enter:
                    if (!gridMode) return false;
                    ExitGrid();
                    break;
                case Key.I: ToggleInfo(); break;
                case Key.E: ShowInExplorer(); break;
                case Key.O: OpenExternal(); break;
                case Key.P: StartSlideshow(); break;
                case Key.C: OnCompareKey(); break;
                case Key.B: ToggleBurst(); break;
                case Key.N: GoToMark(Mark.None, (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1); break;
                case Key.F: GoToMark(Mark.Fav, (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1); break;
                case Key.U: case Key.Back: // quitar la marca (como U en Lightroom)
                    if (Current != null && Current.Mark != Mark.None) SetMark(Mark.None);
                    break;
                case Key.X: GoToMark(Mark.Reject, (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1); break;
                case Key.Z:
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return false;
                    Undo();
                    break;
                case Key.OemPlus: case Key.Add:
                    if (CtrlDown) StepUiScale(1); else if (gridMode) StepGridCols(-1); else ZoomCenter(1.25);
                    break;
                case Key.OemMinus: case Key.Subtract:
                    if (CtrlDown) StepUiScale(-1); else if (gridMode) StepGridCols(1); else ZoomCenter(0.8);
                    break;
                case Key.Home: GoTo(0); break;
                case Key.End: GoTo(view.Count - 1); break;
                case Key.PageUp: Go(-10); break;
                case Key.PageDown: Go(10); break;
                case Key.F11: if (fullView) ExitFullView(); else ToggleFullscreen(); break;
                case Key.Space: ToggleFullView(); break; // vista completa: solo la foto
                case Key.Escape:
                    if (gridMode) ExitGrid();
                    else if (IsZoomed) ResetZoom();
                    else if (fullView) ExitFullView();
                    else if (settings.Comparar) ToggleCompare(); // como en la vista de ráfaga: Esc desactiva el modo
                    else if (fullscreen) ToggleFullscreen();
                    else return false;
                    break;
                case Key.F5: if (!string.IsNullOrEmpty(settings.CarpetaFotos)) OpenPhotoFolder(settings.CarpetaFotos); break;
                default: return false;
            }
            return true;
        }



        void SetMark(Mark m)
        {
            var it = Current;
            if (it == null) return;
            var old = it.Mark;
            ApplyMark(it, m, true);
            // Opción «Avanzar al marcar»: tras marcar (no al quitar una marca) pasa a la siguiente foto
            if (settings.AvanzarAlMarcar && m != Mark.None) Go(1);
            // con «Animar la marca»: también al marcar; al quitarla, el contorno vacío de la marca que tenía.
            // En la vista de ráfaga se anima sobre la foto marcada (aunque ya se haya pasado a la siguiente).
            var target = burstMode ? it : null;
            if (m == Mark.None) { if (old != Mark.None) AnimateMark(old, true, target); }
            else AnimateMark(m, false, target);
        }

        void ToggleAutoAdvance()
        {
            settings.AvanzarAlMarcar = !settings.AvanzarAlMarcar;
            SaveSettings();
            Toast(settings.AvanzarAlMarcar ? "Avanzar al marcar: ↑ / ↓ marcan y pasan a la siguiente foto" : "Avanzar al marcar: desactivado");
        }

        void ApplyMark(PhotoItem it, Mark m, bool recordUndo)
        {
            if (it.Mark == m) return;
            if (folderData == null)
            {
                MessageBox.Show(this, "Para guardar las marcas primero elige la carpeta de datos.", "Selector de Fotos",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                ChooseDataFolder();
                if (folderData == null) return;
            }
            if (readOnlyData)
            {
                Toast("Solo lectura: las marcas de esta ventana no se guardan (ver el aviso al abrir)");
                return;
            }
            if (recordUndo)
            {
                PushUndo(new List<KeyValuePair<PhotoItem, Mark>> { new KeyValuePair<PhotoItem, Mark>(it, it.Mark) });
            }
            it.Mark = m;
            startPending = false;
            if (m == Mark.None) folderData.marcas.Remove(it.Name);
            else folderData.marcas[it.Name] = m == Mark.Fav ? "fav" : "no";
            if (!Pass(it)) sticky = it;
            ScheduleSave();
            UpdateCounts();
            UpdateMarkBadge();
            UpdateStatus();
            foreach (var s in slots)
                if (s.Item == it) SetSlotMark(s);
        }
    }
}
