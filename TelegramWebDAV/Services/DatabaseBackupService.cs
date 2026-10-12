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
        private DateTime? _lastBackupTimeUtc;
        private Timer? _debounceTimer;
        private readonly SemaphoreSlim _backupLock = new SemaphoreSlim(1, 1);
        private bool _isDisposed = false;

        public event Action<string>? OnBackupStarted;
        public event Action<string>? OnBackupCompleted;
        public event Action<string>? OnBackupFailed;

        public bool HasUnsavedChanges => _hasUnsavedChanges;
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
        /// </summary>
        public async Task<bool> PerformBackupAsync(string triggerReason = "Ручной бэкап", bool isGracefulExit = false)
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

                OnBackupStarted?.Invoke(triggerReason);
                AppLogger.Info("DatabaseBackup", $"Запуск резервного копирования базы данных. Причина: '{triggerReason}'...");

                var backupSettings = _configManager.CurrentSettings?.Database?.Backup ?? new DatabaseBackupSettings();
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
                using (var fs = new FileStream(snapshotPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var uploadResult = await _telegramService.UploadBackupDocumentAsync(
                        fs,
                        fileName,
                        caption);
                }

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

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _repository.OnNodeChanged -= HandleDatabaseModified;
            _debounceTimer?.Dispose();
            _backupLock.Dispose();
        }
    }
}
