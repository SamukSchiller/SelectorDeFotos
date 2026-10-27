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
    // Tamaño de la interfaz, restablecer preferencias y avisos.
    public partial class MainWindow
    {
        // ---------------------------------------------------------------- Tamaño de la interfaz

        static bool CtrlDown { get { return (Keyboard.Modifiers & ModifierKeys.Control) != 0; } }
        const int UiStep = 10; // la interfaz se ajusta de 10 % en 10 %

        void StepUiScale(int dir)
        {
            int cur = settings.EscalaInterfaz;
            // Se ajusta a múltiplos de 10: 100 → 110 → 120…
            int next = dir > 0 ? (cur / UiStep + 1) * UiStep : ((cur + UiStep - 1) / UiStep - 1) * UiStep;
            SetUiScale(next);
        }

        void SetUiScale(int percent)
        {
            percent = Math.Max(50, Math.Min(200, percent));
            if (percent == settings.EscalaInterfaz) { Toast("Tamaño de la interfaz: " + percent + " %"); return; }
            settings.EscalaInterfaz = percent;
            SaveSettings();
            ApplyUiScale(true);
            Toast("Tamaño de la interfaz: " + percent + " %" + (percent != 100 ? "   (Ctrl + 0 para volver al 100 %)" : ""));
        }

        void ApplyUiScale(bool relayout)
        {
            double s = UiScale;
            uiRoot.LayoutTransform = s == 1 ? null : new ScaleTransform(s, s);
            if (!relayout) return;
            ResetZoom();
            OnStripResized(); // miniaturas al nuevo tamaño en pantalla
            Dispatcher.BeginInvoke(new Action(LayoutStrip), DispatcherPriority.Loaded);
        }

        // ---------------------------------------------------------------- Restablecer preferencias

        // Vuelve todo a los valores por defecto, salvo las carpetas. Las marcas (archivo de datos) no se tocan.
        void ResetPreferences(bool ask)
        {
            if (ask && MessageBox.Show(this,
                    "Se restablecerán los valores por defecto de:\n" +
                    "   • tamaño de la interfaz\n" +
                    "   • miniaturas: posición, tamaño, columnas y filas, cuadrícula justificada\n" +
                    "   • comparar y su formato, panel de información, barra compacta, separar momentos\n" +
                    "   • objetivo de favoritas, orden, filtros de «Mostrar» y velocidad de la presentación\n\n" +
                    "Se mantienen las carpetas de fotos y de datos, todas las marcas y las miniaturas guardadas en disco.\n\n¿Continuar?",
                    "Restablecer preferencias", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            if (gridMode) ExitGrid();
            // Se conservan las carpetas y la opción de miniaturas en disco (no es una preferencia visual)
            settings = new Settings
            {
                CarpetaFotos = settings.CarpetaFotos, CarpetaDatos = settings.CarpetaDatos,
                GuardarMiniaturas = settings.GuardarMiniaturas
            };
            SaveSettings();

            initializing = true; // evitar reordenar dos veces al cambiar el selector
            cmbOrden.SelectedIndex = Math.Max(0, Math.Min(4, settings.Orden));
            initializing = false;
            UpdateDirButton();
            chkFav.IsChecked = settings.VerFavoritas;
            chkNone.IsChecked = settings.VerSinMarca;
            chkRej.IsChecked = settings.VerNoDeseadas;
            sideStrip = settings.MiniaturasDerecha;
            ApplyStripMode();
            ApplyUiScale(true);
            UpdateLabels();
            UpdateCounts();
            var cur = Current;
            sticky = null;
            SortAll();
            RebuildView(cur);
            ResetZoom();
            ShowCurrent();
            UpdateMarkBadge();
            Toast("Preferencias restablecidas a los valores por defecto");
        }

        // ---------------------------------------------------------------- Avisos

        void Toast(string text)
        {
            toastText.Text = text;
            toast.Visibility = Visibility.Visible;
            toastTimer.Stop();
            toastTimer.Start();
        }
    }
}
