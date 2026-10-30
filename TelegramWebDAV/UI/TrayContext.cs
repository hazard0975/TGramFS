using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using TelegramWebDAV.Config;
using TelegramWebDAV.Database;
using TelegramWebDAV.Server;
using TelegramWebDAV.Services;
using TelegramWebDAV.Utils;

namespace TelegramWebDAV.UI
{
    /// <summary>
    /// Контекст приложения в системном трее Windows (NotifyIcon).
    /// Управляет жизненным циклом фонового WebDAV-сервера, иконкой в трее и вызовом меню.
    /// </summary>
    public class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ConfigManager _configManager;
        private readonly NodeRepository _repository;
        private readonly TelegramService _telegramService;
        private readonly WebDavServer _webDavServer;
        private readonly VirtualDriveManager _virtualDriveManager;
        private readonly DatabaseBackupService? _backupService;
        private readonly TrayProgressOverlay _progressOverlay;
        private AppSettings _settings;
        private AuthSettingsForm? _settingsForm;

        public TrayContext(
            ConfigManager configManager,
            NodeRepository repository,
            TelegramService telegramService,
            WebDavServer webDavServer,
            VirtualDriveManager virtualDriveManager,
            DatabaseBackupService backupService)
        {
            _configManager = configManager;
            _repository = repository;
            _telegramService = telegramService;
            _webDavServer = webDavServer;
            _virtualDriveManager = virtualDriveManager;
            _backupService = backupService;
            _settings = _configManager.Load();
            _progressOverlay = new TrayProgressOverlay
            {
                AutoShowOverlay = _settings.Server.AutoShowProgressOverlay
            };

            Icon? appIcon = null;
            try
            {
                string? exePath = Environment.ProcessPath ?? Application.ExecutablePath;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    appIcon = Icon.ExtractAssociatedIcon(exePath);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Tray", $"Не удалось извлечь иконку приложения: {ex.Message}");
            }

            if (appIcon == null)
            {
                try
                {
                    string localIcon = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                    if (File.Exists(localIcon))
                    {
                        appIcon = new Icon(localIcon);
                    }
                }
                catch { }
            }

            _notifyIcon = new NotifyIcon
            {
                Icon = appIcon ?? SystemIcons.Application,
                Text = "TGramFS — Telegram Virtual Drive",
                Visible = true
            };
            _notifyIcon.DoubleClick += (s, e) => OpenDriveInExplorer();

            CheckDriveMounting();
            BuildContextMenu();
            UpdateTrayTooltip();

            if (_backupService != null)
            {
                _backupService.OnCloudSyncApplied += (message) =>
                {
                    _notifyIcon.ShowBalloonTip(3000, "Синхронизация Telegram", message, ToolTipIcon.Info);
                };

                _backupService.OnCloudSyncConflict += (message) =>
                {
                    _notifyIcon.ShowBalloonTip(4000, "Конфликт синхронизации", message, ToolTipIcon.Warning);
                };

                // Если база данных обновилась или был обнаружен конфликт еще до создания трея (при холодном старте)
                if (!string.IsNullOrEmpty(_backupService.PendingSyncNotification))
                {
                    _notifyIcon.ShowBalloonTip(3500, "Синхронизация Telegram", _backupService.PendingSyncNotification, ToolTipIcon.Info);
                    _backupService.ClearPendingNotifications();
                }
                else if (!string.IsNullOrEmpty(_backupService.PendingConflictNotification))
                {
                    _notifyIcon.ShowBalloonTip(4500, "Конфликт синхронизации", _backupService.PendingConflictNotification, ToolTipIcon.Warning);
                    _backupService.ClearPendingNotifications();
                }
            }

            if (_settings.Server.AddTrashToContextMenu)
            {
                string letter = _virtualDriveManager.MountedLetter ?? _settings.Server.DriveLetter ?? "Z:";
                ShellContextMenuHelper.RegisterTrashContextMenu(letter);
            }

            if (_settings.Server.AutoStartWithWindows)
            {
                AutoStartHelper.SetAutoStart(true);
            }

            _telegramService.OnUploadProgress += (fileName, current, total) =>
            {
                _progressOverlay.UpdateProgress(fileName, current, total, TransferDirection.Upload, _telegramService.PendingUploadsCount);
            };

            _telegramService.OnUploadCompleted += (fileName) =>
            {
                AppLogger.Info("TrayContext", $"Событие OnUploadCompleted для '{fileName}' получено из TelegramService.");
                _progressOverlay.CompleteTransfer(fileName, TransferDirection.Upload);
            };

            _telegramService.OnUploadCanceled += (fileName) =>
            {
                AppLogger.Info("TrayContext", $"Событие OnUploadCanceled для '{fileName}' получено из TelegramService.");
                _progressOverlay.CancelTransfer(fileName, TransferDirection.Upload);
            };

            _telegramService.OnDownloadProgress += (fileName, current, total) =>
            {
                _progressOverlay.UpdateProgress(fileName, current, total, TransferDirection.Download);
            };

            _telegramService.OnMetadataProgress += (fileName, current, total) =>
            {
                _progressOverlay.UpdateMetadataProgress(fileName, current, total);
            };

            _telegramService.OnMetadataCompleted += (fileName) =>
            {
                _progressOverlay.CompleteMetadata(fileName);
            };

            _telegramService.OnDownloadCompleted += (fileName) =>
            {
                AppLogger.Info("TrayContext", $"Событие OnDownloadCompleted для '{fileName}' получено из TelegramService.");
                _progressOverlay.CompleteTransfer(fileName, TransferDirection.Download);
            };
        }

