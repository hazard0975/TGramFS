using System;
using System.IO;
using Microsoft.Data.Sqlite;
using TelegramWebDAV.Services;

namespace TelegramWebDAV.Database
{
    public class DatabaseManager
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public DatabaseManager(string? dbPath = null)
        {
            string path = string.IsNullOrWhiteSpace(dbPath) ? "base.db" : dbPath;
            _dbPath = Path.IsPathRooted(path) ? path : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            _connectionString = $"Data Source={_dbPath};";
        }

        /// <summary>
        /// Инициализация БД при запуске (разворачивание чистой схемы при отсутствии таблиц и создание системных папок)
        /// </summary>
        public void InitializeDatabase()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();

                // Включаем WAL режим для конкурентного доступа
                EnableWalMode(connection);

                // Всегда применяем схему (все команды содержат CREATE TABLE/INDEX IF NOT EXISTS)
                ApplySchema(connection);
                EnsureSchemaColumns(connection);
                SeedSystemFolders(connection);
            }
        }

        private void EnsureSchemaColumns(SqliteConnection connection)
        {
            try
            {
                // Проверяем наличие колонки tg_channel_id в nodes
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(nodes);";
                using var reader = cmd.ExecuteReader();
                bool hasTgChannelId = false;
                while (reader.Read())
                {
                    string colName = reader["name"]?.ToString() ?? "";
                    if (colName.Equals("tg_channel_id", StringComparison.OrdinalIgnoreCase))
                    {
                        hasTgChannelId = true;
                        break;
                    }
                }
                reader.Close();

                if (!hasTgChannelId)
                {
                    using var alterCmd = connection.CreateCommand();
                    alterCmd.CommandText = "ALTER TABLE nodes ADD COLUMN tg_channel_id INTEGER;";
                    alterCmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug("Database", $"Проверка схемы колонок: {ex.Message}");
            }
        }

        private bool HasNodesTable(SqliteConnection connection)
        {
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='nodes';";
                    var result = command.ExecuteScalar();
                    return result != null && Convert.ToInt32(result) > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private void EnableWalMode(SqliteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL;";
                command.ExecuteNonQuery();
            }
        }

        private const string EmbeddedFallbackSchema = @"
PRAGMA journal_mode=WAL;

-- Таблица аккаунтов / профилей Telegram
CREATE TABLE IF NOT EXISTS telegram_accounts (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    api_id INTEGER NOT NULL DEFAULT 0,
    api_hash TEXT NOT NULL DEFAULT '',
    phone_number TEXT,
    session_path TEXT NOT NULL DEFAULT 'user.session',
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- Таблица подключенных каналов-хранилищ Telegram
CREATE TABLE IF NOT EXISTS telegram_channels (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    account_id INTEGER NOT NULL,
    channel_id INTEGER NOT NULL UNIQUE,
    access_hash INTEGER NOT NULL,
    title TEXT NOT NULL,
    is_primary INTEGER NOT NULL DEFAULT 1,
    is_active INTEGER NOT NULL DEFAULT 1,
    channel_type TEXT NOT NULL DEFAULT 'storage',
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (account_id) REFERENCES telegram_accounts(id) ON DELETE CASCADE
);

-- Таблица узлов виртуальной файловой системы (Файлы и Папки)
CREATE TABLE IF NOT EXISTS nodes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    parent_id INTEGER,
    original_node_id INTEGER,
    name TEXT NOT NULL,
    is_dir INTEGER NOT NULL DEFAULT 0,
    version INTEGER NOT NULL DEFAULT 1,
    in_trash INTEGER NOT NULL DEFAULT 0,
    tg_message_id INTEGER,
    tg_preview_message_id INTEGER,
    tg_channel_id INTEGER,
    size INTEGER NOT NULL DEFAULT 0,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    inline_data BLOB,
    
    FOREIGN KEY (parent_id) REFERENCES nodes(id)
);

CREATE INDEX IF NOT EXISTS idx_nodes_parent_id ON nodes(parent_id);
CREATE INDEX IF NOT EXISTS idx_nodes_name ON nodes(name);
CREATE INDEX IF NOT EXISTS idx_nodes_in_trash ON nodes(in_trash);
CREATE INDEX IF NOT EXISTS idx_nodes_tg_message_id ON nodes(tg_message_id);

CREATE TABLE IF NOT EXISTS pending_channel_migrations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    node_id INTEGER NOT NULL,
    source_channel_id INTEGER NOT NULL,
    target_channel_id INTEGER NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending',
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(node_id, target_channel_id)
);
CREATE INDEX IF NOT EXISTS idx_pending_migrations_node ON pending_channel_migrations(node_id);

