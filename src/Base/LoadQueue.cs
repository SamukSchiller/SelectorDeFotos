using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Media.Imaging;

namespace SelectorDeFotos
{
    // Cola de carga en segundo plano: procesa la lista "deseada" en orden de prioridad.
    // Cada nueva petición sustituye a la anterior, así lo que ya no se ve no se carga.
    public class LoadQueue
    {
        readonly object lk = new object();
        List<PhotoItem> wanted = new List<PhotoItem>();
        readonly HashSet<string> busy = new HashSet<string>();
        readonly ConcurrentDictionary<string, BitmapSource> cache;
        readonly Func<PhotoItem, BitmapSource> load;
        readonly Action<PhotoItem> done;
        int active;
        // Cuántas cargas hay en curso (la generación de miniaturas en segundo plano espera a que sea 0)
        public int Active { get { return active; } }

        public LoadQueue(int threads, ThreadPriority priority, ConcurrentDictionary<string, BitmapSource> cache,
                         Func<PhotoItem, BitmapSource> load, Action<PhotoItem> done)
        {
            this.cache = cache;
            this.load = load;
            this.done = done;
            for (int i = 0; i < threads; i++)
            {
                var t = new Thread(Run);
                t.IsBackground = true;
                t.Priority = priority;
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            }
        }

        public void Request(List<PhotoItem> items)
        {
            lock (lk)
            {
                wanted = items;
                Monitor.PulseAll(lk);
            }
        }

        void Run()
        {
            while (true)
            {
                PhotoItem next = null;
                lock (lk)
                {
                    while (true)
                    {
                        foreach (var it in wanted)
                        {
                            if (!cache.ContainsKey(it.Path) && !busy.Contains(it.Path)) { next = it; break; }
                        }
                        if (next != null) { busy.Add(next.Path); break; }
                        Monitor.Wait(lk);
                    }
                }
                BitmapSource b;
                Interlocked.Increment(ref active);
                try { b = load(next); }
                catch { b = null; }
                finally { Interlocked.Decrement(ref active); }
                cache[next.Path] = b; // null = no se pudo abrir
                lock (lk) busy.Remove(next.Path);
                try { done(next); } catch { }
            }
        }
    }
}
