using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TelegramWebDAV.Config;

namespace TelegramWebDAV.Services
{
    public class UpdateInfo
    {
        public string Version { get; set; } = string.Empty;
        public string TagName { get; set; } = string.Empty;
        public DateTime? PublishedAt { get; set; }
        public string? ReleaseNotes { get; set; }
        public string? DownloadUrl { get; set; }
        public string? AssetName { get; set; }
        public long SizeBytes { get; set; }
        public bool IsNewer { get; set; }
    }

    public class UpdateDownloadProgress
    {
        public long BytesReceived { get; set; }
        public long TotalBytesToReceive { get; set; }
        public int ProgressPercentage => TotalBytesToReceive > 0 ? (int)((BytesReceived * 100) / TotalBytesToReceive) : 0;
    }

    public class UpdateService : IDisposable
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly ConfigManager _configManager;
        private System.Threading.Timer? _periodicTimer;
        private bool _isChecking = false;

        public event Action<UpdateInfo>? OnUpdateAvailable;

        static UpdateService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TGramFS-Updater");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public UpdateService(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        public Version GetCurrentVersion()
        {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;
            if (ver != null) return ver;

            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                var fvi = FileVersionInfo.GetVersionInfo(exe);
                if (Version.TryParse(fvi.FileVersion, out var parsed))
                {
                    return parsed;
                }
            }

