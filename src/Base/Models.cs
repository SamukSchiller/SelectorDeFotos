using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace SelectorDeFotos
{
    public enum Mark { None = 0, Fav = 1, Reject = -1 }

    public class PhotoItem
    {
        public string Path;
        public string Name;
        public string Ext;
        public long Size;
        public DateTime Modified;
        public DateTime? Taken;
        public int Orientation = 1;
        public int Width;
        public int Height;
        public volatile bool MetaRead;
        public Mark Mark;
        public double Sharpness = -1; // nitidez (con miniaturas en disco; -1 = sin calcular)
        public ulong Hash;            // huella visual para agrupar fotos parecidas
        public bool HasHash;
        public BurstGroup Burst;   // ráfaga a la que pertenece (null = ninguna)
        public int BurstPos;       // posición dentro de la ráfaga (0 = primera)

        public DateTime SortDate { get { return Taken.HasValue ? Taken.Value : Modified; } }
    }

    // Ráfaga: fotos seguidas tomadas casi al mismo tiempo (ver Bursts.cs)
    public class BurstGroup
    {
        public List<PhotoItem> Items = new List<PhotoItem>();
        public DateTime Start, End;
    }

    // Datos guardados en la carpeta de datos (marcas por carpeta de fotos).
    public class FolderData
    {
        public Dictionary<string, string> marcas { get; set; }
        public string ultima { get; set; }
        // Caché de metadatos: nombre -> "tamaño|modificado|captura|orientación|ancho|alto"
        public Dictionary<string, string> meta { get; set; }
    }

    public class DataFile
    {
        public int version { get; set; }
        public Dictionary<string, FolderData> carpetas { get; set; }
    }

    // Ajustes de la aplicación (junto al .exe).
    public class Settings
    {
        public string CarpetaFotos { get; set; }
        public string CarpetaDatos { get; set; }
        public int Orden { get; set; }
        public bool Descendente { get; set; }
        public bool VerFavoritas { get; set; }
        public bool VerSinMarca { get; set; }
        public bool VerNoDeseadas { get; set; }
        public int AlturaMiniaturas { get; set; }
        public int AnchoMiniaturas { get; set; }
        public bool MiniaturasDerecha { get; set; }
        public int ColumnasMiniaturas { get; set; }
        public int FilasMiniaturas { get; set; }
        public int ColumnasCuadricula { get; set; } // 0 = automático según el ancho
        // Opciones (menú ⚙)
        public bool Comparar { get; set; }
        public int ModoComparar { get; set; } // 0 siguiente, 1 anterior, 2 anterior y siguiente
        public int ObjetivoFavoritas { get; set; } // 0 = sin objetivo
        public bool BarraCompacta { get; set; }
        public bool SepararMomentos { get; set; }
        public bool CuadriculaJustificada { get; set; }
        public bool GuardarMiniaturas { get; set; }  // miniaturas en disco (desactivado por defecto)
        public int EscalaInterfaz { get; set; }      // % (100 = normal)
        public bool MostrarInfo { get; set; }        // panel de información (I)
        public bool MostrarIconoMarca { get; set; }  // icono de la marca en la esquina del visor (activado por defecto)
        public bool AnimarMarca { get; set; }        // animación al marcar y al llegar a una foto marcada (activada por defecto)
        public bool MosaicoMarca { get; set; }       // mosaico de checks / cruces en el fondo del visor (activado por defecto)
        public bool ModoRafaga { get; set; }
        public int BordeFoco { get; set; }
        public bool CentrarMiniaturaActual { get; set; }
        public bool MiniaturasIzquierda { get; set; } // tira lateral a la izquierda (por defecto a la derecha)
        public bool MiniaturasArriba { get; set; }    // tira horizontal arriba (por defecto abajo)
        public bool AvanzarAlMarcar { get; set; }     // ↑ / ↓ marcan y pasan a la siguiente (desactivado por defecto)
        public bool AgruparParecidas { get; set; }
        public double SegundosRafaga { get; set; }    // tiempo máximo entre fotos de una ráfaga (por defecto 2 s)    // ráfagas: unir también fotos casi iguales (con miniaturas en disco) // tira con la actual siempre en el centro (si no, desplazamiento libre)            // borde de la actual: 0 fijo, 1 degradado que gira, 2 líneas que avanzan         // B: las ráfagas se ven lado a lado al llegar a ellas        // al ir a una foto marcada, su icono aparece y se desvanece en el centro
        public double SegundosPresentacion { get; set; } // presentación de favoritas (P), de 0,5 en 0,5 s
        // Ventana (se recuerda al cerrar)
        public double VentanaX { get; set; }
        public double VentanaY { get; set; }
        public double VentanaAncho { get; set; }
        public double VentanaAlto { get; set; }
        public bool VentanaMaximizada { get; set; }
        public bool PantallaCompleta { get; set; }

        public Settings()
        {
            VerFavoritas = true;
            VerSinMarca = true;
            VerNoDeseadas = true;
            SepararMomentos = true;
            CuadriculaJustificada = true;
            MostrarIconoMarca = true;
            AnimarMarca = true;
            MosaicoMarca = true;
            ModoComparar = 2; // comparar con la anterior y la siguiente (la actual en el centro)
            BordeFoco = 1;
            CentrarMiniaturaActual = true;
            AgruparParecidas = true;
            SegundosRafaga = 2;
            EscalaInterfaz = 100;
            SegundosPresentacion = 4;
        }
    }

    public static class Json
    {
        static JavaScriptSerializer Serializer()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            return s;
        }

        public static T Load<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                return Serializer().Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch
            {
                return null;
            }
        }

        // Escritura segura:
        //  1. se escribe a un temporal forzando que llegue al disco (WriteThrough + Flush),
        //  2. se vuelve a leer y se comprueba que es JSON válido,
        //  3. se reemplaza el archivo de forma atómica; con backupPath, la versión anterior queda en ese archivo.
        // Si algo falla, el archivo bueno no se toca.
        public static void Save(string path, object value, string backupPath = null, string tmpPath = null)
        {
            string json = Serializer().Serialize(value);
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            string tmp = tmpPath ?? path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            string check = File.ReadAllText(tmp, Encoding.UTF8);
            if (check.Length != json.Length) throw new IOException("La verificación del archivo guardado falló.");
            Serializer().DeserializeObject(check); // lanza excepción si no es JSON válido

            if (File.Exists(path)) File.Replace(tmp, path, backupPath, true);
            else File.Move(tmp, path);
        }

        // Lectura que distingue «no se pudo leer» (bloqueado, desconectado…) de «está dañado».
        public enum ReadResult { Ok, Missing, Unreadable, Corrupt }

        public static ReadResult TryRead<T>(string path, out T value) where T : class
        {
            value = null;
            if (!File.Exists(path)) return ReadResult.Missing;
            string text = null;
            for (int attempt = 0; attempt < 5 && text == null; attempt++)
            {
                try { text = File.ReadAllText(path, Encoding.UTF8); }
                catch (IOException) { System.Threading.Thread.Sleep(300); }   // p. ej. OneDrive o el antivirus lo tienen abierto
                catch (UnauthorizedAccessException) { System.Threading.Thread.Sleep(300); }
            }
            if (text == null) return ReadResult.Unreadable;
            try
            {
                value = Serializer().Deserialize<T>(text);
                return value != null ? ReadResult.Ok : ReadResult.Corrupt;
            }
            catch
            {
                return ReadResult.Corrupt;
            }
        }
    }
}
