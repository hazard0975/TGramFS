using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TGramFS.Config;
using TGramFS.Database;
using TGramFS.Models;
using TL;
using System.Net.Sockets;

namespace TGramFS.Services
{
    /// <summary>
    /// Исключение, сигнализирующее о временной недоступности сетевого стека или серверов Telegram (Wi-Fi, сокет, офлайн).
    /// </summary>
    public class TelegramNetworkException : IOException
    {
        public TelegramNetworkException(string message) : base(message) { }
        public TelegramNetworkException(string message, Exception innerException) : base(message, innerException) { }
    }

    public enum AuthStep
    {
        NeedsPhone,
        NeedsCode,
        Needs2FA,
        Authorized
    }

    public enum FloodWaitScope
    {
        General = 0,
        Download = 1,
        Upload = 2,
        Caption = 3,
        Background = 4
    }

    public class TelegramUserInfo
    {
        public long Id { get; set; }
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Phone { get; set; }
        public bool IsPremium { get; set; }
        public int DcId { get; set; }
        public int FloodWaitSecondsRemaining { get; set; }

        public string FullName
        {
            get
            {
                string fn = (FirstName ?? "").Trim();
                string ln = (LastName ?? "").Trim();
                if (!string.IsNullOrEmpty(fn) && !string.IsNullOrEmpty(ln)) return $"{fn} {ln}";
                if (!string.IsNullOrEmpty(fn)) return fn;
                if (!string.IsNullOrEmpty(ln)) return ln;
                return "Без имени";
            }
        }

        public string FormattedUsername => !string.IsNullOrWhiteSpace(Username) ? $"@{Username.TrimStart('@')}" : "Не задан";

