using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CapturaFlotante
{
    internal static class Nativo
    {
        public const int WM_CLIPBOARDUPDATE = 0x031D;
        public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int CS_DROPSHADOW = 0x00020000;

        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AddClipboardFormatListener(IntPtr ventana);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RemoveClipboardFormatListener(IntPtr ventana);

        [DllImport("user32.dll")]
        public static extern IntPtr GetClipboardOwner();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr ventana, out uint idProceso);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr ventana);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr icono);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr objeto);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr ventana, int atributo, ref int valor, int tamano);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint acceso, bool heredar, uint idProceso);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr proceso, int flags, StringBuilder ruta, ref int tamano);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr manejador);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        /// <summary>Nombre del ejecutable (sin .exe) del proceso dueño de una ventana, o null si no se puede saber.</summary>
        public static string NombreProcesoDeVentana(IntPtr ventana)
        {
            if (ventana == IntPtr.Zero) return null;
            GetWindowThreadProcessId(ventana, out uint idProceso);
            if (idProceso == 0) return null;

            IntPtr proceso = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, idProceso);
            if (proceso == IntPtr.Zero) return null;
            try
            {
                var ruta = new StringBuilder(1024);
                int tamano = ruta.Capacity;
                if (!QueryFullProcessImageName(proceso, 0, ruta, ref tamano)) return null;
                return System.IO.Path.GetFileNameWithoutExtension(ruta.ToString());
            }
            finally
            {
                CloseHandle(proceso);
            }
        }
    }
}
