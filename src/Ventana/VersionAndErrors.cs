using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SelectorDeFotos
{
    // Versión, «Acerca de» y registro de errores.
    public partial class MainWindow
    {
        public const string AppVersion = "1.0";
        public const string AppVersionDate = "octubre de 2026";
        public const string AppAuthor = "SamukSchiller";
        public const string AppCopyright = "© 2026 " + AppAuthor;
        public const string AppLicense = "MIT";
        public const string AppRepoUrl = "https://github.com/SamukSchiller/SelectorDeFotos";
        const string ErrorLogName = "errores.log";

        static MainWindow instance; // para el registro de errores (lo llaman los manejadores globales)

        // Archivo de errores: en data\ de la carpeta de datos; sin carpeta de datos, junto al programa
        string ErrorLogPath
        {
            get
            {
                if (!string.IsNullOrEmpty(settings.CarpetaDatos) && Directory.Exists(settings.CarpetaDatos))
                    return Path.Combine(settings.CarpetaDatos, AppDataFolderName, ErrorLogName);
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SelectorDeFotos." + ErrorLogName);
            }
        }

        // Guarda un error inesperado con fecha, versión y detalle (para poder diagnosticarlo después).
        // Nunca lanza: si no se puede escribir, se ignora.
        public static void LogError(Exception ex, string where)
        {
            try
            {
                var w = instance;
                string path = w != null ? w.ErrorLogPath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SelectorDeFotos." + ErrorLogName);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // Si crece demasiado (más de 1 MB), se empieza de nuevo guardando el anterior
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                    File.Copy(path, path + ".anterior", true);
                var sb = new StringBuilder();
                sb.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  Selector de Fotos " + AppVersion + "  (" + where + ")");
                if (w != null && w.Current != null) sb.AppendLine("Foto actual: " + w.Current.Name);
                sb.AppendLine(ex.ToString());
                sb.AppendLine();
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                else File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        void ShowAbout()
        {
            new AboutDialog(this).ShowDialog();
        }

        public string AboutDataFolder { get { return string.IsNullOrEmpty(settings.CarpetaDatos) ? null : settings.CarpetaDatos; } }
        public string AboutPhotosFolder { get { return string.IsNullOrEmpty(settings.CarpetaFotos) ? null : settings.CarpetaFotos; } }
        public string AboutErrorLog { get { return ErrorLogPath; } }
    }
}
