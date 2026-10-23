using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CapturaFlotante
{
    /// <summary>Icono junto al reloj, guarda cada captura y muestra su miniatura flotante.</summary>
    internal sealed class AplicacionBandeja : ApplicationContext
    {
        private readonly Ajustes ajustes = Ajustes.Cargar();
        private readonly NotifyIcon iconoBandeja;
        private readonly VigilantePortapapeles vigilante;
        private Miniatura miniaturaActual;

        public AplicacionBandeja()
        {
            iconoBandeja = new NotifyIcon
            {
                Icon = CargarIcono(),
                Text = "Captura flotante",
                Visible = true,
                ContextMenuStrip = CrearMenu(),
            };
            iconoBandeja.DoubleClick += (s, e) => AbrirCarpeta();

            vigilante = new VigilantePortapapeles(ajustes);
            vigilante.CapturaNueva += MostrarCaptura;

            if (ajustes.EsPrimeraVez)
            {
                ajustes.Guardar();
                iconoBandeja.ShowBalloonTip(6000, "Captura flotante está lista",
                    "Haz una captura con Win+Shift+S o Impr Pant y aparecerá abajo a la derecha.",
                    ToolTipIcon.Info);
            }
        }

        private void MostrarCaptura(Bitmap captura)
        {
            string ruta;
            try
            {
                Directory.CreateDirectory(ajustes.CarpetaCapturas);
                ruta = RutaLibre(ajustes.CarpetaCapturas, DateTime.Now);
                captura.Save(ruta, ImageFormat.Png);
            }
            catch (Exception error)
            {
                iconoBandeja.ShowBalloonTip(5000, "No se pudo guardar la captura", error.Message, ToolTipIcon.Error);
                captura.Dispose();
                return;
            }

            // Como en el Mac: la nueva sustituye a la anterior (que ya está guardada).
            miniaturaActual?.Descartar();
            miniaturaActual = new Miniatura(captura, ruta, ajustes.SegundosVisible, CopiarAlPortapapeles);
            Miniatura estaMiniatura = miniaturaActual;
            estaMiniatura.FormClosed += (s, e) => { if (miniaturaActual == estaMiniatura) miniaturaActual = null; };
            captura.Dispose();
            estaMiniatura.Show();
        }

        private static string RutaLibre(string carpeta, DateTime momento)
        {
            string nombreBase = "Captura de pantalla " + momento.ToString("yyyy-MM-dd") + " a las " + momento.ToString("HH.mm.ss");
            string ruta = Path.Combine(carpeta, nombreBase + ".png");
            for (int numero = 2; File.Exists(ruta); numero++)
                ruta = Path.Combine(carpeta, nombreBase + " (" + numero + ").png");
            return ruta;
        }

        private void CopiarAlPortapapeles(string ruta)
        {
            try
            {
                using (var flujo = new MemoryStream(File.ReadAllBytes(ruta)))
                using (Image imagen = Image.FromStream(flujo))
                {
                    vigilante.IgnorarDurante(TimeSpan.FromSeconds(2));
                    Clipboard.SetImage(imagen);
                }
            }
            catch (Exception) { }
        }

        private ContextMenuStrip CrearMenu()
        {
            var menu = new ContextMenuStrip();

            var abrir = new ToolStripMenuItem("Abrir carpeta de capturas", null, (s, e) => AbrirCarpeta());
            abrir.Font = new Font(abrir.Font, FontStyle.Bold);
            menu.Items.Add(abrir);
            menu.Items.Add("Cambiar carpeta…", null, (s, e) => CambiarCarpeta());

            var duracion = new ToolStripMenuItem("Tiempo en pantalla");
            foreach (int segundos in new[] { 3, 5, 8, 15 })
            {
                var opcion = new ToolStripMenuItem(segundos + " segundos") { Tag = segundos };
                opcion.Click += (s, e) => { ajustes.SegundosVisible = (int)opcion.Tag; ajustes.Guardar(); };
                duracion.DropDownItems.Add(opcion);
            }
            duracion.DropDownOpening += (s, e) =>
            {
                foreach (ToolStripMenuItem opcion in duracion.DropDownItems)
                    opcion.Checked = (int)opcion.Tag == ajustes.SegundosVisible;
            };
            menu.Items.Add(duracion);

            var todasLasImagenes = new ToolStripMenuItem("Mostrar también imágenes copiadas")
            {
                ToolTipText = "Además de las capturas, reacciona a cualquier imagen que copies (navegador, Photoshop…)",
            };
            todasLasImagenes.Click += (s, e) =>
            {
                ajustes.MostrarTodasLasImagenes = !ajustes.MostrarTodasLasImagenes;
                ajustes.Guardar();
            };
            menu.Items.Add(todasLasImagenes);

            var arranque = new ToolStripMenuItem("Iniciar con Windows");
            arranque.Click += (s, e) => Ajustes.IniciaConWindows = !Ajustes.IniciaConWindows;
            menu.Items.Add(arranque);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, (s, e) => ExitThread());

            menu.Opening += (s, e) =>
            {
                todasLasImagenes.Checked = ajustes.MostrarTodasLasImagenes;
                arranque.Checked = Ajustes.IniciaConWindows;
            };
            return menu;
        }

        private void AbrirCarpeta()
        {
            Directory.CreateDirectory(ajustes.CarpetaCapturas);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + ajustes.CarpetaCapturas + "\"") { UseShellExecute = true });
        }

        private void CambiarCarpeta()
        {
            using (var selector = new FolderBrowserDialog
            {
                Description = "Carpeta donde se guardan las capturas",
                SelectedPath = ajustes.CarpetaCapturas,
                ShowNewFolderButton = true,
            })
            {
                if (selector.ShowDialog() != DialogResult.OK) return;
                ajustes.CarpetaCapturas = selector.SelectedPath;
                ajustes.Guardar();
            }
        }

        private static Icon CargarIcono()
        {
            try
            {
                using (Icon delEjecutable = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    return new Icon(delEjecutable, SystemInformation.SmallIconSize);
            }
            catch (Exception)
            {
                return SystemIcons.Application;
            }
        }

        protected override void ExitThreadCore()
        {
            miniaturaActual?.Close();
            vigilante.Dispose();
            iconoBandeja.Visible = false;
            iconoBandeja.Dispose();
            base.ExitThreadCore();
        }
    }
}
