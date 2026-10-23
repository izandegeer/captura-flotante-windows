using System;
using System.Threading;
using System.Windows.Forms;

namespace CapturaFlotante
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            using (var instanciaUnica = new Mutex(true, "CapturaFlotante.InstanciaUnica", out bool esLaPrimera))
            {
                if (!esLaPrimera)
                {
                    MessageBox.Show("Captura flotante ya está en marcha (mira el icono junto al reloj).",
                        "Captura flotante", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new AplicacionBandeja());
            }
        }
    }
}
