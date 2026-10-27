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
    // Panel de información de la foto (I).
    public partial class MainWindow
    {
        Border infoPanel;
        TextBlock infoText;
        readonly Dictionary<string, string> infoCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        PhotoItem infoLoading;

        void BuildExtrasUi()
        {
            infoText = new TextBlock { Foreground = Brushes.White, FontSize = 13, LineHeight = 21 };
            infoPanel = new Border
            {
                Background = B("#D0141414"), BorderBrush = B("#3A3A3A"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 16, 10),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(14), Child = BuildHistogramUi(infoText), IsHitTestVisible = false, // mismo margen abajo que a la izquierda
                Visibility = Visibility.Collapsed
            };
            mainPane.Children.Add(infoPanel);
            center.ContextMenu = BuildPhotoMenu();
            strip.ContextMenu = BuildStripMenu();
        }

        void ToggleInfo()
        {
            settings.MostrarInfo = !settings.MostrarInfo;
            SaveSettings();
            UpdateInfoPanel();
            Toast(settings.MostrarInfo ? "Panel de información activado (I para ocultarlo)" : "Panel de información oculto");
        }

        void UpdateInfoPanel()
        {
            if (infoPanel == null) return;
            var it = Current;
            if (!settings.MostrarInfo || it == null)
            {
                infoPanel.Visibility = Visibility.Collapsed;
                return;
            }
            infoPanel.Visibility = Visibility.Visible;
            UpdateHistogram(it);
            string cached;
            if (infoCache.TryGetValue(it.Path, out cached)) { infoText.Text = cached; return; }
            infoText.Text = it.Name + "\nLeyendo datos de la cámara…";
            if (infoLoading == it) return;
            infoLoading = it;
            var item = it;
            var t = new Thread(() =>
            {
                string s;
                try { s = BuildInfoText(item); } catch { s = item.Name + "\n(sin datos de cámara)"; }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    infoCache[item.Path] = s;
                    if (infoLoading == item) infoLoading = null;
                    if (Current == item && settings.MostrarInfo) infoText.Text = s;
                }));
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        static string BuildInfoText(PhotoItem it)
        {
            var lines = new List<string> { it.Name };
            using (var fs = MetaReader.Open(it.Path))
            {
                var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                BitmapMetadata md = null;
                try { md = dec.Frames[0].Metadata as BitmapMetadata; } catch { }
                Func<string, object> q = name =>
                {
                    try { return md == null ? null : md.GetQuery(name); } catch { return null; }
                };
                string make = Convert.ToString(q("System.Photo.CameraManufacturer"));
                string model = Convert.ToString(q("System.Photo.CameraModel"));
                if (!string.IsNullOrEmpty(model))
                {
                    // «Canon» + «Canon EOS R» → «Canon EOS R»
                    string cam = !string.IsNullOrEmpty(make) && !model.StartsWith(make, StringComparison.OrdinalIgnoreCase) ? make + " " + model : model;
                    lines.Add("Cámara:   " + cam.Trim());
                }
                string lens = Convert.ToString(q("System.Photo.LensModel"));
                if (!string.IsNullOrEmpty(lens)) lines.Add("Objetivo: " + lens.Trim());

                var shot = new List<string>();
                object f = q("System.Photo.FNumber");
                if (f != null) shot.Add("f/" + Convert.ToDouble(f).ToString("0.#"));
                object exp = q("System.Photo.ExposureTime");
                if (exp != null)
                {
                    double e = Convert.ToDouble(exp);
                    if (e > 0) shot.Add(e >= 1 ? e.ToString("0.#") + " s" : "1/" + Math.Round(1 / e) + " s");
                }
                object iso = q("System.Photo.ISOSpeed");
                if (iso != null) shot.Add("ISO " + iso);
                object focal = q("System.Photo.FocalLength");
                if (focal != null) shot.Add(Convert.ToDouble(focal).ToString("0.#") + " mm");
                if (shot.Count > 0) lines.Add(string.Join("   ·   ", shot));
            }
            lines.Add((it.Taken.HasValue ? "Captura:  " + it.Taken.Value.ToString("dd/MM/yyyy HH:mm:ss") : "Modificada: " + it.Modified.ToString("dd/MM/yyyy HH:mm:ss")));
            if (it.Width > 0 && it.Height > 0)
            {
                bool rot = it.Orientation >= 5 && it.Orientation <= 8;
                lines.Add((rot ? it.Height + " × " + it.Width : it.Width + " × " + it.Height) + " px   ·   " + FormatSize(it.Size));
            }
            if (lines.Count <= 3) lines.Insert(1, "(sin datos de cámara)");
            return string.Join("\n", lines);
        }
    }
}