        private void BuildContextMenu()
        {
            var menu = new ContextMenuStrip();

            // Статус подключения
            string driveLetter = _virtualDriveManager.MountedLetter ?? _settings.Server.DriveLetter ?? "Z:";
            var itemStatus = new ToolStripLabel
            {
                Text = _telegramService.IsAuthorized ? "Статус: Авторизовано ✔" : "Статус: Не авторизовано ❌"
            };
            menu.Items.Add(itemStatus);

            // Разделительная линия под статусом
            menu.Items.Add(new ToolStripSeparator());

            menu.Renderer = new StatusMenuRenderer(itemStatus, () => _telegramService.IsAuthorized);
            menu.Opening += (s, e) =>
            {
                itemStatus.Text = _telegramService.IsAuthorized ? "Статус: Авторизовано ✔" : "Статус: Не авторизовано ❌";
            };

            // Открыть сетевой диск в Проводнике
            var itemOpenDrive = new ToolStripMenuItem($"Открыть диск ({driveLetter})");
            itemOpenDrive.Click += (s, e) => OpenDriveInExplorer();
            menu.Items.Add(itemOpenDrive);

            // Открыть корзину в Проводнике
            var itemOpenTrash = new ToolStripMenuItem($"Открыть корзину ({driveLetter}\\.Trash)");
            itemOpenTrash.Click += (s, e) => OpenTrashInExplorer();
            menu.Items.Add(itemOpenTrash);

            menu.Items.Add(new ToolStripSeparator());

            // Настройки и авторизация
            var itemSettings = new ToolStripMenuItem("Настройки");
            itemSettings.Click += (s, e) => ShowSettingsDialog();
            menu.Items.Add(itemSettings);

            // Переподключить диск
            var itemRemount = new ToolStripMenuItem("Переподключить сетевой диск");
            itemRemount.Click += (s, e) => RemountDrive();
            menu.Items.Add(itemRemount);

            // Отправить бэкап базы в Telegram
            var itemBackup = new ToolStripMenuItem("Отправить бэкап базы в Telegram");
            itemBackup.Click += async (s, e) =>
            {
                itemBackup.Enabled = false;
                itemBackup.Text = "Отправка бэкапа...";
                try
                {
                    if (_backupService == null) return;
                    _progressOverlay.UpdateProgress("Резервная копия базы...", 0, 100, TransferDirection.Upload);
                    bool success = await _backupService.PerformBackupAsync("Запуск из системного трея");
                    if (success)
                    {
                        _progressOverlay.CompleteTransfer("Резервная копия базы", TransferDirection.Upload);
                        _notifyIcon.ShowBalloonTip(3000, "Бэкап базы данных", "Свежая резервная копия успешно отправлена в Telegram!", ToolTipIcon.Info);
                    }
                    else
                    {
                        if (_backupService.HasConflict)
                        {
                            _notifyIcon.ShowBalloonTip(4500, "Конфликт синхронизации", "Выгрузка бэкапа заблокирована: в Telegram есть более свежая копия от другого ПК! Откройте настройки для проверки.", ToolTipIcon.Warning);
                        }
                        else
                        {
                            _notifyIcon.ShowBalloonTip(3000, "Бэкап базы данных", "Не удалось отправить бэкап (проверьте авторизацию в Telegram).", ToolTipIcon.Warning);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _notifyIcon.ShowBalloonTip(3000, "Ошибка бэкапа", ex.Message, ToolTipIcon.Error);
                }
                finally
                {
                    itemBackup.Text = "Отправить бэкап базы в Telegram";
                    itemBackup.Enabled = true;
                }
            };
            menu.Items.Add(itemBackup);

            menu.Items.Add(new ToolStripSeparator());

            // Выход
            var itemExit = new ToolStripMenuItem("Выход");
            itemExit.Click += async (s, e) => await ExitApplicationAsync();
            menu.Items.Add(itemExit);

            _notifyIcon.ContextMenuStrip = menu;
        }

        private void ShowSettingsDialog()
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                if (_settingsForm.WindowState == FormWindowState.Minimized)
                {
                    _settingsForm.WindowState = FormWindowState.Normal;
                }
                _settingsForm.BringToFront();
                _settingsForm.Activate();
                return;
            }

            _settingsForm = new AuthSettingsForm(_configManager, _telegramService, _repository, _backupService, _virtualDriveManager);
            _settingsForm.SettingsSaved += () =>
            {
                _settings = _configManager.Load();
                _progressOverlay.AutoShowOverlay = _settings.Server.AutoShowProgressOverlay;
                BuildContextMenu();
                UpdateTrayTooltip();
            };
            _settingsForm.FormClosed += (s, e) =>
            {
                _settingsForm = null;
            };
            _settingsForm.Show();
            _settingsForm.BringToFront();
            _settingsForm.Activate();
        }

