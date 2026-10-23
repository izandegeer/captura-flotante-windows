using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace CapturaFlotante
{
    /// <summary>
    /// Arrastre como el del Explorador: el archivo viaja con su imagen pegada al cursor
    /// y, además del archivo, se ofrece la imagen (CF_DIB y PNG) a quien no acepte archivos.
    /// </summary>
    internal static class ArrastreDelSistema
    {
        private const int DRAGDROP_S_DROP = 0x00040100;
        private const int DROPEFFECT_COPY = 1;
        private const int DROPEFFECT_MOVE = 2;
        private const short CF_DIB = 8;

        /// <summary>
        /// Devuelve false si no se pudo montar el arrastre del sistema (y entonces hay que usar el de WinForms).
        /// </summary>
        public static bool Arrastrar(IntPtr ventana, string rutaArchivo, Bitmap imagenArrastre, Point puntoAgarre, out bool soltado)
        {
            soltado = false;
            IntPtr rutaCompleta = IntPtr.Zero;
            IntPtr carpeta = IntPtr.Zero;
            try
            {
                if (SHParseDisplayName(rutaArchivo, IntPtr.Zero, out rutaCompleta, 0, out _) != 0) return false;
                carpeta = ILClone(rutaCompleta);
                ILRemoveLastID(carpeta);
                IntPtr[] elementos = { ILFindLastID(rutaCompleta) };

                Guid idDataObject = typeof(ComTypes.IDataObject).GUID;
                if (SHCreateDataObject(carpeta, 1, elementos, IntPtr.Zero, ref idDataObject, out object creado) != 0) return false;
                var datos = (ComTypes.IDataObject)creado;

                OfrecerImagen(datos, rutaArchivo);
                PonerImagenDeArrastre(datos, imagenArrastre, puntoAgarre);

                int resultado = SHDoDragDrop(ventana, datos, IntPtr.Zero, DROPEFFECT_COPY | DROPEFFECT_MOVE, out _);
                soltado = resultado == DRAGDROP_S_DROP;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (carpeta != IntPtr.Zero) ILFree(carpeta);
                if (rutaCompleta != IntPtr.Zero) ILFree(rutaCompleta);
            }
        }

        private static void OfrecerImagen(ComTypes.IDataObject datos, string rutaArchivo)
        {
            try
            {
                byte[] png = File.ReadAllBytes(rutaArchivo);
                PonerDatos(datos, (short)System.Windows.Forms.DataFormats.GetFormat("PNG").Id, png);

                using (var flujo = new MemoryStream(png))
                using (var original = new Bitmap(flujo))
                using (var sinTransparencia = original.Clone(new Rectangle(Point.Empty, original.Size), PixelFormat.Format24bppRgb))
                using (var bmp = new MemoryStream())
                {
                    // Un .bmp sin su cabecera de archivo (14 bytes) es exactamente un CF_DIB.
                    sinTransparencia.Save(bmp, ImageFormat.Bmp);
                    byte[] dib = new byte[bmp.Length - 14];
                    Array.Copy(bmp.GetBuffer(), 14, dib, 0, dib.Length);
                    PonerDatos(datos, CF_DIB, dib);
                }
            }
            catch (Exception)
            {
                // Sin imagen extra sigue funcionando como archivo.
            }
        }

        private static void PonerDatos(ComTypes.IDataObject datos, short formato, byte[] contenido)
        {
            IntPtr memoria = Marshal.AllocHGlobal(contenido.Length);
            Marshal.Copy(contenido, 0, memoria, contenido.Length);
            var descripcion = new ComTypes.FORMATETC
            {
                cfFormat = formato,
                dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = ComTypes.TYMED.TYMED_HGLOBAL,
            };
            var medio = new ComTypes.STGMEDIUM { tymed = ComTypes.TYMED.TYMED_HGLOBAL, unionmember = memoria };
            try
            {
                datos.SetData(ref descripcion, ref medio, true);
            }
            catch (Exception)
            {
                Marshal.FreeHGlobal(memoria);
                throw;
            }
        }

        private static void PonerImagenDeArrastre(ComTypes.IDataObject datos, Bitmap imagen, Point puntoAgarre)
        {
            IntPtr mapaDeBits = CrearDibPremultiplicado(imagen);
            if (mapaDeBits == IntPtr.Zero) return;

            var imagenArrastre = new SHDRAGIMAGE
            {
                ancho = imagen.Width,
                alto = imagen.Height,
                desplazamientoX = puntoAgarre.X,
                desplazamientoY = puntoAgarre.Y,
                mapaDeBits = mapaDeBits,
                colorTransparente = 0x00FF00FF,
            };

            try
            {
                var ayudante = (IDragSourceHelper)new DragDropHelper();
                // Si lo acepta, el mapa de bits pasa a ser del sistema.
                if (ayudante.InitializeFromBitmap(ref imagenArrastre, datos) != 0) Nativo.DeleteObject(mapaDeBits);
            }
            catch (Exception)
            {
                Nativo.DeleteObject(mapaDeBits);
            }
        }

        /// <summary>El ayudante de arrastre quiere un DIB de 32 bits con alfa premultiplicado.</summary>
        private static IntPtr CrearDibPremultiplicado(Bitmap imagen)
        {
            var cabecera = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                biWidth = imagen.Width,
                biHeight = -imagen.Height, // de arriba abajo, como LockBits
                biPlanes = 1,
                biBitCount = 32,
            };
            IntPtr mapaDeBits = CreateDIBSection(IntPtr.Zero, ref cabecera, 0, out IntPtr pixeles, IntPtr.Zero, 0);
            if (mapaDeBits == IntPtr.Zero) return IntPtr.Zero;

            BitmapData bloque = imagen.LockBits(new Rectangle(Point.Empty, imagen.Size), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppPArgb);
            try
            {
                int bytesPorFila = imagen.Width * 4;
                var fila = new byte[bytesPorFila];
                for (int y = 0; y < imagen.Height; y++)
                {
                    Marshal.Copy(bloque.Scan0 + y * bloque.Stride, fila, 0, bytesPorFila);
                    Marshal.Copy(fila, 0, pixeles + y * bytesPorFila, bytesPorFila);
                }
            }
            finally
            {
                imagen.UnlockBits(bloque);
            }
            return mapaDeBits;
        }

        // ---------- Interop ----------

        [StructLayout(LayoutKind.Sequential)]
        private struct SHDRAGIMAGE
        {
            public int ancho;
            public int alto;
            public int desplazamientoX;
            public int desplazamientoY;
            public IntPtr mapaDeBits;
            public int colorTransparente;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [ComImport, Guid("4657278A-411B-11D2-839A-00C04FD918D0")]
        private class DragDropHelper { }

        [ComImport, Guid("DE5BF786-477A-11D2-839D-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDragSourceHelper
        {
            [PreserveSig]
            int InitializeFromBitmap(ref SHDRAGIMAGE imagen, ComTypes.IDataObject datos);

            [PreserveSig]
            int InitializeFromWindow(IntPtr ventana, IntPtr punto, ComTypes.IDataObject datos);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(string nombre, IntPtr contexto, out IntPtr ruta, uint pedir, out uint obtenido);

        [DllImport("shell32.dll")]
        private static extern IntPtr ILClone(IntPtr ruta);

        [DllImport("shell32.dll")]
        private static extern bool ILRemoveLastID(IntPtr ruta);

        [DllImport("shell32.dll")]
        private static extern IntPtr ILFindLastID(IntPtr ruta);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr ruta);

        [DllImport("shell32.dll")]
        private static extern int SHCreateDataObject(IntPtr carpeta, uint cuantos, IntPtr[] elementos, IntPtr interno,
            ref Guid interfaz, [MarshalAs(UnmanagedType.Interface)] out object datos);

        [DllImport("shell32.dll")]
        private static extern int SHDoDragDrop(IntPtr ventana, ComTypes.IDataObject datos, IntPtr origen, int efectos,
            out int efecto);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr contexto, ref BITMAPINFOHEADER cabecera, uint uso,
            out IntPtr pixeles, IntPtr seccion, uint desplazamiento);
    }
}
