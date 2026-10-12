using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelegramWebDAV.Config;
using TelegramWebDAV.Database;

namespace TelegramWebDAV.Services
{
    /// <summary>
    /// Служба автоматического и ручного резервного копирования базы данных в приватный канал Telegram.
    /// Обеспечивает консистентный сброс WAL-журнала, создание монолитных снимков через VACUUM INTO,
    /// дебаунс-таймер активности, выгрузку при штатном выходе и ротацию локальных копий.
    /// </summary>
    public class DatabaseBackupService : IDisposable
    {
        private readonly DatabaseManager _dbManager;
        private readonly NodeRepository _repository;
        private readonly TelegramService _telegramService;
        private readonly ConfigManager _configManager;

        private bool _hasUnsavedChanges = false;
        private bool _hasConflict = false;
        private string? _conflictDetails;
        private DateTime? _lastBackupTimeUtc;
        private Timer? _debounceTimer;
        private Timer? _cloudSyncTimer;
        private readonly SemaphoreSlim _backupLock = new SemaphoreSlim(1, 1);
        private bool _isDisposed = false;

        public event Action<string>? OnBackupStarted;
        public event Action<string>? OnBackupCompleted;
        public event Action<string>? OnBackupFailed;
        public event Action<string>? OnCloudSyncApplied;
        public event Action<string>? OnCloudSyncConflict;

        public bool HasUnsavedChanges => _hasUnsavedChanges;
        public bool HasConflict => _hasConflict;
        public string? ConflictDetails => _conflictDetails;
        public DateTime? LastBackupTimeUtc => _lastBackupTimeUtc;

        public DatabaseBackupService(
            DatabaseManager dbManager,
            NodeRepository repository,
            TelegramService telegramService,
            ConfigManager configManager)
        {
            _dbManager = dbManager;
            _repository = repository;
            _telegramService = telegramService;
            _configManager = configManager;

            // Подписываемся на любые изменения узлов файловой системы
            _repository.OnNodeChanged += HandleDatabaseModified;

            // Подписка на системное событие пробуждения ОС из режима сна/гибернации
            try
            {
                Microsoft.Win32.SystemEvents.PowerModeChanged += HandlePowerModeChanged;
            }
            catch (Exception ex)
            {
                AppLogger.Debug("DatabaseBackup", $"Не удалось подписаться на PowerModeChanged: {ex.Message}");
            }

            StartCloudSyncTimer();
        }

        private void HandlePowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            {
                AppLogger.Info("DatabaseBackup", "Обнаружено пробуждение Windows из режима сна/гибернации. Запуск проверки свежей базы через 8 сек...");
                Task.Delay(8000).ContinueWith(async _ =>
                {
                    try
                    {
                        await CheckAndSyncFromTelegramAsync(isResumeOrStartup: true);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Debug("DatabaseBackup", $"Ошибка проверки базы после сна: {ex.Message}");
                    }
                });
            }
        }

        /// <summary>
        /// Запускает или перезапускает периодический фоновый таймер проверки свежей базы в Telegram.
        /// </summary>
        public void StartCloudSyncTimer()
        {
            _cloudSyncTimer?.Dispose();
            var settings = _configManager.CurrentSettings?.Database?.Backup;
            if (settings == null || !settings.CheckForRemoteOnStartup) return;

            int intervalMinutes = Math.Clamp(settings.RemoteCheckIntervalMinutes, 1, 120);
            _cloudSyncTimer = new Timer(async _ =>
            {
                try
                {
                    await CheckAndSyncFromTelegramAsync();
                }
                catch (Exception ex)
                {
                    AppLogger.Debug("DatabaseBackup", $"Ошибка периодической фоновой синхронизации: {ex.Message}");
                }
            }, null, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(intervalMinutes));
        }

