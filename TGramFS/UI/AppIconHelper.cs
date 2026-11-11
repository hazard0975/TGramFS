using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TGramFS.UI
{
    /// <summary>
    /// Централизованный помощник для загрузки иконки приложения из встроенного
    /// ресурса EmbeddedResource (высокое разрешение 128x128 / 256x256)
    /// с системным фолбэком.
    /// </summary>
    public static class AppIconHelper
    {
        public const string EmbeddedResourceName = "TGramFS.icon.ico";

        /// <summary>
        /// Возвращает полноценный многослойный системный Icon.
        /// Приоритет: EmbeddedResource -> ExtractAssociatedIcon.
        /// </summary>
        public static Icon? GetAppIcon()
        {
            try
            {
                var asm = typeof(AppIconHelper).Assembly;
                using var stream = asm.GetManifestResourceStream(EmbeddedResourceName);
                if (stream != null)
                {
                    return new Icon(stream);
                }
            }
            catch { }

            try
            {
                string? exe = Environment.ProcessPath ?? Application.ExecutablePath;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    return Icon.ExtractAssociatedIcon(exe);
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Возвращает растровое изображение заданного размера (например, 128x128 или 256x256)
        /// без артефактов растяжения и размытия.
        /// </summary>
        public static Image? GetAppIconBitmap(int desiredWidth = 128, int desiredHeight = 128)
        {
            try
            {
                var asm = typeof(AppIconHelper).Assembly;
                using var stream = asm.GetManifestResourceStream(EmbeddedResourceName);
                if (stream != null)
                {
                    using var ico = new Icon(stream, desiredWidth, desiredHeight);
                    return ico.ToBitmap();
                }
            }
            catch { }

            var fallback = GetAppIcon();
            return fallback?.ToBitmap();
        }
    }
}