        private void OpenDriveInExplorer()
        {
            string drive = _virtualDriveManager.MountedLetter ?? _settings.Server.DriveLetter ?? "Z:";
            if (drive == "AUTO") drive = "Z:";
            if (!drive.EndsWith("\\")) drive += "\\";

            try
            {
                if (Directory.Exists(drive))
                {
                    Process.Start("explorer.exe", drive);
                }
                else
                {
                    // Если диск не смонтирован как буква, открываем WebDAV URL
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = $"http://localhost:{_settings.Server.Port}/",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть папку: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenTrashInExplorer()
        {
            string drive = _virtualDriveManager.MountedLetter ?? _settings.Server.DriveLetter ?? "Z:";
            if (drive == "AUTO") drive = "Z:";
            if (!drive.EndsWith("\\")) drive += "\\";

            string trashPath = Path.Combine(drive, ".Trash");
            try
            {
                if (Directory.Exists(trashPath))
                {
                    Process.Start("explorer.exe", $"\"{trashPath}\"");
                }
                else
                {
                    // Если диск не смонтирован как буква, открываем WebDAV URL корзины
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = $"http://localhost:{_settings.Server.Port}/.Trash",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть корзину: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CheckDriveMounting()
        {
            if (_settings.Server.MountDrive)
            {
                bool success = _virtualDriveManager.Mount(_settings.Server.DriveLetter, out string error);
                NotifyMountResult(success, error, isManual: false);
            }
        }

        private void RemountDrive()
        {
            bool success = _virtualDriveManager.Remount(out string error);
            NotifyMountResult(success, error, isManual: true);
        }

        private void NotifyMountResult(bool success, string error, bool isManual)
        {
            BuildContextMenu();
            UpdateTrayTooltip();

            if (success)
            {
                string engineName = _virtualDriveManager.ActiveEngine == DriveEngine.WinFsp ? "WinFsp" : "WebDAV";
                string letter = _virtualDriveManager.MountedLetter ?? _settings.Server.DriveLetter ?? "Z:";

                if (_virtualDriveManager.IsFallbackActive)
                {
                    _notifyIcon.ShowBalloonTip(5000, "TGramFS — Временная буква диска",
                        $"Буква {_virtualDriveManager.RequestedLetter} занята другим устройством.\nДиск временно смонтирован как {letter} [{engineName}].",
                        ToolTipIcon.Warning);
                }
                else if (isManual)
                {
                    _notifyIcon.ShowBalloonTip(2500, "TGramFS", $"Диск {letter} успешно переподключен [{engineName}]", ToolTipIcon.Info);
                }
            }
            else
            {
                _notifyIcon.ShowBalloonTip(4000, "TGramFS — Ошибка подключения",
                    $"Не удалось смонтировать виртуальный диск: {error}",
                    ToolTipIcon.Error);
            }
        }

        private void UpdateTrayTooltip()
        {
            if (_virtualDriveManager.IsMounted)
            {
                string engineName = _virtualDriveManager.ActiveEngine == DriveEngine.WinFsp ? "WinFsp" : "WebDAV";
                string letter = _virtualDriveManager.MountedLetter ?? _settings.Server.DriveLetter ?? "Z:";
                SetTrayText($"TGramFS — Telegram Virtual Drive\nДиск {letter} подключен в режиме {engineName}");
            }
            else
            {
                SetTrayText("TGramFS — Telegram Virtual Drive\nДиск не подключен");
            }
        }

        private void SetTrayText(string text)
        {
            try
            {
                // Ограничение WinForms/Shell NOTIFYICONDATA: до 127 символов
                if (text.Length >= 128)
                {
                    text = text.Substring(0, 127);
                }
                _notifyIcon.Text = text;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TrayContext", $"Не удалось обновить подсказку трея: {ex.Message}");
            }
        }

        private async Task ExitApplicationAsync()
        {
            try
            {
                if (_backupService != null && _backupService.HasUnsavedChanges && _telegramService.IsAuthorized)
                {
                    _notifyIcon.ShowBalloonTip(1500, "TGramFS", "Сохранение резервной копии базы данных перед выходом...", ToolTipIcon.Info);
                    bool success = await _backupService.HandleApplicationExitAsync();
                    if (success)
                    {
                        _notifyIcon.ShowBalloonTip(3000, "Бэкап базы данных", "Резервная копия базы данных успешно сохранена", ToolTipIcon.Info);
                    }
                    else
                    {
                        _notifyIcon.ShowBalloonTip(3000, "Бэкап базы данных", "Не удалось сохранить резервную копию перед выходом.", ToolTipIcon.Warning);
                    }
                    await Task.Delay(1500);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TrayContext", $"Ошибка при выполнении бэкапа перед закрытием: {ex.Message}");
            }
            finally
            {
                ExitApplication();
            }
        }

        private void ExitApplication()
        {
            try
            {
                ShellContextMenuHelper.UnregisterTrashContextMenu();
            }
            catch (Exception ex)
            {
                AppLogger.Debug("TrayContext", $"Ошибка при удалении контекстного меню при выходе: {ex.Message}");
            }

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _progressOverlay.Dispose();
            _virtualDriveManager.Dispose();
            _webDavServer?.Stop();
            AppLogger.Shutdown();
            Application.Exit();
        }

        private class StatusMenuRenderer : ToolStripProfessionalRenderer
        {
            private readonly ToolStripItem _statusItem;
            private readonly Func<bool> _isAuthorized;

            public StatusMenuRenderer(ToolStripItem statusItem, Func<bool> isAuthorized)
            {
                _statusItem = statusItem;
                _isAuthorized = isAuthorized;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (e.Item == _statusItem)
                {
                    bool isAuth = _isAuthorized();
                    string prefix = "Статус: ";
                    string statusText = isAuth ? "Авторизовано ✔" : "Не авторизовано ❌";
                    Color statusColor = isAuth ? Color.ForestGreen : Color.Firebrick;

                    var baseFont = e.TextFont ?? SystemFonts.MenuFont ?? SystemFonts.DefaultFont;
                    using var regFont = new Font(baseFont.FontFamily, baseFont.Size, FontStyle.Regular);
                    using var boldFont = new Font(baseFont.FontFamily, baseFont.Size, FontStyle.Bold);

                    Size prefixSize = TextRenderer.MeasureText(e.Graphics, prefix, regFont, Size.Empty, TextFormatFlags.NoPadding);
                    int y = e.TextRectangle.Top + (e.TextRectangle.Height - boldFont.Height) / 2;

                    TextRenderer.DrawText(e.Graphics, prefix, regFont, new Point(e.TextRectangle.Left, y), SystemColors.ControlText, TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(e.Graphics, statusText, boldFont, new Point(e.TextRectangle.Left + prefixSize.Width + 2, y), statusColor, TextFormatFlags.NoPadding);
                    return;
                }

                base.OnRenderItemText(e);
            }
        }
    }
}
