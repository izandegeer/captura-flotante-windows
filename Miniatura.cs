using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CapturaFlotante
{
    /// <summary>
    /// Miniatura flotante estilo macOS: aparece abajo a la derecha, se arrastra como archivo,
    /// se descarta deslizándola a la derecha y un clic la abre para editar.
    /// </summary>
    internal sealed class Miniatura : Form
    {
        private enum Gesto { Ninguno, Deslizar, Arrastrar }

        private readonly string rutaCaptura;
        private readonly Bitmap vistaPrevia;
        private readonly float escala;
        private readonly int margenImagen;
        private readonly int radioEsquinas;
        private readonly Rectangle zonaPantalla;
        private readonly Point posicionFinal;
        private readonly Action<string> alCopiar;
        private readonly ContextMenuStrip menu;

        private readonly Timer animacion = new Timer { Interval = 15 };
        private readonly Timer vida = new Timer { Interval = 100 };
        private readonly Stopwatch reloj = new Stopwatch();
        private readonly int milisegundosVisible;
        private int milisegundosRestantes;

        private Point origenAnimacion;
        private Point destinoAnimacion;
        private double opacidadOrigen;
        private double opacidadDestino;
        private int duracionAnimacion;
        private Action alTerminarAnimacion;

        private bool botonPulsado;
        private Gesto gesto;
        private Point cursorInicial;
        private Point posicionInicial;
        private Point puntoAgarre;
        private bool cerrando;

        public Miniatura(Bitmap captura, string rutaCaptura, int segundosVisible, Action<string> alCopiar)
        {
            this.rutaCaptura = rutaCaptura;
            this.alCopiar = alCopiar;
            milisegundosVisible = milisegundosRestantes = segundosVisible * 1000;

            using (Graphics pantalla = Graphics.FromHwnd(IntPtr.Zero))
                escala = pantalla.DpiX / 96f;

            margenImagen = Escalar(3);
            radioEsquinas = Escalar(8);

            Size tamanoImagen = CalcularTamano(captura.Size, Escalar(240), Escalar(160), Escalar(56));
            vistaPrevia = Redimensionar(captura, tamanoImagen);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(236, 236, 236);
            Cursor = Cursors.Hand;
            ClientSize = new Size(tamanoImagen.Width + margenImagen * 2, tamanoImagen.Height + margenImagen * 2);
            Text = "Captura";

            zonaPantalla = Screen.FromPoint(Cursor.Position).WorkingArea;
            int margen = Escalar(16);
            posicionFinal = new Point(zonaPantalla.Right - Width - margen, zonaPantalla.Bottom - Height - margen);
            Location = new Point(posicionFinal.X + Escalar(40), posicionFinal.Y);
            Opacity = 0;

            menu = CrearMenu();
            menu.Opening += (s, e) => vida.Stop();
            menu.Closed += (s, e) => { if (!cerrando) { milisegundosRestantes = milisegundosVisible; vida.Start(); } };

            animacion.Tick += (s, e) => AvanzarAnimacion();
            vida.Tick += (s, e) => ContarVida();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parametros = base.CreateParams;
                parametros.ExStyle |= Nativo.WS_EX_TOOLWINDOW | Nativo.WS_EX_NOACTIVATE | Nativo.WS_EX_TOPMOST;
                parametros.ClassStyle |= Nativo.CS_DROPSHADOW;
                return parametros;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // Windows 11 redondea con antialias; en Windows 10 recortamos con una región.
            int preferencia = Nativo.DWMWCP_ROUND;
            bool redondeadoPorWindows = false;
            try
            {
                redondeadoPorWindows = Nativo.DwmSetWindowAttribute(Handle, Nativo.DWMWA_WINDOW_CORNER_PREFERENCE,
                    ref preferencia, sizeof(int)) == 0;
            }
            catch (DllNotFoundException) { }

            if (!redondeadoPorWindows)
            {
                using (GraphicsPath contorno = Redondeado(new Rectangle(Point.Empty, ClientSize), radioEsquinas))
                    Region = new Region(contorno);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Animar(posicionFinal, 1, 260, null);
            vida.Start();
        }

        protected override void OnPaint(PaintEventArgs e) => DibujarImagen(e.Graphics);

        /// <summary>La miniatura entera con esquinas transparentes, para llevarla pegada al cursor.</summary>
        private Bitmap DibujarParaArrastre()
        {
            var imagen = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(imagen))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath contorno = Redondeado(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), radioEsquinas))
                using (var fondo = new SolidBrush(BackColor))
                    g.FillPath(fondo, contorno);
                DibujarImagen(g);
            }
            return imagen;
        }

        private void DibujarImagen(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            var zonaImagen = new Rectangle(margenImagen, margenImagen, vistaPrevia.Width, vistaPrevia.Height);
            using (GraphicsPath recorte = Redondeado(zonaImagen, Math.Max(1, radioEsquinas - margenImagen)))
            {
                g.SetClip(recorte);
                g.DrawImage(vistaPrevia, zonaImagen);
                g.ResetClip();
                using (var borde = new Pen(Color.FromArgb(40, 0, 0, 0)))
                    g.DrawPath(borde, recorte);
            }
        }

        // ---------- Ratón ----------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || cerrando) return;
            botonPulsado = true;
            gesto = Gesto.Ninguno;
            cursorInicial = Cursor.Position;
            posicionInicial = Location;
            puntoAgarre = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!botonPulsado) return;

            int desplazamientoX = Cursor.Position.X - cursorInicial.X;
            int desplazamientoY = Cursor.Position.Y - cursorInicial.Y;

            if (gesto == Gesto.Ninguno)
            {
                Size umbral = SystemInformation.DragSize;
                if (Math.Abs(desplazamientoX) < umbral.Width && Math.Abs(desplazamientoY) < umbral.Height) return;

                bool haciaLaDerecha = desplazamientoX > 0 && Math.Abs(desplazamientoX) > Math.Abs(desplazamientoY) * 2;
                gesto = haciaLaDerecha ? Gesto.Deslizar : Gesto.Arrastrar;
                if (gesto == Gesto.Arrastrar)
                {
                    ArrastrarComoArchivo();
                    return;
                }
            }

            if (gesto == Gesto.Deslizar)
            {
                int avance = Math.Max(0, desplazamientoX);
                Location = new Point(posicionInicial.X + avance, posicionInicial.Y);
                Opacity = Math.Max(0.25, 1 - avance / (double)(Width * 1.2));
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Right && !cerrando)
            {
                Nativo.SetForegroundWindow(Handle);
                menu.Show(this, e.Location);
                return;
            }

            if (!botonPulsado) return;
            botonPulsado = false;

            if (gesto == Gesto.Ninguno)
            {
                AbrirParaEditar();
            }
            else if (gesto == Gesto.Deslizar)
            {
                if (Location.X - posicionInicial.X > Escalar(50)) Descartar();
                else Animar(posicionInicial, 1, 160, null);
            }
            gesto = Gesto.Ninguno;
        }

        private void ArrastrarComoArchivo()
        {
            botonPulsado = false;
            vida.Stop();

            // Como en el Mac, la miniatura "se despega" y viaja con el cursor.
            Opacity = 0;
            bool soltada;
            using (Bitmap imagenArrastre = DibujarParaArrastre())
            {
                if (!ArrastreDelSistema.Arrastrar(Handle, rutaCaptura, imagenArrastre, puntoAgarre, out soltada))
                    soltada = ArrastrarConWinForms();
            }
            gesto = Gesto.Ninguno;

            if (soltada)
            {
                Descartar();
            }
            else
            {
                Opacity = 1;
                milisegundosRestantes = milisegundosVisible;
                vida.Start();
            }
        }

        /// <summary>Plan B si el arrastre del sistema no se puede montar: solo el archivo, sin imagen en el cursor.</summary>
        private bool ArrastrarConWinForms()
        {
            Opacity = 0.6;
            var datos = new DataObject();
            datos.SetData(DataFormats.FileDrop, new[] { rutaCaptura });
            return DoDragDrop(datos, DragDropEffects.Copy | DragDropEffects.Move) != DragDropEffects.None;
        }

        // ---------- Acciones ----------

        private ContextMenuStrip CrearMenu()
        {
            var nuevoMenu = new ContextMenuStrip();
            var editar = new ToolStripMenuItem("Editar en Paint", null, (s, e) => AbrirParaEditar());
            editar.Font = new Font(editar.Font, FontStyle.Bold);
            nuevoMenu.Items.Add(editar);
            nuevoMenu.Items.Add("Abrir", null, (s, e) => { Abrir(rutaCaptura, null); Descartar(); });
            nuevoMenu.Items.Add("Mostrar en la carpeta", null, (s, e) =>
            {
                Abrir("explorer.exe", "/select,\"" + rutaCaptura + "\"");
                Descartar();
            });
            nuevoMenu.Items.Add("Copiar", null, (s, e) => { alCopiar(rutaCaptura); Descartar(); });
            nuevoMenu.Items.Add(new ToolStripSeparator());
            nuevoMenu.Items.Add("Eliminar", null, (s, e) => { EliminarCaptura(); Descartar(); });
            nuevoMenu.Items.Add("Cerrar", null, (s, e) => Descartar());
            return nuevoMenu;
        }

        private void AbrirParaEditar()
        {
            if (!Abrir("mspaint.exe", "\"" + rutaCaptura + "\"")) Abrir(rutaCaptura, null);
            Descartar();
        }

        private void EliminarCaptura()
        {
            try { File.Delete(rutaCaptura); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static bool Abrir(string programa, string argumentos)
        {
            try
            {
                Process.Start(new ProcessStartInfo(programa) { Arguments = argumentos ?? "", UseShellExecute = true });
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Se va deslizando hacia la derecha y desaparece. El archivo se queda guardado.</summary>
        public void Descartar()
        {
            if (cerrando) return;
            cerrando = true;
            vida.Stop();
            menu.Close();
            Animar(new Point(Location.X + Escalar(60), Location.Y), 0, 200, Close);
        }

        private void ContarVida()
        {
            bool ratonEncima = Bounds.Contains(Cursor.Position);
            if (ratonEncima || botonPulsado || menu.Visible)
            {
                milisegundosRestantes = milisegundosVisible;
                return;
            }
            milisegundosRestantes -= vida.Interval;
            if (milisegundosRestantes <= 0) Descartar();
        }

        // ---------- Animación ----------

        private void Animar(Point destino, double opacidad, int milisegundos, Action alTerminar)
        {
            origenAnimacion = Location;
            destinoAnimacion = destino;
            opacidadOrigen = Opacity;
            opacidadDestino = opacidad;
            duracionAnimacion = milisegundos;
            alTerminarAnimacion = alTerminar;
            reloj.Restart();
            animacion.Start();
        }

        private void AvanzarAnimacion()
        {
            double progreso = Math.Min(1, reloj.ElapsedMilliseconds / (double)duracionAnimacion);
            double suavizado = 1 - Math.Pow(1 - progreso, 3);

            Location = new Point(
                (int)Math.Round(origenAnimacion.X + (destinoAnimacion.X - origenAnimacion.X) * suavizado),
                (int)Math.Round(origenAnimacion.Y + (destinoAnimacion.Y - origenAnimacion.Y) * suavizado));
            Opacity = opacidadOrigen + (opacidadDestino - opacidadOrigen) * suavizado;

            if (progreso >= 1)
            {
                animacion.Stop();
                Action siguiente = alTerminarAnimacion;
                alTerminarAnimacion = null;
                siguiente?.Invoke();
            }
        }

        // ---------- Utilidades ----------

        private int Escalar(int pixeles) => (int)Math.Round(pixeles * escala);

        private static Size CalcularTamano(Size original, int anchoMaximo, int altoMaximo, int ladoMinimo)
        {
            double factor = Math.Min(anchoMaximo / (double)original.Width, altoMaximo / (double)original.Height);
            factor = Math.Min(factor, 1);
            int ancho = Math.Max(ladoMinimo, (int)Math.Round(original.Width * factor));
            int alto = Math.Max(ladoMinimo, (int)Math.Round(original.Height * factor));
            return new Size(Math.Min(ancho, anchoMaximo), Math.Min(alto, altoMaximo));
        }

        private static Bitmap Redimensionar(Bitmap original, Size tamano)
        {
            var resultado = new Bitmap(tamano.Width, tamano.Height);
            using (Graphics g = Graphics.FromImage(resultado))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.White);

                // Rellena el hueco manteniendo la proporción (recorte centrado si la captura es muy alargada).
                double factor = Math.Max(tamano.Width / (double)original.Width, tamano.Height / (double)original.Height);
                int ancho = (int)Math.Round(original.Width * factor);
                int alto = (int)Math.Round(original.Height * factor);
                g.DrawImage(original, (tamano.Width - ancho) / 2, (tamano.Height - alto) / 2, ancho, alto);
            }
            return resultado;
        }

        private static GraphicsPath Redondeado(Rectangle zona, int radio)
        {
            int diametro = radio * 2;
            var contorno = new GraphicsPath();
            contorno.AddArc(zona.X, zona.Y, diametro, diametro, 180, 90);
            contorno.AddArc(zona.Right - diametro, zona.Y, diametro, diametro, 270, 90);
            contorno.AddArc(zona.Right - diametro, zona.Bottom - diametro, diametro, diametro, 0, 90);
            contorno.AddArc(zona.X, zona.Bottom - diametro, diametro, diametro, 90, 90);
            contorno.CloseFigure();
            return contorno;
        }

        protected override void Dispose(bool liberando)
        {
            if (liberando)
            {
                animacion.Dispose();
                vida.Dispose();
                menu.Dispose();
                vistaPrevia.Dispose();
            }
            base.Dispose(liberando);
        }
    }
}
