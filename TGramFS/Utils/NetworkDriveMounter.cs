using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TGramFS.Utils
{
    /// <summary>
    /// Утилита для автомонтирования WebDAV-папки как виртуального сетевого диска в Windows (Z:, T: и т.д.)
    /// Использует функции Windows API из mpr.dll (WNetAddConnection2W, WNetCancelConnection2W).
    /// </summary>
    public static class NetworkDriveMounter
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NETRESOURCE
        {
            public uint dwScope;
            public uint dwType;
            public uint dwDisplayType;
            public uint dwUsage;
            public string lpLocalName;
            public string lpRemoteName;
            public string lpComment;
            public string lpProvider;
        }

        private const uint RESOURCETYPE_DISK = 0x00000001;
        private const uint CONNECT_UPDATE_PROFILE = 0x00000001;
        private const int NO_ERROR = 0;
        private const int ERROR_ALREADY_ASSIGNED = 85;
        private const int ERROR_DEVICE_ALREADY_REMEMBERED = 1202;

        private const uint DDD_RAW_TARGET_PATH = 0x00000001;
        private const uint DDD_REMOVE_DEFINITION = 0x00000002;
        private const uint DDD_EXACT_MATCH_ON_REMOVE = 0x00000004;

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool DefineDosDevice(uint dwFlags, string lpDeviceName, string? lpTargetPath);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

        [DllImport("mpr.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int WNetAddConnection2(
            ref NETRESOURCE lpNetResource,
            string? lpPassword,
            string? lpUserName,
            uint dwFlags);

        [DllImport("mpr.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int WNetCancelConnection2(
            string lpName,
            uint dwFlags,
            bool fForce);

        /// <summary>
        /// Монтирует локальный WebDAV URL на указанную букву диска в Windows.
        /// </summary>
        public static bool Mount(string driveLetter, string webDavUrl, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(driveLetter))
            {
                errorMessage = "Буква диска не указана";
                return false;
            }

            // Форматируем букву (например, "Z:" или "Z")
            string formattedLetter = driveLetter.Trim().ToUpper();
            if (!formattedLetter.EndsWith(":")) formattedLetter += ":";

            // Сначала пробуем аккуратно отмонтировать, если буква была занята старым сеансом
            Unmount(formattedLetter, true);

            var resource = new NETRESOURCE
            {
                dwType = RESOURCETYPE_DISK,
                lpLocalName = formattedLetter,
                lpRemoteName = webDavUrl.TrimEnd('/')
            };

            int result = WNetAddConnection2(ref resource, null, null, CONNECT_UPDATE_PROFILE);

            if (result == NO_ERROR || result == ERROR_ALREADY_ASSIGNED || result == ERROR_DEVICE_ALREADY_REMEMBERED)
            {
                Services.AppLogger.Info("NetworkDriveMounter", $"Диск {formattedLetter} успешно смонтирован на {webDavUrl}");
                return true;
            }

            errorMessage = $"Код ошибки WinAPI: {result}";
            Services.AppLogger.Warn("NetworkDriveMounter", $"Ошибка монтирования диска {formattedLetter}: {errorMessage}");
            return false;
        }

        /// <summary>
        /// Размонтирует сетевой диск из системы.
        /// </summary>
        public static bool Unmount(string driveLetter, bool force = false)
        {
            string formattedLetter = driveLetter.Trim().ToUpper();
            if (!formattedLetter.EndsWith(":")) formattedLetter += ":";

            int result = WNetCancelConnection2(formattedLetter, CONNECT_UPDATE_PROFILE, force);
            return result == NO_ERROR;
        }

        /// <summary>
        /// Находит первую свободную букву диска в Windows от Z до D.
        /// </summary>
        public static string GetFirstAvailableDriveLetter()
        {
            string[] occupiedDrives = Environment.GetLogicalDrives();
            for (char c = 'Z'; c >= 'D'; c--)
            {
                string candidate = $"{c}:\\";
                bool isOccupied = false;
                foreach (string occ in occupiedDrives)
                {
                    if (string.Equals(occ, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        isOccupied = true;
                        break;
                    }
                }
                if (!isOccupied)
                {
                    return $"{c}:";
                }
            }
            return "Z:";
        }

        /// <summary>
        /// Возвращает внутренний NT-путь устройства (например \Device\Volume{GUID}), на которое ссылается буква диска.
        /// </summary>
        public static string? GetDosDeviceTarget(string driveLetter)
        {
            try
            {
                string formattedLetter = driveLetter.Trim().ToUpper();
                if (formattedLetter.EndsWith("\\")) formattedLetter = formattedLetter.TrimEnd('\\');
                if (!formattedLetter.EndsWith(":")) formattedLetter += ":";

                var sb = new StringBuilder(1024);
                uint len = QueryDosDevice(formattedLetter, sb, sb.Capacity);
                if (len > 0)
                {
                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                Services.AppLogger.Warn("NetworkDriveMounter", $"Ошибка QueryDosDevice: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Вызывается в процессе с повышенными правами (UAC) для создания символической ссылки на том WinFsp
        /// или сетевого диска WebDAV в сессии Администратора.
        /// </summary>
        public static bool MountForElevatedSession(string driveLetter, int port, string? targetNtPath)
        {
            string formattedLetter = driveLetter.Trim().ToUpper();
            if (formattedLetter.EndsWith("\\")) formattedLetter = formattedLetter.TrimEnd('\\');
            if (!formattedLetter.EndsWith(":")) formattedLetter += ":";

            bool success = false;

            // 1. Если передан NT-путь тома (режим WinFsp)
            if (!string.IsNullOrWhiteSpace(targetNtPath) && targetNtPath.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    DefineDosDevice(DDD_REMOVE_DEFINITION | DDD_EXACT_MATCH_ON_REMOVE, formattedLetter, targetNtPath);
                    if (DefineDosDevice(DDD_RAW_TARGET_PATH, formattedLetter, targetNtPath))
                    {
                        Services.AppLogger.Info("NetworkDriveMounter", $"Диск {formattedLetter} успешно связан с NT-томом {targetNtPath} в сессии UAC.");
                        success = true;
                    }
                    else
                    {
                        int err = Marshal.GetLastWin32Error();
                        Services.AppLogger.Warn("NetworkDriveMounter", $"DefineDosDevice для {formattedLetter} вернул ошибку Win32: {err}");
                    }
                }
                catch (Exception ex)
                {
                    Services.AppLogger.Warn("NetworkDriveMounter", $"Ошибка DefineDosDevice: {ex.Message}");
                }
            }

            // 2. Если режим WebDAV или если DefineDosDevice не связался
            if (!success)
            {
                string webDavUrl = $"http://localhost:{port}/";
                if (Mount(formattedLetter, webDavUrl, out string err))
                {
                    Services.AppLogger.Info("NetworkDriveMounter", $"Диск {formattedLetter} успешно смонтирован как WebDAV в сессии UAC.");
                    success = true;
                }
                else
                {
                    Services.AppLogger.Warn("NetworkDriveMounter", $"Ошибка монтирования WebDAV в сессии UAC: {err}");
                }
            }

            return success;
        }

        /// <summary>
        /// Запрашивает повышение прав через UAC и подключает диск в сессию Администратора.
        /// </summary>
        public static bool MountWithElevation(string driveLetter, int port, out string statusMessage)
        {
            statusMessage = string.Empty;

            string formattedLetter = driveLetter.Trim().ToUpper();
            if (formattedLetter.EndsWith("\\")) formattedLetter = formattedLetter.TrimEnd('\\');
            if (!formattedLetter.EndsWith(":")) formattedLetter += ":";

            string? ntPath = GetDosDeviceTarget(formattedLetter);
            Services.AppLogger.Info("VirtualDriveManager", $"Запрос на подключение диска {formattedLetter} в сессию Администратора (UAC)...");

            if (WindowsRegistryFixer.IsAdministrator())
            {
                bool directSuccess = MountForElevatedSession(formattedLetter, port, ntPath);
                statusMessage = directSuccess
                    ? $"Диск {formattedLetter} успешно подключен для Администратора!"
                    : $"Не удалось подключить диск {formattedLetter} для Администратора.";
                if (directSuccess)
                    Services.AppLogger.Info("VirtualDriveManager", statusMessage);
                else
                    Services.AppLogger.Warn("VirtualDriveManager", statusMessage);
                return directSuccess;
            }

            try
            {
                string? exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    statusMessage = "Не удалось определить путь к исполняемому файлу приложения.";
                    Services.AppLogger.Warn("VirtualDriveManager", statusMessage);
                    return false;
                }

                string encodedNtPath = !string.IsNullOrWhiteSpace(ntPath)
                    ? Convert.ToBase64String(Encoding.UTF8.GetBytes(ntPath))
                    : "-";

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--mount-uac \"{formattedLetter}\" {port} \"{encodedNtPath}\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                    {
                        statusMessage = $"Диск {formattedLetter} успешно подключен с правами Администратора (UAC)!";
                        Services.AppLogger.Info("VirtualDriveManager", $"Диск {formattedLetter} успешно связан и доступен для программ с правами Администратора (UAC).");
                        return true;
                    }
                    else
                    {
                        statusMessage = $"Процесс монтирования с правами Администратора завершился с кодом {process.ExitCode}.";
                        Services.AppLogger.Warn("VirtualDriveManager", statusMessage);
                        return false;
                    }
                }
                else
                {
                    statusMessage = "Не удалось запустить процесс запроса прав администратора.";
                    Services.AppLogger.Warn("VirtualDriveManager", statusMessage);
                    return false;
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
            {
                statusMessage = "Запрос прав администратора (UAC) был отменен пользователем.";
                Services.AppLogger.Warn("VirtualDriveManager", statusMessage);
                return false;
            }
            catch (Exception ex)
            {
                statusMessage = $"Ошибка вызова UAC: {ex.Message}";
                Services.AppLogger.Error("VirtualDriveManager", $"Ошибка вызова UAC для диска {formattedLetter}: {ex.Message}", ex);
                return false;
            }
        }
    }
}