        public string FormattedPhone
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Phone)) return "Не указан";
                return Phone.StartsWith("+") ? Phone : "+" + Phone;
            }
        }

        public string PremiumDescription => IsPremium
            ? "⭐ Telegram Premium (лимит 1 файла: 4 ГБ)"
            : "Базовый аккаунт (лимит 1 файла: 2 ГБ)";

        public string DcDescription
        {
            get
            {
                return DcId switch
                {
                    1 => "DC 1 (Майами, США)",
                    2 => "DC 2 (Амстердам, Нидерланды)",
                    3 => "DC 3 (Майами, США)",
                    4 => "DC 4 (Амстердам, Нидерланды)",
                    5 => "DC 5 (Сингапур)",
                    _ => DcId > 0 ? $"DC {DcId}" : "Автоопределение"
                };
            }
        }
    }

    public sealed class PendingCaptionUpdate
    {
        public int MessageId { get; }
        public string Caption { get; }
        public long? ChannelId { get; }

        public PendingCaptionUpdate(int messageId, string caption, long? channelId = null)
        {
            MessageId = messageId;
            Caption = caption;
            ChannelId = channelId;
        }
    }

    public sealed class ActiveFloodBannerState
    {
        public FloodWaitScope Scope { get; set; }
        public DateTime FloodWaitUntilUtc { get; set; }
        public int InitialSeconds { get; set; }
        public bool IsUploadStopped { get; set; }
        public bool IsDownload { get; set; }
        public string? FileName { get; set; }
        public int RemainingSeconds => Math.Max(0, (int)Math.Ceiling((FloodWaitUntilUtc - DateTime.UtcNow).TotalSeconds));
    }

    public class FileUploadResult
    {
        public int MessageId { get; set; }
        public int? PreviewMessageId { get; set; }
        public List<Models.NodePart>? Parts { get; set; }
        public List<PendingCaptionUpdate>? PendingCaptions { get; set; }

        public FileUploadResult(
            int messageId,
            int? previewMessageId = null,
            List<Models.NodePart>? parts = null,
            List<PendingCaptionUpdate>? pendingCaptions = null)
        {
            MessageId = messageId;
            PreviewMessageId = previewMessageId;
            Parts = parts;
            PendingCaptions = pendingCaptions;
        }

        public static implicit operator int?(FileUploadResult? res) => res?.MessageId;
    }

    /// <summary>
    /// Сервис для работы с Telegram API через WTelegramClient.
    /// Управляет подключением, сессией (.session), многошаговой авторизацией и защитой от FLOOD_WAIT.
    /// </summary>
    public class TelegramService : IDisposable
    {
        private readonly ConfigManager _configManager;
        private NodeRepository? _repository;
        private AppSettings _currentSettings;
        private readonly SemaphoreSlim _floodLock = new SemaphoreSlim(1, 1);
        private static DateTime _sessionFloodWaitUntilUtc = DateTime.MinValue;
        private static DateTime _downloadFloodWaitUntilUtc = DateTime.MinValue;
        private static DateTime _captionFloodWaitUntilUtc = DateTime.MinValue;
        private static DateTime _backgroundFloodWaitUntilUtc = DateTime.MinValue;
        private WTelegram.Client? _client;
        private System.Threading.CancellationTokenSource? _queueCts;

        // Координация состояния подключения и ожидания сети читателями
        private readonly object _connectionLock = new();
        private TaskCompletionSource<bool>? _connectionReadyTcs;
        private DateTime _lastConnectionFailureTime = DateTime.MinValue;
        private volatile bool _isNetworkOffline = false;

        // Очередь последовательной загрузки файлов в Telegram (Upload Queue)
        // Предотвращает конкуренцию за полосу пропускания, мерцание оверлея и FLOOD_WAIT
        private readonly SemaphoreSlim _uploadSemaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _channelDeleteSemaphore = new SemaphoreSlim(1, 1);
        private int _pendingUploadsCount = 0;
        private int _activeUploadPipelineCount = 0;
        private static int _activeUploadMessageRpcCount = 0;
        private static int _activeDownloadRequestCount = 0;
        private static int _activeCaptionRequestCount = 0;
        private static int _activeBackgroundRequestCount = 0;

        /// <summary>
        /// Количество файлов в очереди на отправку в Telegram (включая текущий передаваемый).
        /// </summary>
        public int PendingUploadsCount => _pendingUploadsCount;

        // Кэш дескрипторов документов Telegram (TL.Document) по составному ключу (channelId, messageId) для исключения межканальных коллизий
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(long channelId, int messageId), (TL.Document? document, DateTime expiresAt)> _documentCache = new();

        // Быстрый кольцевой кэш чанков MTProto в оперативной памяти (настраивается в UI, по умолчанию 128 МБ) для мгновенного чтения плеерами без повторных обращений к сети
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (byte[] data, DateTime expiresAt)> _chunkMemoryCache = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(long channelId, int messageId), SemaphoreSlim> _fileDownloadLocks = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<(long channelId, int messageId), NetworkTransferAudit> _networkAudits = new();
        private MtprotoDownloadWorkerPool? _workerPool;

        private class FileReadSequence
        {
            public long LastChunkIndex { get; set; } = -1;
            public int SequentialCount { get; set; } = 0;
            public long AccumulatedSequentialBytes { get; set; } = 0;
            public DateTime LastReadTime { get; set; } = DateTime.UtcNow;
            public System.Collections.Concurrent.ConcurrentDictionary<long, long> ChunkBytesRead { get; } = new();
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<(long channelId, int messageId), FileReadSequence> _fileReadSequences = new();

        public class NetworkTransferAudit
        {
            public string FileName { get; set; } = string.Empty;
            public string? FullVirtualPath { get; set; }
            public long FileSize { get; set; }
            public DateTime LastActivityTime { get; set; } = DateTime.UtcNow;
            private long _networkBytesDownloaded;
            private long _ramBytesDelivered;
            private int _completionLogged;
            private int _metadataCompletedLogged;

            private readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> _receivedChunkIndexes = new();
            private readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> _chunkBytesAccumulator = new();

            public void TouchActivity() => LastActivityTime = DateTime.UtcNow;

            public int TotalChunks => FileSize > 0 ? (int)Math.Ceiling((double)FileSize / 1048576.0) : 1;
            public int ReceivedChunksCount => _receivedChunkIndexes.Count;
            public string ProgressSummary
            {
                get
                {
                    double pct = TotalChunks > 0 ? (double)ReceivedChunksCount * 100.0 / TotalChunks : 100.0;
                    return $"Скачано {ReceivedChunksCount} из {TotalChunks} чанков ({pct:0.#}%)";
                }
            }

            public void AddNetworkBytes(long bytes)
            {
                TouchActivity();
                Interlocked.Add(ref _networkBytesDownloaded, bytes);
            }

            public void AddRamBytes(long bytes)
            {
                TouchActivity();
                Interlocked.Add(ref _ramBytesDelivered, bytes);
            }

            public long NetworkBytes => Interlocked.Read(ref _networkBytesDownloaded);
            public long RamBytes => Interlocked.Read(ref _ramBytesDelivered);

            /// <summary>
            /// Помечает диапазон байт как полученный. Чанк отмечается как завершенный при получении 100% байт данного блока (в том числе слайсами по 256 КБ).
            /// Возвращает true, если получены ВСЕ чанки файла (100% загрузка).
            /// </summary>
            public bool MarkRangeReceived(long offset, long length)
            {
                TouchActivity();
                if (FileSize <= 0) return true;
                if (length <= 0) return IsAllChunksReceived();

                int total = TotalChunks;
                long endPos = Math.Min(FileSize, offset + length);
                if (endPos <= offset) return IsAllChunksReceived();

                int startChunk = (int)(offset / 1048576);
                int endChunk = (int)((endPos - 1) / 1048576);

                for (int i = startChunk; i <= endChunk && i < total; i++)
                {
                    if (i >= 0)
                    {
                        long chunkStart = (long)i * 1048576;
                        long chunkEnd = Math.Min(FileSize, chunkStart + 1048576);
                        long expectedChunkBytes = chunkEnd - chunkStart;

                        // Считаем пересечение диапазона с данным чанком
                        long overlapStart = Math.Max(offset, chunkStart);
                        long overlapEnd = Math.Min(endPos, chunkEnd);
                        long overlapBytes = Math.Max(0, overlapEnd - overlapStart);

                        if (overlapBytes > 0)
                        {
                            long accumulated = _chunkBytesAccumulator.AddOrUpdate(i, overlapBytes, (_, old) => old + overlapBytes);
                            if (accumulated >= expectedChunkBytes || (offset <= chunkStart && endPos >= chunkEnd))
                            {
                                _receivedChunkIndexes.TryAdd(i, true);
                            }
                        }
                    }
                }

                return IsAllChunksReceived();
            }

            public bool IsAllChunksReceived()
            {
                if (FileSize <= 0) return true;
                return _receivedChunkIndexes.Count >= TotalChunks;
            }

            public bool LogMetadataCompletedOnce()
            {
                return Interlocked.CompareExchange(ref _metadataCompletedLogged, 1, 0) == 0;
            }

            public bool LogCompletionOnce()
            {
                if (Interlocked.CompareExchange(ref _completionLogged, 1, 0) == 0)
                {
                    long net = NetworkBytes;
                    long ram = RamBytes;
                    double ratio = FileSize > 0 ? (double)net / FileSize : 1.0;
                    string verdict;
                    if (ratio <= 1.05)
                        verdict = "1.00x (Идеально — 0% дублирования)";
                    else if (ratio <= 1.25)
                        verdict = $"{ratio:F2}x (Допустимо — остаток хвоста чанка)";
                    else
                        verdict = $"{ratio:F2}x (ВНИМАНИЕ — обнаружено избыточное дублирование сетевых запросов!)";

                    string pathTag = !string.IsNullOrEmpty(FullVirtualPath) ? $" ({FullVirtualPath})" : "";
                    string msg = $"[NetworkAudit] Завершено для '{FileName}'{pathTag}: Размер: {FileSize:N0} б ({FileSize / 1048576.0:F2} МБ) | Из сети TG: {net:N0} б ({net / 1048576.0:F2} МБ) | Из ОЗУ: {ram:N0} б ({ram / 1048576.0:F2} МБ) | Эффективность сети: {verdict}";

                    if (ratio > 1.25)
                        AppLogger.Warn("TelegramService", msg);
                    else
                        AppLogger.Info("TelegramService", msg);

                    return true;
                }
                return false;
            }

            public void ResetCompletion()
            {
                Interlocked.Exchange(ref _completionLogged, 0);
            }
        }

        private void StoreChunkInMemoryCache(long channelId, int messageId, long chunkOffset, byte[] raw, int cacheTtlMinutes)
        {
            EnsureChunkCacheCapacity();
            long megaStart = (chunkOffset / 1048576) * 1048576;
            long incomingStart = chunkOffset;

            // Собираем все фрагменты этого же 1 МБ блока в ОЗУ для интервального слияния
            var fragmentsToMerge = new System.Collections.Generic.List<(string key, long start, byte[] data)>
            {
                (string.Empty, incomingStart, raw)
            };

            foreach (var kvp in _chunkMemoryCache)
            {
                var parts = kvp.Key.Split(':');
                if (parts.Length == 4
                    && long.TryParse(parts[0], out var chId) && chId == channelId
                    && int.TryParse(parts[1], out var mId) && mId == messageId
                    && long.TryParse(parts[2], out var start))
                {
                    if (start >= megaStart && start < megaStart + 1048576 && kvp.Value.data != null)
                    {
                        fragmentsToMerge.Add((kvp.Key, start, kvp.Value.data));
                    }
                }
            }

            // Сортируем фрагменты по возрастанию начального смещения
            fragmentsToMerge.Sort((a, b) => a.start.CompareTo(b.start));

            // Проверяем непрерывность цепочки фрагментов
            long mergedStart = fragmentsToMerge[0].start;
            long mergedEnd = fragmentsToMerge[0].start + fragmentsToMerge[0].data.Length;
            bool canMergeAll = true;

            for (int i = 1; i < fragmentsToMerge.Count; i++)
            {
                if (fragmentsToMerge[i].start <= mergedEnd)
                {
                    mergedEnd = Math.Max(mergedEnd, fragmentsToMerge[i].start + fragmentsToMerge[i].data.Length);
                }
                else
                {
                    canMergeAll = false;
                    break;
                }
            }

            if (canMergeAll && fragmentsToMerge.Count > 1)
            {
                int mergedLength = (int)(mergedEnd - mergedStart);
                byte[] mergedBuffer = new byte[mergedLength];

                foreach (var frag in fragmentsToMerge)
                {
                    int destOffset = (int)(frag.start - mergedStart);
                    Buffer.BlockCopy(frag.data, 0, mergedBuffer, destOffset, frag.data.Length);
                    if (!string.IsNullOrEmpty(frag.key))
                    {
                        _chunkMemoryCache.TryRemove(frag.key, out _);
                    }
                }

                string mergedKey = $"{channelId}:{messageId}:{mergedStart}:{mergedLength}";
                _chunkMemoryCache[mergedKey] = (mergedBuffer, DateTime.UtcNow.AddMinutes(cacheTtlMinutes));
                return;
            }

            // Если пришел полный 1 МБ блок (>= 1048576 байт), вычищаем любые старые частичные фрагменты этого же блока
            if (raw.Length >= 1048576)
            {
                foreach (var key in _chunkMemoryCache.Keys)
                {
                    var parts = key.Split(':');
                    if (parts.Length == 4
                        && long.TryParse(parts[0], out var chId) && chId == channelId
                        && int.TryParse(parts[1], out var mId) && mId == messageId
                        && long.TryParse(parts[2], out var cStart) && cStart >= megaStart && cStart < megaStart + 1048576)
                    {
                        _chunkMemoryCache.TryRemove(key, out _);
                    }
                }
            }
            else
            {
                string exactKey = $"{channelId}:{messageId}:{chunkOffset}:{raw.Length}";
                _chunkMemoryCache.TryRemove(exactKey, out _);
            }

            string canonicalKey = $"{channelId}:{messageId}:{chunkOffset}:{raw.Length}";
            _chunkMemoryCache[canonicalKey] = (raw, DateTime.UtcNow.AddMinutes(cacheTtlMinutes));
        }

        private bool TryGetFromMemoryCache(long channelId, int messageId, long pos, out byte[]? data, out int offsetInChunk)
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _chunkMemoryCache)
            {
                var parts = kvp.Key.Split(':');
                if (parts.Length == 4
                    && long.TryParse(parts[0], out var chId) && chId == channelId
                    && int.TryParse(parts[1], out var mId) && mId == messageId)
                {
                    if (long.TryParse(parts[2], out var chunkStart) && int.TryParse(parts[3], out var _))
                    {
                        var chunkData = kvp.Value.data;
                        if (pos >= chunkStart && pos < chunkStart + chunkData.Length && kvp.Value.expiresAt > now)
                        {
                            data = chunkData;
                            offsetInChunk = (int)(pos - chunkStart);
                            return true;
                        }
                    }
                }
            }
            data = null;
            offsetInChunk = 0;
            return false;
        }

        private void EnsureChunkCacheCapacity()
        {
            int maxChunks = 128; // По умолчанию 128 МБ в ОЗУ
            try
            {
                int configMb = _configManager?.CurrentSettings?.Server?.MemoryCacheSizeMb ?? 128;
                maxChunks = Math.Max(16, configMb); // 1 чанк = 1 МБ
            }
            catch { }

            if (_chunkMemoryCache.Count > maxChunks)
            {
                var now = DateTime.UtcNow;
                foreach (var key in _chunkMemoryCache.Keys)
                {
                    if (_chunkMemoryCache.TryGetValue(key, out var item) && item.expiresAt <= now)
                    {
                        _chunkMemoryCache.TryRemove(key, out _);
                    }
                }
                if (_chunkMemoryCache.Count > maxChunks)
                {
                    int toRemove = Math.Max(8, _chunkMemoryCache.Count - maxChunks);
                    var oldest = _chunkMemoryCache.OrderBy(k => k.Value.expiresAt).Take(toRemove).ToList();
                    foreach (var kvp in oldest)
                    {
                        _chunkMemoryCache.TryRemove(kvp.Key, out _);
                    }
                }
            }
        }

        /// <summary>
        /// Ограничивает размер словарей аудита передачи и контекстов чтения в ОЗУ (LRU + TTL).
        /// Предотвращает накопление объектов в памяти при длительной непрерывной работе ПК без перезагрузки (в т.ч. в режиме гибернации).
        /// </summary>
        private void EnsureAuditCacheCapacity()
        {
            const int maxAudits = 100;
            var now = DateTime.UtcNow;
            int ttlMinutes = Math.Max(15, _currentSettings?.Server?.ChunkMemoryCacheTtlMinutes ?? 30);

            // 1. Очистка по TTL (неактивность дольше ttlMinutes)
            foreach (var kvp in _networkAudits)
            {
                if ((now - kvp.Value.LastActivityTime).TotalMinutes > ttlMinutes)
                {
                    if (_networkAudits.TryRemove(kvp.Key, out _))
                    {
                        _fileReadSequences.TryRemove(kvp.Key, out _);
                        _fileDownloadLocks.TryRemove(kvp.Key, out _);
                    }
                }
            }

            // 2. Вытеснение самых старых по LRU при превышении лимита maxAudits
            if (_networkAudits.Count > maxAudits)
            {
                var oldest = _networkAudits.OrderBy(k => k.Value.LastActivityTime).Take(_networkAudits.Count - maxAudits).ToList();
                foreach (var kvp in oldest)
                {
                    if (_networkAudits.TryRemove(kvp.Key, out _))
                    {
                        _fileReadSequences.TryRemove(kvp.Key, out _);
                        _fileDownloadLocks.TryRemove(kvp.Key, out _);
                    }
                }
            }

            // 3. Очистка устаревших дескрипторов документов Telegram
            foreach (var docKvp in _documentCache)
            {
                if (docKvp.Value.expiresAt <= now)
                {
                    _documentCache.TryRemove(docKvp.Key, out _);
                }
            }
        }

        public bool IsAuthorized { get; private set; }
        public AuthStep CurrentStep { get; private set; } = AuthStep.NeedsPhone;
        public TelegramUserInfo? CurrentUser { get; private set; }
        public string? LastError { get; private set; }

        /// <summary>
        /// Событие успешного установления или восстановления соединения с дата-центром Telegram (Connected to DC).
        /// </summary>
        public event Action? OnConnectionRestored;

        /// <summary>
        /// Событие прогресса загрузки файла в Telegram (имя файла, передано байт, всего байт).
        /// </summary>
        public event Action<string, long, long>? OnUploadProgress;

        public void TriggerUploadProgress(string fileName, long current, long total)
        {
            OnUploadProgress?.Invoke(fileName, current, total);
        }

        /// <summary>
        /// Событие завершения загрузки файла в Telegram.
        /// </summary>
        public event Action<string>? OnUploadCompleted;

        public void TriggerUploadCompleted(string fileName)
        {
            OnUploadCompleted?.Invoke(fileName);
        }

        /// <summary>
        /// Событие отмены загрузки файла в Telegram (пользователь нажал отмену в Проводнике).
        /// </summary>
        public event Action<string>? OnUploadCanceled;

        public void TriggerUploadCanceled(string fileName)
        {
            OnUploadCanceled?.Invoke(fileName);
        }

        /// <summary>
        /// Событие прогресса скачивания файла из Telegram (имя файла, скачано байт, всего байт).
        /// </summary>
        public event Action<string, long, long>? OnDownloadProgress;

        /// <summary>
        /// Событие запроса метаданных / тегов файла из Telegram.
        /// </summary>
        public event Action<string, long, long>? OnMetadataProgress;

        /// <summary>
        /// Событие завершения получения метаданных файла.
        /// </summary>
        public event Action<string>? OnMetadataCompleted;

        /// <summary>
        /// Событие кэширования отдельных чанков / метаданных / тегов файла в ОЗУ.
        /// </summary>
        public event Action<string, long, long>? OnChunkCached;

        /// <summary>
        /// Событие завершения скачивания файла из Telegram.
        /// </summary>
        public event Action<string>? OnDownloadCompleted;

        /// <summary>
        /// Событие фиксации FLOOD_WAIT (осталось секунд, остановлена ли отправка, имя файла/операции, операция скачивания, контур очереди).
        /// </summary>
        public event Action<int, bool, string?, bool, FloodWaitScope>? OnFloodWaitDetected;

        private CancellationTokenSource? _reconnectWaitCts;
        private readonly object _reconnectWaitLock = new();
        private DateTime _lastSuccessfulTrafficTime = DateTime.MinValue;
        private static TelegramService? _activeInstance;
        private static string? _activeUploadFileName;

        public TelegramService(ConfigManager configManager, NodeRepository? repository = null)
        {
            _activeInstance = this;
            _configManager = configManager;
            _repository = repository;
            _currentSettings = _configManager.Load();
            AppLogger.OnWTelegramLog += HandleWTelegramLog;
            try
            {
                System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            }
            catch { }
            StartCaptionQueueWorker();
            StartDeletionQueueWorker();
            StartChannelMigrationQueueWorker();
            LoadPersistedFloodState();
        }

        private void WakeUpReconnectWait(string reason)
        {
            try
            {
                lock (_reconnectWaitLock)
                {
                    _reconnectWaitCts?.Cancel();
                }
            }
            catch { }
        }

        private void OnNetworkAvailabilityChanged(object? sender, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e)
        {
            _lastConnectionFailureTime = DateTime.MinValue;
            if (e.IsAvailable)
            {
                _isNetworkOffline = false;
                if (_client != null && IsAuthorized && (DateTime.UtcNow - _lastSuccessfulTrafficTime).TotalSeconds < 15)
                {
                    return;
                }
                WakeUpReconnectWait("появление сетевого подключения");
                TriggerReactorReconnect("восстановление сетевого интерфейса Windows");
            }
            else
            {
                _isNetworkOffline = true;
                WakeUpReconnectWait("отключение сетевого интерфейса Windows");
            }
        }

        private void OnNetworkAddressChanged(object? sender, EventArgs e)
        {
            _lastConnectionFailureTime = DateTime.MinValue;
            _isNetworkOffline = false;
            if (_client != null && IsAuthorized && (DateTime.UtcNow - _lastSuccessfulTrafficTime).TotalSeconds < 15)
            {
                return;
            }
            WakeUpReconnectWait("изменение сетевых адресов/маршрутов");
            TriggerReactorReconnect("изменение сетевых адресов/маршрутов");
        }

        private void HandleWTelegramLog(int level, string str)
        {
            if (!string.IsNullOrEmpty(str) && (str.Contains("Pong") || str.Contains("RpcResult") || str.Contains("Connected to DC") || str.Contains("Receiving Updates")))
            {
                _lastSuccessfulTrafficTime = DateTime.UtcNow;
            }

            if (!string.IsNullOrEmpty(str) && str.Contains("FLOOD_", StringComparison.Ordinal))
            {
                var match = FloodWaitLogRegex.Match(str);
                if (match.Success && int.TryParse(match.Groups[1].Value, out int waitSeconds) && waitSeconds > 0)
                {
                    int activeUploads = Volatile.Read(ref _activeUploadPipelineCount);
                    int activeUploadMsgs = Volatile.Read(ref _activeUploadMessageRpcCount);
                    int activeDownloads = Volatile.Read(ref _activeDownloadRequestCount);
                    int activeCaptions = Volatile.Read(ref _activeCaptionRequestCount);
                    int activeBackground = Volatile.Read(ref _activeBackgroundRequestCount);

                    // Для длительных FLOOD_WAIT (> 60 сек) WTelegramClient сам выбрасывает RpcException в вызывающий метод,
                    // где точный catch (420) выставит правильный контур очереди без риска ложной атрибуции.
                    if (waitSeconds > UploadHardStopThresholdSeconds)
                    {
                        if ((activeUploadMsgs > 0 || activeUploads > 0) && activeDownloads == 0 && activeCaptions == 0 && activeBackground == 0)
                        {
                            RegisterUploadFloodWait(waitSeconds, "WTelegram", _activeUploadFileName);
                        }
                        else if (activeUploads == 0 && activeUploadMsgs == 0 && activeDownloads == 0 && activeCaptions == 0 && activeBackground == 0 && IsAuthorized)
                        {
                            RecordFloodStateAndPersist(waitSeconds, isUploadStopped: false, fileName: null, isDownload: false, scope: FloodWaitScope.General);
                            NotifyFloodWaitBanner(waitSeconds, isUploadStopped: false, fileName: null, isDownload: false, scope: FloodWaitScope.General);
                        }
                    }
                    else
                    {
                        if (activeUploadMsgs > 0 && activeDownloads == 0 && activeCaptions == 0 && activeBackground == 0)
                        {
                            RegisterUploadFloodWait(waitSeconds, "WTelegram", _activeUploadFileName);
                        }
                        else if (activeDownloads > 0 && activeUploadMsgs == 0 && activeCaptions == 0 && activeBackground == 0)
                        {
                            RecordFloodStateAndPersist(waitSeconds, isUploadStopped: false, fileName: null, isDownload: true, scope: FloodWaitScope.Download);
                            NotifyFloodWaitBanner(waitSeconds, isUploadStopped: false, fileName: null, isDownload: true, scope: FloodWaitScope.Download);
                        }
                        else if (activeCaptions > 0 && activeUploadMsgs == 0 && activeDownloads == 0 && activeBackground == 0)
                        {
                            TriggerCaptionFloodWait(waitSeconds, "WTelegram (автоожидание)");
                        }
                        else if (activeBackground > 0 && activeUploadMsgs == 0 && activeDownloads == 0 && activeCaptions == 0)
                        {
                            TriggerBackgroundFloodWait(waitSeconds, "WTelegram (фоновая операция)");
                        }
                        else if (activeUploadMsgs > 0)
                        {
                            RegisterUploadFloodWait(waitSeconds, "WTelegram", _activeUploadFileName);
                        }
                        else if (activeDownloads > 0)
                        {
                            RecordFloodStateAndPersist(waitSeconds, isUploadStopped: false, fileName: null, isDownload: true, scope: FloodWaitScope.Download);
                            NotifyFloodWaitBanner(waitSeconds, isUploadStopped: false, fileName: null, isDownload: true, scope: FloodWaitScope.Download);
                        }
                        else if (activeCaptions > 0)
                        {
                            TriggerCaptionFloodWait(waitSeconds, "WTelegram (автоожидание)");
                        }
                        else if (activeBackground > 0)
                        {
                            TriggerBackgroundFloodWait(waitSeconds, "WTelegram (фоновая операция)");
                        }
                        else if (activeUploads > 0)
                        {
                            RegisterUploadFloodWait(waitSeconds, "WTelegram", _activeUploadFileName);
                        }
                        else if (IsAuthorized)
                        {
                            RecordFloodStateAndPersist(waitSeconds, isUploadStopped: false, fileName: null, isDownload: false, scope: FloodWaitScope.General);
                            NotifyFloodWaitBanner(waitSeconds, isUploadStopped: false, fileName: null, isDownload: false, scope: FloodWaitScope.General);
                        }
                    }
                }
            }

            if (level == 2 && !string.IsNullOrEmpty(str) && str.Contains("Connected to DC", StringComparison.OrdinalIgnoreCase))
            {
                if (IsAuthorized)
                {
                    OnConnectionRestored?.Invoke();
                }
            }
        }

        /// <summary>
        /// Быстрая проверка живости сокета или провоцирование реактора WTelegram на переподключение после сна.
        /// </summary>
        public async Task PingOrReconnectAsync()
        {
            ClearPersistedFloodStateIfExpired();
            WakeFloodWaitDelaysOnResume();

            if (_client == null || !IsAuthorized)
            {
                AppLogger.Info("TelegramService", "Клиент Telegram не подключен/не авторизован при пробуждении. Запуск восстановления...");
                TriggerReactorReconnect("пробуждение системы при неактивном клиенте");
                return;
            }

            try
            {
                var pingTask = _client.Help_GetNearestDc();
                if (await Task.WhenAny(pingTask, Task.Delay(5000)) == pingTask)
                {
                    await pingTask;
                    _lastSuccessfulTrafficTime = DateTime.UtcNow;
                    AppLogger.Info("TelegramService", "Проверка связи с Telegram после сна успешна: сокет активен.");
                }
                else
                {
                    AppLogger.Warn("TelegramService", "Таймаут (5 сек) проверки связи с Telegram после сна. Запуск процедуры самовосстановления...");
                    TriggerReactorReconnect("таймаут сокета при проверке после сна");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TelegramService", $"Проверка соединения Telegram после сна выявила сбой сокета: {ex.Message}. Запуск восстановления...");
                TriggerReactorReconnect($"сбой сокета при проверке после сна: {ex.Message}");
            }
        }

        public void SetRepository(NodeRepository repository)
        {
            _repository = repository;
            StartCaptionQueueWorker();
            StartDeletionQueueWorker();
            StartChannelMigrationQueueWorker();
        }

        private System.Threading.CancellationTokenSource? _deletionQueueCts;
        private readonly SemaphoreSlim _deletionSignal = new SemaphoreSlim(0, int.MaxValue);
        private long _lastDeletionSignalTicks = 0;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, int> _channelsPendingDeletion = new();

        private System.Threading.CancellationTokenSource? _migrationQueueCts;
        private readonly SemaphoreSlim _migrationSignal = new SemaphoreSlim(0, int.MaxValue);
        private readonly SemaphoreSlim _captionSignal = new SemaphoreSlim(0, int.MaxValue);

        /// <summary>
        /// Помечает канал как ожидающий полного уничтожения, чтобы воркер очереди удаления не удалял его сообщения поштучно.
        /// </summary>
        public void MarkChannelPendingDeletion(long channelId)
        {
            if (channelId == 0) return;
            _channelsPendingDeletion.AddOrUpdate(channelId, 1, (_, count) => count + 1);
        }

        private bool IsChannelPendingDeletion(long channelId)
        {
            return channelId != 0 && _channelsPendingDeletion.ContainsKey(channelId);
        }

        private void ClearChannelPendingDeletion(long channelId)
        {
            if (channelId == 0) return;
            _channelsPendingDeletion.AddOrUpdate(channelId, 0, (_, count) => Math.Max(0, count - 1));
            if (_channelsPendingDeletion.TryGetValue(channelId, out int remaining) && remaining <= 0)
            {
                _channelsPendingDeletion.TryRemove(new KeyValuePair<long, int>(channelId, 0));
            }
        }

        /// <summary>
        /// Сигнализирует фоновому воркеру об отправке новых сообщений в очередь перманентного удаления
        /// </summary>
        public void TriggerDeletionQueueProcessing()
        {
            try
            {
                Volatile.Write(ref _lastDeletionSignalTicks, DateTime.UtcNow.Ticks);
                if (_deletionSignal.CurrentCount == 0)
                {
                    _deletionSignal.Release();
                }
            }
            catch { }
        }

        /// <summary>
        /// Сигнализирует фоновому воркеру о новых элементах в очереди миграции каналов
        /// </summary>
        public void TriggerChannelMigrationProcessing()
        {
            try
            {
                if (_migrationSignal.CurrentCount == 0)
                {
                    _migrationSignal.Release();
                }
            }
            catch { }
        }

        /// <summary>
        /// Сигнализирует воркеру подписей о новых элементах в очереди pending_caption_updates.
        /// </summary>
        public void TriggerCaptionQueueProcessing()
        {
            try
            {
                if (_captionSignal.CurrentCount == 0)
                {
                    _captionSignal.Release();
                }
            }
            catch { }
        }

        /// <summary>
        /// Помещает отложенные обновления подписей (добавление ID сообщения после загрузки) в персистентную очередь SQLite.
        /// </summary>
        public void EnqueuePendingCaptions(int nodeId, FileUploadResult? uploadResult)
        {
            if (_repository == null || nodeId <= 0 || uploadResult?.PendingCaptions == null || uploadResult.PendingCaptions.Count == 0)
            {
                return;
            }

            bool enqueued = false;
            foreach (var item in uploadResult.PendingCaptions)
            {
                if (item.MessageId > 1 && !string.IsNullOrEmpty(item.Caption))
                {
                    _repository.EnqueueCaptionUpdate(nodeId, item.MessageId, item.Caption, item.ChannelId);
                    enqueued = true;
                }
            }

            if (enqueued)
            {
                TriggerCaptionQueueProcessing();
            }
        }

        private void StartDeletionQueueWorker()
        {
            if (_deletionQueueCts != null) return;
            _deletionQueueCts = new System.Threading.CancellationTokenSource();
            Task.Run(() => ProcessDeletionQueueAsync(_deletionQueueCts.Token));
            TriggerDeletionQueueProcessing();
        }

        private void StartChannelMigrationQueueWorker()
        {
            if (_migrationQueueCts != null) return;
            _migrationQueueCts = new System.Threading.CancellationTokenSource();
            Task.Run(() => ProcessChannelMigrationsAsync(_migrationQueueCts.Token));
            TriggerChannelMigrationProcessing();
        }

        private async Task ProcessDeletionQueueAsync(System.Threading.CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // 1. Ожидаем сигнала на удаление. Когда очередь пуста — поток спит без таймаутов и холостых опросов SQLite!
                try
                {
                    await _deletionSignal.WaitAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // Скользящий дебаунс 2 сек (потолок 10 сек): даём Проводнику завершить пакетное удаление файлов
                // до старта поштучных RPC-удалений, чтобы при сносе всей папки в .Trash сразу удалить канал целиком
                DateTime debounceStartUtc = DateTime.UtcNow;
                while (!token.IsCancellationRequested)
                {
                    long lastTicks = Volatile.Read(ref _lastDeletionSignalTicks);
                    DateTime lastSignalUtc = lastTicks > 0 ? new DateTime(lastTicks, DateTimeKind.Utc) : debounceStartUtc;
                    double msSinceLastSignal = (DateTime.UtcNow - lastSignalUtc).TotalMilliseconds;
                    double totalWaitedMs = (DateTime.UtcNow - debounceStartUtc).TotalMilliseconds;
                    if (msSinceLastSignal >= 2000 || totalWaitedMs >= 10000)
                    {
                        break;
                    }

                    int sleepMs = (int)Math.Clamp(2000 - msSinceLastSignal, 100, 500);
                    try
                    {
                        await Task.Delay(sleepMs, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                // 2. Обрабатываем очередь до тех пор, пока она не опустеет
                while (!token.IsCancellationRequested)
                {
                    if (!IsAuthorized || _client == null || _repository == null)
                    {
                        // Не авторизованы — прерываем цикл, обработка возобновится при успешном входе
                        break;
                    }

                    List<(int MessageId, long ChannelId)> batch;
                    try
                    {
                        batch = _repository.GetPendingDeletions(limit: 100);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Debug("TelegramService", $"[DeletionQueue] Ошибка чтения очереди: {ex.Message}");
                        await Task.Delay(3000, token);
                        break;
                    }

                    // Если очередь пуста — выходим из внутреннего цикла и спим глубоким сном до следующего сигнала
                    if (batch.Count == 0)
                    {
                        break;
                    }

                    AppLogger.Info("TelegramService", $"[DeletionQueue] Фоновое перманентное удаление {batch.Count} сообщений из Telegram...");

                    var channelGroups = batch.GroupBy(x => x.ChannelId).ToDictionary(g => g.Key, g => g.Select(x => x.MessageId).ToList());
                    var completedMsgIds = new List<int>();
                    bool hasRetryableFailure = false;
                    bool hasPendingChannelSkip = false;

                    bool hitFloodWait = false;
                    foreach (var kvp in channelGroups)
                    {
                        if (kvp.Key != 0L && IsChannelPendingDeletion(kvp.Key))
                        {
                            hasPendingChannelSkip = true;
                            AppLogger.Debug("TelegramService", $"[DeletionQueue] Канал ID {kvp.Key} уже ожидает полного уничтожения. Пропуск поштучного удаления {kvp.Value.Count} сообщений.");
                            continue;
                        }

                        try
                        {
                            await EnsureBackgroundFloodWaitDelayAsync(token);
                            var peer = await GetStoragePeerAsync(kvp.Key != 0L ? kvp.Key : (long?)null);
                            bool isChannel = peer is TL.InputPeerChannel;
                            const int batchSize = 100;

                            for (int i = 0; i < kvp.Value.Count; i += batchSize)
                            {
                                var count = Math.Min(batchSize, kvp.Value.Count - i);
                                var subBatch = kvp.Value.GetRange(i, count).ToArray();
                                await DeleteBatchWithBisectAsync(peer, isChannel, subBatch);
                            }

                            completedMsgIds.AddRange(kvp.Value);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (TL.RpcException rpcEx) when (rpcEx.Code == 420)
                        {
                            AppLogger.Warn("TelegramService", $"[DeletionQueue] FloodWait при удалении из канала ID {kvp.Key}: пауза {rpcEx.X} сек...");
                            hitFloodWait = true;
                            hasRetryableFailure = true;
                            break;
                        }
                        catch (Exception ex) when (kvp.Key != 0L && (ex.Message.Contains("не найден") || ex.Message.Contains("CHANNEL_INVALID") || ex.Message.Contains("CHANNEL_PRIVATE")))
                        {
                            AppLogger.Info("TelegramService", $"[DeletionQueue] Канал Telegram ID {kvp.Key} уже удален или недоступен. {kvp.Value.Count} сообщений считаются очищенными.");
                            completedMsgIds.AddRange(kvp.Value);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("TelegramService", $"[DeletionQueue] Ошибка удаления из канала ID {kvp.Key}: {ex.Message}", ex);
                            hasRetryableFailure = true;
                        }
                    }

                    if (completedMsgIds.Count > 0)
                    {
                        try
                        {
                            _repository.RemovePendingDeletions(completedMsgIds);
                            AppLogger.Info("TelegramService", $"[DeletionQueue] Очищено {completedMsgIds.Count} сообщений из очереди.");
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Debug("TelegramService", $"[DeletionQueue] Ошибка удаления из базы: {ex.Message}");
                        }
                    }

                    if (hitFloodWait)
                    {
                        await EnsureBackgroundFloodWaitDelayAsync(token);
                        await Task.Delay(1000, token);
                    }
                    else if (hasRetryableFailure)
                    {
                        // При неудаче в сети/биссекции повторяем попытку через 5 секунд только пока в очереди есть записи
                        AppLogger.Warn("TelegramService", $"[DeletionQueue] Не удалось завершить пакетное удаление сообщений из Telegram. Повтор через 5 секунд...");
                        await Task.Delay(5000, token);
                    }
                    else if (hasPendingChannelSkip && completedMsgIds.Count == 0)
                    {
                        // Все записи в текущей пачке относятся к каналам, ждущим очереди _channelDeleteSemaphore
                        await Task.Delay(500, token);
                    }
                }
            }
        }

        private async Task ProcessChannelMigrationsAsync(System.Threading.CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _migrationSignal.WaitAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                while (!token.IsCancellationRequested)
                {
                    if (!IsAuthorized || _client == null || _repository == null)
                    {
                        break;
                    }

                    List<ChannelMigrationItem> batch;
                    try
                    {
                        batch = _repository.GetPendingChannelMigrations(limit: 10);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Debug("TelegramService", $"[MigrationQueue] Ошибка чтения очереди миграции: {ex.Message}");
                        await Task.Delay(3000, token);
                        break;
                    }

                    if (batch.Count == 0)
                    {
                        break;
                    }

                    AppLogger.Info("TelegramService", $"[MigrationQueue] Фоновая обработка пачки из {batch.Count} файлов миграции каналов...");

                    foreach (var item in batch)
                    {
                        if (token.IsCancellationRequested) break;

                        await EnsureBackgroundFloodWaitDelayAsync(token);
                        await EnsureBackgroundPacingDelayAsync(token);

                        var file = _repository.GetNodeById(item.NodeId);
                        if (file == null || file.InTrash || !file.TgMessageId.HasValue || file.TgMessageId.Value <= 0)
                        {
                            _repository.DeleteChannelMigration(item.Id);
                            continue;
                        }

                        TL.InputPeer? targetPeer = null;
                        TL.InputPeer? sourcePeer = null;

                        try
                        {
                            targetPeer = await GetStoragePeerAsync(item.TargetChannelId);
                            sourcePeer = await GetStoragePeerAsync(item.SourceChannelId);
                        }
                        catch (Exception peerEx)
                        {
                            AppLogger.Warn("TelegramService", $"[MigrationQueue] Ошибка получения дескрипторов каналов для элемента #{item.Id}: {peerEx.Message}. Повтор через 5 секунд...");
                            await Task.Delay(5000, token);
                            continue;
                        }

                        if (targetPeer == null || sourcePeer == null)
                        {
                            AppLogger.Warn("TelegramService", $"[MigrationQueue] Канал назначения ID {item.TargetChannelId} или источника ID {item.SourceChannelId} недоступен. Пропуск элемента #{item.Id}.");
                            _repository.DeleteChannelMigration(item.Id);
                            continue;
                        }

                        try
                        {
                            int oldMessageId = file.TgMessageId.Value;
                            int? oldPreviewId = file.TgPreviewMessageId;
                            bool targetPreviewEnabled = _repository.GetEffectivePhotoPreviewEnabled(file.ParentId);
                            bool targetCaptionsEnabled = _repository.GetEffectiveCaptionsEnabled(file.ParentId);

                            int newMsgId = 0;
                            int? newPreviewId = null;

                            // 1. Сначала пересылаем превью для галереи (если есть и в целевой папке превью включены), чтобы оно появилось в ленте первым
                            if (targetPreviewEnabled && oldPreviewId.HasValue && oldPreviewId.Value > 0)
                            {
                                var fwdPreviewReq = new TL.Methods.Messages_ForwardMessages
                                {
                                    from_peer = sourcePeer,
                                    to_peer = targetPeer,
                                    id = new int[] { oldPreviewId.Value },
                                    random_id = new long[] { Random.Shared.NextInt64() },
                                    flags = TL.Methods.Messages_ForwardMessages.Flags.drop_author
                                };

                                BeginBackgroundRpc();
                                TL.UpdatesBase previewUpdates;
                                try
                                {
                                    previewUpdates = await _client.Invoke(fwdPreviewReq);
                                }
                                finally
                                {
                                    EndBackgroundRpc();
                                }
                                newPreviewId = ExtractMessageIdFromUpdates(previewUpdates);
                                if (newPreviewId <= 0) newPreviewId = null;
                                await EnsureBackgroundPacingDelayAsync(token);
                            }

                            // 2. Затем пересылаем основной документ (файл) в целевой канал (drop_author = true)
                            var fwdReq = new TL.Methods.Messages_ForwardMessages
                            {
                                from_peer = sourcePeer,
                                to_peer = targetPeer,
                                id = new int[] { oldMessageId },
                                random_id = new long[] { Random.Shared.NextInt64() },
                                flags = TL.Methods.Messages_ForwardMessages.Flags.drop_author
                            };

                            BeginBackgroundRpc();
                            TL.UpdatesBase fwdUpdates;
                            try
                            {
                                fwdUpdates = await _client.Invoke(fwdReq);
                            }
                            finally
                            {
                                EndBackgroundRpc();
                            }
                            newMsgId = ExtractMessageIdFromUpdates(fwdUpdates);

                            if (newMsgId > 0)
                            {
                                // 3. Очищаем старые сообщения из исходного канала
                                var oldIdsToDelete = new List<int> { oldMessageId };
                                if (oldPreviewId.HasValue && oldPreviewId.Value > 0)
                                {
                                    oldIdsToDelete.Add(oldPreviewId.Value);
                                }

                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await DeleteFilesFromTelegramAsync(oldIdsToDelete, item.SourceChannelId);
                                    }
                                    catch (Exception delEx)
                                    {
                                        AppLogger.Warn("TelegramService", $"[MigrationQueue] Фоновая очистка старых сообщений из канала ID {item.SourceChannelId}: {delEx.Message}");
                                    }
                                });

                                // 4. Обновляем данные файла в БД
                                _repository.UpdateNodeTelegramData(file.Id, newMsgId, newPreviewId, item.TargetChannelId);

                                // 5. Обновляем текстовые подписи (только если они включены для целевой папки)
                                if (targetCaptionsEnabled)
                                {
                                    string fullPathWithVersion = _repository.GetNodeFullPathWithVersion(file.Id);
                                    string newCaption = NodeRepository.FormatTelegramCaption(fullPathWithVersion, newMsgId, isLatest: !file.InTrash);
                                    _repository.EnqueueCaptionUpdate(file.Id, newMsgId, newCaption, item.TargetChannelId);
                                    if (newPreviewId.HasValue && newPreviewId.Value > 0)
                                    {
                                        string previewCaption = NodeRepository.FormatTelegramCaption(fullPathWithVersion, newPreviewId.Value, isLatest: !file.InTrash);
                                        _repository.EnqueueCaptionUpdate(file.Id, newPreviewId.Value, previewCaption, item.TargetChannelId);
                                    }
                                }

                                _repository.DeleteChannelMigration(item.Id);
                                AppLogger.Info("TelegramService", $"[MigrationQueue] Файл '{file.Name}' успешно перенесен: старый msg #{oldMessageId} -> новый msg #{newMsgId} в канале ID {item.TargetChannelId}.");
                            }
                            else
                            {
                                AppLogger.Warn("TelegramService", $"[MigrationQueue] Не удалось получить новый Message ID для файла '{file.Name}' при пересылке в канал ID {item.TargetChannelId}. Повтор через 5 секунд...");
                                await Task.Delay(5000, token);
                            }
                        }
                        catch (TL.RpcException rpcEx) when (rpcEx.Code == 420) // FLOOD_WAIT_X
                        {
                            AppLogger.Warn("TelegramService", $"[MigrationQueue] FloodWait при пересылке файла '{file.Name}': пауза {rpcEx.X} сек...");
                            TriggerBackgroundFloodWait(rpcEx.X, $"миграция '{file.Name}'");
                            await Task.Delay(Math.Max(5000, rpcEx.X * 1000), token);
                            break;
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error("TelegramService", $"[MigrationQueue] Ошибка пересылки файла '{file.Name}': {ex.Message}", ex);
                            _repository.DeleteChannelMigration(item.Id);
                        }
                    }

                    TriggerDeletionQueueProcessing();
                }
            }
        }

        private void StartCaptionQueueWorker()
        {
            if (_queueCts != null) return;
            _queueCts = new System.Threading.CancellationTokenSource();
            Task.Run(() => ProcessCaptionQueueAsync(_queueCts.Token));
        }

        private async Task ProcessCaptionQueueAsync(System.Threading.CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                PendingCaptionItem? currentItem = null;
                try
                {
                    if (IsAuthorized && _client != null && _repository != null)
                    {
                        currentItem = _repository.GetNextPendingCaptionUpdate();
                        if (currentItem != null)
                        {
                            await EnsureCaptionFloodWaitDelayAsync(token);
                            await EnsureCaptionPacingDelayAsync(token);
                            long? targetChannelId = currentItem.ChannelId ?? _repository.GetChannelIdByTgMessageId(currentItem.TgMessageId);
                            await UpdateMessageCaptionAsync(currentItem.TgMessageId, currentItem.NewCaption, targetChannelId);
                            _repository.RemovePendingCaptionUpdate(currentItem.Id);
                            currentItem = null;
                            continue;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (TL.RpcException rpcEx) when (rpcEx.Code == 420) // FLOOD_WAIT_X
                {
                    if (rpcEx.X <= 0)
                    {
                        AppLogger.Warn("TelegramService", "FloodWait при фоновом обновлении подписей (без указания секунд): пауза 5 сек...");
                        await Task.Delay(5000, token);
                    }
                    continue;
                }
                catch (TL.RpcException rpcEx) when (rpcEx.Code == 400)
                {
                    AppLogger.Warn("TelegramService", $"Неустранимая ошибка Telegram при обновлении подписи: {rpcEx.Message}. Удаление элемента из очереди.");
                    if (currentItem != null && _repository != null)
                    {
                        _repository.RemovePendingCaptionUpdate(currentItem.Id);
                    }
                    await Task.Delay(500, token);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("TelegramService", $"Ошибка обработки фоновой очереди подписей: {ex.Message}");
                    await Task.Delay(2000, token);
                }

                try
                {
                    await _captionSignal.WaitAsync(1000, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private TL.InputPeer? _storagePeer;
        private readonly SemaphoreSlim _storageLock = new SemaphoreSlim(1, 1);
        private string? _pendingChannelTitle;

        public void UpdateApiCredentials(int apiId, string apiHash, string? channelTitle = null)
        {
            bool credsChanged = true;
            if (_repository != null)
            {
                var currentAccount = _repository.GetActiveTelegramAccount();
                if (currentAccount != null && currentAccount.ApiId == apiId && currentAccount.ApiHash == apiHash)
                {
                    credsChanged = false;
                }

                var account = _repository.SaveTelegramAccount(apiId, apiHash);
                if (!string.IsNullOrWhiteSpace(channelTitle))
                {
                    string cleanTitle = channelTitle.Trim();
                    var primary = _repository.GetPrimaryTelegramChannel(account.Id);
                    if (primary != null)
                    {
                        if (!string.Equals(primary.Title, cleanTitle, StringComparison.Ordinal))
                        {
                            _repository.UpdateChannelTitle(primary.ChannelId, cleanTitle);
                            if (IsAuthorized && primary.ChannelId != 0 && !credsChanged)
                            {
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await EditChannelTitleAsync(primary.ChannelId, cleanTitle);
                                    }
                                    catch (Exception ex)
                                    {
                                        AppLogger.Warn("TelegramService", $"Не удалось синхронизировать заголовок основного канала ID {primary.ChannelId}: {ex.Message}");
                                    }
                                });
                            }
                            else
                            {
                                _pendingChannelTitle = cleanTitle;
                            }
                        }
                    }
                    else
                    {
                        _pendingChannelTitle = cleanTitle;
                    }
                }
            }

            if (credsChanged)
            {
                _storagePeer = null;
                try { _client?.Dispose(); } catch { }
                _client = null;
                _ = ConnectAsync();
            }
        }

        public void UpdateStorageChannelTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title) || _repository == null) return;
            var account = _repository.GetActiveTelegramAccount();
            if (account == null) return;

            string cleanTitle = title.Trim();
            var primary = _repository.GetPrimaryTelegramChannel(account.Id);
            if (primary != null && !string.Equals(primary.Title, cleanTitle, StringComparison.Ordinal))
            {
                _repository.UpdateChannelTitle(primary.ChannelId, cleanTitle);
                _storagePeer = null;
                if (IsAuthorized && primary.ChannelId != 0)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await EditChannelTitleAsync(primary.ChannelId, cleanTitle);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("TelegramService", $"Не удалось синхронизировать заголовок основного канала ID {primary.ChannelId}: {ex.Message}");
                        }
                    });
                }
                else
                {
                    _pendingChannelTitle = cleanTitle;
                }
            }
            else if (primary == null)
            {
                _pendingChannelTitle = cleanTitle;
            }
        }

        /// <summary>
        /// Сбрасывает закэшированный канал-хранилище (например, при удалении канала пользователем).
        /// </summary>
        public void InvalidateStoragePeer()
        {
            _storagePeer = null;
        }

        /// <summary>
        /// Получает или создает приватный канал-хранилище в Telegram для WebDAV файлов.
        /// Поддерживает явный выбор канала по specificChannelId.
        /// </summary>
        public async Task<TL.InputPeer> GetStoragePeerAsync(long? specificChannelId = null)
        {
            // Если передан конкретный ID канала (например, папка привязана к отдельной группе):
            if (specificChannelId.HasValue && specificChannelId.Value != 0)
            {
                var channelInfo = _repository?.GetTelegramChannel(specificChannelId.Value);
                if (channelInfo != null && channelInfo.AccessHash != 0)
                {
                    return new TL.InputPeerChannel(channelInfo.ChannelId, channelInfo.AccessHash);
                }

                if (_client != null && IsAuthorized)
                {
                    var chats = await _client.Messages_GetAllChats();
                    if (chats.chats.TryGetValue(specificChannelId.Value, out var ch) && ch is TL.Channel channel)
                    {
                        var account = _repository?.GetActiveTelegramAccount();
                        if (account != null && _repository != null)
                        {
                            _repository.SaveOrUpdateTelegramChannel(account.Id, channel.ID, channel.access_hash, channel.Title, isPrimary: false);
                        }
                        return channel.ToInputPeer();
                    }

                    throw new InvalidOperationException($"Целевой канал Telegram ID {specificChannelId.Value} не найден или недоступен.");
                }
            }

            if (_storagePeer != null)
                return _storagePeer;

            await _storageLock.WaitAsync();
            try
            {
                if (_storagePeer != null)
                    return _storagePeer;

                if (_client == null || !IsAuthorized)
                    throw new InvalidOperationException("Клиент Telegram не авторизован.");

                var account = _repository?.GetActiveTelegramAccount();
                if (account == null)
                    throw new InvalidOperationException("Аккаунт Telegram не найден в базе данных.");

                var primaryChannel = _repository?.GetPrimaryTelegramChannel(account.Id);
                string targetTitle = primaryChannel != null && !string.IsNullOrWhiteSpace(primaryChannel.Title)
                    ? primaryChannel.Title.Trim()
                    : (!string.IsNullOrWhiteSpace(_pendingChannelTitle) ? _pendingChannelTitle.Trim() : "TGramFS Storage");

                // 1. Если StorageChannelId и StorageChannelAccessHash уже сохранены в базе SQLite — мгновенно используем их без сетевых вызовов!
                if (primaryChannel != null && primaryChannel.ChannelId != 0 && primaryChannel.AccessHash != 0)
                {
                    if (!string.IsNullOrWhiteSpace(_pendingChannelTitle) &&
                        !string.Equals(primaryChannel.Title, _pendingChannelTitle, StringComparison.Ordinal))
                    {
                        string desiredTitle = _pendingChannelTitle;
                        _pendingChannelTitle = null;
                        try
                        {
                            await EditChannelTitleAsync(primaryChannel.ChannelId, desiredTitle);
                            primaryChannel.Title = desiredTitle;
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("TelegramService", $"Не удалось синхронизировать заголовок основного канала ID {primaryChannel.ChannelId}: {ex.Message}");
                        }
                    }

                    _storagePeer = new TL.InputPeerChannel(primaryChannel.ChannelId, primaryChannel.AccessHash);
                    AppLogger.Info("TelegramService", $"Подключен канал-хранилище из базы SQLite: '{primaryChannel.Title}' (ID: {primaryChannel.ChannelId}).");
                    return _storagePeer;
                }

                // 2. Если StorageChannelId есть, но хэш еще не сохранен — находим канал в диалогах
                if (primaryChannel != null && primaryChannel.ChannelId != 0)
                {
                    var chats = await _client.Messages_GetAllChats();
                    if (chats.chats.TryGetValue(primaryChannel.ChannelId, out var savedChat) &&
                        savedChat is TL.Channel sc)
                    {
                        _storagePeer = sc.ToInputPeer();
                        _repository?.SaveOrUpdateTelegramChannel(account.Id, sc.ID, sc.access_hash, sc.Title, isPrimary: true);
                        AppLogger.Info("TelegramService", $"Подключен существующий приватный канал-хранилище: {sc.Title} (ID: {sc.ID}, AccessHash сохранен в SQLite).");
                        return _storagePeer;
                    }
                    else
                    {
                        AppLogger.Warn("TelegramService", $"Канал с сохраненным ID {primaryChannel.ChannelId} не найден в диалогах пользователя. Создаем новый.");
                    }
                }

                // 3. Если ID канала нет в БД, создаем новый приватный канал
                AppLogger.Info("TelegramService", $"Основной канал-хранилище не найден. Создание нового приватного канала '{targetTitle}'...");
                var createReq = new TL.Methods.Channels_CreateChannel
                {
                    flags = TL.Methods.Channels_CreateChannel.Flags.broadcast,
                    title = targetTitle,
                    about = "Приватное облачное хранилище файлов для TGramFS Drive"
                };

                var createdUpdates = await _client.Invoke(createReq);

                if (createdUpdates is TL.Updates updates)
                {
                    foreach (var chat in updates.chats.Values)
                    {
                        if (chat is TL.Channel newCh)
                        {
                            _storagePeer = newCh.ToInputPeer();
                            _pendingChannelTitle = null;
                            _repository?.SaveOrUpdateTelegramChannel(account.Id, newCh.ID, newCh.access_hash, newCh.Title, isPrimary: true);
                            AppLogger.Info("TelegramService", $"Создан новый приватный канал '{newCh.Title}' (ID: {newCh.ID}). Записан в SQLite.");
                            await TrySetDefaultChannelPhotoAsync(newCh);
                            return _storagePeer;
                        }
                    }
                }

                throw new InvalidOperationException($"Не удалось создать приватный канал '{targetTitle}' в Telegram для хранения файлов.");
            }
            finally
            {
                _storageLock.Release();
            }
        }

        /// <summary>
        /// Создает новый приватный канал Telegram с названием папки и сохраняет его в SQLite.
        /// </summary>
        public async Task<TelegramChannel?> CreateCustomChannelAsync(string title)
        {
            await EnsureBackgroundFloodWaitDelayAsync();
            if (_client == null || !IsAuthorized)
                throw new InvalidOperationException("Клиент Telegram не подключен или не авторизован.");

            var account = _repository?.GetActiveTelegramAccount();
            if (account == null)
                throw new InvalidOperationException("Аккаунт Telegram не найден в базе данных.");

            string cleanTitle = title.Trim();
            AppLogger.Info("TelegramService", $"Создание нового приватного канала '{cleanTitle}'...");

            var createReq = new TL.Methods.Channels_CreateChannel
            {
                flags = TL.Methods.Channels_CreateChannel.Flags.broadcast,
                title = cleanTitle,
                about = "Приватный канал хранилища папки " + cleanTitle
            };

            BeginBackgroundRpc();
            TL.UpdatesBase createdUpdates;
            try
            {
                createdUpdates = await _client.Invoke(createReq);
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 420)
            {
                TriggerBackgroundFloodWait(rpcEx.X, $"создание канала '{cleanTitle}'");
                throw;
            }
            finally
            {
                EndBackgroundRpc();
            }

            if (createdUpdates is TL.Updates updates)
            {
                foreach (var chat in updates.chats.Values)
                {
                    if (chat is TL.Channel newCh)
                    {
                        _repository?.SaveOrUpdateTelegramChannel(account.Id, newCh.ID, newCh.access_hash, newCh.Title, isPrimary: false);
                        AppLogger.Info("TelegramService", $"Создан новый кастомный канал '{newCh.Title}' (ID: {newCh.ID}). Записан в SQLite.");
                        await TrySetDefaultChannelPhotoAsync(newCh);
                        return _repository?.GetTelegramChannel(newCh.ID);
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Обновляет название канала в Telegram при переименовании привязанной папки.
        /// </summary>
        public async Task EditChannelTitleAsync(long channelId, string newTitle)
        {
            await EnsureBackgroundFloodWaitDelayAsync();
            if (_client == null || !IsAuthorized || channelId == 0) return;

            string cleanTitle = newTitle.Trim();
            try
            {
                var peer = await GetStoragePeerAsync(channelId);
                if (peer is TL.InputPeerChannel pc)
                {
                    var req = new TL.Methods.Channels_EditTitle
                    {
                        channel = new TL.InputChannel(pc.channel_id, pc.access_hash),
                        title = cleanTitle
                    };
                    BeginBackgroundRpc();
                    try
                    {
                        await _client.Invoke(req);
                    }
                    finally
                    {
                        EndBackgroundRpc();
                    }
                    _repository?.UpdateChannelTitle(channelId, cleanTitle);
                    AppLogger.Info("TelegramService", $"Название канала Telegram (ID: {channelId}) сменено на '{cleanTitle}'.");
                }
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 420)
            {
                TriggerBackgroundFloodWait(rpcEx.X, $"переименование канала '{cleanTitle}'");
                AppLogger.Warn("TelegramService", $"Не удалось изменить название канала ID {channelId} из-за FLOOD_WAIT ({rpcEx.X} сек).");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TelegramService", $"Не удалось изменить название канала ID {channelId} в Telegram: {ex.Message}");
            }
        }

        private void EvictChannelCaches(long channelId)
        {
            if (channelId == 0) return;

            _workerPool?.CancelPendingChunksForChannel(channelId);

            foreach (var key in _documentCache.Keys)
            {
                if (key.channelId == channelId)
                {
                    _documentCache.TryRemove(key, out _);
                }
            }

            string chunkPrefix = $"{channelId}:";
            foreach (var key in _chunkMemoryCache.Keys)
            {
                if (key.StartsWith(chunkPrefix, StringComparison.Ordinal))
                {
                    _chunkMemoryCache.TryRemove(key, out _);
                }
            }

            foreach (var key in _networkAudits.Keys)
            {
                if (key.channelId == channelId)
                {
                    _networkAudits.TryRemove(key, out _);
                }
            }

            foreach (var key in _fileReadSequences.Keys)
            {
                if (key.channelId == channelId)
                {
                    _fileReadSequences.TryRemove(key, out _);
                }
            }

            foreach (var key in _fileDownloadLocks.Keys)
            {
                if (key.channelId == channelId)
                {
                    _fileDownloadLocks.TryRemove(key, out _);
                }
            }

            try
            {
                string cacheDir = Path.Combine(Path.GetTempPath(), "TGramFS_ReadCache");
                if (Directory.Exists(cacheDir))
                {
                    foreach (var file in Directory.EnumerateFiles(cacheDir, $"{channelId}_*.bin"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Перманентно удаляет канал из Telegram при полном уничтожении привязанной папки.
        /// </summary>
        public async Task DeleteChannelAsync(long channelId)
        {
            if (_client == null || !IsAuthorized || channelId == 0)
            {
                ClearChannelPendingDeletion(channelId);
                return;
            }

            await _channelDeleteSemaphore.WaitAsync();
            try
            {
                await EnsureBackgroundFloodWaitDelayAsync();
                if (_client == null || !IsAuthorized) return;

                var peer = await GetStoragePeerAsync(channelId);
                if (peer is TL.InputPeerChannel pc)
                {
                    var req = new TL.Methods.Channels_DeleteChannel
                    {
                        channel = new TL.InputChannel(pc.channel_id, pc.access_hash)
                    };
                    BeginBackgroundRpc();
                    try
                    {
                        await _client.Invoke(req);
                    }
                    finally
                    {
                        EndBackgroundRpc();
                    }
                    _repository?.DeleteTelegramChannel(channelId);
                    int purged = _repository?.RemovePendingDeletionsForChannel(channelId) ?? 0;
                    EvictChannelCaches(channelId);
                    AppLogger.Info("TelegramService", purged > 0
                        ? $"Канал Telegram ID {channelId} полностью удален (очищено {purged} ожидающих сообщений из очереди)."
                        : $"Канал Telegram ID {channelId} полностью удален.");
                    await Task.Delay(350);
                }
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 420)
            {
                TriggerBackgroundFloodWait(rpcEx.X, $"удаление канала ID {channelId}");
                AppLogger.Warn("TelegramService", $"Не удалось удалить канал ID {channelId} из-за FLOOD_WAIT ({rpcEx.X} сек).");
            }
            catch (Exception ex) when (ex.Message.Contains("CHANNEL_INVALID") || ex.Message.Contains("CHANNEL_PRIVATE") || ex.Message.Contains("не найден"))
            {
                _repository?.DeleteTelegramChannel(channelId);
                int purged = _repository?.RemovePendingDeletionsForChannel(channelId) ?? 0;
                EvictChannelCaches(channelId);
                AppLogger.Info("TelegramService", $"Канал Telegram ID {channelId} уже недоступен/удален (очищено {purged} записей очереди).");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TelegramService", $"Не удалось удалить канал ID {channelId} из Telegram: {ex.Message}");
            }
            finally
            {
                ClearChannelPendingDeletion(channelId);
                _channelDeleteSemaphore.Release();
            }
        }

        /// <summary>
        /// Мигрирует всё содержимое папки (и её подпапок) в новый канал Telegram через механизмы ForwardMessages и очистки старого канала.
        /// </summary>
        public Task MigrateSubtreeToChannelAsync(int folderNodeId, long targetChannelId, Action<string, int, int>? onProgress = null)
        {
            if (_repository == null) return Task.CompletedTask;

            // Фиксируем исходный эффективный канал до смены привязки
            long oldEffectiveChannelId = _repository.GetEffectiveChannelId(folderNodeId) ?? _repository.GetPrimaryTelegramChannel()?.ChannelId ?? 0;

            // Назначаем tg_channel_id строго для привязываемой папки.
            // Дочерние папки динамически наследуют его через GetEffectiveChannelId, а папки с собственным каналом остаются нетронутыми.
            _repository.SetFolderChannelId(folderNodeId, targetChannelId);

            // Собираем файлы для миграции, пропуская вложенные папки, у которых есть собственный уникальный tg_channel_id
            var filesToMigrate = _repository.GetFilesForChannelMigration(folderNodeId)
                .OrderBy(n => n.TgMessageId!.Value)
                .ToList();

            if (filesToMigrate.Count == 0)
            {
                AppLogger.Info("TelegramService", $"[Migration] В папке ID {folderNodeId} нет файлов для миграции.");
                return Task.CompletedTask;
            }

            AppLogger.Info("TelegramService", $"[Migration] Добавление {filesToMigrate.Count} файлов папки ID {folderNodeId} в очередь фоновой миграции в канал ID {targetChannelId}...");

            var nodeIdsBySource = filesToMigrate
                .GroupBy(f => (f.TgChannelId.HasValue && f.TgChannelId.Value != 0) ? f.TgChannelId.Value : oldEffectiveChannelId)
                .Where(g => g.Key != targetChannelId && g.Key != 0);

            foreach (var group in nodeIdsBySource)
            {
                _repository.EnqueueChannelMigrations(group.Select(f => f.Id), group.Key, targetChannelId);
            }

            TriggerChannelMigrationProcessing();
            AppLogger.Info("TelegramService", $"[Migration] Папка ID {folderNodeId} успешно привязана к каналу ID {targetChannelId}. Поставлено в очередь фоновой миграции: {filesToMigrate.Count} файлов.");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Подключение к серверам Telegram и проверка наличия готовой сессии через WTelegramClient.
        /// </summary>
        public async Task ConnectAsync(bool isSilentRetry = false)
        {
            if (!isSilentRetry)
            {
                AppLogger.Info("TelegramService", "Проверка сессии и подключение к Telegram...");
                _currentSettings = _configManager.Load();
                SetPacingDelay(_currentSettings.Server.PacingDelayMs);
                AppLogger.Info("TelegramService", $"[Streaming Config] Воркеры: {_currentSettings.Server.DownloadWorkerCount} | Пейсинг: {_globalPacingDelayMs} мс | RAM Кэш: {_currentSettings.Server.MemoryCacheSizeMb} МБ (TTL: {_currentSettings.Server.ChunkMemoryCacheTtlMinutes} мин) | Буфер аудио: {_currentSettings.Server.AudioPrefetchWindowMb} МБ | Окно стриминга: {_currentSettings.Server.StreamingPrefetchWindowMb} МБ | Дисковый кэш: {(_currentSettings.Server.EnableDiskReadCache ? "ВКЛ" : "ВЫКЛ (100% RAM)")}");
            }
            else
            {
                _currentSettings ??= _configManager.Load();
            }
            
            var account = _repository?.GetActiveTelegramAccount();
            if (account == null || account.ApiId == 0 || string.IsNullOrWhiteSpace(account.ApiHash))
            {
                LastError = "API ID или API Hash не заданы в базе данных.";
                IsAuthorized = false;
                CurrentStep = AuthStep.NeedsPhone;
                return;
            }

            string sessionPath = account.SessionPath;
            if (!File.Exists(sessionPath))
            {
                AppLogger.Info("TelegramService", "Файл сессии не найден. Ожидается ввод номера телефона пользователем.");
                IsAuthorized = false;
                CurrentStep = AuthStep.NeedsPhone;
                LastError = null;
                try { _client?.Dispose(); } catch { }
                _client = null;
                return;
            }

            try
            {
                InitClient();
                if (_client == null) return;

                // Попытка войти по существующей сессии на диске
                string? result = await _client.Login(null);
                HandleWTelegramResult(result);
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 420) // FLOOD_WAIT_X
            {
                TriggerSessionFloodWait(rpcEx.X);
                LastError = FormatFloodWaitMessage(rpcEx.X);
                AppLogger.Warn("TelegramService", $"Ошибка подключения существующей сессии: {LastError}");
                try { _client?.Dispose(); } catch { }
                _client = null;
                throw new InvalidOperationException(LastError, rpcEx);
            }
            catch (Exception ex)
            {
                if (isSilentRetry)
                {
                    AppLogger.Debug("TelegramService", $"Фоновая попытка подключения сессии к Telegram: {ex.Message}");
                }
                else
                {
                    AppLogger.Error("TelegramService", $"Ошибка подключения существующей сессии: {ex.Message}", ex);
                }
                LastError = ex.Message;
                // Сбрасываем экземпляр клиента, чтобы не оставлять его в сбойном (Faulted) состоянии
                try { _client?.Dispose(); } catch { }
                _client = null;
                throw;
            }
        }

        private void InitClient()
        {
            if (_client != null) return;

            var account = _repository?.GetActiveTelegramAccount();
            int apiId = account?.ApiId ?? 0;
            string apiHash = account?.ApiHash ?? string.Empty;
            string sessionPath = account?.SessionPath ?? "user.session";
            string? phoneNumber = account?.PhoneNumber;

            _client = new WTelegram.Client(what =>
            {
                switch (what)
                {
                    case "api_id": return apiId > 0 ? apiId.ToString() : null;
                    case "api_hash": return !string.IsNullOrEmpty(apiHash) ? apiHash : null;
                    case "session_pathname": return sessionPath;
                    case "phone_number": return phoneNumber;
                    default: return null;
                }
            });
            _client.TcpHandler = SafeTcpHandlerAsync;
            _client.OnOther += HandleClientOtherAsync;
        }

        /// <summary>
        /// Безопасная фабрика создания TCP-подключений с контролируемым таймаутом (5 секунд).
        /// Защищает приложение от зависания на системных таймаутах Windows TCP SYN (до 75+ сек) при недоступных IPv4/IPv6 маршрутах.
        /// </summary>
        private static async Task<TcpClient> SafeTcpHandlerAsync(string host, int port)
        {
            var tcpClient = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await tcpClient.ConnectAsync(host, port, cts.Token);
                return tcpClient;
            }
            catch (OperationCanceledException)
            {
                tcpClient.Dispose();
                // Выбрасываем SocketException (TimedOut), чтобы WTelegramClient распознал таймаут сокета и перешел к следующему адресу
                throw new SocketException((int)SocketError.TimedOut);
            }
            catch
            {
                tcpClient.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Быстрый легковесный TCP-зонд к api.telegram.org:443 для проверки реального наличия интернета и доступности серверов Telegram.
        /// Динамически резолвит официальный домен через DNS (без захардкоженных IP), с таймаутом 2.5 секунды.
        /// Защищает от преждевременных попыток создания клиента WTelegram и спама ошибками HostUnreachable при согласовании Wi-Fi/DHCP.
        /// </summary>
        public static async Task<bool> CheckTelegramReachabilityAsync(int timeoutMs = 2500)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                var addresses = await System.Net.Dns.GetHostAddressesAsync("api.telegram.org", cts.Token);
                if (addresses == null || addresses.Length == 0) return false;

                // Отдаем приоритет IPv4 для избежания таймаутов на немаршрутизируемых локальных IPv6-сетях
                var targetIp = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) 
                               ?? addresses[0];

                using var tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(targetIp, 443, cts.Token);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private Task HandleClientOtherAsync(IObject arg)
        {
            if (arg is TL.ReactorError reactorErr)
            {
                AppLogger.Warn("TelegramService", $"[Reactor] Зафиксирован сбой реактора Telegram: {reactorErr.Exception?.Message}.");
                TriggerReactorReconnect("сбой реактора TL.ReactorError");
            }
            return Task.CompletedTask;
        }

        private int _isReconnecting = 0;

        /// <summary>
        /// Потокобезопасный фоновый процесс самовосстановления сессии Telegram.
        /// При отсутствии интернета выводит строго 1 предупреждение в лог, после чего тихо пингует api.telegram.org:443
        /// каждые 3 секунды без спама в лог. Как только сеть восстанавливается, запускает сессию WTelegram.
        /// </summary>
        public void TriggerReactorReconnect(string reason)
        {
            var account = _repository?.GetActiveTelegramAccount();
            if (account == null)
            {
                AppLogger.Warn("TelegramService", $"[Reactor] Запрос переподключения ({reason}) отклонен: активный аккаунт Telegram не найден в базе данных.");
                return;
            }

            // Защита от паразитного цикла: реактор должен работать ТОЛЬКО для уже успешно авторизованных сессий
            if (!IsAuthorized || CurrentStep != AuthStep.Authorized)
            {
                AppLogger.Debug("TelegramService", $"[Reactor] Запрос переподключения ({reason}) пропущен: аккаунт не находится в состоянии успешной авторизации (Статус: {CurrentStep}).");
                return;
            }

            if (Interlocked.CompareExchange(ref _isReconnecting, 1, 0) != 0)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    // Если клиент формально существует и авторизован, проверяем сокет быстрым пингом с таймаутом 3 сек
                    if (_client != null && IsAuthorized)
                    {
                        try
                        {
                            var pingTask = _client.Help_GetNearestDc();
                            if (await Task.WhenAny(pingTask, Task.Delay(3000)) == pingTask)
                            {
                                await pingTask;
                                _lastSuccessfulTrafficTime = DateTime.UtcNow;
                                AppLogger.Info("TelegramService", "[Reactor] Соединение живо, реактор успешно ответил.");
                                return;
                            }
                        }
                        catch
                        {
                            // Сокет мертв, переходим к процедуре восстановления
                        }
                    }

                    if (_client != null)
                    {
                        try { _client.Dispose(); } catch { }
                        _client = null;
                    }
                    _storagePeer = null;

                    bool hasLoggedOffline = false;

                    while (true)
                    {
                        // 1. Быстрый легковесный TCP-зонд к api.telegram.org:443
                        bool isReachable = await CheckTelegramReachabilityAsync(2500);
                        if (!isReachable)
                        {
                            _isNetworkOffline = true;
                            _lastConnectionFailureTime = DateTime.UtcNow;
                            if (!hasLoggedOffline)
                            {
                                AppLogger.Warn("TelegramService", $"[Reactor] Сеть недоступна ({reason}). Ожидание подключения к интернету...");
                                hasLoggedOffline = true;
                            }

                            // Тихо ждем 3 секунды без спама в лог
                            CancellationToken waitToken;
                            lock (_reconnectWaitLock)
                            {
                                _reconnectWaitCts?.Dispose();
                                _reconnectWaitCts = new CancellationTokenSource();
                                waitToken = _reconnectWaitCts.Token;
                            }

                            try
                            {
                                await Task.Delay(3000, waitToken);
                            }
                            catch (OperationCanceledException)
                            {
                                // Системный сигнал изменения интерфейса Windows мгновенно прерывает ожидание
                            }

                            continue;
                        }

                        // 2. Интернет и Telegram доступны!
                        AppLogger.Info("TelegramService", "[Reactor] Подключение к интернету подтверждено (api.telegram.org доступен). Запуск сессии Telegram...");

                        try
                        {
                            await ConnectAsync(isSilentRetry: false);

                            if (IsAuthorized && _client != null)
                            {
                                _isNetworkOffline = false;
                                _lastSuccessfulTrafficTime = DateTime.UtcNow;
                                _lastConnectionFailureTime = DateTime.MinValue;
                                lock (_connectionLock)
                                {
                                    _connectionReadyTcs?.TrySetResult(true);
                                    _connectionReadyTcs = null;
                                }
                                TelegramWinFspFileSystem.ResetNetworkOfflineState();
                                ClearPersistedFloodStateIfExpired();
                                WakeFloodWaitDelaysOnResume();
                                AppLogger.Info("TelegramService", "[Reactor] Сессия Telegram успешно восстановлена!");
                                break;
                            }
                        }
                        catch (TL.RpcException rpcEx) when (rpcEx.Code == 420) // FLOOD_WAIT_X
                        {
                            TriggerSessionFloodWait(rpcEx.X);
                            AppLogger.Warn("TelegramService", $"[Reactor] Получен FLOOD_WAIT на {rpcEx.X} сек от серверов Telegram. Приостановка реактора на {rpcEx.X} секунд...");
                            int waitMs = Math.Max(5000, rpcEx.X * 1000);
                            await Task.Delay(waitMs);
                            continue;
                        }
                        catch (TL.RpcException rpcEx) when (rpcEx.Code == 400 && (rpcEx.Message.Contains("PHONE_NUMBER_INVALID") || rpcEx.Message.Contains("PHONE_CODE_INVALID") || rpcEx.Message.Contains("PHONE_CODE_EXPIRED") || rpcEx.Message.Contains("SESSION_REVOKED") || rpcEx.Message.Contains("AUTH_KEY_UNREGISTERED") || rpcEx.Message.Contains("USER_DEACTIVATED")))
                        {
                            AppLogger.Error("TelegramService", $"[Reactor] Фатальная ошибка авторизации: {rpcEx.Message} (Код: {rpcEx.Code}). Остановка реактора.");
                            IsAuthorized = false;
                            CurrentStep = AuthStep.NeedsPhone;
                            LastError = rpcEx.Message;
                            break;
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("TelegramService", $"[Reactor] Ошибка сессии Telegram: {ex.Message}. Повтор проверки через 3 секунды...");
                        }

                        await Task.Delay(3000);
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _isReconnecting, 0);
                    lock (_connectionLock)
                    {
                        if (_client != null && IsAuthorized)
                        {
                            _connectionReadyTcs?.TrySetResult(true);
                        }
                        else
                        {
                            _connectionReadyTcs?.TrySetResult(false);
                        }
                        _connectionReadyTcs = null;
                    }
                }
            });
        }

        /// <summary>
        /// Ожидает восстановления сессии Telegram и готовности сети к передаче данных.
        /// Если клиент подключен — возвращает true мгновенно (0 задержки).
        /// При подтвержденном офлайне или отключенном адаптере возвращает false мгновенно (0 мс), не блокируя плеер при добавлении файлов.
        /// Если клиент восстанавливает соединение при доступной сети — асинхронно ожидает готовности в пределах timeoutMs.
        /// </summary>
        public async Task<bool> EnsureConnectedAsync(int timeoutMs = 20000, CancellationToken cancellationToken = default)
        {
            if (_client != null && IsAuthorized) return true;

            var account = _repository?.GetActiveTelegramAccount();
            if (account == null) return false;

            // 1. Проверяем системную доступность физической сети Windows (0 мс)
            if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
            {
                _isNetworkOffline = true;
                _lastConnectionFailureTime = DateTime.UtcNow;
                return false;
            }

            // 2. Если сервис уже зафиксировал режим офлайна — мгновенный отказ (0 мс) без подвисания плеера
            if (_isNetworkOffline)
            {
                _lastConnectionFailureTime = DateTime.UtcNow;
                return false;
            }

            // 3. Fast-fail: если последняя попытка ожидания сети завершилась неудачей менее 5 секунд назад — не висим повторно
            if ((DateTime.UtcNow - _lastConnectionFailureTime).TotalSeconds < 5)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _isReconnecting, 0, 0) == 0)
            {
                TriggerReactorReconnect("запрос операции при отключенном клиенте");
            }

            Task<bool> waitTask;
            lock (_connectionLock)
            {
                if (_client != null && IsAuthorized) return true;
                _connectionReadyTcs ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                waitTask = _connectionReadyTcs.Task;
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                var completed = await Task.WhenAny(waitTask, Task.Delay(timeoutMs, cts.Token));
                if (completed == waitTask)
                {
                    bool success = await waitTask;
                    if (success)
                    {
                        _isNetworkOffline = false;
                        _lastConnectionFailureTime = DateTime.MinValue;
                        return true;
                    }
                }
            }
            catch (OperationCanceledException) { }

            _isNetworkOffline = true;
            _lastConnectionFailureTime = DateTime.UtcNow;
            return false;
        }

        private void InitWorkerPool()
        {
            if (_client == null) return;
            int configuredWorkers = Math.Clamp(_currentSettings.Server.DownloadWorkerCount > 0 ? _currentSettings.Server.DownloadWorkerCount : 1, 1, 3);
            if (_workerPool != null)
            {
                _workerPool.UpdateMainClient(_client);
                return;
            }
            _workerPool = new MtprotoDownloadWorkerPool(
                _client,
                workerCount: configuredWorkers,
                clientProvider: GetWorkerClientAsync,
                existingChunkProvider: (chId, msgId, off) => TryGetFromMemoryCache(chId, msgId, off, out var d, out _) ? d : null,
                documentRefresher: async (chId, msgId) => await GetDocumentFromMessageAsync(msgId, chId != 0 ? chId : (long?)null, forceRefresh: true)
            );
        }

        /// <summary>
        /// Мгновенное применение новых настроек в рантайме без перезапуска сервиса.
        /// </summary>
        public void UpdateSettings(AppSettings settings)
        {
            _currentSettings = settings;
            SetPacingDelay(_currentSettings.Server.PacingDelayMs);
            if (_workerPool != null)
            {
                _workerPool.SetWorkerCount(_currentSettings.Server.DownloadWorkerCount);
            }
            else if (_client != null && IsAuthorized)
            {
                InitWorkerPool();
            }
            AppLogger.Info("TelegramService", $"[Config Update] Настройки сервиса обновлены: воркеров={_currentSettings.Server.DownloadWorkerCount}, пейсинг={_currentSettings.Server.PacingDelayMs} мс, RAM кэш={_currentSettings.Server.MemoryCacheSizeMb} МБ.");
        }

        /// <summary>
        /// Возвращает сессию DC основного клиента MTProto для параллельных воркеров пула.
        /// Протокол MTProto поддерживает мультиплексирование параллельных запросов чанков без повторных файловых блокировок.
        /// </summary>
        public async Task<WTelegram.Client> GetWorkerClientAsync(int workerId, int dcId)
        {
            if (_client == null || !IsAuthorized)
            {
                bool reconnected = await EnsureConnectedAsync(20000);
                if (!reconnected || _client == null)
                    throw new TelegramNetworkException("Основной клиент Telegram не инициализирован или не подключен.");
            }

            return dcId != 0 ? await _client.GetClientForDC(dcId) : _client;
        }

        private void HandleWTelegramResult(string? result)
        {
            if (result == null)
            {
                // Успешная авторизация
                IsAuthorized = true;
                CurrentStep = AuthStep.Authorized;
                LastError = null;
                _lastSuccessfulTrafficTime = DateTime.UtcNow;
                InitWorkerPool();

                if (_client?.User != null)
                {
                    var u = _client.User;
                    CurrentUser = new TelegramUserInfo
                    {
                        Id = u.ID,
                        Username = u.MainUsername,
                        FirstName = u.first_name,
                        LastName = u.last_name,
                        Phone = u.phone,
                        IsPremium = u.flags.HasFlag(TL.User.Flags.premium),
                        DcId = 0
                    };
                    AppLogger.Info("TelegramService", $"Успешная авторизация пользователя: {u.first_name} (ID: {u.ID})");

                    if (!string.IsNullOrEmpty(u.phone) && _repository != null)
                    {
                        string phoneFormatted = u.phone.StartsWith("+") ? u.phone : "+" + u.phone;
                        var account = _repository.GetActiveTelegramAccount();
                        if (account != null && account.PhoneNumber != phoneFormatted)
                        {
                            _repository.UpdateAccountPhone(account.Id, phoneFormatted);
                        }
                    }

                    // Фоновый прогрев канала-хранилища и получение конфигурации DC сразу после успешной авторизации,
                    // чтобы первая операция копирования/чтения файлов выполнялась мгновенно
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (_client != null)
                            {
                                var config = await _client.Help_GetConfig();
                                if (config != null && CurrentUser != null)
                                {
                                    CurrentUser.DcId = config.this_dc;
                                }
                            }
                        }
                        catch { }

                        try
                        {
                            await GetStoragePeerAsync();
                            TriggerDeletionQueueProcessing();
                            TriggerChannelMigrationProcessing();
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("TelegramService", $"Фоновый прогрев канала-хранилища: {ex.Message}");
                        }
                    });

                    OnConnectionRestored?.Invoke();
                }
            }
            else if (result == "verification_code")
            {
                IsAuthorized = false;
                CurrentStep = AuthStep.NeedsCode;
                LastError = null;
                AppLogger.Info("TelegramService", "Код подтверждения отправлен в Telegram.");
            }
            else if (result == "password")
            {
                IsAuthorized = false;
                CurrentStep = AuthStep.Needs2FA;
                LastError = null;
                AppLogger.Info("TelegramService", "Требуется ввод двухфакторного (2FA) облачного пароля.");
            }
            else
            {
                IsAuthorized = false;
                CurrentStep = AuthStep.NeedsPhone;
                LastError = $"Ожидался ввод: {result}";
            }
        }

        /// <summary>
        /// Пошаговый вход в Telegram:
        /// 1. Передаем телефон ("+3...") -> Telegram отправляет код в официальное приложение
        /// 2. Передаем код подтверждения -> если включен 2FA, требуется "password", иначе успех
        /// 3. Передаем 2FA пароль -> успех
        /// </summary>
        public async Task<string?> LoginStepAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Входные данные не могут быть пустыми", nameof(input));

            int remainingFlood = GetRemainingFloodWaitSeconds();
            if (remainingFlood > 0)
            {
                string floodMsg = FormatFloodWaitMessage(remainingFlood);
                LastError = floodMsg;
                throw new InvalidOperationException(floodMsg);
            }

            var account = _repository?.GetActiveTelegramAccount();
            if (account == null || account.ApiId == 0 || string.IsNullOrWhiteSpace(account.ApiHash))
            {
                LastError = "Сначала укажите и сохраните API ID и API Hash.";
                throw new InvalidOperationException(LastError);
            }

            // На первом шаге (NeedsPhone) или если клиент отсутствует/сброшен, создаем чистый экземпляр клиента
            if (CurrentStep == AuthStep.NeedsPhone || _client == null)
            {
                string phoneFormatted = input.Trim();
                if (!phoneFormatted.StartsWith("+") && char.IsDigit(phoneFormatted[0]))
                {
                    phoneFormatted = "+" + phoneFormatted;
                }
                _repository?.UpdateAccountPhone(account.Id, phoneFormatted);

                try { _client?.Dispose(); } catch { }
                _client = null;
                InitClient();
            }

            if (_client == null)
                throw new InvalidOperationException("Не удалось инициализировать Telegram Client.");

            AppLogger.Info("TelegramService", $"Отправка данных на шаге {CurrentStep} в Telegram API...");

            try
            {
                string? result = await _client.Login(input.Trim());
                HandleWTelegramResult(result);
                return result;
            }
            catch (TL.RpcException rpcEx)
            {
                AppLogger.Error("TelegramService", $"RPC Ошибка: {rpcEx.Message} (Код: {rpcEx.Code})", rpcEx);
                if (rpcEx.Code == 420) // FLOOD_WAIT_X
                {
                    TriggerSessionFloodWait(rpcEx.X);
                    LastError = FormatFloodWaitMessage(rpcEx.X);
                    throw new InvalidOperationException(LastError, rpcEx);
                }
                else if (rpcEx.Code == 400 && rpcEx.Message.Contains("PHONE_NUMBER_INVALID"))
                {
                    CurrentStep = AuthStep.NeedsPhone;
                    try { _client?.Dispose(); } catch { }
                    _client = null;
                    LastError = "Неверный формат номера телефона. Проверьте правильность и код страны (например, +3...).";
                    throw new InvalidOperationException(LastError, rpcEx);
                }
                else if (rpcEx.Code == 400 && rpcEx.Message.Contains("PHONE_CODE_INVALID"))
                {
                    LastError = "Неверный проверочный код. Проверьте цифры кода из приложения Telegram.";
                    throw new InvalidOperationException(LastError, rpcEx);
                }
                else if (rpcEx.Code == 400 && rpcEx.Message.Contains("PHONE_CODE_EXPIRED"))
                {
                    CurrentStep = AuthStep.NeedsPhone;
                    try { _client?.Dispose(); } catch { }
                    _client = null;
                    LastError = "Срок действия кода подтверждения истек. Запросите код заново.";
                    throw new InvalidOperationException(LastError, rpcEx);
                }
                else if (rpcEx.Code == 400 && rpcEx.Message.Contains("PASSWORD_HASH_INVALID"))
                {
                    LastError = "Неверный облачный пароль двухфакторной аутентификации (2FA).";
                    throw new InvalidOperationException(LastError, rpcEx);
                }

                LastError = rpcEx.Message;
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Error("TelegramService", $"Ошибка входа: {ex.Message}", ex);
                LastError = ex.Message;
                // При ошибке на шаге ввода телефона сбрасываем клиент для чистой следующей попытки
                if (CurrentStep == AuthStep.NeedsPhone)
                {
                    try { _client?.Dispose(); } catch { }
                    _client = null;
                }
                throw;
            }
        }

        /// <summary>
        /// Выход из аккаунта и удаление файла сессии.
        /// </summary>
        public void Logout()
        {
            try
            {
                _workerPool?.Dispose();
                _workerPool = null;
            }
            catch { }

            try
            {
                _client?.Dispose();
                _client = null;
            }
            catch { }

            var account = _repository?.GetActiveTelegramAccount();
            string sessionPath = account?.SessionPath ?? "user.session";

            if (File.Exists(sessionPath))
            {
                try
                {
                    File.Delete(sessionPath);
                }
                catch { }
            }
            if (account != null && _repository != null)
            {
                _repository.ClearAccountSession(account.Id);
            }

            IsAuthorized = false;
            CurrentStep = AuthStep.NeedsPhone;
            CurrentUser = null;
            _storagePeer = null;
            AppLogger.Info("TelegramService", "Сессия сброшена.");
        }

        /// <summary>
        /// Сбрасывает процесс авторизации обратно к вводу номера телефона.
        /// Позволяет пользователю исправить опечатку в номере или начать заново при сбое.
        /// </summary>
        public void ResetAuthToPhone()
        {
            try { _client?.Dispose(); } catch { }
            _client = null;
            CurrentStep = AuthStep.NeedsPhone;
            LastError = null;
            AppLogger.Info("TelegramService", "Процесс авторизации сброшен к шагу ввода номера телефона.");
        }

        public static void BeginDownloadRpc() => Interlocked.Increment(ref _activeDownloadRequestCount);
        public static void EndDownloadRpc()
        {
            if (Interlocked.Decrement(ref _activeDownloadRequestCount) < 0)
            {
                Interlocked.Exchange(ref _activeDownloadRequestCount, 0);
            }
        }

        private static void BeginCaptionRpc() => Interlocked.Increment(ref _activeCaptionRequestCount);
        private static void EndCaptionRpc()
        {
            if (Interlocked.Decrement(ref _activeCaptionRequestCount) < 0)
            {
                Interlocked.Exchange(ref _activeCaptionRequestCount, 0);
            }
        }

        private static void BeginBackgroundRpc() => Interlocked.Increment(ref _activeBackgroundRequestCount);
        private static void EndBackgroundRpc()
        {
            if (Interlocked.Decrement(ref _activeBackgroundRequestCount) < 0)
            {
                Interlocked.Exchange(ref _activeBackgroundRequestCount, 0);
            }
        }

        private static void BeginUploadMessageRpc() => Interlocked.Increment(ref _activeUploadMessageRpcCount);
        private static void EndUploadMessageRpc()
        {
            if (Interlocked.Decrement(ref _activeUploadMessageRpcCount) < 0)
            {
                Interlocked.Exchange(ref _activeUploadMessageRpcCount, 0);
            }
        }

        /// <summary>
        /// Возвращает оставшееся время ожидания в секундах при активном FLOOD_WAIT сессии/авторизации.
        /// </summary>
        public static int GetRemainingFloodWaitSeconds()
        {
            if (_sessionFloodWaitUntilUtc > DateTime.UtcNow)
            {
                return (int)Math.Ceiling((_sessionFloodWaitUntilUtc - DateTime.UtcNow).TotalSeconds);
            }
            return 0;
        }

        /// <summary>
        /// Форматирует человекочитаемое сообщение об ошибке FLOOD_WAIT.
        /// </summary>
        public static string FormatFloodWaitMessage(int seconds)
        {
            if (seconds >= 60)
            {
                int min = seconds / 60;
                int sec = seconds % 60;
                return $"Слишком много запросов (FLOOD_WAIT). Сервер Telegram просит подождать {min} мин {sec} сек перед повторной попыткой.";
            }
            return $"Слишком много запросов (FLOOD_WAIT). Сервер Telegram просит подождать {seconds} сек перед повторной попыткой.";
        }

        private const int FloodWaitPollSliceMs = 10000;
        private static readonly object _floodWakeLock = new();
        private static CancellationTokenSource _floodWakeCts = new();

        /// <summary>
        /// Пробуждает все активные ожидания FLOOD_WAIT после выхода Windows из сна/гибернации,
        /// чтобы они немедленно сверились с реальным временем DateTime.UtcNow.
        /// </summary>
        public static void WakeFloodWaitDelaysOnResume()
        {
            CancellationTokenSource previous;
            lock (_floodWakeLock)
            {
                previous = _floodWakeCts;
                _floodWakeCts = new CancellationTokenSource();
            }
            try { previous.Cancel(); } catch { }
        }

        private static async Task WaitUntilUtcWakeableAsync(Func<DateTime> getUntilUtc, string? logLabel, CancellationToken cancellationToken)
        {
            bool logged = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var now = DateTime.UtcNow;
                var until = getUntilUtc();
                if (until <= now) break;

                var remaining = until - now;
                if (!logged && !string.IsNullOrEmpty(logLabel))
                {
                    AppLogger.Warn("TelegramService", $"[{logLabel}] Пауза до {until.ToLocalTime():HH:mm:ss} ({remaining.TotalSeconds:F1} сек)...");
                    logged = true;
                }

                int sliceMs = (int)Math.Min(FloodWaitPollSliceMs, Math.Ceiling(remaining.TotalMilliseconds));
                if (sliceMs <= 0) break;

                CancellationToken wakeToken;
                lock (_floodWakeLock)
                {
                    wakeToken = _floodWakeCts.Token;
                }

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, wakeToken);
                try
                {
                    await Task.Delay(sliceMs, linkedCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Прервано пробуждением после сна — сразу перепроверяем DateTime.UtcNow
                }
            }

            if (logged)
            {
                _activeInstance?.ClearPersistedFloodStateIfExpired();
            }
        }

        /// <summary>
        /// Ожидание сессионного FLOOD_WAIT (авторизация, восстановление подключения, общие вызовы).
        /// Изолирован от очередей скачивания, обновления подписей и фонового удаления/миграции.
        /// </summary>
        public static Task EnsureFloodWaitDelayAsync(CancellationToken cancellationToken = default)
        {
            return WaitUntilUtcWakeableAsync(
                () => _sessionFloodWaitUntilUtc,
                "Session FLOOD_WAIT",
                cancellationToken);
        }

        /// <summary>
        /// Ожидание FLOOD_WAIT для контура скачивания и стриминга (учитывает только паузу скачивания и общую паузу сессии).
        /// </summary>
        public static Task EnsureDownloadFloodWaitDelayAsync(CancellationToken cancellationToken = default)
        {
            return WaitUntilUtcWakeableAsync(
                () => _downloadFloodWaitUntilUtc > _sessionFloodWaitUntilUtc
                    ? _downloadFloodWaitUntilUtc
                    : _sessionFloodWaitUntilUtc,
                "Download FLOOD_WAIT",
                cancellationToken);
        }

        /// <summary>
        /// Ожидание FLOOD_WAIT для изолированной фоновой очереди обновления подписей.
        /// </summary>
        public static Task EnsureCaptionFloodWaitDelayAsync(CancellationToken cancellationToken = default)
        {
            return WaitUntilUtcWakeableAsync(
                () => _captionFloodWaitUntilUtc > _sessionFloodWaitUntilUtc
                    ? _captionFloodWaitUntilUtc
                    : _sessionFloodWaitUntilUtc,
                "Caption FLOOD_WAIT",
                cancellationToken);
        }

        /// <summary>
        /// Ожидание FLOOD_WAIT для фоновых сервисных очередей (удаление, миграция каналов, копирование-форвард, аватарки).
        /// </summary>
        public static Task EnsureBackgroundFloodWaitDelayAsync(CancellationToken cancellationToken = default)
        {
            return WaitUntilUtcWakeableAsync(
                () => _backgroundFloodWaitUntilUtc > _sessionFloodWaitUntilUtc
                    ? _backgroundFloodWaitUntilUtc
                    : _sessionFloodWaitUntilUtc,
                "Background FLOOD_WAIT",
                cancellationToken);
        }

        // Выделенный планировщик слотов времени отправки для скачивания и стриминга (Token-Bucket Pacing)
        // Гарантирует строгое квантование интервалов запуска исходящих MTProto-пакетов Upload_GetFile
        private static readonly SemaphoreSlim _globalPacingLock = new SemaphoreSlim(1, 1);
        private static DateTime _nextAllowedSendUtc = DateTime.MinValue;
        private static int _globalPacingDelayMs = ServerSettings.DefaultPacingDelayMs;

        /// <summary>
        /// Резервирует уникальный временной слот для отправки исходящего пакета Upload_GetFile.
        /// Исключает одновременную отправку пакетов разными воркерами скачивания в одну миллисекунду.
        /// </summary>
        public static async Task EnsurePacingDelayAsync(CancellationToken cancellationToken = default)
        {
            DateTime scheduledTime;
            await _globalPacingLock.WaitAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                if (_nextAllowedSendUtc < now)
                {
                    _nextAllowedSendUtc = now;
                }
                scheduledTime = _nextAllowedSendUtc;
                _nextAllowedSendUtc = _nextAllowedSendUtc.AddMilliseconds(_globalPacingDelayMs);
            }
            finally
            {
                _globalPacingLock.Release();
            }

            var waitMs = (scheduledTime - DateTime.UtcNow).TotalMilliseconds;
            if (waitMs > 0)
            {
                await Task.Delay((int)waitMs, cancellationToken);
            }
        }

        // Изолированный планировщик для фоновой очереди обновления подписей (ProcessCaptionQueueAsync).
        // Базовый интервал >= 350 мс (Math.Max(350, _globalPacingDelayMs * 2)) защищает от короткого лимита (~100 запр/30 сек),
        // а ступенчатое замедление на длинных сериях (>300 -> 750 мс, >800 -> 1400 мс, пауза 20 сек каждые 500) + реакция на FLOOD_WAIT
        // сдерживают длинное скользящее окно (~2000 редактирований). При простое >= 5 мин серия и усиление сбрасываются.
        private const int MinCaptionPacingDelayMs = 350;
        private const int CaptionTier1Threshold = 300;
        private const int CaptionTier1DelayMs = 750;
        private const int CaptionTier2Threshold = 800;
        private const int CaptionTier2DelayMs = 1400;
        private const int CaptionCooldownIntervalCount = 500;
        private const int CaptionCooldownDurationMs = 20000;
        private const int CaptionBurstIdleResetMinutes = 5;
        private const int CaptionAdaptiveMaxFloorMs = 2000;
        private static readonly SemaphoreSlim _captionPacingLock = new SemaphoreSlim(1, 1);
        private static DateTime _nextCaptionAllowedSendUtc = DateTime.MinValue;
        private static DateTime _lastCaptionSlotReservedUtc = DateTime.MinValue;
        private static DateTime _lastCaptionFloodDetectedUtc = DateTime.MinValue;
        private static int _captionBurstCount = 0;
        private static int _captionAdaptiveFloorDelayMs = 0;

        public static async Task EnsureCaptionPacingDelayAsync(CancellationToken cancellationToken = default)
        {
            int globalDelayMs = Volatile.Read(ref _globalPacingDelayMs);
            int baseDelayMs = Math.Max(MinCaptionPacingDelayMs, globalDelayMs * 2);

            DateTime scheduledTime;
            int cooldownAppliedBurst = 0;
            int tierTransitionDelayMs = 0;
            int tierTransitionCount = 0;
            bool resetByIdle = false;

            await _captionPacingLock.WaitAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                if (_lastCaptionSlotReservedUtc != DateTime.MinValue &&
                    (now - _lastCaptionSlotReservedUtc).TotalMinutes >= CaptionBurstIdleResetMinutes &&
                    (_captionBurstCount > 0 || _captionAdaptiveFloorDelayMs > 0))
                {
                    _captionBurstCount = 0;
                    _captionAdaptiveFloorDelayMs = 0;
                    resetByIdle = true;
                }

                _captionBurstCount++;
                _lastCaptionSlotReservedUtc = now;

                int tierDelayMs = baseDelayMs;
                if (_captionBurstCount > CaptionTier2Threshold)
                {
                    tierDelayMs = Math.Max(baseDelayMs, CaptionTier2DelayMs);
                    if (_captionBurstCount == CaptionTier2Threshold + 1)
                    {
                        tierTransitionDelayMs = tierDelayMs;
                        tierTransitionCount = _captionBurstCount;
                    }
                }
                else if (_captionBurstCount > CaptionTier1Threshold)
                {
                    tierDelayMs = Math.Max(baseDelayMs, CaptionTier1DelayMs);
                    if (_captionBurstCount == CaptionTier1Threshold + 1)
                    {
                        tierTransitionDelayMs = tierDelayMs;
                        tierTransitionCount = _captionBurstCount;
                    }
                }

                int effectiveDelayMs = Math.Max(tierDelayMs, _captionAdaptiveFloorDelayMs);

                if (_nextCaptionAllowedSendUtc < now)
                {
                    _nextCaptionAllowedSendUtc = now;
                }

                if (_captionBurstCount > 0 && _captionBurstCount % CaptionCooldownIntervalCount == 0)
                {
                    var cooldownTarget = now.AddMilliseconds(CaptionCooldownDurationMs);
                    if (cooldownTarget > _nextCaptionAllowedSendUtc)
                    {
                        _nextCaptionAllowedSendUtc = cooldownTarget;
                    }
                    cooldownAppliedBurst = _captionBurstCount;
                }

                scheduledTime = _nextCaptionAllowedSendUtc;
                _nextCaptionAllowedSendUtc = _nextCaptionAllowedSendUtc.AddMilliseconds(effectiveDelayMs);
            }
            finally
            {
                _captionPacingLock.Release();
            }

            if (resetByIdle)
            {
                AppLogger.Info("TelegramService", $"[Caption Pacing] Простой >= {CaptionBurstIdleResetMinutes} мин: серия и интервал подписей сброшены к базовому режиму ({baseDelayMs} мс).");
            }
            if (tierTransitionDelayMs > 0)
            {
                AppLogger.Info("TelegramService", $"[Caption Pacing] Серия достигла {tierTransitionCount} записей: интервал обновления подписей увеличен до {tierTransitionDelayMs} мс.");
            }
            if (cooldownAppliedBurst > 0)
            {
                AppLogger.Info("TelegramService", $"[Caption Pacing] Пройдено {cooldownAppliedBurst} записей подряд — охлаждение {CaptionCooldownDurationMs / 1000} сек...");
            }

            if (scheduledTime > DateTime.UtcNow)
            {
                await WaitUntilUtcWakeableAsync(() => scheduledTime, null, cancellationToken);
            }
        }

        // Изолированный планировщик для фоновых операций (удаление, миграция каналов, форвард),
        // чтобы они не сдвигали временные слоты скачивания/стриминга (_nextAllowedSendUtc).
        private static readonly SemaphoreSlim _backgroundPacingLock = new SemaphoreSlim(1, 1);
        private static DateTime _nextBackgroundAllowedSendUtc = DateTime.MinValue;

        public static async Task EnsureBackgroundPacingDelayAsync(CancellationToken cancellationToken = default)
        {
            int effectiveDelayMs = Math.Max(ServerSettings.DefaultPacingDelayMs, Volatile.Read(ref _globalPacingDelayMs));

            DateTime scheduledTime;
            await _backgroundPacingLock.WaitAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                if (_nextBackgroundAllowedSendUtc < now)
                {
                    _nextBackgroundAllowedSendUtc = now;
                }
                scheduledTime = _nextBackgroundAllowedSendUtc;
                _nextBackgroundAllowedSendUtc = _nextBackgroundAllowedSendUtc.AddMilliseconds(effectiveDelayMs);
            }
            finally
            {
                _backgroundPacingLock.Release();
            }

            var waitMs = (scheduledTime - DateTime.UtcNow).TotalMilliseconds;
            if (waitMs > 0)
            {
                await Task.Delay((int)waitMs, cancellationToken);
            }
        }

        /// <summary>
        /// Фиксирует завершение сетевого вызова к серверам Telegram.
        /// </summary>
        public static void NotifyRequestCompleted()
        {
        }

        public static void SetPacingDelay(int delayMs)
        {
            int effective = (delayMs >= ServerSettings.MinPacingDelayMs && delayMs <= ServerSettings.MaxPacingDelayMs)
                ? delayMs
                : ServerSettings.DefaultPacingDelayMs;
            Interlocked.Exchange(ref _globalPacingDelayMs, effective);
        }

        public static void AdaptPacingDelay(int deltaMs)
        {
            Interlocked.Exchange(ref _globalPacingDelayMs, Math.Clamp(_globalPacingDelayMs + deltaMs, ServerSettings.MinPacingDelayMs, ServerSettings.MaxPacingDelayMs));
        }

        public void TriggerFloodWait(int seconds)
        {
            TriggerSessionFloodWait(seconds);
        }

        public static void TriggerSessionFloodWait(int seconds)
        {
            if (seconds <= 0) return;
            var targetTime = DateTime.UtcNow.AddSeconds(seconds);
            if (targetTime > _sessionFloodWaitUntilUtc)
            {
                _sessionFloodWaitUntilUtc = targetTime;
            }
            AppLogger.Warn("TelegramService", $"[Session Flood] Получен FLOOD_WAIT на {seconds} сек. Пауза сессии до {_sessionFloodWaitUntilUtc.ToLocalTime():HH:mm:ss}.");
            if (_activeInstance?.IsAuthorized == true)
            {
                RecordFloodStateAndPersist(seconds, isUploadStopped: false, fileName: null, isDownload: false, scope: FloodWaitScope.General);
                NotifyFloodWaitBanner(seconds, isUploadStopped: false, fileName: null, isDownload: false, scope: FloodWaitScope.General);
            }
        }

        public static void TriggerDownloadFloodWait(int seconds, string? fileName = null)
        {
            if (seconds <= 0) seconds = 3;
            var targetTime = DateTime.UtcNow.AddSeconds(seconds);
            if (targetTime > _downloadFloodWaitUntilUtc)
            {
                _downloadFloodWaitUntilUtc = targetTime;
            }
            _globalPacingLock.Wait();
            try
            {
                if (targetTime > _nextAllowedSendUtc)
                {
                    _nextAllowedSendUtc = targetTime;
                }
            }
            finally
            {
                _globalPacingLock.Release();
            }
            AdaptPacingDelay(20);
            AppLogger.Warn("TelegramService", $"[Download Flood] Получен FLOOD_WAIT на {seconds} сек при скачивании. Пауза скачивания до {_downloadFloodWaitUntilUtc.ToLocalTime():HH:mm:ss}. Пейсинг скачивания адаптирован до {_globalPacingDelayMs} мс.");
            if (_activeInstance?.IsAuthorized == true)
            {
                RecordFloodStateAndPersist(seconds, isUploadStopped: false, fileName: fileName, isDownload: true, scope: FloodWaitScope.Download);
                NotifyFloodWaitBanner(seconds, isUploadStopped: false, fileName: fileName, isDownload: true, scope: FloodWaitScope.Download);
            }
        }

        public static void TriggerCaptionFloodWait(int seconds, string? source = null)
        {
            if (seconds <= 0) return;
            var now = DateTime.UtcNow;
            var targetTime = now.AddSeconds(seconds);
            if (targetTime > _captionFloodWaitUntilUtc)
            {
                _captionFloodWaitUntilUtc = targetTime;
            }

            int updatedFloorDelay = 0;
            _captionPacingLock.Wait();
            try
            {
                if (targetTime > _nextCaptionAllowedSendUtc)
                {
                    _nextCaptionAllowedSendUtc = targetTime;
                }
                if ((now - _lastCaptionFloodDetectedUtc).TotalMilliseconds >= 1500)
                {
                    _lastCaptionFloodDetectedUtc = now;
                    _captionAdaptiveFloorDelayMs = _captionAdaptiveFloorDelayMs == 0
                        ? CaptionTier1DelayMs
                        : Math.Min(CaptionAdaptiveMaxFloorMs, _captionAdaptiveFloorDelayMs * 2);
                    updatedFloorDelay = _captionAdaptiveFloorDelayMs;
                }
            }
            finally
            {
                _captionPacingLock.Release();
            }

            string srcTag = string.IsNullOrWhiteSpace(source) ? string.Empty : $" ({source})";
            string floorTag = updatedFloorDelay > 0 ? $" Минимальный интервал подписей повышен до {updatedFloorDelay} мс." : string.Empty;
            AppLogger.Warn("TelegramService", $"[Caption Flood] Получен FLOOD_WAIT на {seconds} сек{srcTag}. Очередь обновления подписей приостановлена до {_captionFloodWaitUntilUtc.ToLocalTime():HH:mm:ss} (скачивание и загрузка не затронуты).{floorTag}");
            if (_activeInstance?.IsAuthorized == true)
            {
                const string captionBannerTitle = "Фоновое обновление подписей";
                RecordFloodStateAndPersist(seconds, isUploadStopped: false, fileName: captionBannerTitle, isDownload: false, scope: FloodWaitScope.Caption);
                NotifyFloodWaitBanner(seconds, isUploadStopped: false, fileName: captionBannerTitle, isDownload: false, scope: FloodWaitScope.Caption);
            }
        }

        public static void TriggerBackgroundFloodWait(int seconds, string? source = null)
        {
            if (seconds <= 0) return;
            var targetTime = DateTime.UtcNow.AddSeconds(seconds);
            if (targetTime > _backgroundFloodWaitUntilUtc)
            {
                _backgroundFloodWaitUntilUtc = targetTime;
            }
            _backgroundPacingLock.Wait();
            try
            {
                if (targetTime > _nextBackgroundAllowedSendUtc)
                {
                    _nextBackgroundAllowedSendUtc = targetTime;
                }
            }
            finally
            {
                _backgroundPacingLock.Release();
            }

            string bannerName = string.IsNullOrWhiteSpace(source) ? "Фоновые операции Telegram" : source;
            AppLogger.Warn("TelegramService", $"[Background Flood] Получен FLOOD_WAIT на {seconds} сек ({bannerName}). Фоновая очередь приостановлена до {_backgroundFloodWaitUntilUtc.ToLocalTime():HH:mm:ss} (скачивание не затронуто).");
            if (_activeInstance?.IsAuthorized == true)
            {
                RecordFloodStateAndPersist(seconds, isUploadStopped: false, fileName: bannerName, isDownload: false, scope: FloodWaitScope.Background);
                NotifyFloodWaitBanner(seconds, isUploadStopped: false, fileName: bannerName, isDownload: false, scope: FloodWaitScope.Background);
            }
        }

        // Изолированный адаптивный пейсинг исходящих сообщений при загрузке файлов в Telegram.
        // Стартует с 0 мс; при первом FLOOD_WAIT выставляется 150 мс, далее +50 мс за каждый новый эпизод (потолок 2000 мс).
        // При FLOOD_WAIT > 60 сек выставляется жёсткая блокировка новых отправок (_uploadHardStopUntilUtc) до истечения таймера Telegram.
        // Откат: -25 мс через 90 сек чистой работы; при касании границы интервал удваивается (90 -> 180 -> 300 сек).
        // При простое >= 5 мин между отправками задержка и интервал сбрасываются к 0 мс / 90 сек.
        private static readonly Regex FloodWaitLogRegex = new(@"FLOOD_(?:PREMIUM_)?WAIT_(\d+)", RegexOptions.Compiled);
        private static readonly object _uploadPacingLock = new();
        private static int _uploadPacingDelayMs = 0;
        private static int _uploadCleanSendsSinceLastChange = 0;
        private static int _uploadDecayIntervalSeconds = UploadPacingBaseDecayIntervalSeconds;
        private static DateTime _lastUploadFloodDetectedUtc = DateTime.MinValue;
        private static DateTime _uploadActiveFloodUntilUtc = DateTime.MinValue;
        private static DateTime _uploadHardStopUntilUtc = DateTime.MinValue;
        private static DateTime _uploadLastPacingChangeUtc = DateTime.MinValue;
        private static DateTime _uploadLastDecayAttemptUtc = DateTime.MinValue;
        private static DateTime _lastUploadActivityFinishedUtc = DateTime.MinValue;
        private static readonly Dictionary<FloodWaitScope, DateTime> _lastFloodBannerNotifyByScopeUtc = new();
        private const int UploadPacingInitialFloodDelayMs = 150;
        private const int UploadPacingStepMs = 50;
        private const int UploadPacingMaxDelayMs = 2000;
        private const int UploadPacingDecayStepMs = 25;
        private const int UploadPacingBaseDecayIntervalSeconds = 90;
        private const int UploadPacingMaxDecayIntervalSeconds = 300;
        private const int UploadPacingIdleResetMinutes = 5;
        private const int UploadFloodDedupWindowMs = 1500;
        private const int UploadHardStopThresholdSeconds = 60;
        private const int FloodPersistThresholdSeconds = 10;
        private const string FloodStateFileName = "flood_state.json";
        private static string FloodStateFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FloodStateFileName);
        private static readonly object _floodStateFileLock = new();
        private static readonly Dictionary<FloodWaitScope, ActiveFloodBannerState> _activeFloodBanners = new();

        private sealed class PersistedFloodState
        {
            public DateTime FloodWaitUntilUtc { get; set; }
            public int InitialSeconds { get; set; }
            public bool IsUploadStopped { get; set; }
            public bool IsDownload { get; set; }
            public string? FileName { get; set; }
            public FloodWaitScope? Scope { get; set; }
        }

        private sealed class PersistedFloodStateFile
        {
            public List<PersistedFloodState>? Entries { get; set; }
            // Поля для обратной совместимости со старым однослотовым форматом flood_state.json
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public DateTime FloodWaitUntilUtc { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public int InitialSeconds { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool IsUploadStopped { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool IsDownload { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public string? FileName { get; set; }
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public FloodWaitScope? Scope { get; set; }
        }

        private static int GetBannerPriority(FloodWaitScope scope, bool isUploadStopped)
        {
            if (isUploadStopped) return 4;
            return scope switch
            {
                FloodWaitScope.Download => 3,
                FloodWaitScope.Upload => 3,
                FloodWaitScope.General => 2,
                FloodWaitScope.Background => 1,
                FloodWaitScope.Caption => 0,
                _ => 1
            };
        }

        private static List<PersistedFloodState> CollectPersistableEntriesLocked(DateTime nowUtc)
        {
            return _activeFloodBanners.Values
                .Where(b => b.FloodWaitUntilUtc > nowUtc && b.InitialSeconds > FloodPersistThresholdSeconds)
                .OrderByDescending(b => GetBannerPriority(b.Scope, b.IsUploadStopped))
                .ThenByDescending(b => b.FloodWaitUntilUtc)
                .Select(b => new PersistedFloodState
                {
                    FloodWaitUntilUtc = b.FloodWaitUntilUtc,
                    InitialSeconds = Math.Max(1, b.InitialSeconds),
                    IsUploadStopped = b.IsUploadStopped,
                    IsDownload = b.IsDownload,
                    FileName = b.FileName,
                    Scope = b.Scope
                })
                .ToList();
        }

        private static void WriteOrDeletePersistedFloodState()
        {
            lock (_floodStateFileLock)
            {
                List<PersistedFloodState> entries;
                lock (_uploadPacingLock)
                {
                    entries = CollectPersistableEntriesLocked(DateTime.UtcNow);
                }

                if (entries.Count == 0)
                {
                    DeletePersistedFloodStateNoLock();
                    return;
                }

                try
                {
                    var fileModel = new PersistedFloodStateFile { Entries = entries };
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(fileModel, options);
                    File.WriteAllText(FloodStateFilePath, json);
                }
                catch (Exception ex)
                {
                    AppLogger.Debug("TelegramService", $"[Flood Persist] Не удалось сохранить состояние FLOOD_WAIT в {FloodStateFileName}: {ex.Message}");
                }
            }
        }

        private static void DeletePersistedFloodStateNoLock()
        {
            try
            {
                string path = FloodStateFilePath;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug("TelegramService", $"[Flood Persist] Не удалось удалить {FloodStateFileName}: {ex.Message}");
            }
        }

        private static void LoadPersistedFloodState()
        {
            lock (_floodStateFileLock)
            {
                string path = FloodStateFilePath;
                if (!File.Exists(path)) return;

                try
                {
                    string json = File.ReadAllText(path);
                    var fileModel = JsonSerializer.Deserialize<PersistedFloodStateFile>(json);
                    if (fileModel == null)
                    {
                        DeletePersistedFloodStateNoLock();
                        return;
                    }

                    List<PersistedFloodState> rawEntries;
                    if (fileModel.Entries != null && fileModel.Entries.Count > 0)
                    {
                        rawEntries = fileModel.Entries;
                    }
                    else if (fileModel.InitialSeconds > 0 && fileModel.FloodWaitUntilUtc != DateTime.MinValue)
                    {
                        rawEntries = new List<PersistedFloodState>
                        {
                            new PersistedFloodState
                            {
                                FloodWaitUntilUtc = fileModel.FloodWaitUntilUtc,
                                InitialSeconds = fileModel.InitialSeconds,
                                IsUploadStopped = fileModel.IsUploadStopped,
                                IsDownload = fileModel.IsDownload,
                                FileName = fileModel.FileName,
                                Scope = fileModel.Scope
                            }
                        };
                    }
                    else
                    {
                        DeletePersistedFloodStateNoLock();
                        return;
                    }

                    var now = DateTime.UtcNow;
                    int restoredCount = 0;
                    bool hadExpiredEntries = false;

                    foreach (var state in rawEntries)
                    {
                        if (state == null || state.InitialSeconds <= 0 || state.FloodWaitUntilUtc == DateTime.MinValue)
                        {
                            hadExpiredEntries = true;
                            continue;
                        }

                        DateTime untilUtc = state.FloodWaitUntilUtc.Kind == DateTimeKind.Unspecified
                            ? DateTime.SpecifyKind(state.FloodWaitUntilUtc, DateTimeKind.Utc)
                            : state.FloodWaitUntilUtc.ToUniversalTime();

                        if (untilUtc <= now)
                        {
                            hadExpiredEntries = true;
                            continue;
                        }

                        FloodWaitScope resolvedScope = state.Scope ?? (state.IsUploadStopped
                            ? FloodWaitScope.Upload
                            : (state.IsDownload ? FloodWaitScope.Download : FloodWaitScope.General));

                        int remainingSec = Math.Max(1, (int)Math.Ceiling((untilUtc - now).TotalSeconds));
                        int initialSec = Math.Max(remainingSec, state.InitialSeconds);
                        bool effectiveDownload = resolvedScope == FloodWaitScope.Download && !state.IsUploadStopped;

                        lock (_uploadPacingLock)
                        {
                            if (!_activeFloodBanners.TryGetValue(resolvedScope, out var existing) || untilUtc > existing.FloodWaitUntilUtc)
                            {
                                _activeFloodBanners[resolvedScope] = new ActiveFloodBannerState
                                {
                                    Scope = resolvedScope,
                                    FloodWaitUntilUtc = untilUtc,
                                    InitialSeconds = existing != null ? Math.Max(existing.InitialSeconds, initialSec) : initialSec,
                                    IsUploadStopped = (existing?.IsUploadStopped == true) || state.IsUploadStopped,
                                    IsDownload = effectiveDownload,
                                    FileName = !string.IsNullOrWhiteSpace(state.FileName) ? state.FileName : existing?.FileName
                                };
                            }

                            if (resolvedScope == FloodWaitScope.Upload)
                            {
                                if (untilUtc > _uploadActiveFloodUntilUtc)
                                {
                                    _uploadActiveFloodUntilUtc = untilUtc;
                                }
                                if (state.IsUploadStopped && untilUtc > _uploadHardStopUntilUtc)
                                {
                                    _uploadHardStopUntilUtc = untilUtc;
                                }
                            }
                        }

                        switch (resolvedScope)
                        {
                            case FloodWaitScope.Download:
                                if (untilUtc > _downloadFloodWaitUntilUtc)
                                {
                                    _downloadFloodWaitUntilUtc = untilUtc;
                                }
                                _globalPacingLock.Wait();
                                try
                                {
                                    if (untilUtc > _nextAllowedSendUtc)
                                    {
                                        _nextAllowedSendUtc = untilUtc;
                                    }
                                }
                                finally
                                {
                                    _globalPacingLock.Release();
                                }
                                break;

                            case FloodWaitScope.Caption:
                                if (untilUtc > _captionFloodWaitUntilUtc)
                                {
                                    _captionFloodWaitUntilUtc = untilUtc;
                                }
                                _captionPacingLock.Wait();
                                try
                                {
                                    if (untilUtc > _nextCaptionAllowedSendUtc)
                                    {
                                        _nextCaptionAllowedSendUtc = untilUtc;
                                    }
                                }
                                finally
                                {
                                    _captionPacingLock.Release();
                                }
                                break;

                            case FloodWaitScope.Background:
                                if (untilUtc > _backgroundFloodWaitUntilUtc)
                                {
                                    _backgroundFloodWaitUntilUtc = untilUtc;
                                }
                                _backgroundPacingLock.Wait();
                                try
                                {
                                    if (untilUtc > _nextBackgroundAllowedSendUtc)
                                    {
                                        _nextBackgroundAllowedSendUtc = untilUtc;
                                    }
                                }
                                finally
                                {
                                    _backgroundPacingLock.Release();
                                }
                                break;

                            case FloodWaitScope.General:
                                if (untilUtc > _sessionFloodWaitUntilUtc)
                                {
                                    _sessionFloodWaitUntilUtc = untilUtc;
                                }
                                break;
                        }

                        restoredCount++;
                        AppLogger.Warn("TelegramService", $"[Flood Persist] Восстановлен активный FLOOD_WAIT ({resolvedScope}) из {FloodStateFileName}: осталось {remainingSec} из {initialSec} сек (до {untilUtc.ToLocalTime():HH:mm:ss}, stop={state.IsUploadStopped}, download={effectiveDownload}).");
                    }

                    if (restoredCount == 0)
                    {
                        DeletePersistedFloodStateNoLock();
                        AppLogger.Info("TelegramService", "[Flood Persist] Таймеры сохранённых FLOOD_WAIT уже истекли — файл flood_state.json очищен.");
                    }
                    else if (hadExpiredEntries)
                    {
                        List<PersistedFloodState> cleaned;
                        lock (_uploadPacingLock)
                        {
                            cleaned = CollectPersistableEntriesLocked(now);
                        }
                        if (cleaned.Count == 0)
                        {
                            DeletePersistedFloodStateNoLock();
                        }
                        else
                        {
                            var updatedModel = new PersistedFloodStateFile { Entries = cleaned };
                            var options = new JsonSerializerOptions { WriteIndented = true };
                            File.WriteAllText(FloodStateFilePath, JsonSerializer.Serialize(updatedModel, options));
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("TelegramService", $"[Flood Persist] Ошибка чтения {FloodStateFileName} ({ex.Message}) — файл сброшен.");
                    DeletePersistedFloodStateNoLock();
                }
            }
        }

        private static void RecordFloodStateAndPersist(int waitSeconds, bool isUploadStopped, string? fileName, bool isDownload, FloodWaitScope scope = FloodWaitScope.General)
        {
            if (waitSeconds <= 0) return;

            bool shouldWriteFile = false;
            lock (_uploadPacingLock)
            {
                var now = DateTime.UtcNow;
                var candidateUntil = now.AddSeconds(waitSeconds);
                bool hasExisting = _activeFloodBanners.TryGetValue(scope, out var existing) && now < existing.FloodWaitUntilUtc;

                bool effectiveIsDownload = (scope == FloodWaitScope.Download || isDownload) &&
                                           !isUploadStopped &&
                                           scope != FloodWaitScope.Caption &&
                                           scope != FloodWaitScope.Background;

                if (!hasExisting || existing == null)
                {
                    var created = new ActiveFloodBannerState
                    {
                        Scope = scope,
                        FloodWaitUntilUtc = candidateUntil,
                        InitialSeconds = waitSeconds,
                        IsUploadStopped = isUploadStopped,
                        IsDownload = effectiveIsDownload,
                        FileName = !string.IsNullOrWhiteSpace(fileName) ? fileName : null
                    };
                    _activeFloodBanners[scope] = created;
                    if (created.InitialSeconds > FloodPersistThresholdSeconds)
                    {
                        shouldWriteFile = true;
                    }
                }
                else
                {
                    bool upgradedToStop = isUploadStopped && !existing.IsUploadStopped;
                    bool extendsSignificantly = upgradedToStop || candidateUntil > existing.FloodWaitUntilUtc.AddSeconds(2);
                    if (extendsSignificantly)
                    {
                        if (candidateUntil > existing.FloodWaitUntilUtc)
                        {
                            existing.FloodWaitUntilUtc = candidateUntil;
                        }
                        if (waitSeconds > existing.InitialSeconds)
                        {
                            existing.InitialSeconds = waitSeconds;
                        }
                        existing.IsUploadStopped = existing.IsUploadStopped || isUploadStopped;
                        existing.IsDownload = effectiveIsDownload && !existing.IsUploadStopped;
                        if (!string.IsNullOrWhiteSpace(fileName))
                        {
                            existing.FileName = fileName;
                        }
                        if (existing.InitialSeconds > FloodPersistThresholdSeconds)
                        {
                            shouldWriteFile = true;
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(existing.FileName) && !string.IsNullOrWhiteSpace(fileName))
                    {
                        existing.FileName = fileName;
                        if (existing.InitialSeconds > FloodPersistThresholdSeconds)
                        {
                            shouldWriteFile = true;
                        }
                    }
                }
            }

            if (shouldWriteFile)
            {
                WriteOrDeletePersistedFloodState();
            }
        }

        public void ClearPersistedFloodStateIfExpired()
        {
            bool changed = false;
            var now = DateTime.UtcNow;

            if (_sessionFloodWaitUntilUtc != DateTime.MinValue && now >= _sessionFloodWaitUntilUtc)
                _sessionFloodWaitUntilUtc = DateTime.MinValue;
            if (_downloadFloodWaitUntilUtc != DateTime.MinValue && now >= _downloadFloodWaitUntilUtc)
                _downloadFloodWaitUntilUtc = DateTime.MinValue;
            if (_captionFloodWaitUntilUtc != DateTime.MinValue && now >= _captionFloodWaitUntilUtc)
                _captionFloodWaitUntilUtc = DateTime.MinValue;
            if (_backgroundFloodWaitUntilUtc != DateTime.MinValue && now >= _backgroundFloodWaitUntilUtc)
                _backgroundFloodWaitUntilUtc = DateTime.MinValue;

            lock (_uploadPacingLock)
            {
                if (_uploadHardStopUntilUtc != DateTime.MinValue && now >= _uploadHardStopUntilUtc)
                {
                    _uploadHardStopUntilUtc = DateTime.MinValue;
                }

                if (_activeFloodBanners.Count > 0)
                {
                    var expiredScopes = _activeFloodBanners
                        .Where(kv => now >= kv.Value.FloodWaitUntilUtc)
                        .Select(kv => kv.Key)
                        .ToList();

                    if (expiredScopes.Count > 0)
                    {
                        foreach (var expiredScope in expiredScopes)
                        {
                            _activeFloodBanners.Remove(expiredScope);
                        }
                        changed = true;
                    }
                }
                else
                {
                    changed = File.Exists(FloodStateFilePath);
                }
            }

            if (changed)
            {
                WriteOrDeletePersistedFloodState();
            }
        }

        public List<ActiveFloodBannerState> GetActiveFloodBannerStates()
        {
            bool needCleanup = false;
            var result = new List<ActiveFloodBannerState>();
            lock (_uploadPacingLock)
            {
                var now = DateTime.UtcNow;
                foreach (var entry in _activeFloodBanners.Values)
                {
                    if (entry.FloodWaitUntilUtc > now)
                    {
                        result.Add(new ActiveFloodBannerState
                        {
                            Scope = entry.Scope,
                            FloodWaitUntilUtc = entry.FloodWaitUntilUtc,
                            InitialSeconds = Math.Max(1, entry.InitialSeconds),
                            IsUploadStopped = entry.IsUploadStopped,
                            IsDownload = entry.IsDownload,
                            FileName = entry.FileName
                        });
                    }
                    else
                    {
                        needCleanup = true;
                    }
                }
            }

            if (needCleanup)
            {
                ClearPersistedFloodStateIfExpired();
            }

            return result
                .OrderByDescending(b => GetBannerPriority(b.Scope, b.IsUploadStopped))
                .ThenByDescending(b => b.FloodWaitUntilUtc)
                .ToList();
        }

        public bool TryGetActiveFloodBannerState(
            out int remainingSeconds,
            out int initialSeconds,
            out bool isUploadStopped,
            out string? fileName,
            out bool isDownload,
            out FloodWaitScope scope)
        {
            var active = GetActiveFloodBannerStates();
            if (active.Count > 0)
            {
                var top = active[0];
                remainingSeconds = Math.Max(1, top.RemainingSeconds);
                initialSeconds = Math.Max(remainingSeconds, top.InitialSeconds);
                isUploadStopped = top.IsUploadStopped;
                fileName = top.FileName;
                isDownload = top.IsDownload;
                scope = top.Scope;
                return true;
            }

            remainingSeconds = 0;
            initialSeconds = 0;
            isUploadStopped = false;
            fileName = null;
            isDownload = false;
            scope = FloodWaitScope.General;
            return false;
        }

        private static void NotifyFloodWaitBanner(int waitSeconds, bool isUploadStopped, string? fileName, bool isDownload, FloodWaitScope scope = FloodWaitScope.General)
        {
            var instance = _activeInstance;
            if (instance == null || waitSeconds <= 0) return;

            var now = DateTime.UtcNow;
            lock (_uploadPacingLock)
            {
                if (!isUploadStopped &&
                    _lastFloodBannerNotifyByScopeUtc.TryGetValue(scope, out var lastScopeNotifyUtc) &&
                    (now - lastScopeNotifyUtc).TotalMilliseconds < 800)
                {
                    return;
                }
                _lastFloodBannerNotifyByScopeUtc[scope] = now;
            }

            instance.OnFloodWaitDetected?.Invoke(waitSeconds, isUploadStopped, fileName, isDownload, scope);
        }

        private static bool TryGetRemainingUploadHardStopSeconds(out int remainingSeconds, out DateTime hardStopUntilUtc)
        {
            bool needCleanup = false;
            lock (_uploadPacingLock)
            {
                var now = DateTime.UtcNow;
                if (_uploadHardStopUntilUtc > now)
                {
                    remainingSeconds = Math.Max(1, (int)Math.Ceiling((_uploadHardStopUntilUtc - now).TotalSeconds));
                    hardStopUntilUtc = _uploadHardStopUntilUtc;
                    return true;
                }

                if (_uploadHardStopUntilUtc != DateTime.MinValue)
                {
                    _uploadHardStopUntilUtc = DateTime.MinValue;
                    needCleanup = true;
                }
            }

            if (needCleanup)
            {
                _activeInstance?.ClearPersistedFloodStateIfExpired();
            }

            remainingSeconds = 0;
            hardStopUntilUtc = DateTime.MinValue;
            return false;
        }

        public bool TryGetUploadHardStop(out int remainingSeconds, out DateTime hardStopUntilUtc, string? fileName = null, bool notifyBanner = false)
        {
            if (TryGetRemainingUploadHardStopSeconds(out remainingSeconds, out hardStopUntilUtc))
            {
                if (notifyBanner)
                {
                    NotifyFloodWaitBanner(remainingSeconds, isUploadStopped: true, fileName, isDownload: false, scope: FloodWaitScope.Upload);
                }
                return true;
            }
            return false;
        }

        private static void ThrowIfUploadHardStopped(string fileName)
        {
            if (TryGetRemainingUploadHardStopSeconds(out int remainingSec, out DateTime hardStopUntilUtc))
            {
                AppLogger.Warn("TelegramService", $"[Upload HardStop] Отправка '{fileName}' прекращена без обращения к Telegram: активен FLOOD_WAIT (осталось {remainingSec} сек, до {hardStopUntilUtc.ToLocalTime():HH:mm:ss}).");
                NotifyFloodWaitBanner(remainingSec, isUploadStopped: true, fileName, isDownload: false, scope: FloodWaitScope.Upload);
                throw new InvalidOperationException($"Отправка остановлена из-за FLOOD_WAIT Telegram (осталось {remainingSec} сек, до {hardStopUntilUtc.ToLocalTime():HH:mm:ss}).");
            }
        }

        private static void ResetUploadPacingIfIdle()
        {
            int previousDelay = 0;
            double idleMinutes = 0;
            bool reset = false;

            lock (_uploadPacingLock)
            {
                if (_uploadPacingDelayMs > 0 && _lastUploadActivityFinishedUtc != DateTime.MinValue)
                {
                    idleMinutes = (DateTime.UtcNow - _lastUploadActivityFinishedUtc).TotalMinutes;
                    if (idleMinutes >= UploadPacingIdleResetMinutes)
                    {
                        previousDelay = _uploadPacingDelayMs;
                        _uploadPacingDelayMs = 0;
                        _uploadCleanSendsSinceLastChange = 0;
                        _uploadDecayIntervalSeconds = UploadPacingBaseDecayIntervalSeconds;
                        _uploadLastPacingChangeUtc = DateTime.MinValue;
                        _uploadLastDecayAttemptUtc = DateTime.MinValue;
                        reset = true;
                    }
                }
            }

            if (reset)
            {
                AppLogger.Info("TelegramService", $"[Upload Pacing] Простой {idleMinutes:F1} мин (>= {UploadPacingIdleResetMinutes} мин) без отправки файлов: адаптивная пауза сброшена {previousDelay} -> 0 мс.");
            }
        }

        private static void MarkUploadActivityFinished()
        {
            lock (_uploadPacingLock)
            {
                _lastUploadActivityFinishedUtc = DateTime.UtcNow;
            }
        }

        private static async Task EnsureUploadMessagePacingAsync(CancellationToken cancellationToken = default)
        {
            int delayMs;
            lock (_uploadPacingLock)
            {
                delayMs = _uploadPacingDelayMs;
            }

            if (delayMs > 0)
            {
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        private static void RegisterUploadFloodWait(int waitSeconds, string source, string? fileName = null)
        {
            int previousDelay;
            int updatedDelay;
            int previousInterval;
            int updatedInterval;
            bool wasDeduplicated;
            bool touchedBoundary = false;
            bool triggeredHardStop = false;
            DateTime hardStopUntilLocal = DateTime.MinValue;

            lock (_uploadPacingLock)
            {
                var now = DateTime.UtcNow;
                _uploadCleanSendsSinceLastChange = 0;
                previousInterval = _uploadDecayIntervalSeconds;
                updatedInterval = _uploadDecayIntervalSeconds;

                var candidateFloodUntil = now.AddSeconds(Math.Max(1, waitSeconds));
                bool isSameActiveFlood = now < _uploadActiveFloodUntilUtc &&
                                         candidateFloodUntil <= _uploadActiveFloodUntilUtc.AddSeconds(2);

                if (candidateFloodUntil > _uploadActiveFloodUntilUtc)
                {
                    _uploadActiveFloodUntilUtc = candidateFloodUntil;
                }

                if (waitSeconds > UploadHardStopThresholdSeconds)
                {
                    if (candidateFloodUntil > _uploadHardStopUntilUtc)
                    {
                        _uploadHardStopUntilUtc = candidateFloodUntil;
                    }
                    triggeredHardStop = true;
                    hardStopUntilLocal = _uploadHardStopUntilUtc.ToLocalTime();
                }

                if (isSameActiveFlood || (now - _lastUploadFloodDetectedUtc).TotalMilliseconds < UploadFloodDedupWindowMs)
                {
                    wasDeduplicated = true;
                    previousDelay = _uploadPacingDelayMs;
                    updatedDelay = _uploadPacingDelayMs;
                }
                else
                {
                    wasDeduplicated = false;
                    _lastUploadFloodDetectedUtc = now;
                    _uploadLastPacingChangeUtc = now;
                    previousDelay = _uploadPacingDelayMs;
                    updatedDelay = previousDelay == 0
                        ? UploadPacingInitialFloodDelayMs
                        : Math.Min(UploadPacingMaxDelayMs, previousDelay + UploadPacingStepMs);
                    _uploadPacingDelayMs = updatedDelay;

                    // Анти-пила: если флуд вернулся в пределах окна после недавнего снижения — удваиваем интервал отката (90 -> 180 -> 300 сек)
                    if (_uploadLastDecayAttemptUtc != DateTime.MinValue &&
                        (now - _uploadLastDecayAttemptUtc).TotalSeconds <= _uploadDecayIntervalSeconds)
                    {
                        updatedInterval = Math.Min(UploadPacingMaxDecayIntervalSeconds, _uploadDecayIntervalSeconds * 2);
                        _uploadDecayIntervalSeconds = updatedInterval;
                        _uploadLastDecayAttemptUtc = DateTime.MinValue;
                        touchedBoundary = updatedInterval != previousInterval;
                    }
                }
            }

            if (!wasDeduplicated)
            {
                string boundaryNote = touchedBoundary
                    ? $" Интервал снижения увеличен: {previousInterval} -> {updatedInterval} сек."
                    : string.Empty;
                string hardStopNote = triggeredHardStop
                    ? $" Отправка приостановлена до {hardStopUntilLocal:HH:mm:ss}."
                    : string.Empty;
                AppLogger.Warn("TelegramService", $"[Upload Pacing] Зафиксирован FLOOD_WAIT_{waitSeconds} ({waitSeconds} сек, источник: {source}). Адаптивная пауза отправки увеличена: {previousDelay} -> {updatedDelay} мс.{boundaryNote}{hardStopNote}");
            }

            bool effectiveUploadStopped = waitSeconds > UploadHardStopThresholdSeconds || TryGetRemainingUploadHardStopSeconds(out _, out _);
            string? effectiveBannerFile = fileName ?? _activeUploadFileName;
            RecordFloodStateAndPersist(waitSeconds, effectiveUploadStopped, effectiveBannerFile, isDownload: false, scope: FloodWaitScope.Upload);
            NotifyFloodWaitBanner(waitSeconds, isUploadStopped: effectiveUploadStopped, effectiveBannerFile, isDownload: false, scope: FloodWaitScope.Upload);
        }

        private static void NotifyUploadMessageSucceeded()
        {
            int previousDelay = 0;
            int updatedDelay = 0;
            int currentInterval = 0;
            double cleanElapsedSeconds = 0;
            bool decayed = false;

            lock (_uploadPacingLock)
            {
                if (_uploadPacingDelayMs <= 0)
                {
                    _uploadCleanSendsSinceLastChange = 0;
                    _uploadLastPacingChangeUtc = DateTime.MinValue;
                    _uploadLastDecayAttemptUtc = DateTime.MinValue;
                    _uploadDecayIntervalSeconds = UploadPacingBaseDecayIntervalSeconds;
                    return;
                }

                var now = DateTime.UtcNow;
                _uploadCleanSendsSinceLastChange++;

                if (_uploadLastPacingChangeUtc == DateTime.MinValue)
                {
                    _uploadLastPacingChangeUtc = now;
                    return;
                }

                cleanElapsedSeconds = (now - _uploadLastPacingChangeUtc).TotalSeconds;
                if (cleanElapsedSeconds >= _uploadDecayIntervalSeconds && _uploadCleanSendsSinceLastChange >= 1)
                {
                    previousDelay = _uploadPacingDelayMs;
                    updatedDelay = Math.Max(0, _uploadPacingDelayMs - UploadPacingDecayStepMs);
                    currentInterval = _uploadDecayIntervalSeconds;
                    _uploadPacingDelayMs = updatedDelay;
                    _uploadLastPacingChangeUtc = now;
                    _uploadCleanSendsSinceLastChange = 0;

                    if (updatedDelay == 0)
                    {
                        _uploadLastDecayAttemptUtc = DateTime.MinValue;
                        _uploadDecayIntervalSeconds = UploadPacingBaseDecayIntervalSeconds;
                    }
                    else
                    {
                        _uploadLastDecayAttemptUtc = now;
                    }

                    decayed = updatedDelay != previousDelay;
                }
            }

            if (decayed)
            {
                AppLogger.Info("TelegramService", $"[Upload Pacing] {cleanElapsedSeconds:F0} сек чистой отправки (порог {currentInterval} сек) без FLOOD_WAIT: адаптивная пауза снижена {previousDelay} -> {updatedDelay} мс.");
            }
        }


        /// <summary>
        /// Потоковая прямая загрузка файла в приватный канал-хранилище через WTelegramClient.
        /// Обеспечивает TCP Flow Control (обратное давление) для синхронизации шкалы прогресса в Проводнике Windows.
        /// Для аудио- и видеофайлов автоматически формирует InputMediaUploadedDocument с атрибутами стриминга (DocumentAttributeAudio / DocumentAttributeVideo) и обложкой (thumb).
        /// Возвращает реальный ID сообщения из Telegram, либо null если файл пустой.
        /// </summary>
        public async Task<FileUploadResult?> UploadFileAsync(
            Stream source, 
            string fileName, 
            long length = -1, 
            string? displayFileName = null, 
            string? caption = null,
            AudioMetadataResult? audioMeta = null,
            VideoMetadataResult? videoMeta = null,
            string? originalFilePath = null,
            long? targetChannelId = null,
            bool createGalleryPreview = false,
            bool enableCaptions = true,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _pendingUploadsCount);
            bool uploadSlotAcquired = false;
            bool didAttemptNetworkUpload = false;
            string effectiveFileName = !string.IsNullOrEmpty(displayFileName) ? displayFileName : fileName;
            string rawCaptionPath = !string.IsNullOrEmpty(caption) ? caption : effectiveFileName;
            string effectiveCaption = !enableCaptions
                ? string.Empty
                : (rawCaptionPath.StartsWith("🟢") || rawCaptionPath.StartsWith("🗑️")
                    ? rawCaptionPath
                    : NodeRepository.FormatTelegramCaption(rawCaptionPath, null, isLatest: true));

            Stream uploadStream = source;
            string? tempFilePath = null;

            try
            {
                if (length < 0 || length > 1)
                {
                    ThrowIfUploadHardStopped(effectiveFileName);
                }

                await _uploadSemaphore.WaitAsync(cancellationToken);
                uploadSlotAcquired = true;
                _activeUploadFileName = effectiveFileName;
                Interlocked.Increment(ref _activeUploadPipelineCount);
                ResetUploadPacingIfIdle();

                cancellationToken.ThrowIfCancellationRequested();
                if (length < 0 || length > 1)
                {
                    ThrowIfUploadHardStopped(effectiveFileName);
                }
                await EnsureFloodWaitDelayAsync();

                if (_client == null || !IsAuthorized)
                    throw new InvalidOperationException("Клиент Telegram не подключен или не авторизован.");

                var peer = await GetStoragePeerAsync(targetChannelId);
                bool isGallery = createGalleryPreview && IsGalleryImage(effectiveFileName);

                // Если поток не поддерживает Seek (входящий сетевой поток WebDAV от Проводника)
                // или если это уже StreamingUploadStream (переданный после извлечения аудио-тегов)
                if (source is StreamingUploadStream existingStreaming)
                {
                    // Уже сквозной поток, регистрируем прогресс для TelegramService
                    uploadStream = source;
                }
                else if (!source.CanSeek)
                {
                    long actualLength = length > 0 ? length : GetStreamLengthSafe(source);

                    if (actualLength > 0 && !isGallery)
                    {
                        // Прямой сквозной стриминг с поддержкой обратного давления TCP (только если не картинка для галереи)
                        uploadStream = new StreamingUploadStream(
                            source,
                            actualLength,
                            prefixBuffer: null,
                            onProgress: null
                        );
                    }
                    else if (actualLength == 0)
                    {
                        // Пустой файл
                        uploadStream = new MemoryStream();
                    }
                    else
                    {
                        // Буферизация во временный файл: для потоков неизвестного размера, а также для картинок галереи (чтобы создать превью и отправить оригинал)
                        string subDir = Path.Combine(Path.GetTempPath(), "TGramFS_Buffer", Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(subDir);
                        tempFilePath = Path.Combine(subDir, effectiveFileName);
                        
                        AppLogger.Info("TelegramService", $"Буферизация изображения во временный файл: {tempFilePath}");
                        using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await source.CopyToAsync(fs, cancellationToken);
                        }
                        
                        uploadStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    }
                }

                // Предотвращаем отправку файлов размером <= 1 байт в Telegram (защита от FILE_PART_0_MISSING и probe-запросов Total Commander / Проводника)
                if (uploadStream.Length <= 1)
                {
                    AppLogger.Info("TelegramService", $"Файл '{effectiveFileName}' пустой или является probe-запросом клиента ({uploadStream.Length} байт). Регистрация в БД без загрузки в Telegram.");
                    return null;
                }

                ThrowIfUploadHardStopped(effectiveFileName);
                didAttemptNetworkUpload = true;
                long fileLength = uploadStream.Length;
                byte[]? galleryPhotoBytes = null;
                if (isGallery && fileLength > 1)
                {
                    if (!string.IsNullOrEmpty(originalFilePath) && File.Exists(originalFilePath))
                    {
                        galleryPhotoBytes = CreateOptimizedGalleryThumbnailFromFile(originalFilePath);
                    }
                    else if (uploadStream is not StreamingPipeStream && uploadStream is not StreamingUploadStream && uploadStream.CanSeek)
                    {
                        galleryPhotoBytes = CreateOptimizedGalleryThumbnail(uploadStream);
                        uploadStream.Seek(0, SeekOrigin.Begin);
                    }
                }

                long maxPartSize = GetMaxUploadPartSize();
                if (fileLength > maxPartSize && !isGallery)
                {
                    return await UploadMultiPartFileAsync(
                        uploadStream,
                        effectiveFileName,
                        fileLength,
                        maxPartSize,
                        peer,
                        targetChannelId,
                        effectiveCaption,
                        rawCaptionPath,
                        audioMeta,
                        videoMeta,
                        enableCaptions,
                        cancellationToken
                    );
                }

                AppLogger.Info("TelegramService", $"Прямая потоковая передача файла '{effectiveFileName}' ({fileLength} байт) в Telegram...");
                
                // Передаем прогресс-колбэк также в WTelegramClient для детального трекинга MTProto частей
                var inputFile = await _client.UploadFileAsync(
                    uploadStream, 
                    effectiveFileName, 
                    progress: (pos, total) => OnUploadProgress?.Invoke(effectiveFileName, pos, total)
                );

                AppLogger.Info("TelegramService", $"Файл '{effectiveFileName}' загружен в MTProto, финализация сообщения в канале" + (enableCaptions ? $" (подпись: '{effectiveCaption}')" : " (без подписи)") + "...");
                
                bool isAudio = AudioMetadataExtractor.IsAudioFile(effectiveFileName);
                bool isVideo = VideoMetadataExtractor.IsVideoFile(effectiveFileName);
                TL.Message? message = null;

                if (isAudio)
                {
                    string ext = Path.GetExtension(effectiveFileName).ToLowerInvariant();
                    string mimeType = ext switch
                    {
                        ".mp3" => "audio/mpeg",
                        ".flac" => "audio/flac",
                        ".m4a" => "audio/mp4",
                        ".ogg" => "audio/ogg",
                        ".wav" => "audio/x-wav",
                        ".aac" => "audio/aac",
                        ".opus" => "audio/opus",
                        ".wma" => "audio/x-ms-wma",
                        _ => "audio/mpeg"
                    };

                    string title = !string.IsNullOrWhiteSpace(audioMeta?.Title)
                        ? audioMeta.Title
                        : Path.GetFileNameWithoutExtension(effectiveFileName);
                    string? performer = !string.IsNullOrWhiteSpace(audioMeta?.Artist)
                        ? audioMeta.Artist
                        : null;
                    int duration = audioMeta?.DurationSeconds ?? 0;

                    var audioFlags = TL.DocumentAttributeAudio.Flags.has_title;
                    if (!string.IsNullOrEmpty(performer))
                    {
                        audioFlags |= TL.DocumentAttributeAudio.Flags.has_performer;
                    }

                    var audioAttr = new TL.DocumentAttributeAudio
                    {
                        duration = duration > 0 ? duration : 0,
                        title = title,
                        performer = performer ?? string.Empty,
                        flags = audioFlags
                    };

                    var fileNameAttr = new TL.DocumentAttributeFilename
                    {
                        file_name = effectiveFileName
                    };

                    var attributes = new TL.DocumentAttribute[] { fileNameAttr, audioAttr };

                    TL.InputFileBase? thumbFile = null;
                    if (audioMeta?.AlbumCover != null && audioMeta.AlbumCover.Length > 0)
                    {
                        try
                        {
                            using var thumbMs = new MemoryStream(audioMeta.AlbumCover, false);
                            thumbFile = await _client.UploadFileAsync(thumbMs, "thumb.jpg");
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("TelegramService", $"Не удалось загрузить обложку альбома для '{effectiveFileName}': {ex.Message}");
                        }
                    }

                    var mediaDoc = new TL.InputMediaUploadedDocument(inputFile, mimeType, attributes);
                    if (thumbFile != null)
                    {
                        mediaDoc.thumb = thumbFile;
                        mediaDoc.flags |= TL.InputMediaUploadedDocument.Flags.has_thumb;
                    }

                    (message, peer) = await SendUploadMediaMessageWithRetryAsync(
                        peer,
                        targetChannelId,
                        effectiveCaption,
                        mediaDoc,
                        effectiveFileName,
                        cancellationToken: cancellationToken);
                }
                else if (isVideo)
                {
                    string mimeType = VideoMetadataExtractor.GetVideoMimeType(effectiveFileName);
                    int duration = videoMeta?.DurationSeconds ?? 0;
                    int width = (videoMeta != null && videoMeta.Width > 0) ? videoMeta.Width : 1280;
                    int height = (videoMeta != null && videoMeta.Height > 0) ? videoMeta.Height : 720;

                    var videoFlags = TL.DocumentAttributeVideo.Flags.supports_streaming;
                    var videoAttr = new TL.DocumentAttributeVideo
                    {
                        duration = duration,
                        w = width,
                        h = height,
                        flags = videoFlags
                    };

                    var fileNameAttr = new TL.DocumentAttributeFilename
                    {
                        file_name = effectiveFileName
                    };

                    var attributes = new TL.DocumentAttribute[] { fileNameAttr, videoAttr };

                    TL.InputFileBase? thumbFile = null;
                    if (videoMeta?.Thumbnail != null && videoMeta.Thumbnail.Length > 0)
                    {
                        try
                        {
                            using var thumbMs = new MemoryStream(videoMeta.Thumbnail, false);
                            thumbFile = await _client.UploadFileAsync(thumbMs, "thumb.jpg");
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("TelegramService", $"Не удалось загрузить обложку видео для '{effectiveFileName}': {ex.Message}");
                        }
                    }

                    var mediaDoc = new TL.InputMediaUploadedDocument(inputFile, mimeType, attributes);
                    if (thumbFile != null)
                    {
                        mediaDoc.thumb = thumbFile;
                        mediaDoc.flags |= TL.InputMediaUploadedDocument.Flags.has_thumb;
                    }

                    (message, peer) = await SendUploadMediaMessageWithRetryAsync(
                        peer,
                        targetChannelId,
                        effectiveCaption,
                        mediaDoc,
                        effectiveFileName,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    TL.Message? photoMessage = null;
                    string previewFileName = $"{Path.GetFileNameWithoutExtension(effectiveFileName)}_preview.jpg";
                    if (galleryPhotoBytes != null && galleryPhotoBytes.Length > 0)
                    {
                        try
                        {
                            using var photoMs = new MemoryStream(galleryPhotoBytes, false);
                            var photoInput = await _client.UploadFileAsync(photoMs, previewFileName);
                            var photoMedia = new TL.InputMediaUploadedPhoto { file = photoInput };
                            (photoMessage, peer) = await SendUploadMediaMessageWithRetryAsync(
                                peer,
                                targetChannelId,
                                effectiveCaption,
                                photoMedia,
                                previewFileName,
                                cancellationToken: cancellationToken);
                            AppLogger.Info("TelegramService", $"Фото-превью для галереи успешно опубликовано в Telegram (Message ID: {photoMessage.ID}, имя: '{previewFileName}').");
                        }
                        catch (TL.RpcException rpcEx) when (rpcEx.Code == 420)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("TelegramService", $"Не удалось отправить фото-превью для галереи: {ex.Message}");
                        }
                    }

                    string mimeType = GetDocumentMimeType(effectiveFileName);
                    var fileNameAttr = new TL.DocumentAttributeFilename
                    {
                        file_name = effectiveFileName
                    };
                    var attributes = new TL.DocumentAttribute[] { fileNameAttr };
                    var mediaDoc = new TL.InputMediaUploadedDocument(inputFile, mimeType, attributes);

                    // Если размер файла больше 10 МБ, Telegram не создает серверное превью для документов - прикрепляем локально созданный thumb
                    if (galleryPhotoBytes != null && galleryPhotoBytes.Length > 0 && fileLength > 10 * 1024 * 1024)
                    {
                        try
                        {
                            using var thumbMs = new MemoryStream(galleryPhotoBytes, false);
                            var thumbFile = await _client.UploadFileAsync(thumbMs, previewFileName);
                            if (thumbFile != null)
                            {
                                mediaDoc.thumb = thumbFile;
                                mediaDoc.flags |= TL.InputMediaUploadedDocument.Flags.has_thumb;
                                AppLogger.Info("TelegramService", $"К документу >10 МБ '{effectiveFileName}' успешно прикреплена миниатюра (thumb).");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Debug("TelegramService", $"Не удалось прикрепить thumb к документу >10 МБ: {ex.Message}");
                        }
                    }

                    int replyToId = photoMessage != null ? photoMessage.ID : 0;
                    string docCaption = photoMessage != null ? string.Empty : effectiveCaption;

                    (message, peer) = await SendUploadMediaMessageWithRetryAsync(
                        peer,
                        targetChannelId,
                        docCaption,
                        mediaDoc,
                        effectiveFileName,
                        replyToMsgId: replyToId,
                        cancellationToken: cancellationToken);

                    if (message != null)
                    {
                        long? resolvedChannelId = targetChannelId ?? (peer is TL.InputPeerChannel ipc ? ipc.channel_id : (long?)null);
                        if (resolvedChannelId <= 0) resolvedChannelId = null;

                        List<PendingCaptionUpdate>? pendingCaptions = null;
                        if (enableCaptions && message.ID > 0)
                        {
                            string finalizedCaption = NodeRepository.FormatTelegramCaption(rawCaptionPath, message.ID, isLatest: true);
                            pendingCaptions = new List<PendingCaptionUpdate>
                            {
                                new PendingCaptionUpdate(message.ID, finalizedCaption, resolvedChannelId)
                            };
                        }

                        NotifyUploadMessageSucceeded();
                        AppLogger.Info("TelegramService", $"Файл '{effectiveFileName}' успешно сохранен в Telegram. Message ID: {message.ID}" + (photoMessage != null ? $", Preview ID: {photoMessage.ID}" : ""));
                        OnUploadCompleted?.Invoke(effectiveFileName);
                        return new FileUploadResult(message.ID, photoMessage?.ID, pendingCaptions: pendingCaptions);
                    }
                }

                if (message != null)
                {
                    long? resolvedChannelId = targetChannelId ?? (peer is TL.InputPeerChannel ipc ? ipc.channel_id : (long?)null);
                    if (resolvedChannelId <= 0) resolvedChannelId = null;

                    List<PendingCaptionUpdate>? pendingCaptions = null;
                    if (enableCaptions && message.ID > 0)
                    {
                        string finalizedCaption = NodeRepository.FormatTelegramCaption(rawCaptionPath, message.ID, isLatest: true);
                        pendingCaptions = new List<PendingCaptionUpdate>
                        {
                            new PendingCaptionUpdate(message.ID, finalizedCaption, resolvedChannelId)
                        };
                    }

                    NotifyUploadMessageSucceeded();
                    AppLogger.Info("TelegramService", $"Файл '{effectiveFileName}' успешно сохранен в Telegram. Message ID: {message.ID}");
                    OnUploadCompleted?.Invoke(effectiveFileName);
                    return new FileUploadResult(message.ID, pendingCaptions: pendingCaptions);
                }

                NotifyUploadMessageSucceeded();
                AppLogger.Warn("TelegramService", "Сообщение отправлено, но ID не определен, возвращаем 1.");
                OnUploadCompleted?.Invoke(effectiveFileName);
                return new FileUploadResult(1);
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 420)
            {
                RegisterUploadFloodWait(rpcEx.X, $"отправка '{effectiveFileName}'", effectiveFileName);
                AppLogger.Error("TelegramService", $"Получен FLOOD_WAIT_{rpcEx.X} ({rpcEx.X} сек) при отправке файла '{effectiveFileName}' в Telegram.", rpcEx);
                throw;
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException || cancellationToken.IsCancellationRequested || (ex is IOException ioEx && ioEx.Message.Contains("отменен")))
            {
                AppLogger.Info("TelegramService", $"Загрузка файла '{effectiveFileName}' в Telegram отменена пользователем.");
                TriggerUploadCanceled(effectiveFileName);
                return null;
            }
            finally
            {
                if (tempFilePath != null)
                {
                    try { uploadStream.Dispose(); } catch { }
                    try 
                    { 
                        if (File.Exists(tempFilePath)) File.Delete(tempFilePath); 
                        string? parentDir = Path.GetDirectoryName(tempFilePath);
                        if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                        {
                            Directory.Delete(parentDir, true);
                        }
                    } 
                    catch { }
                }
                else if (uploadStream != source)
                {
                    try { uploadStream.Dispose(); } catch { }
                }

                if (didAttemptNetworkUpload)
                {
                    MarkUploadActivityFinished();
                }
                if (uploadSlotAcquired)
                {
                    Interlocked.Decrement(ref _activeUploadPipelineCount);
                    _activeUploadFileName = null;
                    _uploadSemaphore.Release();
                }
                Interlocked.Decrement(ref _pendingUploadsCount);
            }
        }

        /// <summary>
        /// Возвращает максимальный размер одной части файла в байтах.
        /// Если в настройках задан CustomMaxUploadPartSizeBytes, используется он.
        /// Иначе: 4000 МБ для Telegram Premium, 2000 МБ для обычных аккаунтов.
        /// </summary>
        public long GetMaxUploadPartSize()
        {
            if (_configManager?.CurrentSettings?.Server?.CustomMaxUploadPartSizeBytes is long customSize && customSize > 0)
            {
                return customSize;
            }
            bool isPremium = CurrentUser?.IsPremium ?? false;
            return (isPremium ? 4000L : 2000L) * 1048576L;
        }

        private async Task<(TL.Message Message, TL.InputPeer Peer)> SendUploadMediaMessageWithRetryAsync(
            TL.InputPeer peer,
            long? targetChannelId,
            string caption,
            TL.InputMedia media,
            string logFileName,
            int replyToMsgId = 0,
            CancellationToken cancellationToken = default)
        {
            const int maxServerRetries = 2;
            TL.InputPeer currentPeer = peer;
            bool peerRefreshed = false;

            for (int serverRetry = 0; ; )
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EnsureUploadMessagePacingAsync(cancellationToken);
                try
                {
                    BeginUploadMessageRpc();
                    try
                    {
                        TL.Message sent = replyToMsgId > 0
                            ? await _client!.SendMessageAsync(currentPeer, caption, media, reply_to_msg_id: replyToMsgId)
                            : await _client!.SendMessageAsync(currentPeer, caption, media);
                        return (sent, currentPeer);
                    }
                    finally
                    {
                        EndUploadMessageRpc();
                    }
                }
                catch (TL.RpcException rpcEx) when (!peerRefreshed && rpcEx.Code == 400 && (rpcEx.Message.Contains("CHANNEL_INVALID") || rpcEx.Message.Contains("CHANNEL_PRIVATE")))
                {
                    peerRefreshed = true;
                    AppLogger.Warn("TelegramService", "Канал недоступен по сохраненному хэшу. Сброс хэша и повторный поиск...");
                    InvalidateStoragePeer();
                    currentPeer = await GetStoragePeerAsync(targetChannelId);
                }
                catch (TL.RpcException rpcEx) when (rpcEx.Code == 500 && serverRetry < maxServerRetries)
                {
                    serverRetry++;
                    int backoffMs = serverRetry == 1 ? 500 : 1000;
                    AppLogger.Warn("TelegramService", $"[Upload Retry] Сервер Telegram вернул {rpcEx.Code} ({rpcEx.Message}) при отправке '{logFileName}'. Повторная попытка {serverRetry}/{maxServerRetries} через {backoffMs} мс...");
                    await Task.Delay(backoffMs, cancellationToken);
                }
            }
        }

        private async Task<FileUploadResult?> UploadMultiPartFileAsync(
            Stream uploadStream,
            string effectiveFileName,
            long fileLength,
            long maxPartSize,
            TL.InputPeer peer,
            long? targetChannelId,
            string effectiveCaption,
            string rawCaptionPath,
            AudioMetadataResult? audioMeta,
            VideoMetadataResult? videoMeta,
            bool enableCaptions = true,
            CancellationToken cancellationToken = default)
        {
            int partCount = (int)Math.Ceiling((double)fileLength / maxPartSize);
            AppLogger.Info("TelegramService", $"[BigFile Chunking] Запуск нарезки и загрузки крупного файла '{effectiveFileName}' ({fileLength:N0} байт / {fileLength / (1024.0 * 1024.0 * 1024.0):F2} ГБ). Всего частей: {partCount}, лимит части: {maxPartSize / (1024.0 * 1024.0):F0} МБ.");

            var parts = new List<Models.NodePart>();
            var pendingCaptions = new List<PendingCaptionUpdate>();
            int masterMessageId = 0;
            long channelId = targetChannelId ?? (peer is TL.InputPeerChannel ipc ? ipc.channel_id : 0);

            try
            {
                for (int i = 0; i < partCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfUploadHardStopped(effectiveFileName);
                    await EnsureFloodWaitDelayAsync();

                    long currentPartLength = (i == partCount - 1)
                        ? (fileLength - ((long)i * maxPartSize))
                        : maxPartSize;

                    long partStartGlobal = (long)i * maxPartSize;
                    int partDisplayNum = i + 1;

                    string partFileName = (i == 0)
                        ? effectiveFileName
                        : $"{effectiveFileName}.{partDisplayNum:D3}";

                    AppLogger.Info("TelegramService", $"[BigFile Chunking] Загрузка части {partDisplayNum}/{partCount} ('{partFileName}', {currentPartLength:N0} байт, смещение {partStartGlobal:N0})...");

                    using var boundedStream = new BoundedStream(
                        uploadStream,
                        currentPartLength
                    );

                    var inputFile = await _client!.UploadFileAsync(
                        boundedStream,
                        partFileName,
                        progress: (partPos, partTotal) =>
                        {
                            long globalPos = partStartGlobal + partPos;
                            OnUploadProgress?.Invoke(effectiveFileName, globalPos, fileLength);
                        }
                    );

                    string mimeType = GetDocumentMimeType(effectiveFileName);
                    var fileNameAttr = new TL.DocumentAttributeFilename { file_name = partFileName };
                    var attributes = new TL.DocumentAttribute[] { fileNameAttr };

                    TL.InputFileBase? thumbFile = null;
                    if (i == 0)
                    {
                        if (videoMeta?.Thumbnail != null && videoMeta.Thumbnail.Length > 0)
                        {
                            try
                            {
                                using var thumbMs = new MemoryStream(videoMeta.Thumbnail, false);
                                thumbFile = await _client.UploadFileAsync(thumbMs, "thumb.jpg");
                            }
                            catch { }
                        }
                        else if (audioMeta?.AlbumCover != null && audioMeta.AlbumCover.Length > 0)
                        {
                            try
                            {
                                using var thumbMs = new MemoryStream(audioMeta.AlbumCover, false);
                                thumbFile = await _client.UploadFileAsync(thumbMs, "thumb.jpg");
                            }
                            catch { }
                        }
                    }

                    var mediaDoc = new TL.InputMediaUploadedDocument(inputFile, mimeType, attributes);
                    if (thumbFile != null)
                    {
                        mediaDoc.thumb = thumbFile;
                        mediaDoc.flags |= TL.InputMediaUploadedDocument.Flags.has_thumb;
                    }

                    string partCaption;
                    TL.Message? message;

                    if (i == 0)
                    {
                        double totalGb = (double)fileLength / (1024.0 * 1024.0 * 1024.0);
                        string masterCaptionPath = $"{rawCaptionPath} [Часть 1 из {partCount}, total={totalGb:F2} ГБ]";
                        partCaption = enableCaptions
                            ? NodeRepository.FormatTelegramCaption(masterCaptionPath, null, isLatest: true)
                            : string.Empty;

                        (message, peer) = await SendUploadMediaMessageWithRetryAsync(
                            peer,
                            targetChannelId,
                            partCaption,
                            mediaDoc,
                            partFileName,
                            cancellationToken: cancellationToken);

                        masterMessageId = message.ID;
                        AppLogger.Info("TelegramService", $"[BigFile Chunking] Заглавная Часть 1/{partCount} успешно отправлена в канал (Message ID: {masterMessageId}).");

                        if (enableCaptions && masterMessageId > 0)
                        {
                            string finalizedCaption = NodeRepository.FormatTelegramCaption(masterCaptionPath, masterMessageId, isLatest: true);
                            pendingCaptions.Add(new PendingCaptionUpdate(masterMessageId, finalizedCaption, channelId > 0 ? channelId : null));
                        }
                    }
                    else
                    {
                        string childCaptionPath = $"{rawCaptionPath} [Часть {partDisplayNum} из {partCount}]";
                        partCaption = enableCaptions ? $"↳ {childCaptionPath}" : string.Empty;

                        (message, peer) = await SendUploadMediaMessageWithRetryAsync(
                            peer,
                            targetChannelId,
                            partCaption,
                            mediaDoc,
                            partFileName,
                            replyToMsgId: masterMessageId,
                            cancellationToken: cancellationToken);

                        AppLogger.Info("TelegramService", $"[BigFile Chunking] Дочерняя Часть {partDisplayNum}/{partCount} успешно отправлена в канал (Message ID: {message.ID}, ReplyTo: {masterMessageId}).");

                        if (enableCaptions && message.ID > 0)
                        {
                            string finalizedChildCaption = $"↳ {childCaptionPath}\nID: {message.ID}";
                            pendingCaptions.Add(new PendingCaptionUpdate(message.ID, finalizedChildCaption, channelId > 0 ? channelId : null));
                        }
                    }

                    parts.Add(new Models.NodePart(0, i, currentPartLength, message.ID, channelId));
                }

                NotifyUploadMessageSucceeded();
                AppLogger.Info("TelegramService", $"[BigFile Chunking] Все {parts.Count} частей для '{effectiveFileName}' успешно загружены в Telegram!");
                OnUploadCompleted?.Invoke(effectiveFileName);
                return new FileUploadResult(masterMessageId, null, parts, pendingCaptions.Count > 0 ? pendingCaptions : null);
            }
            catch (Exception ex)
            {
                bool isCancellation = ex is OperationCanceledException ||
                                      ex is ObjectDisposedException ||
                                      cancellationToken.IsCancellationRequested ||
                                      (ex is IOException ioEx && ioEx.Message.Contains("отменен"));

                int floodDelayForCleanupSeconds = 0;
                if (isCancellation)
                {
                    AppLogger.Info("TelegramService", $"[BigFile Chunking] Загрузка файла '{effectiveFileName}' отменена пользователем. Очистка загруженных частей...");
                    TriggerUploadCanceled(effectiveFileName);
                }
                else if (ex is TL.RpcException rpcEx && rpcEx.Code == 420)
                {
                    RegisterUploadFloodWait(rpcEx.X, $"отправка многотомного файла '{effectiveFileName}'", effectiveFileName);
                    if (rpcEx.X > UploadHardStopThresholdSeconds)
                    {
                        floodDelayForCleanupSeconds = rpcEx.X + 1;
                    }
                    AppLogger.Error("TelegramService", $"[BigFile Chunking] Получен FLOOD_WAIT_{rpcEx.X} ({rpcEx.X} сек) при отправке части файла '{effectiveFileName}'. Очистка загруженных частей...", ex);
                }
                else
                {
                    AppLogger.Error("TelegramService", $"[BigFile Chunking] Ошибка загрузки части файла '{effectiveFileName}': {ex.Message}. Очистка загруженных частей...", ex);
                }

                if (parts.Count > 0)
                {
                    var msgIdsToDelete = parts.Select(p => p.TgMessageId).ToArray();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            if (floodDelayForCleanupSeconds > 0)
                            {
                                await Task.Delay(TimeSpan.FromSeconds(floodDelayForCleanupSeconds));
                            }
                            if (_client != null)
                            {
                                var storagePeer = await GetStoragePeerAsync(targetChannelId);
                                if (storagePeer is TL.InputPeerChannel ipc)
                                {
                                    await _client.Channels_DeleteMessages(new TL.InputChannel(ipc.channel_id, ipc.access_hash), msgIdsToDelete);
                                }
                            }
                        }
                        catch (Exception delEx)
                        {
                            AppLogger.Warn("TelegramService", $"Не удалось удалить незавершенные части файла: {delEx.Message}");
                        }
                    });
                }

                if (isCancellation)
                {
                    return null;
                }
                throw;
            }
        }

        /// <summary>
        /// Разрешает peer приватного канала для резервных копий базы данных.
        /// Источник правды — база данных (таблица telegram_channels, channel_type = 'backup').
        /// Если канал уже сохранен в базе, возвращает его мгновенно по channel_id и access_hash.
        /// Если канала в базе нет, создает новый приватный broadcast-канал и сохраняет в базу.
        /// Поиск по всем диалогам не выполняется.
        /// </summary>
        public async Task<TL.InputPeer> GetBackupStoragePeerAsync(string? specificTitle = null)
        {
            if (_client == null || !IsAuthorized)
                throw new InvalidOperationException("Клиент Telegram не подключен или не авторизован.");

            // 1. Проверяем в базе данных (единый источник правды)
            var backupChannel = _repository?.GetBackupChannel();
            if (backupChannel != null && backupChannel.ChannelId != 0 && backupChannel.AccessHash != 0)
            {
                // Если передано новое желаемое название и оно отличается от сохраненного — синхронизируем в Telegram и базе
                if (!string.IsNullOrWhiteSpace(specificTitle))
                {
                    string desiredTitle = specificTitle.Trim();
                    if (desiredTitle.Length > 128) desiredTitle = desiredTitle.Substring(0, 128);

                    if (!string.Equals(backupChannel.Title, desiredTitle, StringComparison.Ordinal))
                    {
                        try
                        {
                            await EditChannelTitleAsync(backupChannel.ChannelId, desiredTitle);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("TelegramService", $"Не удалось синхронизировать заголовок бэкап-канала ID {backupChannel.ChannelId}: {ex.Message}");
                        }
                    }
                }

                return new TL.InputPeerChannel(backupChannel.ChannelId, backupChannel.AccessHash);
            }

            string targetTitle = !string.IsNullOrWhiteSpace(specificTitle) 
                ? specificTitle.Trim() 
                : "TGramFS DB Backups";

            // 2. Если в базе нет — создаем новый приватный канал
            AppLogger.Info("TelegramService", $"Канал резервных копий не найден в базе данных. Создание нового приватного канала '{targetTitle}'...");
            var createReq = new TL.Methods.Channels_CreateChannel
            {
                flags = TL.Methods.Channels_CreateChannel.Flags.broadcast,
                title = targetTitle,
                about = "Автоматические резервные копии базы данных TGramFS Drive"
            };

            TL.UpdatesBase createdUpdates;
            BeginBackgroundRpc();
            try
            {
                createdUpdates = await _client.Invoke(createReq);
            }
            finally
            {
                EndBackgroundRpc();
            }
            if (createdUpdates is TL.Updates updates)
            {
                foreach (var chat in updates.chats.Values)
                {
                    if (chat is TL.Channel newCh)
                    {
                        var account = _repository?.GetActiveTelegramAccount();
                        if (account != null && _repository != null)
                        {
                            _repository.SaveOrUpdateBackupChannel(account.Id, newCh.ID, newCh.access_hash, newCh.Title);
                        }
                        AppLogger.Info("TelegramService", $"Создан новый канал резервных копий: '{newCh.Title}' (ID: {newCh.ID}) и сохранен в базу данных.");
                        await TrySetDefaultChannelPhotoAsync(newCh);
                        return newCh.ToInputPeer();
                    }
                }
            }

            throw new InvalidOperationException($"Не удалось создать канал резервных копий '{targetTitle}'.");
        }

        /// <summary>
        /// Загружает файл резервной копии базы данных в канал резервных копий Telegram.
        /// </summary>
        public async Task<(int MessageId, long ChannelId)> UploadBackupDocumentAsync(
            Stream fileStream,
            string fileName,
            string caption,
            string? backupChannelTitle = null)
        {
            if (TryGetRemainingUploadHardStopSeconds(out int hardStopRemainingSec, out DateTime hardStopUntilUtc))
            {
                AppLogger.Warn("TelegramService", $"[Backup] Выгрузка резервной копии '{fileName}' отложена без обращения к Telegram: активен FLOOD_WAIT отправки (осталось {hardStopRemainingSec} сек, до {hardStopUntilUtc.ToLocalTime():HH:mm:ss}).");
                throw new InvalidOperationException($"Выгрузка бэкапа отложена из-за FLOOD_WAIT Telegram (осталось {hardStopRemainingSec} сек, до {hardStopUntilUtc.ToLocalTime():HH:mm:ss}).");
            }

            await EnsureBackgroundFloodWaitDelayAsync();
            if (_client == null || !IsAuthorized)
            {
                throw new InvalidOperationException("Клиент Telegram не подключен или не авторизован.");
            }

            BeginBackgroundRpc();
            try
            {
                var peer = await GetBackupStoragePeerAsync(backupChannelTitle);
                long resolvedChannelId = peer is TL.InputPeerChannel ipc ? ipc.channel_id : 0;

                AppLogger.Info("TelegramService", $"Загрузка резервной копии '{fileName}' в Telegram (канал ID: {resolvedChannelId})...");

                var inputFile = await _client.UploadFileAsync(fileStream, fileName);
                var fileNameAttr = new TL.DocumentAttributeFilename { file_name = fileName };
                var attributes = new TL.DocumentAttribute[] { fileNameAttr };
                var mediaDoc = new TL.InputMediaUploadedDocument(inputFile, "application/x-sqlite3", attributes);

                TL.Message message;
                const int maxServerRetries = 2;
                for (int serverRetry = 0; ; )
                {
                    try
                    {
                        message = await _client.SendMessageAsync(peer, caption, mediaDoc);
                        break;
                    }
                    catch (TL.RpcException rpcEx) when (rpcEx.Code == 500 && serverRetry < maxServerRetries)
                    {
                        serverRetry++;
                        int backoffMs = serverRetry == 1 ? 500 : 1000;
                        AppLogger.Warn("TelegramService", $"[Backup Retry] Сервер Telegram вернул {rpcEx.Code} ({rpcEx.Message}) при отправке '{fileName}'. Повторная попытка {serverRetry}/{maxServerRetries} через {backoffMs} мс...");
                        await Task.Delay(backoffMs);
                    }
                }

                AppLogger.Info("TelegramService", $"Резервная копия '{fileName}' успешно загружена в Telegram. Message ID: {message.ID}");

                return (message.ID, resolvedChannelId);
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 420)
            {
                TriggerBackgroundFloodWait(rpcEx.X, $"резервное копирование '{fileName}'");
                AppLogger.Warn("TelegramService", $"[Backup] Получен FLOOD_WAIT_{rpcEx.X} ({rpcEx.X} сек) при отправке резервной копии '{fileName}'.");
                throw;
            }
            finally
            {
                EndBackgroundRpc();
            }
        }

        /// <summary>
        /// Выполняет поиск последнего сообщения с резервной копией базы данных в канале резервных копий Telegram.
        /// </summary>
        public async Task<(int MessageId, TL.Document Document, string Caption, DateTime Date, long Size, string FileName)?> GetLatestBackupDocumentAsync(string? backupChannelTitle = null)
        {
            await EnsureBackgroundFloodWaitDelayAsync();
            if (_client == null || !IsAuthorized)
            {
                AppLogger.Warn("TelegramService", "Поиск бэкапа отменен: клиент Telegram не подключен или не авторизован.");
                throw new InvalidOperationException("Клиент Telegram не подключен или не авторизован.");
            }

            BeginBackgroundRpc();
            try
            {
                AppLogger.Info("TelegramService", "Запрос к Telegram: поиск последней резервной копии базы данных...");
                var peer = await GetBackupStoragePeerAsync(backupChannelTitle);
                long resolvedChannelId = peer is TL.InputPeerChannel ipc ? ipc.channel_id : 0;

                var messagesBase = await _client.Messages_GetHistory(peer, limit: 20);
                TL.MessageBase[] messages = messagesBase switch
                {
                    TL.Messages_ChannelMessages cm => cm.messages,
                    TL.Messages_Messages mm => mm.messages,
                    _ => Array.Empty<TL.MessageBase>()
                };

                AppLogger.Debug("TelegramService", $"Получено сообщений из канала бэкапов (ID: {resolvedChannelId}): {messages.Length}. Проверка файлов...");

                foreach (var msgBase in messages)
                {
                    if (msgBase is TL.Message msg && msg.media is TL.MessageMediaDocument mediaDoc && mediaDoc.document is TL.Document doc)
                    {
                        string fileName = "";
                        var fnAttr = doc.attributes?.OfType<TL.DocumentAttributeFilename>().FirstOrDefault();
                        if (fnAttr != null)
                        {
                            fileName = fnAttr.file_name;
                        }

                        bool isDbBackup = !string.IsNullOrEmpty(fileName) && fileName.StartsWith("base_") && fileName.EndsWith(".db");

                        if (isDbBackup)
                        {
                            AppLogger.Info("TelegramService", $"Найден файл бэкапа базы в Telegram: Message #{msg.ID}, '{fileName}', {doc.size / 1024.0:F1} КБ, дата: {msg.Date.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
                            return (msg.ID, doc, msg.message ?? "", msg.Date, doc.size, fileName);
                        }
                    }
                }

                AppLogger.Info("TelegramService", "В последних 20 сообщениях канала бэкапов файл резервной копии (base_*.db) не найден.");
                return null;
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 420)
            {
                TriggerBackgroundFloodWait(rpcEx.X, "поиск резервной копии");
                throw;
            }
            finally
            {
                EndBackgroundRpc();
            }
        }

        /// <summary>
        /// Скачивает документ резервной копии базы данных из Telegram в целевой поток.
        /// </summary>
        public async Task DownloadBackupDocumentAsync(TL.Document document, Stream outputStream, Action<long, long>? progressCallback = null)
        {
            await EnsureBackgroundFloodWaitDelayAsync();
            if (_client == null || !IsAuthorized)
            {
                throw new InvalidOperationException("Клиент Telegram не подключен или не авторизован.");
            }

            BeginBackgroundRpc();
            try
            {
                AppLogger.Info("TelegramService", $"Скачивание резервной копии базы данных ({document.size / (1024.0 * 1024.0):F2} МБ)...");
                await _client.DownloadFileAsync(document, outputStream, progress: (current, total) =>
                {
                    progressCallback?.Invoke(current, total);
                });
                AppLogger.Info("TelegramService", "Скачивание резервной копии базы данных успешно завершено.");
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 420)
            {
                TriggerBackgroundFloodWait(rpcEx.X, "скачивание резервной копии");
                throw;
            }
            finally
            {
                EndBackgroundRpc();
            }
        }

        /// <summary>
        /// Обновляет текстовую подпись (Caption) у существующего сообщения в Telegram (например, при переименовании из .tmp)
        /// </summary>
        public async Task UpdateMessageCaptionAsync(int messageId, string newCaption, long? channelId = null)
        {
            if (_client == null || !IsAuthorized || messageId <= 1) return;

            try
            {
                var peer = await GetStoragePeerAsync(channelId);
                var editReq = new TL.Methods.Messages_EditMessage
                {
                    flags = TL.Methods.Messages_EditMessage.Flags.has_message,
                    peer = peer,
                    id = messageId,
                    message = newCaption
                };

                BeginCaptionRpc();
                try
                {
                    await _client.Invoke(editReq);
                }
                finally
                {
                    EndCaptionRpc();
                }
                AppLogger.Info("TelegramService", $"Подпись сообщения #{messageId} в Telegram успешно обновлена на: '{newCaption}'.");
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 400 && rpcEx.Message.Contains("MESSAGE_NOT_MODIFIED"))
            {
                AppLogger.Debug("TelegramService", $"Подпись сообщения #{messageId} уже актуальна в Telegram ({newCaption}).");
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 420) // FLOOD_WAIT_X
            {
                TriggerCaptionFloodWait(rpcEx.X, $"сообщение #{messageId}");
                throw;
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 400 && (rpcEx.Message.Contains("MESSAGE_ID_INVALID") || rpcEx.Message.Contains("CHAT_ADMIN_REQUIRED")))
            {
                // Сообщение удалено или нет прав — логируем и не прерываем очередь
                AppLogger.Warn("TelegramService", $"Невозможно обновить подпись сообщения #{messageId} (ошибка Telegram: {rpcEx.Message}). Запись будет пропущена.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TelegramService", $"Не удалось обновить подпись сообщения #{messageId} в Telegram: {ex.Message}");
            }
        }

        /// <summary>
        /// Мгновенно копирует (форвардит) файл в Telegram без повторной передачи байтов контента.
        /// Работает как внутри одного канала (дублирование медиа на серверах Telegram), так и между разными каналами.
        /// Поддерживает многосоставные файлы (> 2 ГБ / 4 ГБ): форвардит все связанные части (node_parts) и возвращает новый список частей.
        /// </summary>
        public async Task<FileUploadResult?> ForwardFileAsync(
            int sourceMessageId,
            int? sourcePreviewMessageId = null,
            long? sourceChannelId = null,
            long? targetChannelId = null,
            string? newCaption = null,
            int targetNodeId = 0,
            List<Models.NodePart>? sourceParts = null,
            bool includePreview = true,
            bool enableCaptions = true,
            CancellationToken cancellationToken = default)
        {
            if (_client == null || !IsAuthorized || sourceMessageId <= 0)
            {
                AppLogger.Warn("TelegramService", "[ForwardFileAsync] Клиент Telegram не подключен или некорректный ID сообщения.");
                return null;
            }

            TL.InputPeer? targetPeer = null;
            var forwardedMsgIds = new List<int>();

            void ScheduleForwardedCleanup()
            {
                if (forwardedMsgIds.Count == 0 || targetPeer == null) return;
                int[] idsToDelete = forwardedMsgIds.Where(id => id > 0).Distinct().ToArray();
                if (idsToDelete.Length == 0) return;
                bool isChannel = targetPeer is TL.InputPeerChannel;
                var cleanupPeer = targetPeer;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await DeleteBatchWithBisectAsync(cleanupPeer, isChannel, idsToDelete);
                    }
                    catch (Exception delEx)
                    {
                        AppLogger.Warn("TelegramService", $"[ForwardFileAsync] Не удалось очистить незавершенные пересланные сообщения [{string.Join(", ", idsToDelete)}]: {delEx.Message}");
                    }
                });
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EnsureBackgroundFloodWaitDelayAsync(cancellationToken);

                var sourcePeer = await GetStoragePeerAsync(sourceChannelId);
                targetPeer = await GetStoragePeerAsync(targetChannelId);
                long effectiveTargetChannelId = targetChannelId ?? (targetPeer is TL.InputPeerChannel tipc ? tipc.channel_id : 0);

                int? newPreviewId = null;
                int newMsgId = 0;

                // 1. Форвардим превью (если есть и включены фото-превью для целевой папки)
                if (includePreview && sourcePreviewMessageId.HasValue && sourcePreviewMessageId.Value > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var fwdPreviewReq = new TL.Methods.Messages_ForwardMessages
                        {
                            from_peer = sourcePeer,
                            to_peer = targetPeer,
                            id = new int[] { sourcePreviewMessageId.Value },
                            random_id = new long[] { Random.Shared.NextInt64() },
                            flags = TL.Methods.Messages_ForwardMessages.Flags.drop_author
                        };

                        BeginBackgroundRpc();
                        TL.UpdatesBase previewUpdates;
                        try
                        {
                            previewUpdates = await _client.Invoke(fwdPreviewReq);
                        }
                        finally
                        {
                            EndBackgroundRpc();
                        }
                        newPreviewId = ExtractMessageIdFromUpdates(previewUpdates);
                        if (newPreviewId.HasValue && newPreviewId.Value > 0)
                        {
                            forwardedMsgIds.Add(newPreviewId.Value);
                        }
                        await EnsureBackgroundPacingDelayAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("TelegramService", $"[ForwardFileAsync] Не удалось переслать превью #{sourcePreviewMessageId}: {ex.Message}");
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                // 2. Форвардим основной файл (мастер-сообщение)
                var fwdReq = new TL.Methods.Messages_ForwardMessages
                {
                    from_peer = sourcePeer,
                    to_peer = targetPeer,
                    id = new int[] { sourceMessageId },
                    random_id = new long[] { Random.Shared.NextInt64() },
                    flags = TL.Methods.Messages_ForwardMessages.Flags.drop_author
                };

                BeginBackgroundRpc();
                TL.UpdatesBase fwdUpdates;
                try
                {
                    fwdUpdates = await _client.Invoke(fwdReq);
                }
                finally
                {
                    EndBackgroundRpc();
                }
                newMsgId = ExtractMessageIdFromUpdates(fwdUpdates);

                if (newMsgId <= 0)
                {
                    AppLogger.Warn("TelegramService", $"[ForwardFileAsync] Сообщение #{sourceMessageId} переслано, но не удалось извлечь новый MessageId.");
                    ScheduleForwardedCleanup();
                    return null;
                }

                forwardedMsgIds.Add(newMsgId);
                cancellationToken.ThrowIfCancellationRequested();

                AppLogger.Info("TelegramService", $"[ForwardFileAsync] Файл (Msg #{sourceMessageId}) успешно скопирован в Telegram! Новый Msg #{newMsgId}" +
                    (newPreviewId.HasValue ? $", превью #{newPreviewId}" : "") +
                    (sourceChannelId != targetChannelId ? $" (канал {sourceChannelId} -> {targetChannelId})" : " (в пределах канала)"));

                // 3. Проверяем, является ли файл составным (multi-part > 2 ГБ / 4 ГБ)
                var partsToForward = sourceParts ?? _repository?.GetNodePartsByMessageId(sourceMessageId, sourceChannelId);
                List<Models.NodePart>? newParts = null;

                if (partsToForward != null && partsToForward.Count > 1)
                {
                    AppLogger.Info("TelegramService", $"[ForwardFileAsync] Обнаружен составной файл ({partsToForward.Count} частей). Начинаем пересылку связанных частей...");
                    newParts = new List<Models.NodePart>();

                    // Первая часть (Part 0) привязана к master newMsgId
                    var firstPart = partsToForward.FirstOrDefault(p => p.PartIndex == 0);
                    newParts.Add(new Models.NodePart(
                        targetNodeId,
                        0,
                        firstPart?.PartSize ?? 0,
                        newMsgId,
                        effectiveTargetChannelId
                    ));

                    // Форвардим все остальные части (Part 1 .. N-1); сбой или отмена любой части прерывает всю операцию
                    foreach (var childPart in partsToForward.Where(p => p.PartIndex > 0).OrderBy(p => p.PartIndex))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await EnsureBackgroundFloodWaitDelayAsync(cancellationToken);
                        await EnsureBackgroundPacingDelayAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();

                        var fwdChildReq = new TL.Methods.Messages_ForwardMessages
                        {
                            from_peer = sourcePeer,
                            to_peer = targetPeer,
                            id = new int[] { childPart.TgMessageId },
                            random_id = new long[] { Random.Shared.NextInt64() },
                            flags = TL.Methods.Messages_ForwardMessages.Flags.drop_author
                        };

                        BeginBackgroundRpc();
                        TL.UpdatesBase childUpdates;
                        try
                        {
                            childUpdates = await _client.Invoke(fwdChildReq);
                        }
                        finally
                        {
                            EndBackgroundRpc();
                        }
                        int childNewMsgId = ExtractMessageIdFromUpdates(childUpdates);

                        if (childNewMsgId <= 0)
                        {
                            AppLogger.Error("TelegramService", $"[ForwardFileAsync] Не удалось получить новый MessageId для дочерней части #{childPart.PartIndex} (исходный Msg #{childPart.TgMessageId}). Прерываем копирование составного файла и очищаем уже пересланные части.");
                            ScheduleForwardedCleanup();
                            return null;
                        }

                        forwardedMsgIds.Add(childNewMsgId);
                        newParts.Add(new Models.NodePart(
                            targetNodeId,
                            childPart.PartIndex,
                            childPart.PartSize,
                            childNewMsgId,
                            effectiveTargetChannelId
                        ));
                        AppLogger.Info("TelegramService", $"[ForwardFileAsync] Дочерняя часть #{childPart.PartIndex} (исходный Msg #{childPart.TgMessageId}) скопирована -> новый Msg #{childNewMsgId}.");
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }

                // 4. Если задана новая подпись и подписи включены для целевой папки, планируем её обновление через очередь
                if (enableCaptions && !string.IsNullOrEmpty(newCaption))
                {
                    string effectiveCaption;
                    if (newParts != null && newParts.Count > 1)
                    {
                        long totalSize = newParts.Sum(p => p.PartSize);
                        double totalGb = (double)totalSize / (1024.0 * 1024.0 * 1024.0);
                        string captionWithPathAndPart = $"{newCaption} [Часть 1 из {newParts.Count}, total={totalGb:F2} ГБ]";
                        effectiveCaption = NodeRepository.FormatTelegramCaption(captionWithPathAndPart, newMsgId, isLatest: true);
                    }
                    else
                    {
                        effectiveCaption = newCaption.StartsWith("🟢") || newCaption.StartsWith("🗑️")
                            ? newCaption
                            : NodeRepository.FormatTelegramCaption(newCaption, newMsgId, isLatest: true);
                    }
                    _repository?.EnqueueCaptionUpdate(targetNodeId, newMsgId, effectiveCaption, effectiveTargetChannelId > 0 ? effectiveTargetChannelId : null);

                    if (newPreviewId.HasValue && newPreviewId.Value > 0)
                    {
                        string previewCaption = NodeRepository.FormatTelegramCaption(newCaption, newPreviewId.Value, isLatest: true);
                        _repository?.EnqueueCaptionUpdate(targetNodeId, newPreviewId.Value, previewCaption, effectiveTargetChannelId > 0 ? effectiveTargetChannelId : null);
                    }

                    if (newParts != null && newParts.Count > 1)
                    {
                        foreach (var childPart in newParts.Where(p => p.PartIndex > 0))
                        {
                            int partDisplayNum = childPart.PartIndex + 1;
                            string childCaption = $"↳ {newCaption} [Часть {partDisplayNum} из {newParts.Count}]\nID: {childPart.TgMessageId}";
                            _repository?.EnqueueCaptionUpdate(targetNodeId, childPart.TgMessageId, childCaption, effectiveTargetChannelId > 0 ? effectiveTargetChannelId : null);
                        }
                    }
                }

                return new FileUploadResult(newMsgId, newPreviewId, newParts);
            }
            catch (OperationCanceledException)
            {
                AppLogger.Info("TelegramService", $"[ForwardFileAsync] Пересылка сообщения #{sourceMessageId} отменена пользователем. Очистка пересланных частей...");
                ScheduleForwardedCleanup();
                return null;
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 420) // FLOOD_WAIT_X
            {
                TriggerBackgroundFloodWait(rpcEx.X, $"копирование сообщения #{sourceMessageId}");
                AppLogger.Warn("TelegramService", $"[ForwardFileAsync] FloodWait: пауза {rpcEx.X} секунд.");
                ScheduleForwardedCleanup();
                return null;
            }
            catch (Exception ex)
            {
                AppLogger.Error("TelegramService", $"[ForwardFileAsync] Ошибка пересылки сообщения #{sourceMessageId}: {ex.Message}", ex);
                ScheduleForwardedCleanup();
                return null;
            }
        }

        private static int ExtractMessageIdFromUpdates(TL.UpdatesBase? updatesBase)
        {
            if (updatesBase == null) return 0;
            if (updatesBase is TL.Updates u && u.updates != null)
            {
                foreach (var update in u.updates)
                {
                    if (update is TL.UpdateNewMessage unm && unm.message is TL.Message m) return m.ID;
                    if (update is TL.UpdateNewChannelMessage uncm && uncm.message is TL.Message cm) return cm.ID;
                }
            }
            else if (updatesBase is TL.UpdatesCombined uc && uc.updates != null)
            {
                foreach (var update in uc.updates)
                {
                    if (update is TL.UpdateNewMessage unm && unm.message is TL.Message m) return m.ID;
                    if (update is TL.UpdateNewChannelMessage uncm && uncm.message is TL.Message cm) return cm.ID;
                }
            }
            return 0;
        }

        private long GetStreamLengthSafe(Stream stream)
        {
            try
            {
                return stream.Length;
            }
            catch
            {
                return -1;
            }
        }

        private static string GetDocumentMimeType(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                // .gif отправляем как обычный документ (application/octet-stream), чтобы не попадать в жёсткий антиспам-лимит Telegram для анимаций image/gif
                ".gif" => "application/octet-stream",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                ".tif" or ".tiff" => "image/tiff",
                ".pdf" => "application/pdf",
                ".zip" => "application/zip",
                ".rar" => "application/x-rar-compressed",
                ".7z" => "application/x-7z-compressed",
                ".tar" => "application/x-tar",
                ".gz" => "application/gzip",
                ".txt" => "text/plain",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".html" or ".htm" => "text/html",
                _ => "application/octet-stream"
            };
        }

        public static bool IsGalleryImage(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp";
        }

        public static byte[]? CreateOptimizedGalleryThumbnailFromFile(string filePath, int maxW = 1920, int maxH = 1920)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return CreateOptimizedGalleryThumbnail(fs, maxW, maxH);
            }
            catch (Exception ex)
            {
                AppLogger.Debug("TelegramService", $"Не удалось сформировать превью галереи из '{filePath}': {ex.Message}");
                return null;
            }
        }

        public static byte[]? CreateOptimizedGalleryThumbnail(Stream stream, int maxW = 1920, int maxH = 1920)
        {
            try
            {
                if (!stream.CanSeek) return null;
                long origin = stream.Position;
                stream.Seek(0, SeekOrigin.Begin);

                using var memStream = new MemoryStream();
                stream.CopyTo(memStream);
                stream.Seek(origin, SeekOrigin.Begin);
                memStream.Seek(0, SeekOrigin.Begin);

                using var originalBmp = System.Drawing.Image.FromStream(memStream, false, false);

                int origW = originalBmp.Width;
                int origH = originalBmp.Height;
                if (origW <= 0 || origH <= 0) return null;

                double ratioW = (double)maxW / origW;
                double ratioH = (double)maxH / origH;
                double ratio = Math.Min(ratioW, ratioH);
                if (ratio > 1.0) ratio = 1.0;

                int newW = Math.Max(1, (int)(origW * ratio));
                int newH = Math.Max(1, (int)(origH * ratio));

                using var resized = new System.Drawing.Bitmap(newW, newH);
                using (var g = System.Drawing.Graphics.FromImage(resized))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    g.DrawImage(originalBmp, new System.Drawing.Rectangle(0, 0, newW, newH), 0, 0, origW, origH, System.Drawing.GraphicsUnit.Pixel);
                }

                using var outMs = new MemoryStream();
                var encoder = GetEncoder(System.Drawing.Imaging.ImageFormat.Jpeg);
                if (encoder != null)
                {
                    using var encoderParams = new System.Drawing.Imaging.EncoderParameters(1);
                    encoderParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                    resized.Save(outMs, encoder, encoderParams);
                }
                else
                {
                    resized.Save(outMs, System.Drawing.Imaging.ImageFormat.Jpeg);
                }

                return outMs.ToArray();
            }
            catch (Exception ex)
            {
                AppLogger.Debug("TelegramService", $"Не удалось сформировать превью галереи: {ex.Message}");
                return null;
            }
        }

        private static System.Drawing.Imaging.ImageCodecInfo? GetEncoder(System.Drawing.Imaging.ImageFormat format)
        {
            var codecs = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        private long ResolveEffectiveChannelId(int messageId, long? channelId)
        {
            if (channelId.HasValue && channelId.Value != 0)
                return channelId.Value;

            long? dbChannelId = _repository?.GetChannelIdByTgMessageId(messageId);
            if (dbChannelId.HasValue && dbChannelId.Value != 0)
                return dbChannelId.Value;

            return _repository?.GetPrimaryTelegramChannel()?.ChannelId ?? 0L;
        }

        private async Task<TL.Document?> GetDocumentFromMessageAsync(int messageId, long? channelId = null, bool forceRefresh = false)
        {
            long effectiveChannelId = ResolveEffectiveChannelId(messageId, channelId);
            var cacheKey = (effectiveChannelId, messageId);

            if (!forceRefresh && _documentCache.TryGetValue(cacheKey, out var cached) && cached.expiresAt > DateTime.UtcNow)
            {
                return cached.document;
            }

            if (_client == null || !IsAuthorized)
            {
                bool reconnected = await EnsureConnectedAsync(20000);
                if (!reconnected || _client == null) return null;
            }

            long? peerChannelId = effectiveChannelId != 0 ? effectiveChannelId : (long?)null;
            var peer = await GetStoragePeerAsync(peerChannelId);
            TL.Messages_MessagesBase messagesBase;
            BeginDownloadRpc();
            try
            {
                messagesBase = await _client.GetMessages(peer, new TL.InputMessage[] { new TL.InputMessageID { id = messageId } });
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 420)
            {
                TriggerDownloadFloodWait(rpcEx.X);
                throw;
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 400 && (rpcEx.Message.Contains("CHANNEL_INVALID") || rpcEx.Message.Contains("CHANNEL_PRIVATE")))
            {
                AppLogger.Warn("TelegramService", "Канал недоступен по сохраненному хэшу. Сброс хэша и повторный поиск...");
                InvalidateStoragePeer();
                peer = await GetStoragePeerAsync(peerChannelId);
                messagesBase = await _client.GetMessages(peer, new TL.InputMessage[] { new TL.InputMessageID { id = messageId } });
            }
            catch (Exception ex) when (ex is SocketException || ex.InnerException is SocketException || ex is IOException || ex is TelegramNetworkException)
            {
                TriggerReactorReconnect("сбой сети при запросе дескриптора сообщения");
                throw new TelegramNetworkException($"Сбой сети при запросе документа ID {messageId}: {ex.Message}", ex);
            }
            finally
            {
                EndDownloadRpc();
            }
            
            TL.Document? document = null;
            if (messagesBase is TL.Messages_Messages messages && messages.messages.Length > 0)
            {
                var msg = messages.messages[0] as TL.Message;
                if (msg?.media is TL.MessageMediaDocument mediaDoc && mediaDoc.document is TL.Document doc)
                {
                    document = doc;
                }
            }
            else if (messagesBase is TL.Messages_ChannelMessages channelMessages && channelMessages.messages.Length > 0)
            {
                var msg = channelMessages.messages[0] as TL.Message;
                if (msg?.media is TL.MessageMediaDocument mediaDoc && mediaDoc.document is TL.Document doc)
                {
                    document = doc;
                }
            }

            if (document != null)
            {
                _documentCache[cacheKey] = (document, DateTime.UtcNow.AddMinutes(15));
            }
            else
            {
                // Негативный кэш: фиксируем отсутствие документа на 10 минут, предотвращая шквал повторных RPC-запросов и FLOOD_WAIT
                _documentCache[cacheKey] = (null, DateTime.UtcNow.AddMinutes(10));
                AppLogger.Warn("TelegramService", $"[GetDocumentFromMessageAsync] Медиа-документ для сообщения ID {messageId} (канал {effectiveChannelId}) не найден в Telegram. Сохранено в негативный кэш.");
            }

            return document;
        }

        private async Task ReadFromFileCacheAsync(string cacheFilePath, Stream destination, long offset, long length, int messageId)
        {
            using (var fs = new FileStream(cacheFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                long remaining = length;
                byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(131072);
                try
                {
                    while (remaining > 0)
                    {
                        int toRead = (int)Math.Min(buffer.Length, remaining);
                        int read = await fs.ReadAsync(buffer, 0, toRead);
                        if (read <= 0) break;

                        await destination.WriteAsync(buffer, 0, read);
                        remaining -= read;
                    }
                    await destination.FlushAsync();
                }
                catch (Exception ex)
                {
                    AppLogger.Debug("TelegramService", $"Клиент прервал чтение из кэша для сообщения {messageId}: {ex.Message}");
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }

        /// <summary>
        /// Потоковое скачивание файла напрямую из Telegram через MTProto Upload_GetFile.
        /// Поддерживает как обычные файлы, так и крупногабаритные составные файлы (> 2 ГБ / 4 ГБ) из таблицы node_parts.
        /// В режиме Pure RAM Mode качает данные 100% через ОЗУ (без файлов на диске), сохраняя чанки в динамический 128 МБ RAM-кэш.
        /// </summary>
        public async Task DownloadFileAsync(int messageId, Stream destination, long offset, long length, string fileName = "файл", long totalFileSize = -1, long? channelId = null, int? nodeId = null, string? fullVirtualPath = null)
        {
            List<Models.NodePart>? parts = null;
            if (nodeId.HasValue && nodeId.Value > 0)
            {
                parts = _repository?.GetNodeParts(nodeId.Value);
            }
            else if (messageId > 0)
            {
                parts = _repository?.GetNodePartsByMessageId(messageId, channelId);
            }

            if (parts != null && parts.Count > 1)
            {
                await DownloadMultiPartFileAsync(parts, destination, offset, length, fileName, totalFileSize, channelId, fullVirtualPath);
                return;
            }

            await DownloadSingleDocumentSliceAsync(messageId, destination, offset, length, fileName, totalFileSize, channelId, nodeId, fullVirtualPath);
        }

        private async Task DownloadMultiPartFileAsync(
            List<Models.NodePart> parts,
            Stream destination,
            long offset,
            long length,
            string fileName,
            long totalFileSize,
            long? channelId,
            string? fullVirtualPath = null)
        {
            var sortedParts = parts.OrderBy(p => p.PartIndex).ToList();
            long currentStart = 0;
            var ranges = new List<(Models.NodePart part, long start, long end, long size)>();
            foreach (var p in sortedParts)
            {
                long pSize = p.PartSize;
                ranges.Add((p, currentStart, currentStart + pSize, pSize));
                currentStart += pSize;
            }
            long aggregatedSize = currentStart;
            if (totalFileSize <= 0) totalFileSize = aggregatedSize;

            if (offset >= aggregatedSize)
            {
                return;
            }

            if (offset + length > aggregatedSize)
            {
                length = aggregatedSize - offset;
            }

            if (length <= 0) return;

            long requestedEnd = offset + length;

            foreach (var (p, pStart, pEnd, pSize) in ranges)
            {
                if (offset < pEnd && requestedEnd > pStart)
                {
                    long overlapStart = Math.Max(offset, pStart);
                    long overlapEnd = Math.Min(requestedEnd, pEnd);
                    long overlapLength = overlapEnd - overlapStart;

                    long localOffset = overlapStart - pStart;
                    long localTotalSize = pSize;
                    long partChannelId = p.TgChannelId > 0 ? p.TgChannelId : (channelId ?? 0);

                    string partDisplayName = $"{fileName} [Часть {p.PartIndex + 1}/{sortedParts.Count}]";
                    AppLogger.Debug("TelegramService", $"[MultiPart Read] Чтение диапазона [{overlapStart}..{overlapEnd}) ({overlapLength:N0} байт) из части {p.PartIndex + 1} (Msg ID {p.TgMessageId}, localOffset={localOffset}).");

                    await DownloadSingleDocumentSliceAsync(p.TgMessageId, destination, localOffset, overlapLength, partDisplayName, localTotalSize, partChannelId, p.NodeId, fullVirtualPath);
                }
            }
        }

        private async Task DownloadSingleDocumentSliceAsync(int messageId, Stream destination, long offset, long length, string fileName = "файл", long totalFileSize = -1, long? channelId = null, int? nodeId = null, string? fullVirtualPath = null)
        {
            await EnsureDownloadFloodWaitDelayAsync();

            if (_client == null || !IsAuthorized)
            {
                bool reconnected = await EnsureConnectedAsync(20000);
                if (!reconnected || _client == null || !IsAuthorized)
                    throw new TelegramNetworkException("Сеть Telegram недоступна (клиент не подключен или восстанавливает соединение).");
            }

            bool enableDiskCache = _configManager?.CurrentSettings?.Server?.EnableDiskReadCache ?? false;
            long effectiveChannelId = ResolveEffectiveChannelId(messageId, channelId);
            var fileKey = (effectiveChannelId, messageId);

            // 1. Если дисковый кэш включен и файл уже закэширован на диске полностью
            if (enableDiskCache)
            {
                string cacheDir = Path.Combine(Path.GetTempPath(), "TGramFS_ReadCache");
                Directory.CreateDirectory(cacheDir);
                string cacheFilePath = Path.Combine(cacheDir, $"{effectiveChannelId}_{messageId}.bin");

                if (File.Exists(cacheFilePath))
                {
                    await ReadFromFileCacheAsync(cacheFilePath, destination, offset, length, messageId);
                    return;
                }
            }

            // 2. Запрашиваем дескриптор документа
            var document = await GetDocumentFromMessageAsync(messageId, effectiveChannelId);
            if (document == null)
            {
                string reason = $"Не удалось найти медиа-документ для сообщения ID {messageId} (канал {effectiveChannelId}) в Telegram.";
                _repository?.HandleLostFileByMessageId(messageId, reason, nodeId, effectiveChannelId != 0 ? effectiveChannelId : (long?)null);
                throw new FileNotFoundException(reason);
            }

            long actualTotalSize = totalFileSize > 0 ? totalFileSize : (document.size > 0 ? document.size : offset + length);
            bool isSmallFile = actualTotalSize <= 1048576; // Файл меньше или равен 1 МБ
            long currentChunkIdx = offset / 1048576;
            long totalChunks = actualTotalSize > 0 ? (long)Math.Ceiling((double)actualTotalSize / 1048576.0) : 1;

            EnsureAuditCacheCapacity();

            int cacheTtlMinutes = _currentSettings.Server.ChunkMemoryCacheTtlMinutes;
            if (_networkAudits.TryGetValue(fileKey, out var existingAudit) && (DateTime.UtcNow - existingAudit.LastActivityTime).TotalMinutes > cacheTtlMinutes)
            {
                _networkAudits.TryRemove(fileKey, out _);
            }

            var audit = _networkAudits.GetOrAdd(fileKey, _ => new NetworkTransferAudit { FileName = fileName, FileSize = actualTotalSize, FullVirtualPath = fullVirtualPath });
            audit.FileName = fileName;
            if (!string.IsNullOrEmpty(fullVirtualPath)) audit.FullVirtualPath = fullVirtualPath;
            audit.FileSize = actualTotalSize;
            audit.TouchActivity();

            var readSeq = _fileReadSequences.GetOrAdd(fileKey, _ => new FileReadSequence());
            long activationThresholdBytes = Math.Max(1, _currentSettings.Server.StreamingActivationThresholdMb) * 1048576L;
            lock (readSeq)
            {
                var now = DateTime.UtcNow;
                // Время жизни непрерывной сессии воспроизведения привязано к TTL кэша из настроек
                if ((now - readSeq.LastReadTime).TotalMinutes > cacheTtlMinutes)
                {
                    readSeq.SequentialCount = 0;
                    readSeq.AccumulatedSequentialBytes = 0;
                    readSeq.LastChunkIndex = -1;
                    readSeq.ChunkBytesRead.Clear();
                }

                if (readSeq.LastChunkIndex != -1 && (currentChunkIdx == readSeq.LastChunkIndex || currentChunkIdx == readSeq.LastChunkIndex + 1))
                {
                    if (currentChunkIdx == readSeq.LastChunkIndex + 1)
                    {
                        readSeq.SequentialCount++;
                    }
                }
                else
                {
                    // Перемотка (Seek) или новый старт: сбрасываем накопленные байты и отменяем старый префетч
                    readSeq.SequentialCount = 1;
                    readSeq.AccumulatedSequentialBytes = 0;
                    readSeq.ChunkBytesRead.Clear();
                    if (readSeq.LastChunkIndex != -1)
                    {
                        _workerPool?.CancelPendingChunksForFile(effectiveChannelId, messageId);
                    }
                }

                readSeq.LastChunkIndex = currentChunkIdx;
                readSeq.LastReadTime = now;
            }

            // ПРОВЕРКА ЗОНДОВ МЕТАДАННЫХ И СТРИМИНГА:
            // 1) Запрос в хвост файла (последние 512 КБ для ID3v1/тетрисов метаданных)
            bool isTailProbe = actualTotalSize > 1048576 && offset >= Math.Max(0, actualTotalSize - 524288);

            // 2) Зонд метаданных / эскиза обложки:
            // - Хвостовой запрос (ID3v1)
            // - Либо одиночный точечный запрос малого размера (length <= 262144) при единичном последовательном счетчике (SequentialCount <= 1)
            // - Либо чтение самого первого 1 МБ файла (offset < 1048576) до того, как суммарно вычитан первый мегабайт (AccumulatedSequentialBytes < 1048576)
            bool isFirstMb = offset < 1048576 && length <= 1048576 && readSeq.AccumulatedSequentialBytes < 1048576;
            bool isSinglePointProbe = length <= 262144 && readSeq.SequentialCount <= 1;
            bool isMetadataProbe = isTailProbe || isFirstMb || isSinglePointProbe;

            if (offset == 0 && !isMetadataProbe && actualTotalSize > 1048576)
            {
                audit.ResetCompletion();
            }

            // Если дисковый кэш включен в настройках: скачиваем файл в дисковый кэш %TEMP%
            if (enableDiskCache && offset == 0 && !isMetadataProbe && actualTotalSize > 1048576)
            {
                string cacheDir = Path.Combine(Path.GetTempPath(), "TGramFS_ReadCache");
                Directory.CreateDirectory(cacheDir);
                string cacheFilePath = Path.Combine(cacheDir, $"{effectiveChannelId}_{messageId}.bin");

                var fileLock = _fileDownloadLocks.GetOrAdd(fileKey, _ => new SemaphoreSlim(1, 1));
                await fileLock.WaitAsync();
                try
                {
                    if (!File.Exists(cacheFilePath))
                    {
                        int diskWorkers = Math.Clamp(_currentSettings.Server.DownloadWorkerCount > 0 ? _currentSettings.Server.DownloadWorkerCount : 1, 1, 3);
                        AppLogger.Info("TelegramService", $"[Disk Cache Mode] Скачивание файла '{fileName}' (ID {messageId}, канал {effectiveChannelId}, {actualTotalSize:N0} байт) через {diskWorkers} воркеров в дисковый кэш...");
                        if (_workerPool == null) InitWorkerPool();
                        bool success = _workerPool != null && await _workerPool.DownloadFileAsync(
                            document,
                            cacheFilePath,
                            onProgress: (transferred, total) => OnDownloadProgress?.Invoke(fileName, transferred, total));

                        if (success && File.Exists(cacheFilePath))
                        {
                            AppLogger.Info("TelegramService", $"[Disk Cache Mode] Файл '{fileName}' (ID {messageId}) успешно сохранен в кэш.");
                            audit.AddNetworkBytes(new FileInfo(cacheFilePath).Length);
                            audit.MarkRangeReceived(0, actualTotalSize);
                            if (!isMetadataProbe && audit.IsAllChunksReceived() && audit.LogCompletionOnce())
                            {
                                OnDownloadCompleted?.Invoke(fileName);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("TelegramService", $"[Disk Cache Mode] Ошибка при фоновом скачивании файла ID {messageId}: {ex.Message}.");
                }
                finally
                {
                    fileLock.Release();
                }

                if (File.Exists(cacheFilePath))
                {
                    await ReadFromFileCacheAsync(cacheFilePath, destination, offset, length, messageId);
                    return;
                }
            }

            // 1. Проверяем, есть ли запрашиваемый чанк уже в ОЗУ (был ранее упреждающе выкачан воркером)
            if (!enableDiskCache && TryGetFromMemoryCache(effectiveChannelId, messageId, offset, out var ramCachedRaw, out var ramCachedOffset) && ramCachedRaw != null)
            {
                int availableInChunk = ramCachedRaw.Length - ramCachedOffset;
                int bytesToSend = (int)Math.Min(availableInChunk, length);
                if (bytesToSend == length) // Возвращаем результат сразу, только если кэш закрыл ВСЮ запрошенную клиентом длину
                {
                    try
                    {
                        await destination.WriteAsync(ramCachedRaw, ramCachedOffset, bytesToSend);
                        await destination.FlushAsync();
                        audit.AddRamBytes(bytesToSend);
                        lock (readSeq)
                        {
                            readSeq.AccumulatedSequentialBytes += bytesToSend;
                            readSeq.ChunkBytesRead.AddOrUpdate(offset / 1048576, bytesToSend, (_, old) => old + bytesToSend);
                            readSeq.LastReadTime = DateTime.UtcNow;
                        }
                        bool allReceived = audit.MarkRangeReceived(offset, bytesToSend);
                        AppLogger.Info("TelegramService", $"[Cache RAM] Точечное чтение из ОЗУ для '{fileName}' (ID {messageId}): Глобальный Чанк #{(offset / 1048576) + 1} (смещение {offset:N0}, {bytesToSend:N0} байт). {audit.ProgressSummary}.");

                        if (!isMetadataProbe && allReceived && audit.LogCompletionOnce())
                        {
                            OnDownloadCompleted?.Invoke(fileName);
                        }

                        // Запускаем непрерывный фоновый конвейер скачивания оставшихся чанков файла только при реальном воспроизведении/скачивании
                        if (!isMetadataProbe)
                        {
                            TriggerContinuousPrefetch(effectiveChannelId, messageId, document, fileName, actualTotalSize, offset, audit, fullVirtualPath);
                        }
                        return;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Debug("TelegramService", $"Клиент прервал соединение при чтении из RAM кэша: {ex.Message}");
                        return;
                    }
                }
            }

            long currentPos = offset;
            long remainingBytes = length;
            long totalSent = 0;

            while (remainingBytes > 0)
            {
                await EnsureDownloadFloodWaitDelayAsync();

                // 1. Проверяем, есть ли уже нужные байты в быстром кэше оперативной памяти
                if (TryGetFromMemoryCache(effectiveChannelId, messageId, currentPos, out var cachedRaw, out var cachedOffset) && cachedRaw != null)
                {
                    int available = cachedRaw.Length - cachedOffset;
                    int toSend = (int)Math.Min(available, remainingBytes);

                    try
                    {
                        await destination.WriteAsync(cachedRaw, cachedOffset, toSend);
                        await destination.FlushAsync();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Debug("TelegramService", $"Клиент прервал соединение для '{fileName}' (ID {messageId}): {ex.Message}");
                        return;
                    }

                    currentPos += toSend;
                    remainingBytes -= toSend;
                    totalSent += toSend;
                    audit.AddRamBytes(toSend);

                    lock (readSeq)
                    {
                        readSeq.AccumulatedSequentialBytes += toSend;
                        readSeq.ChunkBytesRead.AddOrUpdate(currentPos / 1048576, toSend, (_, old) => old + toSend);
                        readSeq.LastReadTime = DateTime.UtcNow;
                    }

                    bool allReceived = audit.MarkRangeReceived(currentPos - toSend, toSend);

                    AppLogger.Info("TelegramService", $"[Cache RAM] Потоковое чтение из ОЗУ для '{fileName}' (ID {messageId}): смещение {currentPos - toSend:N0}, отдано {toSend:N0} байт ({currentPos:N0} / {actualTotalSize:N0} байт, {(double)currentPos * 100 / Math.Max(1, actualTotalSize):F1}%). {audit.ProgressSummary}.");

                    if (allReceived && audit.LogCompletionOnce())
                    {
                        OnDownloadCompleted?.Invoke(fileName);
                    }

                    // Если мы в режиме стриминга, проверяем необходимость запуска префетча
                    if (!enableDiskCache && !isSmallFile && (!isMetadataProbe || readSeq.AccumulatedSequentialBytes >= 1048576) && remainingBytes > 0)
                    {
                        TriggerContinuousPrefetch(effectiveChannelId, messageId, document, fileName, actualTotalSize, currentPos, audit, fullVirtualPath);
                    }

                    continue;
                }

                // 2. Адаптивный выбор размера чанка MTProto и загрузка строго через единый пул воркеров:
                // - Для маленьких файлов (<= 256 КБ): качаем файл целиком [Зонд 256 КБ].
                // - Первый мегабайт (0..1 МБ):
                //     * При чтении в пределах первой четверти (0..256 КБ): качаем ровно 256 КБ [Зонд 256 КБ - 1/4].
                //       Это мгновенно закрывает чтение тегов/обложек плеерами и экономит 75% сетевого трафика.
                //     * При чтении дальше первой четверти (256 КБ..1 МБ): клиент явно копирует или воспроизводит файл.
                //       Вместо трех отдельных запросов по 256 КБ делаем ОДИН монолитный запрос на оставшиеся 768 КБ [Зонд 768 КБ - 2..4/4].
                //       Universal Interval Merger сшивает обе части в готовый 1 МБ блок в ОЗУ.
                // - Хвостовой зонд (isTailProbe в последних 512 КБ больших файлов): качаем квантом 256 КБ без префетча.
                // - Начиная со второго мегабайта (>= 1 МБ) при обычном стриминге/копировании: качаем полными 1 МБ (1 048 576 байт).

                // Единый канонический размер чанка MTProto (1 МБ) с монолитным остатком в конце файла:
                // - Каждый полный чанк (#1, #2, #3...) скачивается ровно по 1 048 576 байт (1 МБ).
                // - Последний чанк файла (#N) скачивается ровно на остаток от границы чанка до EOF (1 монолитный запрос).
                // - Маленькие файлы (<= 1 МБ) скачиваются целиком за 1 запрос.
                // Это полностью исключает двойные сетевые раундтрипы (256 КБ + 768 КБ, 256 КБ + 27 КБ) на старте и финише,
                // сокращает задержку первого воспроизведения на 350-500 мс и предотвращает перерасход RPC-квоты Telegram API.
                long chunkOffset = (currentPos / 1048576) * 1048576;
                int internalOffset = (int)(currentPos - chunkOffset);
                int requestLimit = (int)Math.Min(1048576L, actualTotalSize - chunkOffset);
                bool isCurrentChunkProbe = isMetadataProbe;

                if (_workerPool == null) InitWorkerPool();
                if (_workerPool != null)
                {
                    var chunkTask = _workerPool.EnqueueChunk(
                        effectiveChannelId,
                        messageId,
                        document,
                        fileName,
                        actualTotalSize,
                        chunkOffset,
                        requestLimit: requestLimit,
                        isHighPriority: true,
                        onChunkReceived: (chunkBytes, offset) =>
                        {
                            audit.AddNetworkBytes(chunkBytes.Length);
                            bool allReceived = audit.MarkRangeReceived(offset, chunkBytes.Length);
                            StoreChunkInMemoryCache(effectiveChannelId, messageId, offset, chunkBytes, cacheTtlMinutes);
                            OnChunkCached?.Invoke(fileName, offset + chunkBytes.Length, actualTotalSize);
                            if (allReceived && audit.LogCompletionOnce())
                            {
                                OnDownloadCompleted?.Invoke(fileName);
                            }
                        },
                        onProgress: (transferred, total) =>
                        {
                            if (!isCurrentChunkProbe)
                            {
                                OnDownloadProgress?.Invoke(fileName, chunkOffset + transferred, actualTotalSize);
                            }
                        },
                        fullVirtualPath: fullVirtualPath);

                    var raw = await chunkTask;
                    if (raw != null && raw.Length > 0)
                    {
                        if (internalOffset >= raw.Length)
                        {
                            AppLogger.Warn("TelegramService", $"[MTProto] Предупреждение: internalOffset ({internalOffset}) >= raw.Length ({raw.Length}) для '{fileName}' (ID {messageId}, chunkOffset {chunkOffset}). Завершение чтения диапазона.");
                            break;
                        }

                        int available = raw.Length - internalOffset;
                        int toSend = (int)Math.Min(available, remainingBytes);

                        try
                        {
                            await destination.WriteAsync(raw, internalOffset, toSend);
                            await destination.FlushAsync();
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Debug("TelegramService", $"Клиент прервал соединение для '{fileName}' (ID {messageId}): {ex.Message}");
                            return;
                        }

                        currentPos += toSend;
                        remainingBytes -= toSend;
                        totalSent += toSend;

                        lock (readSeq)
                        {
                            readSeq.AccumulatedSequentialBytes += toSend;
                            readSeq.ChunkBytesRead.AddOrUpdate(currentPos / 1048576, toSend, (_, old) => old + toSend);
                            readSeq.LastReadTime = DateTime.UtcNow;
                        }

                        if (!isCurrentChunkProbe)
                        {
                            OnDownloadProgress?.Invoke(fileName, currentPos, actualTotalSize);
                        }
                        else
                        {
                            OnMetadataProgress?.Invoke(fileName, audit.NetworkBytes, actualTotalSize);
                            OnChunkCached?.Invoke(fileName, audit.NetworkBytes, actualTotalSize);
                        }

                        // Если Telegram вернул меньше данных, чем requestLimit — достигнут конец файла
                        if (raw.Length < requestLimit && remainingBytes > 0)
                        {
                            break;
                        }

                        if (!enableDiskCache && !isSmallFile && !isCurrentChunkProbe && remainingBytes > 0)
                        {
                            TriggerContinuousPrefetch(effectiveChannelId, messageId, document, fileName, actualTotalSize, currentPos, audit, fullVirtualPath);
                        }

                        continue;
                    }
                    else
                    {
                        AppLogger.Warn("TelegramService", $"Не удалось получить чанк смещением {chunkOffset} для '{fileName}' (ID {messageId}) из пула воркеров.");
                        break;
                    }
                }
            }

            if (audit.IsAllChunksReceived())
            {
                if (audit.LogCompletionOnce())
                {
                    OnDownloadCompleted?.Invoke(fileName);
                }
            }
            else if (isMetadataProbe && readSeq.AccumulatedSequentialBytes < 1048576)
            {
                if (audit.LogMetadataCompletedOnce())
                {
                    OnMetadataCompleted?.Invoke(fileName);
                }
            }
            else if (!enableDiskCache && !isSmallFile)
            {
                // Запускаем непрерывный фоновый конвейер воркеров MTProto строго ПОСЛЕ того, как текущий чанк скачан и готов в RAM
                TriggerContinuousPrefetch(effectiveChannelId, messageId, document, fileName, actualTotalSize, currentPos, audit, fullVirtualPath);
            }
        }

        /// <summary>
        /// Постановка упреждающих чанков в единый глобальный пул воркеров MTProto.
        /// Не создает новых пулов и исключает дублирование сетевых запросов.
        /// </summary>
        private void TriggerContinuousPrefetch(long channelId, int messageId, TL.Document document, string fileName, long actualTotalSize, long currentReadOffset, NetworkTransferAudit audit, string? fullVirtualPath = null)
        {
            bool enableDiskCache = _currentSettings.Server.EnableDiskReadCache;
            if (enableDiskCache || document == null || actualTotalSize <= 1048576 || _client == null) return;

            // Проверяем порог активации упреждения: пока не вычитано N МБ от точки старта/перемотки, префетч не запускается
            long activationThresholdBytes = Math.Max(1, _currentSettings.Server.StreamingActivationThresholdMb) * 1048576L;
            if (_fileReadSequences.TryGetValue((channelId, messageId), out var seq))
            {
                lock (seq)
                {
                    if (seq.AccumulatedSequentialBytes < activationThresholdBytes)
                    {
                        return;
                    }
                }
            }

            if (_workerPool == null)
            {
                InitWorkerPool();
                if (_workerPool == null) return;
            }

            int cacheTtlMinutes = _currentSettings.Server.ChunkMemoryCacheTtlMinutes;
            bool isAudio = IsAudioFileName(fileName);
            int windowMb = isAudio
                ? _currentSettings.Server.AudioPrefetchWindowMb
                : _currentSettings.Server.StreamingPrefetchWindowMb;

            long windowBytes = (long)windowMb * 1024 * 1024;
            long startChunkOffset = (currentReadOffset / 1048576) * 1048576;

            var newChunksToQueue = new List<long>();
            long totalBytesToQueue = 0;

            // 1. Проверяем текущий чанк: если он скачан лишь частично (например, 256 КБ зонд), сначала докачиваем его!
            int startExpectedSize = (int)Math.Min(1048576L, actualTotalSize - startChunkOffset);
            bool currentInRam = TryGetFromMemoryCache(channelId, messageId, startChunkOffset, out var currData, out _) && (currData != null && currData.Length >= startExpectedSize);
            bool currentInFlight = _workerPool.IsChunkInFlight(channelId, messageId, startChunkOffset);

            if (!currentInRam && !currentInFlight)
            {
                newChunksToQueue.Add(startChunkOffset);
                totalBytesToQueue += startExpectedSize;
            }

            // 2. Сканируем строго следующие чанки в пределах окна упреждения
            long scanOffset = startChunkOffset + 1048576;
            long maxPrefetchLimit = Math.Min(actualTotalSize, scanOffset + windowBytes);

            while (scanOffset < maxPrefetchLimit)
            {
                int expectedSize = (int)Math.Min(1048576L, actualTotalSize - scanOffset);
                bool inRam = TryGetFromMemoryCache(channelId, messageId, scanOffset, out var cachedData, out _) && (cachedData != null && cachedData.Length >= expectedSize);
                bool inFlight = _workerPool.IsChunkInFlight(channelId, messageId, scanOffset);

                if (!inRam && !inFlight)
                {
                    newChunksToQueue.Add(scanOffset);
                    totalBytesToQueue += expectedSize;
                }
                scanOffset += 1048576;
            }

            if (newChunksToQueue.Count > 0)
            {
                int totalChunks = (int)Math.Ceiling((double)actualTotalSize / 1048576.0);
                int firstChunk = (int)(newChunksToQueue[0] / 1048576) + 1;
                int lastChunk = (int)(newChunksToQueue[^1] / 1048576) + 1;
                double mb = (double)totalBytesToQueue / (1024.0 * 1024.0);
                int activeWorkers = _workerPool.ActiveWorkerCount;

                string rangeStr = firstChunk == lastChunk
                    ? $"чанк #{firstChunk}"
                    : $"чанки #{firstChunk}..#{lastChunk}";

                string workerWord = activeWorkers switch
                {
                    >= 1 and <= 4 => "воркера",
                    _ => "воркеров"
                };

                string pathTag = !string.IsNullOrEmpty(fullVirtualPath) ? $" ({fullVirtualPath})" : "";
                AppLogger.Info("MtprotoWorkerPool", $"[RAM Streaming] Скачивание {mb:0.00} МБ ({rangeStr} из {totalChunks}) для '{fileName}'{pathTag} через {activeWorkers} {workerWord} MTProto...");

                foreach (var chunkOffsetToQueue in newChunksToQueue)
                {
                    _workerPool.EnqueueChunk(
                        channelId,
                        messageId,
                        document,
                        fileName,
                        actualTotalSize,
                        chunkOffsetToQueue,
                        requestLimit: 1048576,
                        isHighPriority: false,
                        onChunkReceived: (chunkBytes, chunkOffset) =>
                        {
                            audit.AddNetworkBytes(chunkBytes.Length);
                            bool allReceived = audit.MarkRangeReceived(chunkOffset, chunkBytes.Length);
                            StoreChunkInMemoryCache(channelId, messageId, chunkOffset, chunkBytes, cacheTtlMinutes);

                            OnChunkCached?.Invoke(fileName, chunkOffset + chunkBytes.Length, actualTotalSize);

                            if (allReceived && audit.LogCompletionOnce())
                            {
                                OnDownloadCompleted?.Invoke(fileName);
                            }
                        },
                        onProgress: (transferred, total) =>
                        {
                            OnDownloadProgress?.Invoke(fileName, chunkOffsetToQueue + transferred, actualTotalSize);
                        },
                        fullVirtualPath: fullVirtualPath);
                }
            }
        }

        public async Task<bool> DeleteFileFromTelegramAsync(int messageId, long? channelId = null)
        {
            return await DeleteFilesFromTelegramAsync(new System.Collections.Generic.List<int> { messageId }, channelId);
        }

        public async Task<bool> DeleteFilesFromTelegramAsync(System.Collections.Generic.List<int> messageIds, long? channelId = null)
        {
            if (messageIds == null || messageIds.Count == 0) return true;

            // Фильтруем только валидные положительные ID, исключая дубликаты
            var validIds = messageIds.Where(id => id > 0).Distinct().ToList();
            if (validIds.Count == 0) return true;

            await EnsureBackgroundFloodWaitDelayAsync();

            if (_client == null || !IsAuthorized)
                throw new InvalidOperationException("Клиент Telegram не подключен или не авторизован.");

            try
            {
                const int batchSize = 100;

                if (channelId.HasValue && channelId.Value != 0)
                {
                    var peer = await GetStoragePeerAsync(channelId.Value);
                    bool isChannel = peer is TL.InputPeerChannel;

                    for (int i = 0; i < validIds.Count; i += batchSize)
                    {
                        var count = Math.Min(batchSize, validIds.Count - i);
                        var batch = validIds.GetRange(i, count).ToArray();

                        await DeleteBatchWithBisectAsync(peer, isChannel, batch);
                    }
                }
                else
                {
                    // Группируем сообщения по их каналам для корректного удаления из разных каналов
                    var channelGroups = new Dictionary<long, List<int>>();
                    foreach (var id in validIds)
                    {
                        long chKey = _repository?.GetChannelIdByTgMessageId(id) ?? 0L;
                        if (!channelGroups.TryGetValue(chKey, out var list))
                        {
                            list = new List<int>();
                            channelGroups[chKey] = list;
                        }
                        list.Add(id);
                    }

                    foreach (var kvp in channelGroups)
                    {
                        try
                        {
                            var peer = await GetStoragePeerAsync(kvp.Key != 0L ? kvp.Key : (long?)null);
                            bool isChannel = peer is TL.InputPeerChannel;

                            for (int i = 0; i < kvp.Value.Count; i += batchSize)
                            {
                                var count = Math.Min(batchSize, kvp.Value.Count - i);
                                var batch = kvp.Value.GetRange(i, count).ToArray();

                                await DeleteBatchWithBisectAsync(peer, isChannel, batch);
                            }
                        }
                        catch (Exception ex) when (kvp.Key != 0L && (ex.Message.Contains("не найден") || ex.Message.Contains("CHANNEL_INVALID") || ex.Message.Contains("CHANNEL_PRIVATE")))
                        {
                            AppLogger.Info("TelegramService", $"[DeletionQueue] Канал Telegram ID {kvp.Key} уже удален или недоступен. {kvp.Value.Count} сообщений считаются очищенными.");
                        }
                    }
                }
                return true;
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 420)
            {
                AppLogger.Warn("TelegramService", $"Удаление сообщений приостановлено из-за FLOOD_WAIT ({rpcEx.X} сек).");
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Error("TelegramService", $"Критическая ошибка при удалении сообщений из Telegram: {ex.Message}", ex);
                return false;
            }
        }

        /// <summary>
        /// Отказоустойчивое удаление пакета сообщений с применением алгоритма бинарного деления (Биссекции).
        /// При возникновении ошибки (например, MESSAGE_ID_INVALID из-за уже удаленного сообщения)
        /// пакет делится пополам, позволяя успешно удалить все валидные сообщения за минимальное число запросов.
        /// </summary>
        private async Task DeleteBatchWithBisectAsync(TL.InputPeer peer, bool isChannel, int[] ids)
        {
            if (ids == null || ids.Length == 0 || _client == null) return;
            await EnsureBackgroundFloodWaitDelayAsync();
            await EnsureBackgroundPacingDelayAsync();

            AppLogger.Debug("TelegramService", $"[DeleteBatchWithBisectAsync] Отправка запроса на удаление {ids.Length} сообщений: [{string.Join(", ", ids)}]");

            try
            {
                BeginBackgroundRpc();
                try
                {
                    if (isChannel && peer is TL.InputPeerChannel pc)
                    {
                        var channel = new TL.InputChannel(pc.channel_id, pc.access_hash);
                        var deleteReq = new TL.Methods.Channels_DeleteMessages
                        {
                            channel = channel,
                            id = ids
                        };
                        await _client.Invoke(deleteReq);
                    }
                    else
                    {
                        var deleteReq = new TL.Methods.Messages_DeleteMessages
                        {
                            id = ids
                        };
                        await _client.Invoke(deleteReq);
                    }
                }
                finally
                {
                    EndBackgroundRpc();
                }

                AppLogger.Info("TelegramService", $"Пакет из {ids.Length} сообщений успешно удален из Telegram.");
                AppLogger.Debug("TelegramService", $"Удалены Telegram ID: [{string.Join(", ", ids)}]");
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 420) // FLOOD_WAIT_X — не бисектируем и не теряем сообщения!
            {
                TriggerBackgroundFloodWait(rpcEx.X, $"удаление {ids.Length} сообщений");
                throw;
            }
            catch (Exception ex)
            {
                // Если остался всего 1 элемент и он упал (например, MESSAGE_ID_INVALID или уже удален в TG вручную)
                if (ids.Length == 1)
                {
                    AppLogger.Warn("TelegramService", $"Пропущено невалидное или уже удаленное сообщение ID {ids[0]}: {ex.Message}");
                    return;
                }

                // Иначе делим группу пополам (биссекция)
                int mid = ids.Length / 2;
                var left = ids.Take(mid).ToArray();
                var right = ids.Skip(mid).ToArray();

                AppLogger.Warn("TelegramService", $"Сбой при пакетном удалении группы из {ids.Length} сообщений ({ex.Message}). Разделяем пополам на {left.Length} и {right.Length}...");
                await DeleteBatchWithBisectAsync(peer, isChannel, left);
                await DeleteBatchWithBisectAsync(peer, isChannel, right);
            }
        }

        private static readonly object _channelIconLock = new();
        private static byte[]? _cachedChannelIconJpeg;

        /// <summary>
        /// Возвращает кэшированный JPEG-снимок встроенной иконки приложения (256x256) для аватарки каналов Telegram.
        /// </summary>
        private static byte[]? GetChannelIconJpegBytes()
        {
            lock (_channelIconLock)
            {
                if (_cachedChannelIconJpeg != null)
                    return _cachedChannelIconJpeg;

                try
                {
                    using var iconImage = TGramFS.UI.AppIconHelper.GetAppIconBitmap(256, 256);
                    if (iconImage == null)
                        return null;

                    using var canvas = new System.Drawing.Bitmap(256, 256, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    using (var g = System.Drawing.Graphics.FromImage(canvas))
                    {
                        g.Clear(System.Drawing.Color.White);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        g.DrawImage(iconImage, 0, 0, 256, 256);
                    }

                    using var outMs = new MemoryStream();
                    var encoder = GetEncoder(System.Drawing.Imaging.ImageFormat.Jpeg);
                    if (encoder != null)
                    {
                        using var encoderParams = new System.Drawing.Imaging.EncoderParameters(1);
                        encoderParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                        canvas.Save(outMs, encoder, encoderParams);
                    }
                    else
                    {
                        canvas.Save(outMs, System.Drawing.Imaging.ImageFormat.Jpeg);
                    }

                    _cachedChannelIconJpeg = outMs.ToArray();
                    return _cachedChannelIconJpeg;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("TelegramService", $"Не удалось подготовить JPEG-иконку канала из ресурсов: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Устанавливает стандартную иконку приложения в качестве аватарки нового канала Telegram.
        /// Ошибки загрузки фото не прерывают процесс создания канала.
        /// </summary>
        private async Task TrySetDefaultChannelPhotoAsync(TL.Channel channel)
        {
            if (_client == null || !IsAuthorized || channel == null || channel.ID == 0)
                return;

            try
            {
                byte[]? jpegBytes = GetChannelIconJpegBytes();
                if (jpegBytes == null || jpegBytes.Length == 0)
                {
                    AppLogger.Warn("TelegramService", $"Не удалось подготовить иконку канала для '{channel.Title}' (ID: {channel.ID}).");
                    return;
                }

                await EnsureBackgroundFloodWaitDelayAsync();

                BeginBackgroundRpc();
                try
                {
                    using var ms = new MemoryStream(jpegBytes, writable: false);
                    var inputFile = await _client.UploadFileAsync(ms, "tgramfs_channel_icon.jpg");
                    var photo = new TL.InputChatUploadedPhoto
                    {
                        flags = TL.InputChatUploadedPhoto.Flags.has_file,
                        file = inputFile
                    };

                    await _client.Channels_EditPhoto(new TL.InputChannel(channel.ID, channel.access_hash), photo);
                }
                finally
                {
                    EndBackgroundRpc();
                }
                AppLogger.Info("TelegramService", $"Установлена стандартная иконка для канала '{channel.Title}' (ID: {channel.ID}).");
            }
            catch (TL.RpcException rpcEx) when (rpcEx.Code == 420)
            {
                TriggerBackgroundFloodWait(rpcEx.X, $"иконка канала '{channel.Title}'");
                AppLogger.Warn("TelegramService", $"Не удалось установить иконку для канала '{channel.Title}' (ID: {channel.ID}) из-за FLOOD_WAIT ({rpcEx.X} сек).");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TelegramService", $"Не удалось установить иконку для канала '{channel.Title}' (ID: {channel.ID}): {ex.Message}");
            }
        }

        private static bool IsAudioFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext is ".mp3" or ".flac" or ".aac" or ".m4a" or ".ogg" or ".wav" or ".wma" or ".opus" or ".alac" or ".aiff" or ".ape";
        }

        public void Dispose()
        {
            try
            {
                AppLogger.OnWTelegramLog -= HandleWTelegramLog;
            }
            catch { }
            try
            {
                System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
            }
            catch { }
            try
            {
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            }
            catch { }
            try
            {
                lock (_reconnectWaitLock)
                {
                    _reconnectWaitCts?.Cancel();
                    _reconnectWaitCts?.Dispose();
                    _reconnectWaitCts = null;
                }
            }
            catch { }
            try
            {
                _deletionQueueCts?.Cancel();
                _deletionQueueCts?.Dispose();
                _deletionQueueCts = null;
            }
            catch { }
            try
            {
                _workerPool?.Dispose();
                _workerPool = null;
            }
            catch { }
            try
            {
                _client?.Dispose();
                _client = null;
            }
            catch { }
            _floodLock?.Dispose();
            _uploadSemaphore?.Dispose();
            _channelDeleteSemaphore?.Dispose();
        }
    }
}