CREATE TABLE IF NOT EXISTS pending_caption_updates (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    node_id INTEGER NOT NULL,
    tg_message_id INTEGER NOT NULL UNIQUE,
    new_caption TEXT NOT NULL,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    status INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_pending_caption_tg_msg ON pending_caption_updates(tg_message_id);

CREATE TABLE IF NOT EXISTS pending_deletions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    tg_message_id INTEGER NOT NULL,
    channel_id INTEGER NOT NULL DEFAULT 0,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_pending_deletions_tg_msg ON pending_deletions(tg_message_id);
";

        private void ApplySchema(SqliteConnection connection)
        {
            string schemaSql;
            string schemaPath = GetSchemaFilePath();
            
            if (File.Exists(schemaPath))
            {
                schemaSql = File.ReadAllText(schemaPath);
            }
            else
            {
                schemaSql = EmbeddedFallbackSchema;
            }

            var statements = schemaSql.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawStatement in statements)
            {
                string statement = rawStatement.Trim();
                if (string.IsNullOrWhiteSpace(statement)) continue;

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = statement;
                    command.ExecuteNonQuery();
                }
            }

            // Миграция: убедимся, что колонка channel_id существует в таблице pending_deletions
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "ALTER TABLE pending_deletions ADD COLUMN channel_id INTEGER NOT NULL DEFAULT 0;";
                    command.ExecuteNonQuery();
                }
            }
            catch
            {
                // Колонка уже существует, игнорируем ошибку
            }

            // Миграция: убедимся, что колонка channel_type существует в таблице telegram_channels
            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "ALTER TABLE telegram_channels ADD COLUMN channel_type TEXT NOT NULL DEFAULT 'storage';";
                    command.ExecuteNonQuery();
                }
            }
            catch
            {
                // Колонка уже существует, игнорируем ошибку
            }
        }

        private string GetSchemaFilePath()
        {
            string path1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database", "Schema.sql");
            if (File.Exists(path1)) return path1;

            string path2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Schema.sql");
            if (File.Exists(path2)) return path2;

            string path3 = Path.Combine(Directory.GetCurrentDirectory(), "Database", "Schema.sql");
            if (File.Exists(path3)) return path3;

            string path4 = Path.Combine(Directory.GetCurrentDirectory(), "TelegramWebDAV", "Database", "Schema.sql");
            if (File.Exists(path4)) return path4;

            return path1;
        }

        private void SeedSystemFolders(SqliteConnection connection)
        {
            // 1. Создаем корневую директорию 'Root' (ID = 1), если её нет
            using (var rootCmd = connection.CreateCommand())
            {
                rootCmd.CommandText = @"
                    INSERT INTO nodes (parent_id, name, is_dir)
                    SELECT NULL, 'Root', 1
                    WHERE NOT EXISTS (SELECT 1 FROM nodes WHERE parent_id IS NULL AND name = 'Root');
                ";
                rootCmd.ExecuteNonQuery();
            }

            // 2. Создаем системную корзину '.Trash' (ID = 2) в корневом каталоге, если её нет
            using (var trashCmd = connection.CreateCommand())
            {
                trashCmd.CommandText = @"
                    INSERT INTO nodes (parent_id, name, is_dir)
                    SELECT (SELECT id FROM nodes WHERE parent_id IS NULL AND name = 'Root' LIMIT 1), '.Trash', 1
                    WHERE NOT EXISTS (
                        SELECT 1 FROM nodes 
                        WHERE parent_id = (SELECT id FROM nodes WHERE parent_id IS NULL AND name = 'Root' LIMIT 1) 
                          AND name = '.Trash'
                    );
                ";
                trashCmd.ExecuteNonQuery();
            }
        }

        public string DatabasePath => _dbPath;

        private static readonly object _vacuumLock = new object();
        private static System.Threading.Timer? _debounceVacuumTimer;

        /// <summary>
        /// Выполняет дефрагментацию и оптимизацию базы данных (VACUUM), освобождая дисковое пространство.
        /// </summary>
        public void VacuumDatabase()
        {
            lock (_vacuumLock)
            {
                try
                {
                    AppLogger.Info("Database", "Запуск оптимизации базы данных SQLite (VACUUM)...");
                    using var connection = GetConnection();
                    using var command = connection.CreateCommand();
                    command.CommandText = "VACUUM;";
                    command.ExecuteNonQuery();
                    AppLogger.Info("Database", "Оптимизация базы данных (VACUUM) успешно завершена.");
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Database", $"Ошибка при выполнении VACUUM: {ex.Message}", ex);
                    throw;
                }
            }
        }

        /// <summary>
        /// Отложенный запуск VACUUM с дебаунсом (сжатие выполняется один раз после завершения пачки удалений).
        /// </summary>
        public void ScheduleVacuum(int delayMs = 3000)
        {
            lock (_vacuumLock)
            {
                _debounceVacuumTimer?.Dispose();
                _debounceVacuumTimer = new System.Threading.Timer(_ =>
                {
                    try
                    {
                        VacuumDatabase();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("Database", $"Фоновый отложенный VACUUM завершился с ошибкой: {ex.Message}");
                    }
                }, null, delayMs, System.Threading.Timeout.Infinite);
            }
        }

        /// <summary>
        /// Создает консистентный монолитный снимок базы данных через принудительный сброс WAL и VACUUM INTO.
        /// </summary>
        public void CreateConsistentSnapshot(string targetPath)
        {
            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(targetPath))
            {
                try { File.Delete(targetPath); } catch { }
            }

            lock (_vacuumLock)
            {
                try
                {
                    AppLogger.Info("Database", $"Создание консистентного снимка базы данных в: {targetPath}...");
                    using var connection = GetConnection();

                    // 1. Принудительный сброс всех страниц WAL обратно в основной файл base.db и усечение WAL
                    using (var checkpointCmd = connection.CreateCommand())
                    {
                        checkpointCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                        checkpointCmd.ExecuteNonQuery();
                    }

                    // 2. Создание изолированного атомарного снимка через VACUUM INTO
                    using (var vacuumIntoCmd = connection.CreateCommand())
                    {
                        string escapedPath = targetPath.Replace("'", "''");
                        vacuumIntoCmd.CommandText = $"VACUUM INTO '{escapedPath}';";
                        vacuumIntoCmd.ExecuteNonQuery();
                    }

                    AppLogger.Info("Database", $"Снимок базы данных успешно создан: {targetPath}");
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Database", $"Ошибка при создании снимка базы данных: {ex.Message}", ex);
                    throw;
                }
            }
        }

        /// <summary>
        /// Получает сводную статистику по узлам базы данных для карточки резервного копирования.
        /// </summary>
        public (int totalNodes, int filesCount, int foldersCount, long totalSizeBytes) GetDatabaseStats()
        {
            try
            {
                using var connection = GetConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT 
                        COUNT(*) as total,
                        SUM(CASE WHEN is_dir = 0 THEN 1 ELSE 0 END) as files,
                        SUM(CASE WHEN is_dir = 1 THEN 1 ELSE 0 END) as folders,
                        SUM(size) as total_size
                    FROM nodes 
                    WHERE in_trash = 0;
                ";
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    int total = reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader[0]);
                    int files = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader[1]);
                    int folders = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader[2]);
                    long size = reader.IsDBNull(3) ? 0L : Convert.ToInt64(reader[3]);
                    return (total, files, folders, size);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug("Database", $"Ошибка получения статистики базы: {ex.Message}");
            }
            return (0, 0, 0, 0L);
        }

        /// <summary>
        /// Безопасно накатывает файл снимка базы данных на основную базу данных base.db.
        /// Создает защитный локальный снимок перед накатом, сбрасывает пулы соединений SQLite,
        /// заменяет файл, удаляет временные WAL/SHM файлы и проводит валидацию целостности.
        /// </summary>
        public void RestoreFromSnapshot(string snapshotFilePath)
        {
            if (!File.Exists(snapshotFilePath))
            {
                throw new FileNotFoundException($"Файл снимка для восстановления не найден: {snapshotFilePath}");
            }

            lock (_vacuumLock)
            {
                AppLogger.Info("Database", $"Начало процедуры восстановления базы из снимка: {snapshotFilePath}...");

                // 1. Сброс всех пулов соединений SQLite, чтобы освободить дескрипторы файлов
                SqliteConnection.ClearAllPools();

                // 2. Создание защитного снимка текущей базы (Safety Snapshot)
                string backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
                Directory.CreateDirectory(backupDir);
                string safetyBackupPath = Path.Combine(backupDir, $"base_pre_restore_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.db");

                if (File.Exists(_dbPath))
                {
                    try
                    {
                        File.Copy(_dbPath, safetyBackupPath, overwrite: true);
                        AppLogger.Info("Database", $"Создан аварийный снимок перед восстановлением: {safetyBackupPath}");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("Database", $"Не удалось создать аварийный снимок базы: {ex.Message}");
                    }
                }

                // 3. Замена основного файла base.db
                try
                {
                    File.Copy(snapshotFilePath, _dbPath, overwrite: true);

                    // Удаляем связанные WAL и SHM файлы, чтобы база открылась в чистом виде
                    string walPath = _dbPath + "-wal";
                    string shmPath = _dbPath + "-shm";
                    if (File.Exists(walPath)) { try { File.Delete(walPath); } catch { } }
                    if (File.Exists(shmPath)) { try { File.Delete(shmPath); } catch { } }

                    AppLogger.Info("Database", "Файл base.db успешно заменен снимком из бэкапа.");
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Database", $"Ошибка при замене файла base.db: {ex.Message}", ex);
                    // Попытка отката к аварийному снимку при сбое
                    if (File.Exists(safetyBackupPath))
                    {
                        try { File.Copy(safetyBackupPath, _dbPath, overwrite: true); } catch { }
                    }
                    throw;
                }

                // 4. Инициализация и проверка целостности восстановленной базы
                InitializeDatabase();

                using (var connection = GetConnection())
                {
                    using var checkCmd = connection.CreateCommand();
                    checkCmd.CommandText = "PRAGMA integrity_check;";
                    var result = checkCmd.ExecuteScalar()?.ToString();
                    if (result != "ok")
                    {
                        AppLogger.Warn("Database", $"Предупреждение целостности базы данных: {result}");
                    }
                    else
                    {
                        AppLogger.Info("Database", "Целостность восстановленной базы данных подтверждена: ok");
                    }
                }
            }
        }

        public SqliteConnection GetConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }
    }
}
