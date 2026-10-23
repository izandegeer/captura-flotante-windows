using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CapturaFlotante
{
    /// <summary>
    /// Ventana invisible que escucha WM_CLIPBOARDUPDATE y avisa cuando entra una captura nueva.
    /// </summary>
    internal sealed class VigilantePortapapeles : NativeWindow, IDisposable
    {
        // Procesos que ponen capturas en el portapapeles (Win+Shift+S, Impr Pant, Recortes).
        private static readonly HashSet<string> ProcesosDeCaptura = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SnippingTool", "ScreenClippingHost", "ScreenSketch", "explorer", "csrss", "dwm",
        };

        private readonly Ajustes ajustes;
        private readonly System.Windows.Forms.Timer esperaCambios;
        private DateTime ignorarHasta = DateTime.MinValue;
        private string procesoPropietario;
        private long firmaAnterior;
        private DateTime momentoAnterior = DateTime.MinValue;

        public event Action<Bitmap> CapturaNueva;

        public VigilantePortapapeles(Ajustes ajustes)
        {
            this.ajustes = ajustes;
            CreateHandle(new CreateParams { Parent = Nativo.HWND_MESSAGE });
            Nativo.AddClipboardFormatListener(Handle);

            // La herramienta de recortes escribe varios formatos seguidos: esperamos a que termine.
            esperaCambios = new System.Windows.Forms.Timer { Interval = 200 };
            esperaCambios.Tick += (s, e) => { esperaCambios.Stop(); Procesar(); };
        }

        /// <summary>Evita reaccionar a lo que copiamos nosotros mismos (botón "Copiar").</summary>
        public void IgnorarDurante(TimeSpan duracion) => ignorarHasta = DateTime.Now + duracion;

        protected override void WndProc(ref Message mensaje)
        {
            if (mensaje.Msg == Nativo.WM_CLIPBOARDUPDATE && DateTime.Now >= ignorarHasta)
            {
                procesoPropietario = Nativo.NombreProcesoDeVentana(Nativo.GetClipboardOwner());
                esperaCambios.Stop();
                esperaCambios.Start();
            }
            base.WndProc(ref mensaje);
        }

        private void Procesar()
        {
            if (!ajustes.MostrarTodasLasImagenes && !EsDeCaptura(procesoPropietario)) return;

            Image imagen = LeerImagen();
            if (imagen == null) return;

            var captura = new Bitmap(imagen);
            imagen.Dispose();

            // Algunas herramientas disparan dos actualizaciones con la misma imagen.
            long firma = CalcularFirma(captura);
            if (firma == firmaAnterior && DateTime.Now - momentoAnterior < TimeSpan.FromSeconds(3))
            {
                captura.Dispose();
                return;
            }
            firmaAnterior = firma;
            momentoAnterior = DateTime.Now;

            CapturaNueva?.Invoke(captura);
        }

        private static bool EsDeCaptura(string proceso) => proceso == null || ProcesosDeCaptura.Contains(proceso);

        private static Image LeerImagen()
        {
            for (int intento = 0; intento < 6; intento++)
            {
                try
                {
                    return Clipboard.ContainsImage() ? Clipboard.GetImage() : null;
                }
                catch (ExternalException)
                {
                    // Portapapeles bloqueado por otro proceso: reintentar un momento después.
                    Thread.Sleep(60);
                }
            }
            return null;
        }

        private static long CalcularFirma(Bitmap imagen)
        {
            long firma = imagen.Width * 73856093L ^ imagen.Height * 19349663L;
            const int muestras = 12;
            for (int fila = 0; fila < muestras; fila++)
            for (int columna = 0; columna < muestras; columna++)
            {
                int x = columna * (imagen.Width - 1) / (muestras - 1);
                int y = fila * (imagen.Height - 1) / (muestras - 1);
                firma = firma * 31 + imagen.GetPixel(x, y).ToArgb();
            }
            return firma;
        }

        public void Dispose()
        {
            esperaCambios.Dispose();
            if (Handle != IntPtr.Zero)
            {
                Nativo.RemoveClipboardFormatListener(Handle);
                DestroyHandle();
            }
        }
    }
}
