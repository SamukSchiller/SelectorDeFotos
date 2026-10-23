using System;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SelectorDeFotos
{
    // Lee fecha de captura, orientación y dimensiones.
    public static class MetaReader
    {
        public static void Read(PhotoItem it)
        {
            if (it.MetaRead) return;
            try
            {
                if (IsJpeg(it.Ext)) ReadJpeg(it);
                else ReadWic(it);
            }
            catch { }
            it.MetaRead = true;
        }

        public static bool IsJpeg(string ext)
        {
            return ext == ".jpg" || ext == ".jpeg" || ext == ".jpe" || ext == ".jfif";
        }

        public static FileStream Open(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        }

        // Lector EXIF propio para JPEG: mucho más rápido que WIC.
        static void ReadJpeg(PhotoItem it)
        {
            using (var fs = Open(it.Path))
            {
                if (fs.ReadByte() != 0xFF || fs.ReadByte() != 0xD8) { ReadWic(it); return; }
                while (fs.Position < fs.Length)
                {
                    if (fs.ReadByte() != 0xFF) return;
                    int marker = fs.ReadByte();
                    while (marker == 0xFF) marker = fs.ReadByte();
                    if (marker < 0 || marker == 0xD9 || marker == 0xDA) return;
                    int len = (fs.ReadByte() << 8) | fs.ReadByte();
                    if (len < 2) return;
                    bool isSof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                    if (marker == 0xE1 || isSof)
                    {
                        var d = new byte[len - 2];
                        if (!ReadFull(fs, d)) return;
                        if (isSof)
                        {
                            it.Height = (d[1] << 8) | d[2];
                            it.Width = (d[3] << 8) | d[4];
                            return;
                        }
                        ParseExif(d, it);
                    }
                    else
                    {
                        fs.Seek(len - 2, SeekOrigin.Current);
                    }
                }
            }
        }

        static bool ReadFull(Stream s, byte[] buf)
        {
            int off = 0;
            while (off < buf.Length)
            {
                int n = s.Read(buf, off, buf.Length - off);
                if (n <= 0) return false;
                off += n;
            }
            return true;
        }

        static void ParseExif(byte[] d, PhotoItem it)
        {
            if (d.Length < 14 || d[0] != 'E' || d[1] != 'x' || d[2] != 'i' || d[3] != 'f') return;
            int t = 6;
            bool le = d[t] == 'I';
            Func<int, int> u16 = p => le ? (d[p] | d[p + 1] << 8) : (d[p] << 8 | d[p + 1]);
            Func<int, int> u32 = p => le ? (d[p] | d[p + 1] << 8 | d[p + 2] << 16 | d[p + 3] << 24)
                                         : (d[p] << 24 | d[p + 1] << 16 | d[p + 2] << 8 | d[p + 3]);
            string dt0 = null, dtOrig = null, dtDig = null;
            int exifIfd = -1;

            int ifd0 = t + u32(t + 4);
            int n = u16(ifd0);
            for (int i = 0; i < n; i++)
            {
                int p = ifd0 + 2 + i * 12;
                int tag = u16(p);
                if (tag == 0x0112) it.Orientation = u16(p + 8);
                else if (tag == 0x8769) exifIfd = t + u32(p + 8);
                else if (tag == 0x0132) dt0 = Ascii(d, t + u32(p + 8), u32(p + 4));
            }
            if (exifIfd > 0 && exifIfd < d.Length - 2)
            {
                n = u16(exifIfd);
                for (int i = 0; i < n; i++)
                {
                    int p = exifIfd + 2 + i * 12;
                    int tag = u16(p);
                    if (tag == 0x9003) dtOrig = Ascii(d, t + u32(p + 8), u32(p + 4));
                    else if (tag == 0x9004) dtDig = Ascii(d, t + u32(p + 8), u32(p + 4));
                }
            }
            DateTime v;
            if (TryParseExifDate(dtOrig, out v) || TryParseExifDate(dtDig, out v) || TryParseExifDate(dt0, out v))
                it.Taken = v;
        }

        static string Ascii(byte[] d, int off, int count)
        {
            if (off < 0 || count <= 0 || off + count > d.Length) return null;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < count && d[off + i] != 0; i++) sb.Append((char)d[off + i]);
            return sb.ToString();
        }

        static bool TryParseExifDate(string s, out DateTime v)
        {
            v = DateTime.MinValue;
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s.Length > 19) s = s.Substring(0, 19);
            return DateTime.TryParseExact(s, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out v);
        }

        // Para PNG, HEIC, TIFF, WEBP... usa los códecs de Windows.
        static void ReadWic(PhotoItem it)
        {
            using (var fs = Open(it.Path))
            {
                var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                var fr = dec.Frames[0];
                it.Width = fr.PixelWidth;
                it.Height = fr.PixelHeight;
                BitmapMetadata md = null;
                try { md = fr.Metadata as BitmapMetadata; } catch { }
                if (md == null) return;
                try
                {
                    DateTime v;
                    if (md.DateTaken != null && DateTime.TryParse(md.DateTaken, out v)) it.Taken = v;
                }
                catch { }
                try
                {
                    object o = md.GetQuery("System.Photo.Orientation");
                    if (o != null) it.Orientation = Convert.ToInt32(o);
                }
                catch { }
            }
        }
    }

    public static class ImageLoader
    {
        public static BitmapSource Load(PhotoItem it, int maxDim)
        {
            MetaReader.Read(it);
            try { return LoadCore(it, maxDim, BitmapCreateOptions.None); }
            catch { return LoadCore(it, maxDim, BitmapCreateOptions.IgnoreColorProfile); }
        }

        static BitmapSource LoadCore(PhotoItem it, int maxDim, BitmapCreateOptions opt)
        {
            using (var fs = MetaReader.Open(it.Path))
            {
                int w = it.Width, h = it.Height;
                if (w <= 0 || h <= 0)
                {
                    var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.DelayCreation | opt, BitmapCacheOption.None);
                    w = dec.Frames[0].PixelWidth;
                    h = dec.Frames[0].PixelHeight;
                    it.Width = w;
                    it.Height = h;
                    fs.Position = 0;
                }
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = opt;
                bi.StreamSource = fs;
                if (maxDim > 0 && Math.Max(w, h) > maxDim)
                {
                    if (w >= h) bi.DecodePixelWidth = maxDim;
                    else bi.DecodePixelHeight = maxDim;
                }
                bi.EndInit();
                bi.Freeze();
                return Orient(bi, it.Orientation);
            }
        }

        static BitmapSource Orient(BitmapSource src, int o)
        {
            if (o <= 1 || o > 8) return src;
            Transform t;
            switch (o)
            {
                case 2: t = new ScaleTransform(-1, 1); break;
                case 3: t = new RotateTransform(180); break;
                case 4: t = new ScaleTransform(1, -1); break;
                case 5: t = Group(new RotateTransform(90), new ScaleTransform(-1, 1)); break;
                case 6: t = new RotateTransform(90); break;
                case 7: t = Group(new RotateTransform(270), new ScaleTransform(-1, 1)); break;
                default: t = new RotateTransform(270); break;
            }
            var tb = new TransformedBitmap(src, t);
            // Se fuerza el cálculo aquí (hilo de fondo) y no al pintar.
            var cached = new CachedBitmap(tb, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            cached.Freeze();
            return cached;
        }

        static Transform Group(Transform a, Transform b)
        {
            var g = new TransformGroup();
            g.Children.Add(a);
            g.Children.Add(b);
            return g;
        }
    }
}
