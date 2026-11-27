using System;
using System.IO;
using System.Runtime.InteropServices;
using TGramFS.Services;

namespace TGramFS.Utils
{
    /// <summary>
    /// Утилита для обновления системных атрибутов и иконки привязанных к каналам папок в Проводнике Windows.
    /// </summary>
    public static class BoundFolderShellHelper
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, string? dwItem1, string? dwItem2);

        [DllImport("shell32.dll", EntryPoint = "SHChangeNotify")]
        private static extern void SHChangeNotifyIdList(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ILCreateFromPathW")]
        private static extern IntPtr ILCreateFromPath(string pszPath);

        [DllImport("shell32.dll", EntryPoint = "ILFree")]
        private static extern void ILFree(IntPtr pidl);

        private static bool NotifyShellByPidlOrPath(uint eventId, string path)
        {
            const uint SHCNF_IDLIST = 0x0000;
            const uint SHCNF_PATHW = 0x0005;
            const uint SHCNF_FLUSH = 0x1000;

            IntPtr pidl = IntPtr.Zero;
            try
            {
                pidl = ILCreateFromPath(path);
                if (pidl != IntPtr.Zero)
                {
                    SHChangeNotifyIdList(eventId, SHCNF_IDLIST | SHCNF_FLUSH, pidl, IntPtr.Zero);
                    return true;
                }
            }
            catch
            {
                // Fallback to SHCNF_PATHW below
            }
            finally
            {
                if (pidl != IntPtr.Zero)
                {
                    ILFree(pidl);
                }
            }

            SHChangeNotify(eventId, SHCNF_PATHW | SHCNF_FLUSH, path, null);
            return false;
        }

        public static string? ResolveBoundFolderWindowsPath(string? rawPath, string? relPath, string? fallbackDriveLetter = null)
        {
            string trimmed = (rawPath ?? "").Trim();
            if (trimmed.Contains(':'))
            {
                return trimmed.Replace('/', '\\').TrimEnd('\\');
            }

            string? drive = !string.IsNullOrWhiteSpace(WinFspServer.ActiveMountPoint)
                ? WinFspServer.ActiveMountPoint
                : fallbackDriveLetter;

            if (!string.IsNullOrWhiteSpace(drive) && !string.Equals(drive, "AUTO", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(relPath))
            {
                string cleanDrive = drive.TrimEnd('\\', '/');
                if (!cleanDrive.EndsWith(":")) cleanDrive += ":";
                string sub = relPath.Replace('/', '\\').TrimStart('\\');
                return string.IsNullOrEmpty(sub) ? cleanDrive : $"{cleanDrive}\\{sub}";
            }

            return null;
        }

        public static void ApplyBoundFolderAttributesThroughVfs(string? rawPath, string? relPath, string? fallbackDriveLetter = null)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            try
            {
                string? windowsPath = ResolveBoundFolderWindowsPath(rawPath, relPath, fallbackDriveLetter);
                if (string.IsNullOrWhiteSpace(windowsPath) || !Directory.Exists(windowsPath))
                    return;

                var currentAttrs = File.GetAttributes(windowsPath);
                File.SetAttributes(windowsPath, currentAttrs | FileAttributes.ReadOnly);
            }
            catch (Exception ex)
            {
                AppLogger.Debug("Shell", $"[ApplyBoundFolderAttributesThroughVfs] Не удалось обновить атрибуты папки через ФС: {ex.Message}");
            }
        }

        public static void NotifyExplorerFolderIconChanged(string? rawPath, string? relPath, string? fallbackDriveLetter = null)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            try
            {
                string? windowsPath = ResolveBoundFolderWindowsPath(rawPath, relPath, fallbackDriveLetter);
                if (string.IsNullOrWhiteSpace(windowsPath))
                    return;

                const uint SHCNE_CREATE = 0x00000002;
                const uint SHCNE_ATTRIBUTES = 0x00000800;
                const uint SHCNE_UPDATEDIR = 0x00001000;
                const uint SHCNE_UPDATEITEM = 0x00002000;

                string desktopIniPath = windowsPath.TrimEnd('\\') + "\\desktop.ini";
                bool iniUsedPidl = NotifyShellByPidlOrPath(SHCNE_CREATE, desktopIniPath);
                bool attrUsedPidl = NotifyShellByPidlOrPath(SHCNE_ATTRIBUTES, windowsPath);
                NotifyShellByPidlOrPath(SHCNE_UPDATEITEM, windowsPath);
                NotifyShellByPidlOrPath(SHCNE_UPDATEDIR, windowsPath);

                AppLogger.Debug("Shell", $"[NotifyExplorerFolderIconChanged] Отправлено SHChangeNotify для '{windowsPath}' (iniPidl={iniUsedPidl}, folderPidl={attrUsedPidl}).");
            }
            catch (Exception ex)
            {
                AppLogger.Debug("Shell", $"Предупреждение при обновлении иконки папки в Проводнике: {ex.Message}");
            }
        }
    }
}