        private void HandleDatabaseModified()
        {
            _hasUnsavedChanges = true;

            var settings = _configManager.CurrentSettings?.Database?.Backup;
            if (settings == null || !settings.EnableAutoBackup) return;

            int debounceMinutes = Math.Clamp(settings.DebounceMinutes, 1, 120);

            // Перезапускаем отложенный таймер тишины (Debounce)
            lock (_backupLock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = new Timer(async _ =>
                {
                    try
                    {
                        AppLogger.Info("DatabaseBackup", $"Сработал Debounce-таймер резервного копирования ({debounceMinutes} мин тишины).");
                        await PerformBackupAsync($"Автоматический бэкап ({debounceMinutes} мин тишины)");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("DatabaseBackup", $"Ошибка выполнения автоматического бэкапа по таймеру: {ex.Message}");
                    }
                }, null, TimeSpan.FromMinutes(debounceMinutes), Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Выполняет процедуру создания консистентного снимка и выгрузки в Telegram-канал.
        /// Если обнаружен неразрешенный конфликт с удаленной базой, операция блокируется (если force == false).
        /// </summary>
        public async Task<bool> PerformBackupAsync(string triggerReason = "Ручной бэкап", bool isGracefulExit = false, bool force = false)
        {
            int timeoutMs = isGracefulExit ? 15000 : 3000;
            bool lockAcquired = await _backupLock.WaitAsync(timeoutMs);
            if (!lockAcquired)
            {
                AppLogger.Warn("DatabaseBackup", "Операция бэкапа уже выполняется другим потоком. Запрос пропущен.");
                return false;
            }

            string? snapshotPath = null;
            try
            {
                if (!_telegramService.IsAuthorized)
                {
                    AppLogger.Warn("DatabaseBackup", "Клиент Telegram не авторизован. Выгрузка резервной копии отменена.");
                    OnBackupFailed?.Invoke("Telegram не авторизован");
                    return false;
                }

                // Защита от потери данных: проверка неразрешенного конфликта версий
                if (!force)
                {
                    if (_hasConflict)
                    {
                        string conflictMsg = _conflictDetails ?? "Обнаружен конфликт версий: в Telegram есть более свежая копия базы от другого устройства.";
                        AppLogger.Warn("DatabaseBackup", $"Выгрузка бэкапа заблокирована из-за неразрешенного конфликта: {conflictMsg}");
                        OnCloudSyncConflict?.Invoke(conflictMsg);
                        OnBackupFailed?.Invoke("Выгрузка заблокирована: конфликт версий базы данных");
                        return false;
                    }

                    // Проверяем актуальность удаленной базы перед отправкой
                    try
                    {
                        var latestRemote = await _telegramService.GetLatestBackupDocumentAsync();
                        if (latestRemote.HasValue && latestRemote.Value.Document != null)
                        {
                            int remoteMsgId = latestRemote.Value.MessageId;
                            var backupSettings = _configManager.CurrentSettings?.Database?.Backup;
                            int lastSyncedId = backupSettings?.LastSyncedBackupMessageId ?? 0;
                            if (remoteMsgId > lastSyncedId)
                            {
                                string captionCheck = latestRemote.Value.Caption ?? "";
                                string currentMachine = Environment.MachineName;
                                bool isOwnMachine = captionCheck.Contains(currentMachine, StringComparison.OrdinalIgnoreCase);
                                if (!isOwnMachine)
                                {
                                    string senderMachine = ExtractMachineName(captionCheck);
                                    _hasConflict = true;
                                    _conflictDetails = $"В Telegram найдена более свежая копия базы от {senderMachine} (сообщение #{remoteMsgId}), которая еще не была синхронизирована.";
                                    AppLogger.Warn("DatabaseBackup", $"Выгрузка бэкапа заблокирована: {_conflictDetails}");
                                    OnCloudSyncConflict?.Invoke($"Выгрузка отменена: в Telegram доступна более свежая база от {senderMachine}. Синхронизируйте базу перед выгрузкой.");
                                    OnBackupFailed?.Invoke("Выгрузка заблокирована: в облаке есть более свежая копия от другого ПК");
                                    return false;
                                }
                            }
                        }
                    }
                    catch (Exception exCheck)
                    {
                        AppLogger.Debug("DatabaseBackup", $"Не удалось проверить удаленный бэкап перед выгрузкой: {exCheck.Message}");
                    }
                }

                OnBackupStarted?.Invoke(triggerReason);
                AppLogger.Info("DatabaseBackup", $"Запуск резервного копирования базы данных. Причина: '{triggerReason}'...");

                var backupSettingsConfig = _configManager.CurrentSettings?.Database?.Backup ?? new DatabaseBackupSettings();
                string backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
                Directory.CreateDirectory(backupDir);

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string fileName = $"base_{timestamp}.db";
                snapshotPath = Path.Combine(backupDir, fileName);

                // 1. Создание консистентного монолитного снимка через WAL-чекпоинт и VACUUM INTO
                _dbManager.CreateConsistentSnapshot(snapshotPath);

                if (!File.Exists(snapshotPath))
                {
                    throw new FileNotFoundException($"Файл снимка базы данных не найден: {snapshotPath}");
                }

                // 2. Сбор статистики по базе данных
                var stats = _dbManager.GetDatabaseStats();
                var fi = new FileInfo(snapshotPath);
                double sizeMb = (double)fi.Length / (1024 * 1024);

                // 3. Формирование информационной карточки для Telegram
                string caption = 
                    $"💾 Резервная копия базы данных base.db\n" +
                    $"📅 Дата: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                    $"💻 Устройство: {Environment.MachineName}\n" +
                    $"📁 Всего узлов: {stats.totalNodes:N0} (файлов: {stats.filesCount:N0}, папок: {stats.foldersCount:N0})\n" +
                    $"📦 Размер базы: {sizeMb:F2} МБ\n" +
                    $"🏷️ Причина: {triggerReason}\n" +
                    $"#db_backup";

                // 4. Загрузка в Telegram канал
                (int MessageId, long ChannelId) uploadResult;
                using (var fs = new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    uploadResult = await _telegramService.UploadBackupDocumentAsync(
                        fs,
                        fileName,
                        caption);
                }

                // Запоминаем Message ID выгруженного сообщения, чтобы собственный ПК не пытался скачивать его повторно
                if (uploadResult.MessageId > 0)
                {
                    var currentCfg = _configManager.CurrentSettings;
                    if (currentCfg?.Database?.Backup != null)
                    {
                        currentCfg.Database.Backup.LastSyncedBackupMessageId = uploadResult.MessageId;
                        _configManager.Save(currentCfg);
                    }
                }

                _hasConflict = false;
                _conflictDetails = null;
                _hasUnsavedChanges = false;
                _lastBackupTimeUtc = DateTime.UtcNow;

                // 5. Ротация локальных копий (оставляем последние 5 локальных снимков)
                RotateLocalBackups(backupDir, keepCount: 5);

                AppLogger.Info("DatabaseBackup", $"Резервная копия базы данных успешно отправлена в Telegram! (Размер: {sizeMb:F2} МБ).");
                OnBackupCompleted?.Invoke($"Успешно отправлено в Telegram ({sizeMb:F2} МБ)");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error("DatabaseBackup", $"Ошибка при выполнении резервного копирования базы: {ex.Message}", ex);
                OnBackupFailed?.Invoke(ex.Message);
                return false;
            }
            finally
            {
                _backupLock.Release();
            }
        }

        private void RotateLocalBackups(string backupDir, int keepCount)
        {
            try
            {
                var files = Directory.GetFiles(backupDir, "base_*.db")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .ToList();

                if (files.Count > keepCount)
                {
                    foreach (var oldFile in files.Skip(keepCount))
                    {
                        try
                        {
                            oldFile.Delete();
                            AppLogger.Debug("DatabaseBackup", $"Удален старый локальный бэкап: {oldFile.Name}");
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug("DatabaseBackup", $"Ошибка ротации локальных бэкапов: {ex.Message}");
            }
        }

        /// <summary>
        /// Обработчик штатного завершения работы приложения (Graceful Exit).
        /// Асинхронно сохраняет базу и отправляет в Telegram, не блокируя UI-поток.
        /// </summary>
        public async Task<bool> HandleApplicationExitAsync()
        {
            try
            {
                var settings = _configManager.CurrentSettings?.Database?.Backup;
                if (settings == null || !settings.BackupOnExit) return false;

                if (_hasUnsavedChanges && _telegramService.IsAuthorized)
                {
                    AppLogger.Info("DatabaseBackup", "Выполняется финальное сохранение базы данных при выходе из программы...");
                    return await PerformBackupAsync("Штатный выход из программы", isGracefulExit: true).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DatabaseBackup", $"Ошибка финального бэкапа при выходе: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Синхронная обертка для обратной совместимости, если вызов происходит из внешнего синхронного события.
        /// Запускает операцию через Task.Run с безопасным таймаутом, чтобы предотвратить дедлок в UI SynchronizationContext.
        /// </summary>
        public void HandleApplicationExit()
        {
            try
            {
                Task.Run(async () => await HandleApplicationExitAsync().ConfigureAwait(false)).Wait(15000);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DatabaseBackup", $"Ошибка при ожидании фонового бэкапа при выходе: {ex.Message}");
            }
        }

        /// <summary>
        /// Запрашивает информацию о последней резервной копии базы данных из канала Telegram.
        /// </summary>
        public async Task<(bool Found, int MessageId, string Caption, DateTime Date, long Size, string FileName, TL.Document? Document)> GetLatestBackupInfoAsync()
        {
            if (!_telegramService.IsAuthorized)
            {
                return (false, 0, "Telegram не авторизован", DateTime.MinValue, 0, "", null);
            }

            try
            {
                var backup = await _telegramService.GetLatestBackupDocumentAsync();
                if (backup.HasValue)
                {
                    return (true, backup.Value.MessageId, backup.Value.Caption, backup.Value.Date, backup.Value.Size, backup.Value.FileName, backup.Value.Document);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("DatabaseBackup", $"Ошибка при поиске бэкапа в Telegram: {ex.Message}", ex);
            }

            return (false, 0, "Бэкапы не найдены", DateTime.MinValue, 0, "", null);
        }

        /// <summary>
        /// Скачивает резервную копию из Telegram и накатывает ее на локальную базу base.db.
        /// </summary>
        public async Task<bool> RestoreFromTelegramAsync(TL.Document document, Action<string>? statusCallback = null, int messageId = 0)
        {
            bool lockAcquired = await _backupLock.WaitAsync(10000);
            if (!lockAcquired)
            {
                AppLogger.Warn("DatabaseBackup", "Операция с базой данных уже выполняется другим потоком.");
                return false;
            }

            string tempRestoreFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups", "restore_incoming.db");
            try
            {
                statusCallback?.Invoke("Скачивание резервной копии из Telegram...");
                string backupsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
                Directory.CreateDirectory(backupsDir);

                if (File.Exists(tempRestoreFile))
                {
                    try { File.Delete(tempRestoreFile); } catch { }
                }

                using (var fs = new FileStream(tempRestoreFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await _telegramService.DownloadBackupDocumentAsync(document, fs, (cur, tot) =>
                    {
                        if (tot > 0)
                        {
                            int pct = (int)(cur * 100 / tot);
                            statusCallback?.Invoke($"Скачивание из Telegram: {pct}%");
                        }
                    });
                }

                statusCallback?.Invoke("Применение резервной копии к базе данных...");
                _dbManager.RestoreFromSnapshot(tempRestoreFile);

                if (messageId > 0)
                {
                    var currentCfg = _configManager.CurrentSettings;
                    if (currentCfg?.Database?.Backup != null)
                    {
                        currentCfg.Database.Backup.LastSyncedBackupMessageId = messageId;
                        _configManager.Save(currentCfg);
                    }
                }

                _hasConflict = false;
                _conflictDetails = null;
                _hasUnsavedChanges = false;
                _lastBackupTimeUtc = DateTime.UtcNow;

                // Уведомляем систему об изменении узлов, чтобы обновилось дерево
                _repository.NotifyNodeChanged();

                AppLogger.Info("DatabaseBackup", "База данных успешно восстановлена из Telegram!");
                statusCallback?.Invoke("База данных успешно восстановлена!");
                return true;
            }
            catch (Exception ex)
            {
                _hasConflict = true;
                _conflictDetails = $"Сбой восстановления базы из Telegram: {ex.Message}. Выгрузка локальной базы заблокирована.";
                AppLogger.Error("DatabaseBackup", $"Ошибка восстановления базы данных из Telegram: {ex.Message}", ex);
                statusCallback?.Invoke($"Ошибка: {ex.Message}");
                OnCloudSyncConflict?.Invoke(_conflictDetails);
                return false;
            }
            finally
            {
                if (File.Exists(tempRestoreFile))
                {
                    try { File.Delete(tempRestoreFile); } catch { }
                }
                _backupLock.Release();
            }
        }

        /// <summary>
        /// Выполняет фоновую проверку наличия более свежей версии базы данных в канале Telegram.
        /// Если найдена копия от другого ПК и локально нет несохраненных изменений, автоматически накатывает ее.
        /// </summary>
        public async Task<bool> CheckAndSyncFromTelegramAsync(bool isResumeOrStartup = false)
        {
            var settings = _configManager.CurrentSettings?.Database?.Backup;
            if (settings == null || !settings.CheckForRemoteOnStartup) return false;
            if (!_telegramService.IsAuthorized) return false;

            // Не мешаем активному процессу
            if (!await _backupLock.WaitAsync(1000))
            {
                AppLogger.Debug("DatabaseBackup", "Фоновая проверка пропущена: занят _backupLock.");
                return false;
            }

            try
            {
                var backupInfo = await _telegramService.GetLatestBackupDocumentAsync();
                if (!backupInfo.HasValue || backupInfo.Value.Document == null)
                {
                    return false;
                }

                int latestMsgId = backupInfo.Value.MessageId;
                if (latestMsgId <= settings.LastSyncedBackupMessageId)
                {
                    // Уже синхронизировано или локальная версия актуальна
                    return false;
                }

                string caption = backupInfo.Value.Caption ?? "";
                string currentMachine = Environment.MachineName;

                // Проверяем, кто автор бэкапа
                bool isOwnMachine = caption.Contains(currentMachine, StringComparison.OrdinalIgnoreCase);

                if (isOwnMachine)
                {
                    // Бэкап отправлен этим же компьютером ранее, просто обновляем указатель
                    var appSettings = _configManager.CurrentSettings;
                    if (appSettings?.Database?.Backup != null)
                    {
                        appSettings.Database.Backup.LastSyncedBackupMessageId = latestMsgId;
                        _configManager.Save(appSettings);
                    }
                    return false;
                }

                string senderMachine = ExtractMachineName(caption);

                // Если есть локальные несохраненные изменения — это конфликт
                if (_hasUnsavedChanges)
                {
                    _hasConflict = true;
                    _conflictDetails = $"В Telegram доступна более свежая база от {senderMachine} (сообщение #{latestMsgId}), но на локальном ПК есть несохраненные изменения.";
                    AppLogger.Warn("DatabaseBackup", $"Обнаружен конфликт синхронизации: {_conflictDetails}");
                    OnCloudSyncConflict?.Invoke($"В Telegram доступна свежая база от {senderMachine}, но на этом ПК есть несохраненные изменения. Выгрузка базы заблокирована.");
                    return false;
                }

                // Безопасно накатываем свежую копию из Telegram без лишних вопросов
                AppLogger.Info("DatabaseBackup", $"Автоматическая синхронизация: найдена свежая база от {senderMachine} (Message #{latestMsgId}). Применение...");

                string tempRestoreFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups", "restore_incoming.db");
                string backupsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
                Directory.CreateDirectory(backupsDir);

                if (File.Exists(tempRestoreFile))
                {
                    try { File.Delete(tempRestoreFile); } catch { }
                }

                using (var fs = new FileStream(tempRestoreFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await _telegramService.DownloadBackupDocumentAsync(backupInfo.Value.Document, fs);
                }

                _dbManager.RestoreFromSnapshot(tempRestoreFile);

                if (File.Exists(tempRestoreFile))
                {
                    try { File.Delete(tempRestoreFile); } catch { }
                }

                // Запоминаем Message ID примененного бэкапа
                var currentCfg = _configManager.CurrentSettings;
                if (currentCfg?.Database?.Backup != null)
                {
                    currentCfg.Database.Backup.LastSyncedBackupMessageId = latestMsgId;
                    _configManager.Save(currentCfg);
                }

                _hasConflict = false;
                _conflictDetails = null;
                _hasUnsavedChanges = false;
                _lastBackupTimeUtc = DateTime.UtcNow;

                _repository.NotifyNodeChanged();

                string timeStr = backupInfo.Value.Date.ToLocalTime().ToString("HH:mm");
                string syncNotice = $"Структура диска синхронизирована с {senderMachine} (копия за {timeStr})";
                AppLogger.Info("DatabaseBackup", syncNotice);
                OnCloudSyncApplied?.Invoke(syncNotice);
                return true;
            }
            catch (Exception ex)
            {
                _hasConflict = true;
                _conflictDetails = $"Сбой фоновой синхронизации базы из Telegram: {ex.Message}. Выгрузка локальной базы заблокирована.";
                AppLogger.Error("DatabaseBackup", $"Ошибка при фоновой синхронизации базы из Telegram: {ex.Message}", ex);
                OnCloudSyncConflict?.Invoke(_conflictDetails);
                return false;
            }
            finally
            {
                _backupLock.Release();
            }
        }

        private static string ExtractMachineName(string caption)
        {
            if (string.IsNullOrWhiteSpace(caption)) return "другого ПК";
            foreach (var line in caption.Split('\n'))
            {
                if (line.Contains("Устройство:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(':');
                    if (parts.Length > 1)
                    {
                        return parts[1].Trim();
                    }
                }
            }
            return "другого ПК";
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                Microsoft.Win32.SystemEvents.PowerModeChanged -= HandlePowerModeChanged;
            }
            catch { }

            _repository.OnNodeChanged -= HandleDatabaseModified;
            _debounceTimer?.Dispose();
            _cloudSyncTimer?.Dispose();
            _backupLock.Dispose();
        }
    }
}