            return new Version(1, 0, 0, 0);
        }

        public DateTime GetCurrentBuildDate()
        {
            try
            {
                string? exe = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    return File.GetLastWriteTime(exe);
                }
            }
            catch { }

            return DateTime.Now;
        }

        public void StartPeriodicChecks()
        {
            // Запускаем первую проверку через 15 секунд после старта приложения,
            // затем опрашиваем таймер каждый час для сверки интервала
            _periodicTimer?.Dispose();
            _periodicTimer = new System.Threading.Timer(async _ =>
            {
                await CheckAutoUpdateScheduleAsync();
            }, null, TimeSpan.FromSeconds(15), TimeSpan.FromHours(1));
        }

        private async Task CheckAutoUpdateScheduleAsync()
        {
            try
            {
                var settings = _configManager.Load();
                if (!settings.Updates.AutoCheckUpdates) return;

                var now = DateTime.Now;
                var lastCheck = settings.Updates.LastCheckTime;
                var interval = GetIntervalTimeSpan(settings.Updates.CheckInterval);

                if (!lastCheck.HasValue || (now - lastCheck.Value) >= interval)
                {
                    var info = await CheckForUpdatesAsync();
                    if (info != null && info.IsNewer)
                    {
                        OnUpdateAvailable?.Invoke(info);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("UpdateService", $"Ошибка при фоновой проверке обновлений: {ex.Message}");
            }
        }

        private static TimeSpan GetIntervalTimeSpan(string? interval)
        {
            return interval?.ToLowerInvariant() switch
            {
                "weekly" or "еженедельно" => TimeSpan.FromDays(7),
                "monthly" or "ежемесячно" => TimeSpan.FromDays(30),
                _ => TimeSpan.FromDays(1) // "daily" / "ежедневно"
            };
        }

        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            if (_isChecking) return null;
            _isChecking = true;

            try
            {
                var settings = _configManager.Load();
                string repo = string.IsNullOrWhiteSpace(settings.Updates.GitHubRepo)
                    ? "hazard0975/TGramFS"
                    : settings.Updates.GitHubRepo.Trim();

                string url = $"https://api.github.com/repos/{repo}/releases/latest";
                AppLogger.Info("UpdateService", $"Проверка обновлений на GitHub: {url}");

                using var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Warn("UpdateService", $"GitHub API вернул статус: {response.StatusCode} ({response.ReasonPhrase})");
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tagName = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
                string body = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
                DateTime? publishedAt = null;
                if (root.TryGetProperty("published_at", out var pubEl) && pubEl.TryGetDateTime(out var dt))
                {
                    publishedAt = dt.ToLocalTime();
                }

                // Ищем подходящий ассет (.exe)
                string? downloadUrl = null;
                string? assetName = null;
                long assetSize = 0;

                if (root.TryGetProperty("assets", out var assetsEl) && assetsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsEl.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            assetName = name;
                            downloadUrl = asset.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() : null;
                            assetSize = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;
                            break;
                        }
                    }
                }

                string cleanVersion = tagName.TrimStart('v', 'V');
                Version.TryParse(cleanVersion, out var latestVer);
                var currentVer = GetCurrentVersion();

                bool isNewer = false;
                if (latestVer != null)
                {
                    isNewer = latestVer > currentVer;
                }

                // Сохраняем дату проверки
                settings.Updates.LastCheckTime = DateTime.Now;
                _configManager.Save(settings);

                var updateInfo = new UpdateInfo
                {
                    Version = cleanVersion,
                    TagName = tagName,
                    PublishedAt = publishedAt,
                    ReleaseNotes = body,
                    DownloadUrl = downloadUrl,
                    AssetName = assetName,
                    SizeBytes = assetSize,
                    IsNewer = isNewer
                };

                AppLogger.Info("UpdateService", $"Текущая версия: {currentVer}, Последняя на GitHub: {cleanVersion} (isNewer: {isNewer})");
                return updateInfo;
            }
            catch (Exception ex)
            {
                AppLogger.Error("UpdateService", $"Не удалось проверить обновления: {ex.Message}", ex);
                throw;
            }
            finally
            {
                _isChecking = false;
            }
        }

        public async Task<string> DownloadUpdateAsync(UpdateInfo info, IProgress<UpdateDownloadProgress>? progress, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(info.DownloadUrl))
            {
                throw new InvalidOperationException("Ссылка на загрузку обновления отсутствует.");
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string fileName = !string.IsNullOrWhiteSpace(info.AssetName)
                ? info.AssetName
                : $"TGramFS-{(info.TagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? info.TagName : "v" + info.Version)}.exe";

            string finalFilePath = Path.Combine(baseDir, fileName);

            // Если файл с таким именем уже существует и его размер совпадает с размером в релизе — не скачиваем повторно
            if (File.Exists(finalFilePath) && info.SizeBytes > 0)
            {
                var existingFi = new FileInfo(finalFilePath);
                if (existingFi.Length == info.SizeBytes)
                {
                    AppLogger.Info("UpdateService", $"Файл обновления {fileName} уже существует на диске ({existingFi.Length} байт). Повторное скачивание пропущено.");
                    progress?.Report(new UpdateDownloadProgress
                    {
                        BytesReceived = existingFi.Length,
                        TotalBytesToReceive = existingFi.Length
                    });
                    return finalFilePath;
                }
            }

            string downloadTempPath = finalFilePath + ".download";
            if (File.Exists(downloadTempPath))
            {
                try { File.Delete(downloadTempPath); } catch { }
            }

            AppLogger.Info("UpdateService", $"Скачивание обновления {fileName} в {downloadTempPath} из {info.DownloadUrl}");

            using (var response = await _httpClient.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                long totalBytes = response.Content.Headers.ContentLength ?? info.SizeBytes;

                using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
                using (var fileStream = new FileStream(downloadTempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    byte[] buffer = new byte[81920];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                        totalRead += bytesRead;
                        progress?.Report(new UpdateDownloadProgress
                        {
                            BytesReceived = totalRead,
                            TotalBytesToReceive = totalBytes
                        });
                    }
                }
            }

            // Проверяем корректность скачанного файла
            var fi = new FileInfo(downloadTempPath);
            if (fi.Length == 0)
            {
                try { File.Delete(downloadTempPath); } catch { }
                throw new IOException("Скачанный файл обновления пуст.");
            }

            // Перемещаем готовый файл на целевое постоянное имя с версией
            File.Move(downloadTempPath, finalFilePath, true);

            AppLogger.Info("UpdateService", $"Файл обновления успешно сохранен как {finalFilePath}: {fi.Length} байт.");
            return finalFilePath;
        }

        public void ApplyUpdateAndRestart(string newExePath, Action onExitRequested)
        {
            if (string.IsNullOrEmpty(newExePath) || !File.Exists(newExePath))
            {
                throw new FileNotFoundException($"Файл новой версии не найден по пути: {newExePath}");
            }

            int currentPid = Environment.ProcessId;
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string scriptPath = Path.Combine(baseDir, $"launch_update_{Guid.NewGuid():N}.cmd");

            // Формируем cmd-скрипт с ожиданием завершения старого процесса и прямым запуском нового exe с версией
            string scriptContent = $@"@echo off
chcp 65001 > nul
set TARGET_PID={currentPid}
set NEW_EXE=""{newExePath}""

:wait_loop
tasklist /fi ""PID eq %TARGET_PID%"" 2>nul | find ""%TARGET_PID%"" > nul
if not errorlevel 1 (
    timeout /t 1 /nobreak > nul
    goto wait_loop
)

timeout /t 1 /nobreak > nul

start """" %NEW_EXE%
del ""%~f0"" > nul 2>&1
";

            File.WriteAllText(scriptPath, scriptContent, System.Text.Encoding.GetEncoding(866)); // OEM кодировка для cmd

            AppLogger.Info("UpdateService", $"Создан скрипт запуска новой версии {scriptPath}. Старый процесс закрывается, затем запустится {newExePath}.");

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = baseDir
            };

            Process.Start(psi);

            // Инициируем штатное закрытие программы
            onExitRequested();
        }

        public void Dispose()
        {
            _periodicTimer?.Dispose();
        }
    }
}
