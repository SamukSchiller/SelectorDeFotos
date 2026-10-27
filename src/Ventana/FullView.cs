using System;
using System.Windows;

namespace SelectorDeFotos
{
    // Vista completa (barra espaciadora): solo la foto, ocupando toda la pantalla (sin barra superior, sin tira de
    // miniaturas, sin barra de estado). Todo lo demás sigue funcionando: navegar, marcar (teclas y barra de abajo),
    // zoom y lupa, comparar, ráfagas, panel de información…
    // Lo que cambia la distribución de la ventana o abre la cuadrícula de miniaturas (G, M, columnas, posición de
    // la tira) primero cierra la vista completa. Espacio o Esc la cierran.
    public partial class MainWindow
    {
        bool fullView;
        bool fullViewMadeFullscreen; // la vista completa puso la ventana en pantalla completa (al salir se quita)

        void ToggleFullView()
        {
            if (fullView) { ExitFullView(); return; }
            if (gridMode) ExitGrid(); // desde la cuadrícula: la foto actual en vista completa
            fullView = true;
            fullViewMadeFullscreen = !fullscreen;
            if (!fullscreen) ToggleFullscreen();
            ApplyFullViewLayout();
            Toast("Vista completa  ·  Espacio o Esc para salir");
        }

        void ExitFullView()
        {
            if (!fullView) return;
            fullView = false;
            ApplyFullViewLayout();
            if (fullViewMadeFullscreen && fullscreen) ToggleFullscreen();
            fullViewMadeFullscreen = false;
        }

        void ApplyFullViewLayout()
        {
            var v = fullView ? Visibility.Collapsed : Visibility.Visible;
            stripHost.Visibility = v;
            splitter.Visibility = v;
            statusBar.Visibility = v;
            if (fullView) toolbar.Visibility = Visibility.Collapsed;
            else if (!fullscreen) toolbar.Visibility = Visibility.Visible;
            center.Margin = new Thickness(fullView ? 0 : 2); // sin el borde fino: la foto llega a los bordes
            ResetZoom();
            if (!fullView) Dispatcher.BeginInvoke(new Action(LayoutStrip), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }
}
