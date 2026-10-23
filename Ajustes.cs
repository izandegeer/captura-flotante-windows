using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace CapturaFlotante
{
    /// <summary>Ajustes guardados en %APPDATA%\CapturaFlotante\ajustes.txt (formato clave=valor).</summary>
    internal sealed class Ajustes
    {
        private const string ClaveArranque = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string NombreArranque = "CapturaFlotante";

        private static readonly string CarpetaAjustes =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CapturaFlotante");
        private static readonly string ArchivoAjustes = Path.Combine(CarpetaAjustes, "ajustes.txt");

        public string CarpetaCapturas { get; set; } =
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        /// <summary>Si es false, solo reacciona a imágenes puestas por la herramienta de recortes / Impr Pant.</summary>
        public bool MostrarTodasLasImagenes { get; set; }

        public int SegundosVisible { get; set; } = 5;

        public bool EsPrimeraVez { get; private set; }

        public static Ajustes Cargar()
        {
            var ajustes = new Ajustes();
            if (!File.Exists(ArchivoAjustes))
            {
                ajustes.EsPrimeraVez = true;
                return ajustes;
            }

            var valores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string linea in File.ReadAllLines(ArchivoAjustes))
            {
                int igual = linea.IndexOf('=');
                if (igual > 0) valores[linea.Substring(0, igual).Trim()] = linea.Substring(igual + 1).Trim();
            }

            if (valores.TryGetValue("carpeta", out string carpeta) && Directory.Exists(carpeta))
                ajustes.CarpetaCapturas = carpeta;
            if (valores.TryGetValue("todas_las_imagenes", out string todas))
                ajustes.MostrarTodasLasImagenes = todas == "1";
            if (valores.TryGetValue("segundos", out string segundos) && int.TryParse(segundos, out int numero) && numero > 0)
                ajustes.SegundosVisible = numero;

            return ajustes;
        }

        public void Guardar()
        {
            Directory.CreateDirectory(CarpetaAjustes);
            File.WriteAllLines(ArchivoAjustes, new[]
            {
                "carpeta=" + CarpetaCapturas,
                "todas_las_imagenes=" + (MostrarTodasLasImagenes ? "1" : "0"),
                "segundos=" + SegundosVisible,
            });
        }

        public static bool IniciaConWindows
        {
            get
            {
                using (RegistryKey clave = Registry.CurrentUser.OpenSubKey(ClaveArranque))
                    return clave?.GetValue(NombreArranque) != null;
            }
            set
            {
                using (RegistryKey clave = Registry.CurrentUser.OpenSubKey(ClaveArranque, true)
                                         ?? Registry.CurrentUser.CreateSubKey(ClaveArranque))
                {
                    if (value) clave.SetValue(NombreArranque, "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
                    else clave.DeleteValue(NombreArranque, false);
                }
            }
        }
    }
}
