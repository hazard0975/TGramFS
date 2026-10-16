using System;
using System.IO;
using Microsoft.Win32;
using TelegramWebDAV.Services;

namespace TelegramWebDAV.Utils
{
    /// <summary>
    /// Управляет регистрацией приложения в автозагрузке Windows через реестр текущего пользователя (HKCU).
    /// Не требует повышенных привилегий администратора.
    /// </summary>
    public static class AutoStartHelper
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "TelegramWebDAV";

        /// <summary>
        /// Проверяет, зарегистрировано ли приложение в автозагрузке HKCU.
        /// </summary>
        public static bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                if (key == null) return false;
                var val = key.GetValue(AppName) as string;
                return !string.IsNullOrEmpty(val);
            }
            catch (Exception ex)
            {
                AppLogger.Debug("AutoStartHelper", $"Ошибка проверки автозапуска: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Включает или отключает автозапуск приложения при входе в Windows.
        /// При включении актуализирует путь к текущему исполняемому файлу.
        /// </summary>
        public static bool SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                if (key == null)
                {
                    AppLogger.Warn("AutoStartHelper", "Не удалось открыть ключ реестра HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run для записи.");
                    return false;
                }

                if (enable)
                {
                    string exePath = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TelegramWebDAV.exe");
                    string command = $"\"{exePath}\"";
                    key.SetValue(AppName, command, RegistryValueKind.String);
                    AppLogger.Info("AutoStartHelper", $"Автозапуск при входе в Windows активирован: {command}");
                }
                else
                {
                    if (key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName, false);
                        AppLogger.Info("AutoStartHelper", "Автозапуск при входе в Windows деактивирован.");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("AutoStartHelper", $"Ошибка настройки автозапуска (enable={enable}): {ex.Message}", ex);
                return false;
            }
        }
    }
}
