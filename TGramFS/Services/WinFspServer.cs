using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fsp;
using Fsp.Interop;
using Microsoft.Win32;
using TGramFS.Config;
using TGramFS.Database;
using TGramFS.Models;
using FileInfo = Fsp.Interop.FileInfo;

namespace TGramFS.Services
{
    /// <summary>
    /// Сервис виртуальной файловой системы WinFsp (FUSE для Windows).
    /// Предоставляет нативный поблочный доступ к файлам Telegram прямо в оперативную память,
    /// минуя системную службу Windows WebClient и кэширование на системном диске C: (TfsStore\Tfs_DAV).
    /// </summary>
    public class WinFspServer : IDisposable
    {
        private readonly ConfigManager _configManager;
        private readonly NodeRepository _repository;
        private readonly TelegramService _telegramService;
        private FileSystemHost? _host;
        private static string? _activeMountPoint;
        public static string? ActiveMountPoint => _activeMountPoint;
        private readonly object _lock = new object();

        public bool IsMounted => _host != null;
        public string? MountPoint => _activeMountPoint;

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        private static bool _dllLoaded = false;
        private static readonly object _dllLock = new object();

        /// <summary>
        /// Гарантирует регистрацию нативной библиотеки WinFsp (winfsp-x64.dll) в системном пути DLL Windows.
        /// </summary>
        public static bool EnsureWinFspNativeDllLoaded()
        {
            if (_dllLoaded) return true;
            lock (_dllLock)
            {
                if (_dllLoaded) return true;
                try
                {
                    string? installDir = null;
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WinFsp") ??
                                     Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\WinFsp"))
                    {
                        if (key != null)
                        {
                            installDir = key.GetValue("InstallDir") as string;
                        }
                    }

                    if (string.IsNullOrEmpty(installDir))
                    {
                        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                        if (Directory.Exists(Path.Combine(pf86, "WinFsp"))) installDir = Path.Combine(pf86, "WinFsp");
                        else if (Directory.Exists(Path.Combine(pf, "WinFsp"))) installDir = Path.Combine(pf, "WinFsp");
                    }

                    if (!string.IsNullOrEmpty(installDir))
                    {
                        string binPath = Path.Combine(installDir, "bin");
                        if (Directory.Exists(binPath))
                        {
                            SetDllDirectory(binPath);
                            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                            if (!currentPath.Contains(binPath, StringComparison.OrdinalIgnoreCase))
                            {
                                Environment.SetEnvironmentVariable("PATH", binPath + Path.PathSeparator + currentPath);
                            }

                            // Выбираем соответствующую DLL по архитектуре процесса (x64 или x86)
                            string dllName = Environment.Is64BitProcess ? "winfsp-x64.dll" : "winfsp-x86.dll";
                            string fullDllPath = Path.Combine(binPath, dllName);

                            if (File.Exists(fullDllPath))
                            {
                                try
                                {
                                    // Прямая предзагрузка нативной библиотеки в память процесса Windows через .NET 8 API
                                    NativeLibrary.Load(fullDllPath);
                                    AppLogger.Info("WinFsp", $"Нативная библиотека {dllName} успешно предзагружена в память процесса из {fullDllPath}");
                                }
                                catch (Exception loadEx)
                                {
                                    AppLogger.Warn("WinFsp", $"Не удалось предзагрузить {dllName} через NativeLibrary.Load: {loadEx.Message}");
                                }
                            }

                            AppLogger.Info("WinFsp", $"Зарегистрирован путь к нативным DLL WinFsp: {binPath}");
                        }
                    }

                    _dllLoaded = true;
                    return true;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("WinFsp", $"Предупреждение при регистрации путей WinFsp: {ex.Message}");
                    return false;
                }
            }
        }

        public WinFspServer(
            ConfigManager configManager,
            NodeRepository repository,
            TelegramService telegramService)
        {
            _configManager = configManager;
            _repository = repository;
            _telegramService = telegramService;
            EnsureWinFspNativeDllLoaded();
        }

        /// <summary>
        /// Проверяет, установлен ли драйвер WinFsp в текущей операционной системе Windows.
        /// </summary>
        public static bool IsAvailable()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            EnsureWinFspNativeDllLoaded();

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WinFsp") ??
                                Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\WinFsp");
                if (key != null)
                {
                    string? installDir = key.GetValue("InstallDir") as string;
                    if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
                        return true;
                }
            }
            catch
            {
                // Игнорируем ошибки доступа к реестру
            }

            // Дополнительная проверка наличия в системной папке или PATH
            try
            {
                string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (File.Exists(Path.Combine(pf86, "WinFsp", "bin", "winfsp-x64.dll")) ||
                    File.Exists(Path.Combine(pf, "WinFsp", "bin", "winfsp-x64.dll")))
                {
                    return true;
                }

                string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                if (File.Exists(Path.Combine(systemDir, "winfsp-x64.dll")) ||
                    File.Exists(Path.Combine(systemDir, "winfsp-x86.dll")))
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Монтирует файловую систему на указанную букву диска (например "Z:" или "Z:\").
        /// </summary>
        public bool Start(string driveLetter, out string errorMessage)
        {
            errorMessage = string.Empty;

            lock (_lock)
            {
                if (IsMounted)
                {
                    Stop();
                }

                string formattedLetter = driveLetter.Trim().ToUpper();
                if (!formattedLetter.EndsWith(":")) formattedLetter += ":";

                try
                {
                    var fileSystem = new TelegramWinFspFileSystem(_configManager, _repository, _telegramService);
                    _host = new FileSystemHost(fileSystem);

                    var settings = _configManager.Load();
                    _host.FileSystemName = "NTFS";
                    _host.VolumeCreationTime = (ulong)DateTime.UtcNow.ToFileTimeUtc();
                    _host.VolumeSerialNumber = 0x54454C47; // TELG
                    _host.SectorSize = 4096;
                    _host.SectorsPerAllocationUnit = 1;
                    _host.CaseSensitiveSearch = false;
                    _host.CasePreservedNames = true;
                    _host.UnicodeOnDisk = true;

                    AppLogger.Info("WinFsp", $"Попытка монтирования виртуального диска {formattedLetter} через WinFsp...");

                    int result = _host.Mount(formattedLetter, TelegramWinFspFileSystem.DefaultSecurityDescriptor, false, 0);
                    if (result != FileSystemBase.STATUS_SUCCESS)
                    {
                        errorMessage = $"Код ошибки WinFsp: 0x{result:X8}";
                        AppLogger.Warn("WinFsp", $"Не удалось смонтировать диск {formattedLetter}: {errorMessage}");
                        _host.Dispose();
                        _host = null;
                        return false;
                    }

                    _activeMountPoint = formattedLetter;
                    AppLogger.Info("WinFsp", $"Виртуальный диск {formattedLetter} успешно смонтирован через WinFsp (прямой стриминг в ОЗУ).");
                    return true;
                }
                catch (Exception ex)
                {
                    string details = ex.InnerException != null ? $"{ex.Message} (Детали: {ex.InnerException.Message})" : ex.Message;
                    errorMessage = details;
                    AppLogger.Error("WinFsp", $"Исключение при монтировании диска {formattedLetter}: {details}", ex);
                    if (_host != null)
                    {
                        try { _host.Dispose(); } catch { }
                        _host = null;
                    }
                    return false;
                }
            }
        }

        /// <summary>
        /// Размонтирует файловую систему WinFsp.
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                if (_host != null)
                {
                    try
                    {
                        AppLogger.Info("WinFsp", $"Размонтирование диска {_activeMountPoint}...");
                        _host.Unmount();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("WinFsp", $"Предупреждение при размонтировании WinFsp: {ex.Message}");
                    }
                    finally
                    {
                        _host.Dispose();
                        _host = null;
                        _activeMountPoint = null;
                    }
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    /// <summary>
    /// Реализация интерфейсов ядра файловой системы Windows для WinFsp.
    /// Поддерживает полноценное чтение (прямой стриминг MTProto чанков в память плеера) 
    /// и запись (копирование файлов в Telegram, создание папок, переименование и удаление).
    /// </summary>
    internal class TelegramWinFspFileSystem : FileSystemBase
    {
        private readonly ConfigManager _configManager;
        private readonly NodeRepository _repository;
        private readonly TelegramService _telegramService;
        private int _lastListedDirId = -1;
        private DateTime _lastListedTime = DateTime.MinValue;
        private readonly object _dirListingLock = new object();

        private class ActiveVirtualCopyInfo
        {
            public int SourceNodeId { get; set; }
            public int TargetNodeId { get; set; }
            public int CreatorPid { get; set; }
            public DateTime CreatedAtUtc { get; set; }
        }

        private static readonly ConcurrentDictionary<int, ActiveVirtualCopyInfo> _activeVirtualCopies = new();
        private static readonly ConcurrentDictionary<string, Node> _activeUploadNodes = new(StringComparer.OrdinalIgnoreCase);
        private static int _nextTempNodeId = 0;

        private const uint NT_FILE_CREATE = 2u;
        private const uint FILE_READ_DATA = 0x00000001u;
        private const uint FILE_WRITE_DATA = 0x00000002u;
        private const uint FILE_APPEND_DATA = 0x00000004u;

        private class RecentVirtualSignal
        {
            public Node SourceNode { get; set; } = null!;
            public string VirtualPath { get; set; } = "";
            public string ParentPath { get; set; } = "/";
            public int ProcessId { get; set; }
            public DateTime TimestampUtc { get; set; }
            public bool Consumed { get; set; }
        }

        private class TwoPassVirtualCopyInfo
        {
            public int NodeId { get; set; }
            public int SourceNodeId { get; set; }
            public long TargetSize { get; set; }
            public int CreatorPid { get; set; }
            public DateTime CreatedAtUtc { get; set; }
        }

        private static readonly object _virtualSignalsLock = new object();
        private static readonly List<RecentVirtualSignal> _recentVirtualCollisions = new();
        private static readonly List<RecentVirtualSignal> _recentVirtualReadOpens = new();
        private static readonly ConcurrentDictionary<string, TwoPassVirtualCopyInfo> _recentTwoPassVirtualCopies = new(StringComparer.OrdinalIgnoreCase);

        // Временная диагностика WinFsp для трассировки Create/Open/GetSecurityByName/Cleanup/Close
        private static readonly ConcurrentDictionary<string, string> _diagLastSecByName = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> _diagLastOpen = new(StringComparer.OrdinalIgnoreCase);

        private static bool IsDiagNoisePath(string cleanPath)
        {
            if (string.IsNullOrEmpty(cleanPath) || cleanPath == "/" || cleanPath.Contains(':'))
                return true;
            string name = GetFileName(cleanPath);
            return string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "autorun.inf", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "thumbs.db", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "folder.jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "folder.gif", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetDiagProcessName(int pid)
        {
            if (pid <= 0) return pid.ToString();
            try
            {
                return $"{Process.GetProcessById(pid).ProcessName}({pid})";
            }
            catch
            {
                return pid.ToString();
            }
        }

        private static void RecordDiagHistory(ConcurrentDictionary<string, string> map, string cleanPath, string summary)
        {
            if (map.Count > 512) map.Clear();
            map[cleanPath] = summary;
        }

        private static void RecordVirtualSignal(List<RecentVirtualSignal> list, string cleanPath, string parentPath, Node sourceNode, int pid)
        {
            if (sourceNode.IsDir || string.IsNullOrWhiteSpace(cleanPath))
                return;

            DateTime now = DateTime.UtcNow;
            DateTime cutoff = now.AddSeconds(-30);
            lock (_virtualSignalsLock)
            {
                list.RemoveAll(s => s.TimestampUtc < cutoff ||
                    (s.SourceNode.Id == sourceNode.Id && s.ProcessId == pid && string.Equals(s.VirtualPath, cleanPath, StringComparison.OrdinalIgnoreCase)));
                if (list.Count >= 64)
                {
                    list.RemoveRange(0, list.Count - 48);
                }
                list.Add(new RecentVirtualSignal
                {
                    SourceNode = sourceNode,
                    VirtualPath = cleanPath,
                    ParentPath = parentPath,
                    ProcessId = pid,
                    TimestampUtc = now,
                    Consumed = false
                });
            }
        }

        private static void RecordVirtualCollision(string cleanPath, string parentPath, Node sourceNode, int pid)
            => RecordVirtualSignal(_recentVirtualCollisions, cleanPath, parentPath, sourceNode, pid);

        private static void RecordVirtualReadOpen(string cleanPath, string parentPath, Node sourceNode, int pid)
        {
            if ((sourceNode.TgMessageId ?? 0) > 0)
            {
                RecordVirtualSignal(_recentVirtualReadOpens, cleanPath, parentPath, sourceNode, pid);
            }
        }

        private static HashSet<string>? GetRecentCollisionNames(string? targetVirtualPath, int callerPid)
        {
            if (string.IsNullOrWhiteSpace(targetVirtualPath))
                return null;

            string targetParent = GetParentPath(targetVirtualPath);
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-20);
            HashSet<string>? names = null;
            lock (_virtualSignalsLock)
            {
                foreach (var c in _recentVirtualCollisions)
                {
                    if (c.TimestampUtc < cutoff) continue;
                    if (!string.Equals(c.ParentPath, targetParent, StringComparison.OrdinalIgnoreCase)) continue;
                    if (callerPid > 0 && c.ProcessId > 0 && c.ProcessId != callerPid) continue;
                    if (string.Equals(c.VirtualPath, targetVirtualPath, StringComparison.OrdinalIgnoreCase)) continue;

                    names ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    names.Add(c.SourceNode.Name);
                }
            }
            return names;
        }

        private static void RegisterTwoPassVirtualCopy(string cleanPath, int targetNodeId, int sourceNodeId, long targetSize, int creatorPid)
        {
            if (string.IsNullOrWhiteSpace(cleanPath) || targetNodeId <= 0 || targetSize <= 0)
                return;

            string norm = NormalizePath(cleanPath);
            DateTime now = DateTime.UtcNow;
            if (_recentTwoPassVirtualCopies.Count > 256)
            {
                DateTime cutoff = now.AddSeconds(-20);
                foreach (var kvp in _recentTwoPassVirtualCopies)
                {
                    if (kvp.Value.CreatedAtUtc < cutoff)
                    {
                        _recentTwoPassVirtualCopies.TryRemove(kvp.Key, out _);
                    }
                }
            }

            _recentTwoPassVirtualCopies[norm] = new TwoPassVirtualCopyInfo
            {
                NodeId = targetNodeId,
                SourceNodeId = sourceNodeId,
                TargetSize = targetSize,
                CreatorPid = creatorPid,
                CreatedAtUtc = now
            };
        }

        private static bool TryGetTwoPassVirtualCopy(string cleanPath, int nodeId, int openPid, out TwoPassVirtualCopyInfo info)
        {
            info = null!;
            if (string.IsNullOrWhiteSpace(cleanPath) || nodeId <= 0)
                return false;

            string norm = NormalizePath(cleanPath);
            if (_recentTwoPassVirtualCopies.TryGetValue(norm, out var candidate))
            {
                bool notExpired = (DateTime.UtcNow - candidate.CreatedAtUtc) <= TimeSpan.FromSeconds(15);
                bool sameNode = candidate.NodeId == nodeId;
                bool samePid = candidate.CreatorPid <= 0 || openPid <= 4 || candidate.CreatorPid == openPid;
                if (notExpired && sameNode && samePid)
                {
                    info = candidate;
                    return true;
                }
                if (!notExpired)
                {
                    _recentTwoPassVirtualCopies.TryRemove(norm, out _);
                }
            }
            return false;
        }

        public static void RegisterActiveVirtualCopy(int targetNodeId, int sourceNodeId, int creatorPid)
        {
            _activeVirtualCopies[targetNodeId] = new ActiveVirtualCopyInfo
            {
                SourceNodeId = sourceNodeId,
                TargetNodeId = targetNodeId,
                CreatorPid = creatorPid,
                CreatedAtUtc = DateTime.UtcNow
            };
            AppLogger.Debug("WinFsp", $"[VirtualCopy] Зарегистрирована активная операция виртуального копирования: TargetNodeId={targetNodeId}, SourceNodeId={sourceNodeId}, CreatorPid={creatorPid}");
        }

        public static void UnregisterActiveVirtualCopy(int targetNodeId)
        {
            if (_activeVirtualCopies.TryRemove(targetNodeId, out var removed))
            {
                AppLogger.Debug("WinFsp", $"[VirtualCopy] Завершена активная операция виртуального копирования: TargetNodeId={targetNodeId}, SourceNodeId={removed.SourceNodeId}");
            }
        }

        public static bool IsVirtualCopySourceActive(int sourceNodeId, int callerPid)
        {
            if (_activeVirtualCopies.IsEmpty) return false;

            DateTime now = DateTime.UtcNow;
            foreach (var kvp in _activeVirtualCopies)
            {
                var info = kvp.Value;
                // Срок действия активной копии - до 2 минут (на случай зависших/аварийных дескрипторов)
                if (now - info.CreatedAtUtc > TimeSpan.FromMinutes(2))
                {
                    _activeVirtualCopies.TryRemove(kvp.Key, out _);
                    continue;
                }

                if (info.SourceNodeId == sourceNodeId)
                {
                    // Совпал исходный файл. Проверяем вызывающий процесс:
                    // Либо тот же процесс-создатель копии (например explorer.exe), либо системный поток ядра Windows (PID <= 4)
                    if (callerPid <= 4 || info.CreatorPid <= 0 || callerPid == info.CreatorPid)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private const int NT_STATUS_SUCCESS = 0;
        private const int NT_STATUS_UNSUCCESSFUL = unchecked((int)0xC0000001);
        private const int NT_STATUS_END_OF_FILE = unchecked((int)0xC0000011);
        private const int NT_STATUS_OBJECT_NAME_NOT_FOUND = unchecked((int)0xC0000034);
        private const int NT_STATUS_OBJECT_NAME_COLLISION = unchecked((int)0xC0000035);
        private const int NT_STATUS_OBJECT_PATH_NOT_FOUND = unchecked((int)0xC000003A);
        private const int NT_STATUS_SHARING_VIOLATION = unchecked((int)0xC0000043);
        private const int NT_STATUS_FILE_IS_A_DIRECTORY = unchecked((int)0xC00000BA);
        private const int NT_STATUS_NOT_A_DIRECTORY = unchecked((int)0xC0000103);
        private const int NT_STATUS_ACCESS_DENIED = unchecked((int)0xC0000022);
        private const int NT_STATUS_DIRECTORY_NOT_EMPTY = unchecked((int)0xC0000101);
        private const int NT_STATUS_CANNOT_DELETE = unchecked((int)0xC0000121);
        private const int NT_STATUS_IO_TIMEOUT = unchecked((int)0xC00000B5);
        private const int NT_STATUS_NETWORK_UNREACHABLE = unchecked((int)0xC000023C);

        private static int _isNetworkOfflineLogged = 0;

        /// <summary>
        /// Сбрасывает флаг зафиксированного сбоя сети при восстановлении соединения,
        /// обеспечивая ровно одно чистое предупреждение при следующем падении.
        /// </summary>
        public static void ResetNetworkOfflineState()
        {
            Interlocked.Exchange(ref _isNetworkOfflineLogged, 0);
        }

        private static void LogNetworkWarning(string fileName, string reason)
        {
            // State-Based Logging: логируем строго 1 раз при переходе в офлайн.
            // Пока сеть лежит — 100% тишина в логе для всех последующих файлов и запросов.
            if (Interlocked.CompareExchange(ref _isNetworkOfflineLogged, 1, 0) == 0)
            {
                AppLogger.Warn("WinFsp", $"[Network] Сеть Telegram недоступна при чтении '{fileName}': {reason}");
            }
        }

        private static bool IsNetworkException(Exception ex)
        {
            var baseEx = ex.GetBaseException();
            return baseEx is TelegramNetworkException
                || baseEx is System.Net.Sockets.SocketException
                || baseEx is TimeoutException
                || (baseEx is IOException io && io.Message.Contains("разорвала установленное подключение"))
                || baseEx.Message.Contains("Клиент Telegram не подключен")
                || baseEx.Message.Contains("Сеть Telegram недоступна");
        }

        public TelegramWinFspFileSystem(
            ConfigManager configManager,
            NodeRepository repository,
            TelegramService telegramService)
        {
            _configManager = configManager;
            _repository = repository;
            _telegramService = telegramService;
        }

        public override int Init(object host)
        {
            return STATUS_SUCCESS;
        }

        // Дескриптор безопасности:
        // O:WD - Owner: World (Everyone / Все пользователи)
        // G:WD - Group: World (Everyone / Все пользователи)
        // D:P(A;;FA;;;WD) - DACL: Protected, Allow Full Access (FA) to World (WD)
        // Гарантирует полный неограниченный доступ для всех пользователей и программ Windows
        public static readonly byte[] DefaultSecurityDescriptor = CreateDefaultSecurityDescriptor();

        private static byte[] CreateDefaultSecurityDescriptor()
        {
            try
            {
                var raw = new RawSecurityDescriptor("O:WDG:WDD:P(A;;FA;;;WD)");
                byte[] binary = new byte[raw.BinaryLength];
                raw.GetBinaryForm(binary, 0);
                return binary;
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        public override int GetVolumeInfo(out VolumeInfo volumeInfo)
        {
            volumeInfo = default;
            var settings = _configManager.CurrentSettings;
            long usedBytes = _repository.GetTotalUsedSpaceBytes(settings.Server.IncludeTrashInUsedSpace);

            long baseCapacityGb = settings.Server.VirtualDiskCapacityGb > 0 ? settings.Server.VirtualDiskCapacityGb : 1024;
            long totalCapacityBytes = baseCapacityGb * 1024L * 1024L * 1024L;

            // Если включено авто-расширение: если занято более 70%, динамически увеличиваем емкость,
            // чтобы диск в Проводнике всегда имел >= 30% свободного места и не окрашивался в красный цвет
            if (settings.Server.AutoExpandDiskCapacity && usedBytes > 0)
            {
                double usageRatio = (double)usedBytes / totalCapacityBytes;
                if (usageRatio >= 0.70)
                {
                    long requiredCapacity = (long)(usedBytes / 0.70);
                    long step = 100L * 1024L * 1024L * 1024L; // Шаг округления 100 ГБ
                    long rounded = ((requiredCapacity + step - 1) / step) * step;
                    totalCapacityBytes = Math.Max(totalCapacityBytes, rounded);
                }
            }

            long freeBytes = Math.Max(0, totalCapacityBytes - usedBytes);

            volumeInfo.TotalSize = (ulong)totalCapacityBytes;
            volumeInfo.FreeSize = (ulong)freeBytes;
            string driveName = settings.Server.DriveName ?? "Telegram Drive";
            volumeInfo.SetVolumeLabel(driveName);
            return STATUS_SUCCESS;
        }

        public override int GetSecurity(object fileNode, object fileDesc, ref byte[] securityDescriptor)
        {
            securityDescriptor = DefaultSecurityDescriptor;
            return STATUS_SUCCESS;
        }

        public override int GetSecurityByName(
            string fileName,
            out uint fileAttributes,
            ref byte[] securityDescriptor)
        {
            bool inputSecWasNull = securityDescriptor == null;
            securityDescriptor = DefaultSecurityDescriptor;
            string cleanPath = NormalizePath(fileName);
            if (cleanPath.Contains(":")) // Alternate Data Stream (:Zone.Identifier и др.)
            {
                fileAttributes = 0;
                return NT_STATUS_OBJECT_NAME_NOT_FOUND;
            }

            if (cleanPath == "/")
            {
                fileAttributes = (uint)FileAttributes.Directory;
                return STATUS_SUCCESS;
            }

            var dbNode = _repository.GetNodeByPath(cleanPath);
            Node? activeNode = null;
            var node = dbNode;
            if (node == null && _activeUploadNodes.TryGetValue(cleanPath, out activeNode))
            {
                node = activeNode;
            }

            int secPid = 0;
            try { secPid = FileSystemBase.GetOperationProcessId(); } catch { }

            if (node == null)
            {
                fileAttributes = 0;
                string missSummary = $"{DateTime.UtcNow:HH:mm:ss.fff} NOT_FOUND pid={secPid} secInNull={inputSecWasNull} raw='{fileName}'";
                RecordDiagHistory(_diagLastSecByName, cleanPath, missSummary);
                if (!IsDiagNoisePath(cleanPath))
                {
                    AppLogger.Debug("WinFsp", $"[DIAG GetSecurityByName] '{cleanPath}' -> NOT_FOUND ({missSummary})");
                }
                return NT_STATUS_OBJECT_NAME_NOT_FOUND;
            }

            fileAttributes = node.IsDir ? (uint)FileAttributes.Directory : (uint)FileAttributes.Normal;
            string hitSummary = $"{DateTime.UtcNow:HH:mm:ss.fff} OK pid={secPid} dbId={dbNode?.Id.ToString() ?? "null"} actId={activeNode?.Id.ToString() ?? "null"} isDir={node.IsDir} size={node.Size} attrs=0x{fileAttributes:X} secInNull={inputSecWasNull} secOut={securityDescriptor?.Length ?? 0} raw='{fileName}'";
            RecordDiagHistory(_diagLastSecByName, cleanPath, hitSummary);
            if (!IsDiagNoisePath(cleanPath))
            {
                AppLogger.Debug("WinFsp", $"[DIAG GetSecurityByName] '{cleanPath}' -> {hitSummary}");
            }
            return STATUS_SUCCESS;
        }

        public override int Open(
            string fileName,
            uint createOptions,
            uint grantedAccess,
            out object fileNode,
            out object fileDesc,
            out FileInfo fileInfo,
            out string normalizedName)
        {
            string cleanPath = NormalizePath(fileName);
            if (cleanPath.Contains(":"))
            {
                fileNode = null!;
                fileDesc = null!;
                fileInfo = default;
                normalizedName = null!;
                return NT_STATUS_OBJECT_NAME_NOT_FOUND;
            }

            int openPid = 0;
            try { openPid = FileSystemBase.GetOperationProcessId(); } catch { }

            Node? node;
            if (cleanPath == "/")
            {
                node = _repository.GetRootNode() ?? new Node
                {
                    Id = 1,
                    ParentId = null,
                    Name = "/",
                    IsDir = true,
                    Size = 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
            }
            else
            {
                node = _repository.GetNodeByPath(cleanPath);
                if (node == null)
                {
                    if (_activeUploadNodes.TryGetValue(cleanPath, out var activeOpenNode))
                    {
                        string shareSummary = $"{DateTime.UtcNow:HH:mm:ss.fff} SHARING_VIOLATION pid={openPid} actId={activeOpenNode.Id} opts=0x{createOptions:X8} access=0x{grantedAccess:X8} raw='{fileName}'";
                        RecordDiagHistory(_diagLastOpen, cleanPath, shareSummary);
                        AppLogger.Info("WinFsp", $"[DIAG Open] '{cleanPath}' -> {shareSummary}");
                        fileNode = null!;
                        fileDesc = null!;
                        fileInfo = default;
                        normalizedName = null!;
                        return NT_STATUS_SHARING_VIOLATION;
                    }

                    string missOpenSummary = $"{DateTime.UtcNow:HH:mm:ss.fff} NOT_FOUND pid={openPid} opts=0x{createOptions:X8} access=0x{grantedAccess:X8} raw='{fileName}'";
                    RecordDiagHistory(_diagLastOpen, cleanPath, missOpenSummary);
                    AppLogger.Warn("WinFsp", $"[Open NOT FOUND] Проводник пытается открыть элемент '{fileName}' (path: '{cleanPath}', pid={openPid}, opts=0x{createOptions:X8}, access=0x{grantedAccess:X8}), но узел равен NULL.");
                    fileNode = null!;
                    fileDesc = null!;
                    fileInfo = default;
                    normalizedName = null!;
                    return NT_STATUS_OBJECT_NAME_NOT_FOUND;
                }
            }

            var openCtx = new FspNodeContext(node) { CreatorProcessId = openPid, VirtualPath = cleanPath };
            if (cleanPath != "/" && !node.IsDir)
            {
                if ((grantedAccess & FILE_READ_DATA) != 0 && (node.TgMessageId ?? 0) > 0)
                {
                    RecordVirtualReadOpen(cleanPath, GetParentPath(cleanPath), node, openPid);
                }

                if ((grantedAccess & (FILE_WRITE_DATA | FILE_APPEND_DATA)) != 0 &&
                    TryGetTwoPassVirtualCopy(cleanPath, node.Id, openPid, out var twoPassInfo))
                {
                    openCtx.IsVirtualCopyPassThrough = true;
                    openCtx.VirtualCopyPassThroughTargetSize = twoPassInfo.TargetSize;
                    openCtx.KnownTargetSize = twoPassInfo.TargetSize;
                    AppLogger.Info("WinFsp", $"[VirtualCopy Pass2] Обнаружен второй проход записи (Open opts=0x{createOptions:X8}, access=0x{grantedAccess:X8}) для '{cleanPath}' (ID {node.Id}, {AppLogger.FormatSizeDetailed(twoPassInfo.TargetSize)}). Повторная отправка в Telegram отключена.");
                }
            }

            fileNode = node;
            fileDesc = openCtx;
            FillFileInfo(node, out fileInfo);
            normalizedName = fileName;
            if (cleanPath != "/")
            {
                string okOpenSummary = $"{DateTime.UtcNow:HH:mm:ss.fff} OK pid={openPid} nodeId={node.Id} isDir={node.IsDir} size={node.Size} opts=0x{createOptions:X8} access=0x{grantedAccess:X8} passThrough={openCtx.IsVirtualCopyPassThrough} raw='{fileName}'";
                RecordDiagHistory(_diagLastOpen, cleanPath, okOpenSummary);
                if (!IsDiagNoisePath(cleanPath))
                {
                    AppLogger.Debug("WinFsp", $"[DIAG Open] '{cleanPath}' -> {okOpenSummary}");
                }
            }
            return STATUS_SUCCESS;
        }

        public override int Create(
            string fileName,
            uint createOptions,
            uint grantedAccess,
            uint fileAttributes,
            byte[] securityDescriptor,
            ulong allocationSize,
            out object fileNode,
            out object fileDesc,
            out FileInfo fileInfo,
            out string normalizedName)
        {
            string cleanPath = NormalizePath(fileName);
            if (cleanPath.Contains(":"))
            {
                fileNode = null!;
                fileDesc = null!;
                fileInfo = default;
                normalizedName = null!;
                return NT_STATUS_OBJECT_NAME_NOT_FOUND;
            }

            int callerPid = 0;
            try { callerPid = FileSystemBase.GetOperationProcessId(); } catch { }

            string parentPath = GetParentPath(cleanPath);
            string itemName = GetFileName(cleanPath);
            var parentNode = parentPath == "/" ? _repository.GetRootNode() : _repository.GetNodeByPath(parentPath);
            if (parentNode == null)
            {
                AppLogger.Info("WinFsp", $"[DIAG Create PATH_NOT_FOUND] raw='{fileName}', clean='{cleanPath}', parent='{parentPath}', pid={GetDiagProcessName(callerPid)}, opts=0x{createOptions:X8}");
                fileNode = null!;
                fileDesc = null!;
                fileInfo = default;
                normalizedName = null!;
                return NT_STATUS_OBJECT_PATH_NOT_FOUND;
            }

            uint createDisposition = (createOptions >> 24) & 0xFF;
            bool isDir = (createOptions & FILE_DIRECTORY_FILE) != 0;
            var diagDbExisting = _repository.GetNodeByPath(cleanPath);
            _activeUploadNodes.TryGetValue(cleanPath, out var diagActiveExisting);
            _diagLastSecByName.TryGetValue(cleanPath, out var diagLastSec);
            _diagLastOpen.TryGetValue(cleanPath, out var diagLastOpen);

            AppLogger.Info("WinFsp",
                $"[DIAG Create] raw='{fileName}', clean='{cleanPath}', isDir={isDir}, disp={createDisposition}, opts=0x{createOptions:X8}, access=0x{grantedAccess:X8}, attrs=0x{fileAttributes:X8}, alloc={allocationSize}, secLen={securityDescriptor?.Length ?? -1}, pid={GetDiagProcessName(callerPid)} | " +
                $"dbExisting={(diagDbExisting != null ? $"ID={diagDbExisting.Id},Dir={diagDbExisting.IsDir},Size={diagDbExisting.Size},Msg={diagDbExisting.TgMessageId?.ToString() ?? "null"},Trash={diagDbExisting.InTrash}" : "null")}, " +
                $"activeExisting={(diagActiveExisting != null ? $"ID={diagActiveExisting.Id},Size={diagActiveExisting.Size}" : "null")} | " +
                $"lastSec=[{diagLastSec ?? "none"}], lastOpen=[{diagLastOpen ?? "none"}]");

            if (!isDir && createDisposition == NT_FILE_CREATE && (diagDbExisting != null || diagActiveExisting != null))
            {
                if (diagDbExisting != null && !diagDbExisting.IsDir)
                {
                    RecordVirtualCollision(cleanPath, parentPath, diagDbExisting, callerPid);
                }

                AppLogger.Info("WinFsp", $"[Create] Коллизия FILE_CREATE для существующего файла '{cleanPath}' (dbId={diagDbExisting?.Id.ToString() ?? "null"}, actId={diagActiveExisting?.Id.ToString() ?? "null"}, pid={GetDiagProcessName(callerPid)}). Мгновенный возврат NT_STATUS_OBJECT_NAME_COLLISION.");
                fileNode = null!;
                fileDesc = null!;
                fileInfo = default;
                normalizedName = null!;
                return NT_STATUS_OBJECT_NAME_COLLISION;
            }

            if (isDir)
            {
                bool existed = diagDbExisting != null;
                var dirNode = _repository.EnsureDirectoryPathExists(cleanPath);
                if (dirNode == null)
                {
                    fileNode = null!;
                    fileDesc = null!;
                    fileInfo = default;
                    normalizedName = null!;
                    return NT_STATUS_UNSUCCESSFUL;
                }

                fileNode = dirNode;
                fileDesc = new FspNodeContext(dirNode) { CreatorProcessId = callerPid, VirtualPath = cleanPath };
                FillFileInfo(dirNode, out fileInfo);
                normalizedName = fileName;
                if (existed)
                {
                    AppLogger.Info("WinFsp", $"Открыт каталог: '{cleanPath}' (ID {dirNode.Id})");
                }
                else
                {
                    AppLogger.Info("WinFsp", $"Создан новый каталог: '{cleanPath}' (ID {dirNode.Id})");
                }
                return STATUS_SUCCESS;
            }
            else
            {
                long expectedSize = allocationSize > 0 ? (long)allocationSize : -1;
                var (virtualSource, originalSource) = ResolveFileSource(itemName, cleanPath, callerPid, _repository, expectedSize);
                if (virtualSource != null)
                {
                    AppLogger.Info("WinFsp", $"[VirtualCopy] Обнаружен исходный виртуальный файл для '{cleanPath}': ID {virtualSource.Id} ('{virtualSource.Name}'), размер: {AppLogger.FormatSizeDetailed(virtualSource.Size)}, MsgId: {virtualSource.TgMessageId?.ToString() ?? "null"}.");

                    int tempVirtualId = Interlocked.Decrement(ref _nextTempNodeId);
                    var vNode = new Node
                    {
                        Id = tempVirtualId,
                        ParentId = parentNode.Id,
                        Name = itemName,
                        IsDir = false,
                        Version = _repository.GetNextVersionForFile(parentNode.Id, itemName),
                        InTrash = false,
                        Size = virtualSource.Size,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _activeUploadNodes[cleanPath] = vNode;

                    var vCtx = new FspNodeContext(vNode)
                    {
                        IsModified = true,
                        KnownTargetSize = virtualSource.Size,
                        VirtualSourceNode = virtualSource,
                        CreatorProcessId = callerPid,
                        VirtualPath = cleanPath
                    };

                    RegisterActiveVirtualCopy(vNode.Id, virtualSource.Id, callerPid);

                    if (virtualSource.TgMessageId.HasValue && virtualSource.TgMessageId.Value > 0)
                    {
                        long? sourceChannelId = _repository.GetEffectiveChannelId(virtualSource.ParentId);
                        long? targetChannelId = _repository.GetEffectiveChannelId(parentNode.Id);

                        string parentFullPath = _repository.GetNodeFullPath(parentNode.Id);
                        if (parentFullPath == "/") parentFullPath = "";
                        int nextVersion = _repository.GetNextVersionForFile(parentNode.Id, itemName);
                        string ext = Path.GetExtension(itemName);
                        string nameNoExt = Path.GetFileNameWithoutExtension(itemName);
                        string fullPathWithVersion = $"{parentFullPath}/{nameNoExt}_v{nextVersion}{ext}";

                        AppLogger.Info("WinFsp", $"[VirtualCopy] Запуск мгновенного форвардинга сообщения #{virtualSource.TgMessageId.Value} в Telegram ({sourceChannelId} -> {targetChannelId})...");
                        vCtx.ForwardTask = Task.Run(() => _telegramService.ForwardFileAsync(
                            virtualSource.TgMessageId.Value,
                            virtualSource.TgPreviewMessageId,
                            sourceChannelId,
                            targetChannelId,
                            fullPathWithVersion,
                            vNode.Id
                        ));
                    }

                    fileNode = vNode;
                    fileDesc = vCtx;
                    FillFileInfo(vNode, out fileInfo);
                    normalizedName = fileName;
                    AppLogger.Info("WinFsp", $"Создан файл виртуальной копии (в памяти): '{cleanPath}' (размер: {AppLogger.FormatSizeDetailed(virtualSource.Size)})");
                    return STATUS_SUCCESS;
                }

                int tempNodeId = Interlocked.Decrement(ref _nextTempNodeId);
                var node = new Node
                {
                    Id = tempNodeId,
                    ParentId = parentNode.Id,
                    Name = itemName,
                    IsDir = false,
                    Version = _repository.GetNextVersionForFile(parentNode.Id, itemName),
                    InTrash = false,
                    Size = 0,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _activeUploadNodes[cleanPath] = node;

                var ctx = new FspNodeContext(node)
                {
                    IsModified = true,
                    KnownTargetSize = allocationSize > 0 ? (long)allocationSize : -1,
                    OriginalSourcePath = originalSource,
                    CreatorProcessId = callerPid,
                    VirtualPath = cleanPath
                };

                fileNode = node;
                fileDesc = ctx;
                FillFileInfo(node, out fileInfo);
                normalizedName = fileName;
                AppLogger.Info("WinFsp", $"Создан файл для записи (в памяти): '{cleanPath}' (alloc: {allocationSize})" + (!string.IsNullOrEmpty(ctx.OriginalSourcePath) ? $", источник: '{ctx.OriginalSourcePath}'" : ""));
                return STATUS_SUCCESS;
            }
        }

        public override int Overwrite(
            object fileNode,
            object fileDesc,
            uint fileAttributes,
            bool replaceFileAttributes,
            ulong allocationSize,
            out FileInfo fileInfo)
        {
            var node = (Node)fileNode;
            var ctx = (FspNodeContext)fileDesc;
            long prevSize = node.Size;

            int overwritePid = 0;
            try { overwritePid = FileSystemBase.GetOperationProcessId(); } catch { }
            if (overwritePid > 0)
            {
                ctx.CreatorProcessId = overwritePid;
            }
            int effectivePid = ctx.CreatorProcessId;

            if (ctx.IsVirtualCopyPassThrough)
            {
                long targetPassSize = ctx.VirtualCopyPassThroughTargetSize > 0 ? ctx.VirtualCopyPassThroughTargetSize : prevSize;
                node.Size = targetPassSize;
                node.UpdatedAt = DateTime.UtcNow;
                ctx.IsModified = true;
                ctx.TotalBytesWritten = 0;
                ctx.KnownTargetSize = targetPassSize;
                AppLogger.Info("WinFsp", $"[VirtualCopy Pass2 Overwrite] Сохранен проброс второго прохода для '{node.Name}' (ID {node.Id}, {AppLogger.FormatSizeDetailed(targetPassSize)}, alloc={allocationSize}).");
                FillFileInfo(node, out fileInfo);
                return STATUS_SUCCESS;
            }

            node.Size = 0;
            node.UpdatedAt = DateTime.UtcNow;
            ctx.Dispose();
            ctx.IsModified = true;
            ctx.KnownTargetSize = allocationSize > 0 ? (long)allocationSize : -1;
            ctx.TotalBytesWritten = 0;
            ctx.IsUploadStarted = false;
            ctx.PipeStream = null;
            ctx.UploadTask = null;
            ctx.UploadCts = null;

            string fullNodePath = _repository.GetNodeFullPath(node.Id);
            var (virtualSource, originalSource) = ResolveFileSource(node.Name, fullNodePath, effectivePid, _repository, ctx.KnownTargetSize);
            AppLogger.Info("WinFsp", $"[DIAG Overwrite] nodeId={node.Id}, name='{node.Name}', path='{fullNodePath}', prevSize={prevSize}, alloc={allocationSize}, attrs=0x{fileAttributes:X}, replaceAttrs={replaceFileAttributes}, pid={GetDiagProcessName(effectivePid)}, virtSrc={virtualSource?.Id.ToString() ?? "null"}, origSrc='{originalSource ?? "null"}'");
            if (virtualSource != null)
            {
                ctx.VirtualSourceNode = virtualSource;
                ctx.KnownTargetSize = virtualSource.Size;
                RegisterActiveVirtualCopy(node.Id, virtualSource.Id, effectivePid);
                if (virtualSource.TgMessageId.HasValue && virtualSource.TgMessageId.Value > 0)
                {
                    int parentId = node.ParentId ?? 1;
                    long? sourceChannelId = _repository.GetEffectiveChannelId(virtualSource.ParentId);
                    long? targetChannelId = _repository.GetEffectiveChannelId(parentId);

                    string parentFullPath = _repository.GetNodeFullPath(parentId);
                    if (parentFullPath == "/") parentFullPath = "";
                    int nextVersion = _repository.GetNextVersionForFile(parentId, node.Name);
                    string ext = Path.GetExtension(node.Name);
                    string nameNoExt = Path.GetFileNameWithoutExtension(node.Name);
                    string fullPathWithVersion = $"{parentFullPath}/{nameNoExt}_v{nextVersion}{ext}";

                    ctx.ForwardTask = Task.Run(() => _telegramService.ForwardFileAsync(
                        virtualSource.TgMessageId.Value,
                        virtualSource.TgPreviewMessageId,
                        sourceChannelId,
                        targetChannelId,
                        fullPathWithVersion,
                        node.Id
                    ));
                }
            }
            else
            {
                ctx.OriginalSourcePath = originalSource;
            }

            FillFileInfo(node, out fileInfo);
            return STATUS_SUCCESS;
        }

        public override int Write(
            object fileNode,
            object fileDesc,
            IntPtr buffer,
            ulong offset,
            uint length,
            bool writeToEndOfFile,
            bool constrainedIo,
            out uint bytesTransferred,
            out FileInfo fileInfo)
        {
            var node = (Node)fileNode;
            var ctx = (FspNodeContext)fileDesc;
            if (node.IsDir)
            {
                bytesTransferred = 0;
                FillFileInfo(node, out fileInfo);
                return NT_STATUS_FILE_IS_A_DIRECTORY;
            }

            if (length == 0)
            {
                bytesTransferred = 0;
                FillFileInfo(node, out fileInfo);
                return STATUS_SUCCESS;
            }

            if (ctx.VirtualSourceNode != null || ctx.IsVirtualCopyPassThrough)
            {
                // Это виртуальный клон (или второй проход мелкого виртуального клона)!
                // Данные уже скопированы через Telegram ForwardMessages на сервере;
                // Проводник пишет считанные байты, мы их подтверждаем без повторной сетевой передачи!
                bytesTransferred = length;
                ctx.TotalBytesWritten += length;
                ctx.IsModified = true;
                if (ctx.IsVirtualCopyPassThrough && (long)(offset + length) > node.Size)
                {
                    node.Size = (long)(offset + length);
                }
                FillFileInfo(node, out fileInfo);
                return STATUS_SUCCESS;
            }

            byte[] poolBuffer = ArrayPool<byte>.Shared.Rent((int)length);
            try
            {
                Marshal.Copy(buffer, poolBuffer, 0, (int)length);

                // Если размер файла известен заранее и превышает 1 МБ -> потоковый стриминг в Telegram на лету (Zero-Temp)
                long targetTotalSize = ctx.KnownTargetSize > 0 ? ctx.KnownTargetSize : (long)(offset + length);

                if (targetTotalSize > 1048576)
                {
                    // Инициализируем потоковую передачу в Telegram, если еще не запущена
                    if (!ctx.IsUploadStarted)
                    {
                        ctx.IsUploadStarted = true;
                        ctx.UploadCts = new CancellationTokenSource();
                        ctx.PipeStream = new StreamingPipeStream(targetTotalSize);

                        int parentId = node.ParentId ?? 1;
                        string nodeName = node.Name;
                        int nextVersion = _repository.GetNextVersionForFile(parentId, nodeName);
                        string parentPath = _repository.GetNodeFullPath(parentId);
                        if (parentPath == "/") parentPath = "";
                        string ext = Path.GetExtension(nodeName);
                        string nameNoExt = Path.GetFileNameWithoutExtension(nodeName);
                        string fullPathWithVersion = $"{parentPath}/{nameNoExt}_v{nextVersion}{ext}";

                        if (string.IsNullOrEmpty(ctx.OriginalSourcePath) && ctx.VirtualSourceNode == null)
                        {
                            AppLogger.Info("WinFsp", $"[Write] Пробуем определить источник (Drag-and-Drop / Буфер обмена) для '{nodeName}'...");
                            ctx.OriginalSourcePath = TryFindSourceFile(nodeName, $"{parentPath}/{nodeName}", ctx.CreatorProcessId, _repository, targetTotalSize);
                        }

                        AudioMetadataResult? audioMeta = null;
                        VideoMetadataResult? videoMeta = null;

                        if (!string.IsNullOrEmpty(ctx.OriginalSourcePath))
                        {
                            AppLogger.Info("WinFsp", $"[Write] Начинаем извлечение метаданных из оригинального файла: '{ctx.OriginalSourcePath}'");

                            if (VideoMetadataExtractor.IsPotentialVideo(nodeName))
                            {
                                try
                                {
                                    videoMeta = VideoMetadataExtractor.ExtractFromFile(ctx.OriginalSourcePath, nodeName);
                                    // AppLogger.Debug("WinFsp", $"[Write] Результат VideoMetadataExtractor для '{nodeName}': длительность = {videoMeta.DurationSeconds} сек, разрешение = {videoMeta.Width}x{videoMeta.Height}, обложка = {(videoMeta.Thumbnail != null ? $"{videoMeta.Thumbnail.Length} байт" : "НЕТ")}");
                                }
                                catch (Exception ex)
                                {
                                    AppLogger.Error("WinFsp", $"[Write] Ошибка VideoMetadataExtractor для '{ctx.OriginalSourcePath}': {ex.Message}", ex);
                                }
                            }
                            else if (AudioMetadataExtractor.IsPotentialAudio(nodeName))
                            {
                                try
                                {
                                    audioMeta = AudioMetadataExtractor.ExtractFromFile(ctx.OriginalSourcePath, nodeName);
                                    // AppLogger.Debug("WinFsp", $"[Write] Результат AudioMetadataExtractor для '{nodeName}': трек = '{audioMeta.Artist} - {audioMeta.Title}', длительность = {audioMeta.DurationSeconds} сек, обложка = {(audioMeta.AlbumCover != null ? $"{audioMeta.AlbumCover.Length} байт" : "НЕТ")}");
                                }
                                catch (Exception ex)
                                {
                                    AppLogger.Error("WinFsp", $"[Write] Ошибка AudioMetadataExtractor для '{ctx.OriginalSourcePath}': {ex.Message}", ex);
                                }
                            }
                        }
                        else
                        {
                            AppLogger.Warn("WinFsp", $"[Write] Исходный путь на диске для '{nodeName}' НЕ найден (буфер обмена / хэндлы процесса).");
                        }

                        // Если оригинал не найден в буфере обмена (например, перетаскивание), для аудио извлекаем из первых 256 КБ
                        if (audioMeta == null && AudioMetadataExtractor.IsPotentialAudio(nodeName))
                        {
                            try
                            {
                                using var headerMs = new MemoryStream(poolBuffer, 0, (int)Math.Min((long)length, AudioMetadataExtractor.HeaderCacheSize));
                                audioMeta = AudioMetadataExtractor.ExtractFromStream(headerMs, nodeName);
                                AppLogger.Info("WinFsp", $"[Write] Резервный парсинг тегов аудио из первых 256 КБ потока для '{nodeName}': " +
                                    $"'{audioMeta.Artist} - {audioMeta.Title}', обложка: {(audioMeta.AlbumCover != null ? $"{audioMeta.AlbumCover.Length} байт" : "НЕТ")}");
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Warn("WinFsp", $"[Write] Ошибка резервного парсинга тегов из потока для '{nodeName}': {ex.Message}");
                            }
                        }

                        long? targetChannelId = _repository.GetEffectiveChannelId(node.ParentId);
                        ctx.PendingAudioMeta = audioMeta;
                        string? audioTagsSummary = AudioMetadataExtractor.FormatTagsSummary(audioMeta);
                        AppLogger.Info("WinFsp", $"Запуск прямой потоковой передачи '{nodeName}' ({AppLogger.FormatSizeDetailed(targetTotalSize)}) в Telegram" + 
                            (targetChannelId != null ? $", канал: {targetChannelId}" : "") +
                            (videoMeta != null ? $", видео: {videoMeta.Width}x{videoMeta.Height}, {videoMeta.DurationSeconds} сек, обложка: {(videoMeta.Thumbnail != null ? "ДА" : "НЕТ")}" : "") +
                            (audioMeta != null ? $", аудио: {audioMeta.DurationSeconds} сек, обложка: {(audioMeta.AlbumCover != null ? "ДА" : "НЕТ")}" + (!string.IsNullOrEmpty(audioTagsSummary) ? $", теги: '{audioTagsSummary}'" : "") : "") + "...");
                        ctx.UploadTask = Task.Run(() => _telegramService.UploadFileAsync(
                            ctx.PipeStream,
                            nodeName,
                            targetTotalSize,
                            caption: fullPathWithVersion,
                            audioMeta: audioMeta,
                            videoMeta: videoMeta,
                            originalFilePath: ctx.OriginalSourcePath,
                            targetChannelId: targetChannelId,
                            cancellationToken: ctx.UploadCts?.Token ?? default
                        ));
                    }

                    if (ctx.PipeStream != null)
                    {
                        ctx.PipeStream.PushData(poolBuffer, 0, (int)length);
                    }
                }
                else
                {
                    // Для небольших файлов (<= 1 МБ) буферизируем в ОЗУ через MemoryStream
                    ctx.SmallFileBuffer ??= new MemoryStream();
                    if (writeToEndOfFile)
                    {
                        ctx.SmallFileBuffer.Seek(0, SeekOrigin.End);
                    }
                    else
                    {
                        ctx.SmallFileBuffer.Seek((long)offset, SeekOrigin.Begin);
                    }
                    ctx.SmallFileBuffer.Write(poolBuffer, 0, (int)length);
                }

                ctx.TotalBytesWritten += length;
                bytesTransferred = length;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(poolBuffer);
            }

            ctx.IsModified = true;
            if ((long)(offset + length) > node.Size)
            {
                node.Size = (long)(offset + length);
            }
            node.UpdatedAt = DateTime.UtcNow;
            FillFileInfo(node, out fileInfo);
            return STATUS_SUCCESS;
        }

        public override int SetFileSize(
            object fileNode,
            object fileDesc,
            ulong newSize,
            bool setAllocationSize,
            out FileInfo fileInfo)
        {
            var node = (Node)fileNode;
            var ctx = (FspNodeContext)fileDesc;
            if (!setAllocationSize)
            {
                node.Size = (long)newSize;
                ctx.KnownTargetSize = (long)newSize;
                ctx.IsModified = true;
            }
            else if (ctx.KnownTargetSize <= 0 && newSize > 0)
            {
                ctx.KnownTargetSize = (long)newSize;
            }
            FillFileInfo(node, out fileInfo);
            return STATUS_SUCCESS;
        }

        public override int SetBasicInfo(
            object fileNode,
            object fileDesc,
            uint fileAttributes,
            ulong creationTime,
            ulong lastAccessTime,
            ulong lastWriteTime,
            ulong changeTime,
            out FileInfo fileInfo)
        {
            var node = (Node)fileNode;
            DateTime? newUpdatedAt = null;
            DateTime? newCreatedAt = null;

            if (lastWriteTime != 0)
            {
                newUpdatedAt = DateTime.FromFileTimeUtc((long)lastWriteTime);
                node.UpdatedAt = newUpdatedAt.Value;
            }
            if (creationTime != 0)
            {
                newCreatedAt = DateTime.FromFileTimeUtc((long)creationTime);
                node.CreatedAt = newCreatedAt.Value;
            }

            if (newUpdatedAt.HasValue || newCreatedAt.HasValue)
            {
                if (node.Id > 0)
                {
                    _repository.UpdateNodeTimestamps(node.Id, newUpdatedAt, newCreatedAt);
                }
                AppLogger.Info("WinFsp", $"Обновлены системные даты для '{node.Name}' (ID {node.Id}): Изменен: {node.UpdatedAt:yyyy-MM-dd HH:mm:ss.fff}, Создан: {node.CreatedAt:yyyy-MM-dd HH:mm:ss.fff}");
            }

            FillFileInfo(node, out fileInfo);
            return STATUS_SUCCESS;
        }

        public override int CanDelete(object fileNode, object fileDesc, string fileName)
        {
            var node = (Node)fileNode;
            if (node.Id <= 0)
            {
                return STATUS_SUCCESS;
            }
            AppLogger.Info("WinFsp", $"[CanDelete] Проводник запрашивает возможность удаления '{fileName}' (ID {node.Id}, Name '{node.Name}', InTrash={node.InTrash}).");

            // Защита системной папки .Trash от удаления снаружи
            if (_repository.IsTrashFolder(node.Id) || string.Equals(fileName.Trim('/', '\\'), ".Trash", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Warn("WinFsp", "Попытка удаления системной папки '.Trash' отклонена: корзина защищена от удаления.");
                return NT_STATUS_CANNOT_DELETE;
            }

            if (node.IsDir)
            {
                var children = _repository.GetChildren(node.Id);
                if (children.Count > 0)
                {
                    AppLogger.Warn("WinFsp", $"[CanDelete] Нельзя удалить непустую папку '{node.Name}' (ID {node.Id}, детей: {children.Count}).");
                    return NT_STATUS_DIRECTORY_NOT_EMPTY;
                }
            }
            return STATUS_SUCCESS;
        }

        public override int SetDelete(object fileNode, object fileDesc, string fileName, bool deleteFile)
        {
            var node = (Node)fileNode;
            AppLogger.Debug("WinFsp", $"[SetDelete] Проводник установил deleteFile={deleteFile} для '{fileName}' (ID {node.Id}, Name '{node.Name}').");
            if (fileDesc is FspNodeContext ctx)
            {
                ctx.DeleteOnClose = deleteFile;
            }
            return STATUS_SUCCESS;
        }

        public override int Rename(
            object fileNode,
            object fileDesc,
            string fileName,
            string newFileName,
            bool replaceIfExists)
        {
            var node = (Node)fileNode;
            string oldClean = NormalizePath(fileName);
            string newClean = NormalizePath(newFileName);

            string newParentPath = GetParentPath(newClean);
            string newName = GetFileName(newClean);

            var targetParent = newParentPath == "/" ? _repository.GetRootNode() : _repository.GetNodeByPath(newParentPath);
            if (targetParent == null)
            {
                return NT_STATUS_OBJECT_PATH_NOT_FOUND;
            }

            try
            {
                // Защита от перемещения папки внутрь самой себя или в свои подпапки
                if (node.IsDir && node.Id > 0)
                {
                    int currentAncestorId = targetParent.Id;
                    while (currentAncestorId > 0)
                    {
                        if (currentAncestorId == node.Id)
                        {
                            AppLogger.Warn("WinFsp", $"[Rename] Попытка переместить директорию '{node.Name}' (ID {node.Id}) внутрь самой себя.");
                            return NT_STATUS_ACCESS_DENIED;
                        }
                        var ancestorNode = _repository.GetNodeById(currentAncestorId);
                        if (ancestorNode == null || !ancestorNode.ParentId.HasValue) break;
                        currentAncestorId = ancestorNode.ParentId.Value;
                    }
                }

                // Проверка коллизий: существует ли уже активный файл/папка с таким именем в целевой папке
                var existingTarget = _repository.GetChildByName(targetParent.Id, newName, inTrash: false);
                if (existingTarget != null && existingTarget.Id != node.Id)
                {
                    if (!replaceIfExists)
                    {
                        AppLogger.Info("WinFsp", $"[Rename] Коллизия имен: '{newName}' уже существует в папке '{newParentPath}' (ID {existingTarget.Id}). Возврат NT_STATUS_OBJECT_NAME_COLLISION (replaceIfExists=false).");
                        return NT_STATUS_OBJECT_NAME_COLLISION;
                    }

                    // replaceIfExists == true: замена существующего элемента
                    if (existingTarget.IsDir && !node.IsDir)
                    {
                        AppLogger.Warn("WinFsp", $"[Rename] Нельзя заменить директорию '{existingTarget.Name}' файлом '{node.Name}'.");
                        return NT_STATUS_FILE_IS_A_DIRECTORY;
                    }
                    if (!existingTarget.IsDir && node.IsDir)
                    {
                        AppLogger.Warn("WinFsp", $"[Rename] Нельзя заменить файл '{existingTarget.Name}' директорией '{node.Name}'.");
                        return NT_STATUS_NOT_A_DIRECTORY;
                    }
                    if (existingTarget.IsDir && node.IsDir)
                    {
                        var targetChildren = _repository.GetChildren(existingTarget.Id);
                        if (targetChildren.Count > 0)
                        {
                            AppLogger.Warn("WinFsp", $"[Rename] Нельзя заменить непустую директорию '{existingTarget.Name}' (ID {existingTarget.Id}, детей: {targetChildren.Count}).");
                            return NT_STATUS_DIRECTORY_NOT_EMPTY;
                        }
                    }

                    AppLogger.Info("WinFsp", $"[Rename] Замена существующего элемента '{existingTarget.Name}' (ID {existingTarget.Id}) при перемещении '{node.Name}' (ID {node.Id}). Перемещение старого элемента в корзину...");
                    _repository.SoftDeleteNode(existingTarget.Id);
                }

                if (node.Id <= 0)
                {
                    _activeUploadNodes.TryRemove(oldClean, out _);
                    _activeUploadNodes[newClean] = node;
                    node.Name = newName;
                    node.ParentId = targetParent.Id;
                    if (fileDesc is FspNodeContext renameActiveCtx)
                    {
                        renameActiveCtx.VirtualPath = newClean;
                    }
                    AppLogger.Info("WinFsp", $"Переименование активного файла в памяти: '{oldClean}' -> '{newClean}'");
                    return STATUS_SUCCESS;
                }

                long? sourceChannelId = _repository.GetEffectiveChannelId(node.ParentId);
                long? targetChannelId = _repository.GetEffectiveChannelId(targetParent.Id);

                _repository.MoveNode(node.Id, targetParent.Id, newName);
                node.Name = newName;
                node.ParentId = targetParent.Id;
                if (fileDesc is FspNodeContext renameCtx)
                {
                    renameCtx.VirtualPath = newClean;
                }
                AppLogger.Info("WinFsp", $"Переименование/перемещение: '{oldClean}' -> '{newClean}'");

                if (node.IsDir)
                {
                    if (node.TgChannelId.HasValue && node.TgChannelId.Value != 0)
                    {
                        long primaryId = _repository.GetPrimaryTelegramChannel()?.ChannelId ?? 0;
                        long? parentEffectiveChannelId = _repository.GetEffectiveChannelId(node.ParentId);
                        if (node.TgChannelId.Value != primaryId && node.TgChannelId.Value != parentEffectiveChannelId)
                        {
                            long channelToEdit = node.TgChannelId.Value;
                            string titleToEdit = newName;
                            _ = Task.Run(async () =>
                            {
                                await _telegramService.EditChannelTitleAsync(channelToEdit, titleToEdit);
                            });
                        }
                    }
                    else if (sourceChannelId != targetChannelId && targetChannelId.HasValue)
                    {
                        var subtreeFiles = _repository.GetSubtreeNodes(node.Id)
                            .Where(n => !n.IsDir && n.TgMessageId.HasValue && n.TgMessageId.Value > 0)
                            .OrderBy(n => n.TgMessageId!.Value)
                            .Select(n => n.Id)
                            .ToList();
                        if (subtreeFiles.Count > 0)
                        {
                            _repository.EnqueueChannelMigrations(subtreeFiles, sourceChannelId ?? 0, targetChannelId.Value);
                            _telegramService.TriggerChannelMigrationProcessing();
                            AppLogger.Info("WinFsp", $"[MOVE] Перемещение папки '{newName}': {subtreeFiles.Count} файлов поставлено в очередь миграции каналов ({sourceChannelId} -> {targetChannelId.Value}).");
                        }
                    }
                }
                else if (sourceChannelId != targetChannelId && targetChannelId.HasValue)
                {
                    if (node.TgMessageId.HasValue && node.TgMessageId.Value > 0)
                    {
                        _repository.EnqueueChannelMigrations(new[] { node.Id }, sourceChannelId ?? 0, targetChannelId.Value);
                        _telegramService.TriggerChannelMigrationProcessing();
                        AppLogger.Info("WinFsp", $"[MOVE] Перемещение файла '{node.Name}': поставлен в очередь миграции каналов ({sourceChannelId} -> {targetChannelId.Value}).");
                    }
                }

                return STATUS_SUCCESS;
            }
            catch (Exception ex)
            {
                AppLogger.Error("WinFsp", $"Ошибка переименования '{oldClean}' -> '{newClean}': {ex.Message}", ex);
                return NT_STATUS_UNSUCCESSFUL;
            }
        }

        public override void Cleanup(object fileNode, object fileDesc, string fileName, uint flags)
        {
            var node = (Node)fileNode;
            var ctx = (FspNodeContext)fileDesc;
            ctx.CleanupCalled = true;

            if (node.Id <= 0 || ctx.IsModified || (flags & CleanupDelete) != 0 || ctx.DeleteOnClose)
            {
                AppLogger.Info("WinFsp", $"[DIAG Cleanup] raw='{fileName}', vPath='{ctx.VirtualPath}', nodeId={node.Id}, name='{node.Name}', isDir={node.IsDir}, flags=0x{flags:X}, isMod={ctx.IsModified}, delOnClose={ctx.DeleteOnClose}, written={ctx.TotalBytesWritten}, nodeSize={node.Size}, virtSrc={ctx.VirtualSourceNode?.Id.ToString() ?? "null"}, pid={GetDiagProcessName(ctx.CreatorProcessId)}");
            }

            if ((flags & CleanupDelete) != 0 || ctx.DeleteOnClose)
            {
                if (node.Id <= 0)
                {
                    string cleanCancelPath = ResolveCleanupVirtualPath(node, ctx, fileName);
                    _activeUploadNodes.TryRemove(cleanCancelPath, out _);
                    ReleaseSessionReservation(cleanCancelPath);
                    ctx.UploadCts?.Cancel();
                    try { ctx.PipeStream?.Abort(); } catch { }
                    try { ctx.PipeStream?.Dispose(); } catch { }
                    _telegramService.TriggerUploadCanceled(node.Name);
                    AppLogger.Info("WinFsp", $"[CleanupDelete] Отменено создание незавершенного файла '{node.Name}'. Удален из памяти без записи в базу и корзину.");
                    ctx.Dispose();
                    return;
                }

                AppLogger.Debug("WinFsp", $"[CleanupDelete] Закрытие с флагом удаления для '{fileName}' (ID {node.Id}, Name '{node.Name}', flags={flags}, DeleteOnClose={ctx.DeleteOnClose}).");

                // Защита от удаления системной папки корзины
                if (_repository.IsTrashFolder(node.Id) || string.Equals(fileName.Trim('/', '\\'), ".Trash", StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Warn("WinFsp", "Отклонено удаление системной папки корзины '.Trash'.");
                    ctx.Dispose();
                    return;
                }

                try
                {
                    // Проверяем, находится ли элемент уже в корзине (.Trash) или помечен ли он как удаленный
                    bool isPermanent = node.InTrash || _repository.IsNodeInTrash(node.Id);

                    if (isPermanent)
                    {
                        AppLogger.Info("WinFsp", $"Перманентное удаление элемента '{node.Name}' (ID {node.Id}) из корзины...");
                        // Порядок снизу вверх: сначала файлы, затем папки от самых глубоких к корню (безопасно для FOREIGN KEY parent_id)
                        var sortedSubtree = _repository.GetPhysicalSubtreeNodesBottomUp(node.Id);
                        var dbNodeIds = new List<int>();

                        // 1. Вычисляем кастомные каналы, которые будут полностью уничтожены в Telegram вместе с удаляемой папкой
                        long primaryChannelId = _repository.GetPrimaryTelegramChannel()?.ChannelId ?? 0;
                        var channelsToDestroy = new HashSet<long>();

                        // Канал подлежит полному уничтожению через Channels_DeleteChannel ТОЛЬКО если:
                        // 1. Удаляемый узел является самой корневой привязанной папкой (node.Id == n.Id для папки с этим каналом).
                        // 2. В базе данных больше не осталось других узлов, принадлежащих этому каналу (удаляется всё поддерево целиком).
                        var boundFoldersToDelete = sortedSubtree
                            .Where(n => n.IsDir && n.TgChannelId.HasValue && n.TgChannelId.Value != 0 && n.TgChannelId.Value != primaryChannelId && n.Id == node.Id)
                            .Select(n => n.TgChannelId!.Value)
                            .Distinct()
                            .ToList();

                        foreach (var chId in boundFoldersToDelete)
                        {
                            int nodesInSubtreeForChannel = sortedSubtree.Count(n => (n.TgChannelId ?? _repository.GetEffectiveChannelId(n.ParentId)) == chId);
                            int totalNodesInDbForChannel = _repository.GetNodeCountForChannel(chId);
                            if (totalNodesInDbForChannel > 0 && totalNodesInDbForChannel <= nodesInSubtreeForChannel)
                            {
                                channelsToDestroy.Add(chId);
                            }
                        }

                        // 2. Формируем списки на удаление: файлы каналов, уничтожаемых целиком через Channels_DeleteChannel,
                        // не спамят очередь поштучного удаления сообщений
                        var deletionItems = new List<(int MessageId, long ChannelId)>();
                        foreach (var n in sortedSubtree)
                        {
                            dbNodeIds.Add(n.Id);

                            long fileChannelId = n.TgChannelId ?? _repository.GetEffectiveChannelId(n.ParentId) ?? primaryChannelId;
                            if (channelsToDestroy.Contains(fileChannelId))
                            {
                                // Сообщения этого канала мгновенно уничтожаются на сервере Telegram вместе с самим каналом
                                continue;
                            }

                            if (n.TgMessageId.HasValue && n.TgMessageId.Value > 0)
                            {
                                deletionItems.Add((n.TgMessageId.Value, fileChannelId));
                            }
                            else if (!n.IsDir && n.Size > 0 && n.InlineData == null)
                            {
                                AppLogger.Warn("WinFsp", $"[CleanupDelete Subtree] ВНИМАНИЕ: Файл '{n.Name}' (ID {n.Id}) не имеет TgMessageId в БД (null/0). Сообщение в Telegram не может быть удалено!");
                            }
                            if (n.TgPreviewMessageId.HasValue && n.TgPreviewMessageId.Value > 0)
                            {
                                deletionItems.Add((n.TgPreviewMessageId.Value, fileChannelId));
                            }
                            AppLogger.Debug("WinFsp", $"[CleanupDelete Subtree] Элемент поддерева: ID {n.Id} ('{n.Name}'), IsDir={n.IsDir}, InTrash={n.InTrash}, ParentId={n.ParentId}, TgMessageId={n.TgMessageId?.ToString() ?? "NULL"}, TgPreviewMessageId={n.TgPreviewMessageId?.ToString() ?? "NULL"}");
                        }

                        // Атомарно помещаем сообщения в гарантированную очередь удаления и удаляем узлы из базы
                        _repository.EnqueuePermanentDeletion(deletionItems, dbNodeIds);
                        AppLogger.Info("WinFsp", $"Удалено {dbNodeIds.Count} узлов из базы; {deletionItems.Count} сообщений отправлено в очередь удаления Telegram.");

                        if (deletionItems.Count > 0)
                        {
                            _telegramService.TriggerDeletionQueueProcessing();
                        }

                        // 3. Авто-удаление уничтожаемых каналов из Telegram
                        foreach (var chId in channelsToDestroy)
                        {
                            AppLogger.Info("WinFsp", $"Привязанная папка удалена навсегда, канал Telegram ID {chId} больше не используется. Авто-удаление канала и всех его сообщений из Telegram...");
                            _ = Task.Run(async () =>
                            {
                                await _telegramService.DeleteChannelAsync(chId);
                            });
                        }

                        if (_configManager.Load().Database.AutoVacuumOnTrashDelete)
                        {
                            _repository.ScheduleVacuum(3000);
                        }
                    }
                    else
                    {
                        AppLogger.Info("WinFsp", $"Перемещение элемента '{node.Name}' (ID {node.Id}) в корзину (.Trash)...");
                        _repository.SoftDeleteNode(node.Id);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error("WinFsp", $"[CleanupDelete] Ошибка при удалении элемента '{node.Name}' (ID {node.Id}): {ex.Message}", ex);
                }
                finally
                {
                    ctx.Dispose();
                }

                return;
            }

            if (ctx.IsModified)
            {
                ctx.IsModified = false;
                int parentId = node.ParentId ?? 1;
                string nodeName = node.Name;
                DateTime targetUpdatedAt = node.UpdatedAt;
                DateTime targetCreatedAt = node.CreatedAt;
                string cleanFilePath = ResolveCleanupVirtualPath(node, ctx, fileName);

                try
                {
                    if (ctx.IsVirtualCopyPassThrough)
                    {
                        if (ctx.VirtualCopyPassThroughTargetSize > 0 && node.Size < ctx.VirtualCopyPassThroughTargetSize)
                        {
                            node.Size = ctx.VirtualCopyPassThroughTargetSize;
                        }
                        if (node.Id > 0)
                        {
                            _repository.UpdateNodeTimestamps(node.Id, targetUpdatedAt, targetCreatedAt);
                        }
                        _activeUploadNodes.TryRemove(cleanFilePath, out _);
                        if (ctx.TotalBytesWritten > 0)
                        {
                            _recentTwoPassVirtualCopies.TryRemove(NormalizePath(cleanFilePath), out _);
                        }
                        MarkSessionFileCompleted(cleanFilePath);
                        AppLogger.Info("WinFsp", $"[VirtualCopy Pass2] Второй проход записи для '{nodeName}' (ID {node.Id}) подтвержден без дублирования в Telegram (записано: {ctx.TotalBytesWritten}, размер в БД: {AppLogger.FormatSizeDetailed(node.Size)}).");
                        ctx.Dispose();
                        return;
                    }

                    if (ctx.VirtualSourceNode != null)
                    {
                        UnregisterActiveVirtualCopy(node.Id);
                        if (ctx.ForwardTask != null)
                        {
                            try
                            {
                                var fwdResult = ctx.ForwardTask.GetAwaiter().GetResult();
                                if (fwdResult != null && fwdResult.MessageId > 0)
                                {
                                    int newNodeId = _repository.CreateOrUpdateFile(
                                        parentId,
                                        nodeName,
                                        ctx.VirtualSourceNode.Size,
                                        fwdResult.MessageId,
                                        fwdResult.PreviewMessageId,
                                        inlineData: null,
                                        lastModified: targetUpdatedAt,
                                        creationDate: targetCreatedAt,
                                        parts: fwdResult.Parts
                                    );

                                    if (newNodeId > 0) node.Id = newNodeId;
                                    _activeUploadNodes.TryRemove(cleanFilePath, out _);

                                    node.TgMessageId = fwdResult.MessageId;
                                    node.TgPreviewMessageId = fwdResult.PreviewMessageId;
                                    node.Size = ctx.VirtualSourceNode.Size;
                                    node.UpdatedAt = targetUpdatedAt;
                                    node.CreatedAt = targetCreatedAt;

                                    if (ctx.TotalBytesWritten == 0 && ctx.VirtualSourceNode.Size > 0)
                                    {
                                        RegisterTwoPassVirtualCopy(cleanFilePath, node.Id, ctx.VirtualSourceNode.Id, ctx.VirtualSourceNode.Size, ctx.CreatorProcessId);
                                    }

                                    MarkSessionFileCompleted(cleanFilePath);
                                    AppLogger.Info("WinFsp", $"[VirtualCopy] Файл '{nodeName}' успешно сохранен через Telegram ForwardMessages (новый Msg ID: {fwdResult.MessageId}, размер: {AppLogger.FormatSizeDetailed(node.Size)}" +
                                        (fwdResult.Parts != null ? $", частей: {fwdResult.Parts.Count}" : "") + ").");
                                }
                                else
                                {
                                    _activeUploadNodes.TryRemove(cleanFilePath, out _);
                                    AppLogger.Warn("WinFsp", $"[VirtualCopy] Форвардинг для '{nodeName}' вернул null. Возможно, исходное сообщение удалено из Telegram.");
                                }
                            }
                            catch (Exception ex)
                            {
                                _activeUploadNodes.TryRemove(cleanFilePath, out _);
                                AppLogger.Error("WinFsp", $"[VirtualCopy] Ошибка завершения ForwardTask для '{nodeName}': {ex.Message}", ex);
                            }
                        }
                        else
                        {
                            // Небольшой файл или inline_data
                            int newNodeId = _repository.CreateOrUpdateFile(
                                parentId,
                                nodeName,
                                ctx.VirtualSourceNode.Size,
                                tgMessageId: null,
                                tgPreviewMessageId: null,
                                inlineData: ctx.VirtualSourceNode.InlineData,
                                lastModified: targetUpdatedAt,
                                creationDate: targetCreatedAt
                            );

                            if (newNodeId > 0) node.Id = newNodeId;
                            _activeUploadNodes.TryRemove(cleanFilePath, out _);

                            node.Size = ctx.VirtualSourceNode.Size;
                            node.InlineData = ctx.VirtualSourceNode.InlineData;
                            node.UpdatedAt = targetUpdatedAt;
                            node.CreatedAt = targetCreatedAt;

                            if (ctx.TotalBytesWritten == 0 && ctx.VirtualSourceNode.Size > 0)
                            {
                                RegisterTwoPassVirtualCopy(cleanFilePath, node.Id, ctx.VirtualSourceNode.Id, ctx.VirtualSourceNode.Size, ctx.CreatorProcessId);
                            }

                            MarkSessionFileCompleted(cleanFilePath);
                            AppLogger.Info("WinFsp", $"[VirtualCopy] Файл '{nodeName}' сохранен мгновенно из локальных данных ({AppLogger.FormatSizeDetailed(node.Size)}).");
                        }

                        ctx.Dispose();
                        return;
                    }

                    // Вариант 1: Большой файл, который передавался на лету через StreamingPipeStream
                    if (ctx.PipeStream != null && ctx.UploadTask != null)
                    {
                        var pipe = ctx.PipeStream;
                        var uploadTask = ctx.UploadTask;
                        var cts = ctx.UploadCts;
                        ctx.PipeStream = null;
                        ctx.UploadTask = null;
                        ctx.UploadCts = null;

                        pipe.CompleteWriting();

                        try
                        {
                            // Синхронно ожидаем завершения загрузки в Telegram, чтобы Проводник Windows держал нативное окно копирования с графиком скорости
                            var uploadResult = uploadTask.GetAwaiter().GetResult();
                            if (uploadResult?.MessageId != null)
                            {
                                long finalLength = node.Size;
                                int newNodeId = _repository.CreateOrUpdateFile(
                                    parentId,
                                    nodeName,
                                    finalLength,
                                    uploadResult.MessageId,
                                    uploadResult.PreviewMessageId,
                                    inlineData: null,
                                    lastModified: targetUpdatedAt,
                                    creationDate: targetCreatedAt,
                                    parts: uploadResult.Parts
                                );

                                if (newNodeId > 0) node.Id = newNodeId;
                                _activeUploadNodes.TryRemove(cleanFilePath, out _);

                                node.TgMessageId = uploadResult.MessageId;
                                node.TgPreviewMessageId = uploadResult.PreviewMessageId;
                                node.InlineData = null;
                                node.UpdatedAt = targetUpdatedAt;
                                node.CreatedAt = targetCreatedAt;

                                MarkSessionFileCompleted(cleanFilePath);
                                AppLogger.Info("WinFsp", $"Файл '{nodeName}' успешно сохранен в Telegram (Msg ID: {uploadResult.MessageId}" + (uploadResult.PreviewMessageId != null ? $", Preview ID: {uploadResult.PreviewMessageId}" : "") + $", дата изменения: {targetUpdatedAt:yyyy-MM-dd HH:mm:ss}). Загрузка на лету (Zero-Temp) завершена.");
                                if (ctx.PendingAudioMeta?.HasBadTags == true)
                                {
                                    AppLogger.LogBadAudioTag(cleanFilePath, ctx.OriginalSourcePath, finalLength, uploadResult.MessageId, AudioMetadataExtractor.FormatTagsSummary(ctx.PendingAudioMeta));
                                }
                            }
                            else
                            {
                                _activeUploadNodes.TryRemove(cleanFilePath, out _);
                            }
                        }
                        catch (Exception ex)
                        {
                            _activeUploadNodes.TryRemove(cleanFilePath, out _);
                            bool isCancelled = ex is OperationCanceledException ||
                                               ex.InnerException is OperationCanceledException ||
                                               ex is ObjectDisposedException ||
                                               cts?.IsCancellationRequested == true ||
                                               (ex is IOException ioEx && ioEx.Message.Contains("отменен"));
                            if (isCancelled)
                            {
                                AppLogger.Info("WinFsp", $"Потоковая загрузка '{nodeName}' в Telegram отменена пользователем.");
                                _telegramService.TriggerUploadCanceled(nodeName);
                            }
                            else
                            {
                                AppLogger.Error("WinFsp", $"Ошибка при завершении потоковой загрузки '{nodeName}' в Telegram: {ex.Message}", ex);
                            }
                        }
                        finally
                        {
                            ctx.PendingAudioMeta = null;
                            try { pipe.Dispose(); } catch { }
                            try { cts?.Dispose(); } catch { }
                        }
                    }
                    // Вариант 2: Небольшой файл (<= 1 МБ), сохраненный в памяти, либо пустой файл (0 байт)
                    else
                    {
                        long finalLength = ctx.SmallFileBuffer?.Length ?? (node.Size >= 0 ? node.Size : 0);
                        byte[] fileBytes = ctx.SmallFileBuffer?.ToArray() ?? Array.Empty<byte>();

                        try
                        {
                            if (finalLength <= 1)
                            {
                                byte[]? inlineBytes = finalLength == 1 ? fileBytes : null;
                                int newNodeId = _repository.CreateOrUpdateFile(
                                    parentId,
                                    nodeName,
                                    finalLength,
                                    tgMessageId: null,
                                    tgPreviewMessageId: null,
                                    inlineData: inlineBytes,
                                    lastModified: targetUpdatedAt,
                                    creationDate: targetCreatedAt
                                );

                                if (newNodeId > 0) node.Id = newNodeId;
                                _activeUploadNodes.TryRemove(cleanFilePath, out _);

                                node.Size = finalLength;
                                node.InlineData = inlineBytes;
                                node.TgMessageId = null;
                                node.UpdatedAt = targetUpdatedAt;
                                node.CreatedAt = targetCreatedAt;

                                if (finalLength > 0)
                                {
                                    MarkSessionFileCompleted(cleanFilePath);
                                }
                                AppLogger.Info("WinFsp", $"Файл '{nodeName}' ({AppLogger.FormatSizeDetailed(finalLength)}) сохранен в базе данных без отправки в Telegram (inline_data).");
                            }
                            else
                            {
                                int nextVersion = _repository.GetNextVersionForFile(parentId, nodeName);
                                string parentPath = _repository.GetNodeFullPath(parentId);
                                if (parentPath == "/") parentPath = "";
                                string ext = Path.GetExtension(nodeName);
                                string nameNoExt = Path.GetFileNameWithoutExtension(nodeName);
                                string fullPathWithVersion = $"{parentPath}/{nameNoExt}_v{nextVersion}{ext}";

                                if (string.IsNullOrEmpty(ctx.OriginalSourcePath) && ctx.VirtualSourceNode == null)
                                {
                                    ctx.OriginalSourcePath = TryFindSourceFile(nodeName, cleanFilePath, ctx.CreatorProcessId, _repository, finalLength);
                                }

                                AudioMetadataResult? fspAudioMeta = null;
                                VideoMetadataResult? fspVideoMeta = null;

                                if (!string.IsNullOrEmpty(ctx.OriginalSourcePath) && File.Exists(ctx.OriginalSourcePath))
                                {
                                    if (VideoMetadataExtractor.IsPotentialVideo(nodeName))
                                    {
                                        try { fspVideoMeta = VideoMetadataExtractor.ExtractFromFile(ctx.OriginalSourcePath, nodeName); } catch { }
                                    }
                                    else if (AudioMetadataExtractor.IsPotentialAudio(nodeName))
                                    {
                                        try { fspAudioMeta = AudioMetadataExtractor.ExtractFromFile(ctx.OriginalSourcePath, nodeName); } catch { }
                                    }
                                }

                                if (fspAudioMeta == null && AudioMetadataExtractor.IsPotentialAudio(nodeName))
                                {
                                    using var ms = new MemoryStream(fileBytes, false);
                                    fspAudioMeta = AudioMetadataExtractor.ExtractFromStream(ms, nodeName);
                                }

                                long? targetChannelId = _repository.GetEffectiveChannelId(parentId);
                                string? fspAudioTagsSummary = AudioMetadataExtractor.FormatTagsSummary(fspAudioMeta);
                                AppLogger.Info("WinFsp", $"Отправка файла '{nodeName}' ({AppLogger.FormatSizeDetailed(finalLength)}) из памяти в Telegram (Zero-Temp)" +
                                    (targetChannelId != null ? $", канал: {targetChannelId}" : "") +
                                    (fspAudioMeta != null ? $", аудио: {fspAudioMeta.DurationSeconds ?? 0} сек, обложка: {(fspAudioMeta.AlbumCover != null ? "ДА" : "НЕТ")}" + (!string.IsNullOrEmpty(fspAudioTagsSummary) ? $", теги: '{fspAudioTagsSummary}'" : "") : "") + "...");
                                using var uploadMs = new MemoryStream(fileBytes, false);
                                var uploadResult = _telegramService.UploadFileAsync(
                                    uploadMs,
                                    nodeName,
                                    finalLength,
                                    caption: fullPathWithVersion,
                                    audioMeta: fspAudioMeta,
                                    videoMeta: fspVideoMeta,
                                    originalFilePath: ctx.OriginalSourcePath,
                                    targetChannelId: targetChannelId
                                ).GetAwaiter().GetResult();

                                if (uploadResult?.MessageId != null)
                                {
                                    int newNodeId = _repository.CreateOrUpdateFile(
                                        parentId,
                                        nodeName,
                                        finalLength,
                                        uploadResult.MessageId,
                                        uploadResult.PreviewMessageId,
                                        inlineData: null,
                                        lastModified: targetUpdatedAt,
                                        creationDate: targetCreatedAt,
                                        parts: uploadResult.Parts
                                    );

                                    if (newNodeId > 0) node.Id = newNodeId;
                                    _activeUploadNodes.TryRemove(cleanFilePath, out _);

                                    node.Size = finalLength;
                                    node.TgMessageId = uploadResult.MessageId;
                                    node.TgPreviewMessageId = uploadResult.PreviewMessageId;
                                    node.InlineData = null;
                                    node.UpdatedAt = targetUpdatedAt;
                                    node.CreatedAt = targetCreatedAt;

                                    MarkSessionFileCompleted(cleanFilePath);
                                    AppLogger.Info("WinFsp", $"Файл '{nodeName}' успешно сохранен в Telegram (Msg ID: {uploadResult.MessageId}" + (uploadResult.PreviewMessageId != null ? $", Preview ID: {uploadResult.PreviewMessageId}" : "") + $", дата изменения: {targetUpdatedAt:yyyy-MM-dd HH:mm:ss}).");
                                    if (fspAudioMeta?.HasBadTags == true)
                                    {
                                        AppLogger.LogBadAudioTag(cleanFilePath, ctx.OriginalSourcePath, finalLength, uploadResult.MessageId, fspAudioTagsSummary);
                                    }
                                }
                                else
                                {
                                    _activeUploadNodes.TryRemove(cleanFilePath, out _);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _activeUploadNodes.TryRemove(cleanFilePath, out _);
                            AppLogger.Error("WinFsp", $"Ошибка загрузки '{nodeName}' в Telegram: {ex.Message}", ex);
                        }
                    }
                }
                finally
                {
                    _activeUploadNodes.TryRemove(cleanFilePath, out _);
                }
            }
        }

        public override void Close(object fileNode, object fileDesc)
        {
            var node = fileNode as Node;
            if (fileDesc is FspNodeContext ctx)
            {
                if ((node != null && node.Id <= 0) || !ctx.CleanupCalled || ctx.IsModified || ctx.DeleteOnClose)
                {
                    AppLogger.Info("WinFsp", $"[DIAG Close] nodeId={node?.Id.ToString() ?? "null"}, name='{node?.Name}', isDir={node?.IsDir}, vPath='{ctx.VirtualPath}', cleanupCalled={ctx.CleanupCalled}, isMod={ctx.IsModified}, delOnClose={ctx.DeleteOnClose}, written={ctx.TotalBytesWritten}, pid={GetDiagProcessName(ctx.CreatorProcessId)}");
                }

                if (node != null && node.Id <= 0 && !ctx.CleanupCalled)
                {
                    string vPath = ResolveCleanupVirtualPath(node, ctx, null);
                    if (!string.IsNullOrWhiteSpace(vPath) && vPath != "/" &&
                        _activeUploadNodes.TryGetValue(vPath, out var activeNode) &&
                        (ReferenceEquals(activeNode, node) || activeNode.Id == node.Id))
                    {
                        _activeUploadNodes.TryRemove(new KeyValuePair<string, Node>(vPath, activeNode));
                    }
                    ReleaseSessionReservation(vPath);
                }

                ctx.Dispose();
            }
            else if (node != null && node.Id <= 0)
            {
                AppLogger.Info("WinFsp", $"[DIAG Close] nodeId={node.Id}, name='{node.Name}' (no FspNodeContext)");
            }
        }

        public override int GetFileInfo(object fileNode, object fileDesc, out FileInfo fileInfo)
        {
            var node = (Node)fileNode;
            if (node.Id > 1)
            {
                var fresh = _repository.GetNodeById(node.Id);
                if (fresh != null) node = fresh;
            }
            FillFileInfo(node, out fileInfo);
            return STATUS_SUCCESS;
        }

        public override bool ReadDirectoryEntry(
            object fileNode,
            object fileDesc,
            string pattern,
            string marker,
            ref object context,
            out string fileName,
            out FileInfo fileInfo)
        {
            var dirNode = (Node)fileNode;
            if (!dirNode.IsDir)
            {
                fileName = default!;
                fileInfo = default;
                return false;
            }

            if (context == null)
            {
                var children = _repository.GetChildren(dirNode.Id);
                var settings = _configManager.Load();
                if (settings.Server.HideTrashFromRoot && dirNode.Id == 1)
                {
                    children = children.FindAll(c => !string.Equals(c.Name, ".Trash", StringComparison.OrdinalIgnoreCase));
                }

                children.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                context = new FspDirectoryEnumContext(children);

                bool shouldLog = false;
                lock (_dirListingLock)
                {
                    var now = DateTime.UtcNow;
                    if (dirNode.Id != _lastListedDirId && ((now - _lastListedTime).TotalMilliseconds >= 500 || _lastListedDirId == -1))
                    {
                        _lastListedDirId = dirNode.Id;
                        _lastListedTime = now;
                        shouldLog = true;
                    }
                }

                if (shouldLog)
                {
                    string displayName = dirNode.Id == 1 ? "Root" : dirNode.Name;
                    string fullDirPath = dirNode.Id == 1 ? "/" : _repository.GetNodeFullPath(dirNode.Id);
                    AppLogger.Info("WinFsp", $"[ReadDirectoryEntry] Открыта папка '{displayName}' ({fullDirPath}) (ID {dirNode.Id}), элементов: {children.Count}");
                }
            }

            var dirEnum = (FspDirectoryEnumContext)context;
            if (marker != null)
            {
                while (dirEnum.Index < dirEnum.Items.Count &&
                       string.Compare(dirEnum.Items[dirEnum.Index].Name, marker, StringComparison.OrdinalIgnoreCase) <= 0)
                {
                    dirEnum.Index++;
                }
            }

            if (dirEnum.Index < dirEnum.Items.Count)
            {
                var child = dirEnum.Items[dirEnum.Index++];
                fileName = child.Name;
                FillFileInfo(child, out fileInfo);
                return true;
            }

            fileName = default!;
            fileInfo = default;
            return false;
        }

        public override int Read(
            object fileNode,
            object fileDesc,
            IntPtr buffer,
            ulong offset,
            uint length,
            out uint bytesTransferred)
        {
            var node = (Node)fileNode;
            if (node.IsDir)
            {
                bytesTransferred = 0;
                return NT_STATUS_FILE_IS_A_DIRECTORY;
            }

            if (offset >= (ulong)node.Size)
            {
                bytesTransferred = 0;
                return NT_STATUS_END_OF_FILE;
            }

            ulong remaining = (ulong)node.Size - offset;
            uint toRead = (uint)Math.Min((ulong)length, remaining);

            if (toRead == 0)
            {
                bytesTransferred = 0;
                return STATUS_SUCCESS;
            }

            // Проверяем, является ли текущее чтение частью активного виртуального копирования внутри диска.
            // Если да — Проводник Windows считывает файл-источник исключительно ради передачи байтов в Write() целевого файла,
            // где Write() их отбросит, так как дублирование уже выполнено на сервере через Telegram ForwardMessages.
            // Мгновенно отдаем очищенный буфер из ОЗУ (0 байт сетевого трафика и 0 секунд задержки).
            int callerPid = 0;
            try { callerPid = FileSystemBase.GetOperationProcessId(); } catch { }

            if (IsVirtualCopySourceActive(node.Id, callerPid))
            {
                unsafe
                {
                    new Span<byte>((void*)buffer, (int)toRead).Clear();
                }
                bytesTransferred = toRead;
                AppLogger.Debug("WinFsp", $"[VirtualCopy Read Bypass] Файл '{node.Name}' (ID {node.Id}): чтение {toRead} байт (offset {offset}) обслужено из ОЗУ (0 байт из сети TG).");
                return STATUS_SUCCESS;
            }

            try
            {
                if (node.TgMessageId == null)
                {
                    if (node.InlineData != null && node.InlineData.Length > 0)
                    {
                        if (offset < (ulong)node.InlineData.Length)
                        {
                            uint count = (uint)Math.Min((ulong)toRead, (ulong)node.InlineData.Length - offset);
                            System.Runtime.InteropServices.Marshal.Copy(node.InlineData, (int)offset, buffer, (int)count);
                            bytesTransferred = count;
                            return STATUS_SUCCESS;
                        }
                    }

                    bytesTransferred = 0;
                    return NT_STATUS_UNSUCCESSFUL;
                }

                unsafe
                {
                    var fspCtx = fileDesc as FspNodeContext;
                    string? fullVPath = fspCtx?.VirtualPath ?? (node.Id > 1 ? _repository.GetNodeFullPath(node.Id) : null);

                    // Прямой стриминг в предоставленный ядром Windows буфер без создания промежуточных файлов на диске C:!
                    using var memStream = new UnmanagedMemoryStream((byte*)buffer.ToPointer(), toRead, toRead, FileAccess.Write);
                    var downloadTask = _telegramService.DownloadFileAsync(node.TgMessageId.Value, memStream, (long)offset, (long)toRead, node.Name, node.Size, node.TgChannelId, node.Id, fullVPath);
                    if (!downloadTask.Wait(TimeSpan.FromSeconds(25)))
                    {
                        LogNetworkWarning(node.Name, "прервано по таймауту 25 сек (сеть Telegram недоступна или восстанавливается)");
                        bytesTransferred = 0;
                        return NT_STATUS_NETWORK_UNREACHABLE;
                    }
                    bytesTransferred = (uint)memStream.Position;
                }
                if (_isNetworkOfflineLogged != 0)
                {
                    Interlocked.Exchange(ref _isNetworkOfflineLogged, 0);
                }
                return STATUS_SUCCESS;
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex.InnerException is FileNotFoundException)
            {
                var fnf = (ex as FileNotFoundException) ?? (ex.InnerException as FileNotFoundException)!;
                AppLogger.Warn("WinFsp", $"Файл '{node.Name}' (ID {node.Id}, MsgId: {node.TgMessageId}) не найден в Telegram: {fnf.Message}");
                _repository.HandleLostFile(node.Id, node.TgMessageId ?? 0, fnf.Message);
                bytesTransferred = 0;
                return NT_STATUS_OBJECT_NAME_NOT_FOUND;
            }
            catch (Exception ex) when (IsNetworkException(ex))
            {
                LogNetworkWarning(node.Name, ex.GetBaseException().Message);
                bytesTransferred = 0;
                return NT_STATUS_NETWORK_UNREACHABLE;
            }
            catch (Exception ex)
            {
                AppLogger.Error("WinFsp", $"Ошибка прямого чтения '{node.Name}' (смещение {offset}, длина {toRead}): {ex.Message}", ex);
                bytesTransferred = 0;
                return NT_STATUS_UNSUCCESSFUL;
            }
        }

        private static void FillFileInfo(Node node, out FileInfo fileInfo)
        {
            fileInfo = default;
            fileInfo.FileAttributes = node.IsDir ? (uint)FileAttributes.Directory : (uint)FileAttributes.Normal;
            fileInfo.FileSize = (ulong)node.Size;
            fileInfo.AllocationSize = (ulong)((node.Size + 4095) / 4096 * 4096);
            fileInfo.CreationTime = (ulong)node.CreatedAt.ToFileTimeUtc();
            fileInfo.LastWriteTime = (ulong)node.UpdatedAt.ToFileTimeUtc();
            fileInfo.LastAccessTime = (ulong)node.UpdatedAt.ToFileTimeUtc();
            fileInfo.ChangeTime = (ulong)node.UpdatedAt.ToFileTimeUtc();
            fileInfo.IndexNumber = (ulong)node.Id;
            fileInfo.HardLinks = 1;
        }

        private string ResolveCleanupVirtualPath(Node node, FspNodeContext ctx, string? fileName)
        {
            if (node.ParentId.HasValue && !string.IsNullOrWhiteSpace(node.Name))
            {
                string parentPath = _repository.GetNodeFullPath(node.ParentId.Value);
                return parentPath == "/" ? $"/{node.Name}" : $"{parentPath}/{node.Name}";
            }

            if (!string.IsNullOrWhiteSpace(ctx.VirtualPath))
            {
                return NormalizePath(ctx.VirtualPath);
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return NormalizePath(fileName);
            }

            return "/";
        }

        private static string NormalizePath(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "/";
            string p = fileName.Replace('\\', '/').Trim();
            if (!p.StartsWith("/")) p = "/" + p;
            p = p.TrimEnd('/');
            return string.IsNullOrEmpty(p) ? "/" : p;
        }

        private static string GetParentPath(string path)
        {
            string p = NormalizePath(path);
            if (p == "/") return "/";
            int lastSlash = p.LastIndexOf('/');
            if (lastSlash <= 0) return "/";
            return p.Substring(0, lastSlash);
        }

        private static string GetFileName(string path)
        {
            string p = NormalizePath(path);
            if (p == "/") return "";
            int lastSlash = p.LastIndexOf('/');
            return lastSlash >= 0 ? p.Substring(lastSlash + 1) : p;
        }

#region Windows Drag-and-Drop & Clipboard Source Detection (NtHandles & CF_HDROP)

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW", SetLastError = true)]
        private static extern uint DragQueryFileCount(IntPtr hDrop, uint iFile, IntPtr lpszFile, uint cch);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW", SetLastError = true)]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, [Out] StringBuilder lpszFile, uint cch);

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength, out int ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DuplicateHandle(IntPtr hSourceProcessHandle, IntPtr hSourceHandle, IntPtr hTargetProcessHandle, out IntPtr lpTargetHandle, uint dwDesiredAccess, bool bInheritHandle, uint dwOptions);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetFinalPathNameByHandle(IntPtr hFile, [Out] StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

        [DllImport("kernel32.dll")]
        private static extern uint GetFileType(IntPtr hFile);

        private const uint CF_HDROP = 15;
        private const int SystemExtendedHandleInformation = 64;
        private const uint FILE_TYPE_DISK = 0x0001;
        private const uint PROCESS_DUP_HANDLE = 0x0040;
        private const uint DUPLICATE_SAME_ACCESS = 0x00000002;
        private const uint VOLUME_NAME_DOS = 0x00000000;

        private static readonly object _sessionLock = new object();
        private static CopySession? _activeSession = null;
        private static uint _consumedClipboardSeq = 0;

        private class CopySession
        {
            public List<string> PendingFiles { get; } = new List<string>();
            public Dictionary<string, string> VirtualDirToPhysicalDir { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> ReservedVirtualToPhysical { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> ConsumedVirtualFiles { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public uint ClipboardSequenceNumber { get; set; }
            public int InitialCount { get; set; }
            public bool IsFromClipboard { get; set; }
            public bool IsVirtualClipboard { get; set; }
            public int VirtualRemainingCount { get; set; }
            public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

            public bool IsVirtualFileConsumed(string? virtualPath)
            {
                if (string.IsNullOrWhiteSpace(virtualPath))
                    return false;
                return ConsumedVirtualFiles.Contains(NormalizePath(virtualPath));
            }

            public void ReserveVirtualFile(string? virtualPath, string? physicalFilePath)
            {
                if (!string.IsNullOrWhiteSpace(virtualPath) && !string.IsNullOrWhiteSpace(physicalFilePath))
                {
                    ReservedVirtualToPhysical[NormalizePath(virtualPath)] = physicalFilePath;
                }
            }

            public void ReleaseReservation(string? virtualPath)
            {
                if (!string.IsNullOrWhiteSpace(virtualPath))
                {
                    ReservedVirtualToPhysical.Remove(NormalizePath(virtualPath));
                }
            }

            public void MarkConsumed(string? virtualPath)
            {
                if (!string.IsNullOrWhiteSpace(virtualPath))
                {
                    string norm = NormalizePath(virtualPath);
                    if (ConsumedVirtualFiles.Add(norm) && IsVirtualClipboard && VirtualRemainingCount > 0)
                    {
                        VirtualRemainingCount--;
                    }
                    ReservedVirtualToPhysical.Remove(norm);
                }
                LastActivityUtc = DateTime.UtcNow;
            }

            public void UnmarkConsumed(string? virtualPath)
            {
                if (!string.IsNullOrWhiteSpace(virtualPath))
                {
                    string norm = NormalizePath(virtualPath);
                    ConsumedVirtualFiles.Remove(norm);
                    ReservedVirtualToPhysical.Remove(norm);
                }
            }

            public void MapVirtualDirectory(string? virtualPath, string? physicalFilePath)
            {
                if (string.IsNullOrWhiteSpace(virtualPath) || string.IsNullOrWhiteSpace(physicalFilePath))
                    return;

                string virtualParent = GetParentPath(virtualPath);
                string? physicalDir = Path.GetDirectoryName(physicalFilePath);
                if (!string.IsNullOrEmpty(physicalDir))
                {
                    VirtualDirToPhysicalDir[virtualParent] = physicalDir;
                }
            }

            public string? FindAndConsume(string targetFileName, string? relativeVirtualPath, long expectedSize = -1)
            {
                if (IsVirtualFileConsumed(relativeVirtualPath))
                {
                    return null;
                }

                // 0. Проверяем активную бронь для этого виртуального пути (защита от 0-байтных заглушек Explorer:
                // Create -> 0-byte Cleanup -> Open -> Write повторно использует свой же зарезервированный источник,
                // не воруя одноименный файл из соседней папки и не сдвигая очередь PendingFiles)
                if (!string.IsNullOrWhiteSpace(relativeVirtualPath))
                {
                    string normVirtualPath = NormalizePath(relativeVirtualPath);
                    if (ReservedVirtualToPhysical.TryGetValue(normVirtualPath, out string? reservedPath))
                    {
                        if (!IsCandidateOnMountedDrive(reservedPath) &&
                            File.Exists(reservedPath) &&
                            DoesFileSizeMatch(reservedPath, expectedSize))
                        {
                            string? verifiedReserved = VerifyReadable(reservedPath);
                            if (verifiedReserved != null)
                            {
                                LastActivityUtc = DateTime.UtcNow;
                                return verifiedReserved;
                            }
                        }
                        ReservedVirtualToPhysical.Remove(normVirtualPath);
                    }
                }

                // 1. Проверяем явную очередь файлов (например, Ctrl+C / CF_HDROP),
                // выбирая лучший вариант по совпадению сегментов пути и размеру файла.
                // VerifyReadable вызывается строго один раз после цикла для победившего кандидата.
                int bestIndex = -1;
                int bestScore = -1;
                string? bestCandidate = null;

                for (int i = 0; i < PendingFiles.Count; i++)
                {
                    string candidate = PendingFiles[i];
                    if (IsCandidateOnMountedDrive(candidate) || !File.Exists(candidate))
                        continue;

                    string candidateName = Path.GetFileName(candidate);
                    if (!string.Equals(candidateName, targetFileName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!DoesFileSizeMatch(candidate, expectedSize))
                        continue;

                    int score = ScoreCandidatePathMatch(candidate, relativeVirtualPath);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIndex = i;
                        bestCandidate = candidate;
                    }
                }

                if (bestIndex >= 0 && bestCandidate != null)
                {
                    string? verified = VerifyReadable(bestCandidate);
                    if (verified != null)
                    {
                        PendingFiles.RemoveAt(bestIndex);
                        ReserveVirtualFile(relativeVirtualPath, verified);
                        MapVirtualDirectory(relativeVirtualPath, verified);
                        LastActivityUtc = DateTime.UtcNow;
                        return verified;
                    }
                }

                // 2. Если файл не из очереди Ctrl+C — проверяем физическую папку-источник,
                // привязанную СТРОГО к текущей виртуальной папке назначения (VirtualParentDir)
                if (!string.IsNullOrWhiteSpace(relativeVirtualPath))
                {
                    string virtualParent = GetParentPath(relativeVirtualPath);
                    if (VirtualDirToPhysicalDir.TryGetValue(virtualParent, out string? mappedPhysicalDir))
                    {
                        string directCandidate = Path.Combine(mappedPhysicalDir, targetFileName);
                        if (!IsCandidateOnMountedDrive(directCandidate) && File.Exists(directCandidate))
                        {
                            if (DoesFileSizeMatch(directCandidate, expectedSize))
                            {
                                string? verified = VerifyReadable(directCandidate);
                                if (verified != null)
                                {
                                    ReserveVirtualFile(relativeVirtualPath, verified);
                                    LastActivityUtc = DateTime.UtcNow;
                                    return verified;
                                }
                            }
                        }
                    }
                }

                return null;
            }

            public bool IsExpired(TimeSpan timeout)
            {
                // Если ещё есть необработанные файлы из заранее известного списка (пачка Ctrl+C),
                // даём увеличенный таймаут (2 минуты)
                if (PendingFiles.Count > 0)
                {
                    return (DateTime.UtcNow - LastActivityUtc) > TimeSpan.FromMinutes(2);
                }
                // Если файлы идут по одному (Drag-and-Drop или бэкапер), держим привязки активными заданный таймаут
                return (DateTime.UtcNow - LastActivityUtc) > timeout;
            }
        }

        private static uint SafeGetClipboardSequenceNumber()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return 0;
            try
            {
                return GetClipboardSequenceNumber();
            }
            catch
            {
                return 0;
            }
        }

        private static void MarkSessionFileCompleted(string? virtualPath)
        {
            lock (_sessionLock)
            {
                if (_activeSession != null)
                {
                    _activeSession.MarkConsumed(virtualPath);
                    if (_activeSession.IsFromClipboard && _activeSession.ClipboardSequenceNumber != 0)
                    {
                        bool physicalDone = !_activeSession.IsVirtualClipboard && _activeSession.PendingFiles.Count == 0;
                        bool virtualDone = _activeSession.IsVirtualClipboard && _activeSession.VirtualRemainingCount <= 0;
                        if (physicalDone || virtualDone)
                        {
                            _consumedClipboardSeq = _activeSession.ClipboardSequenceNumber;
                        }
                    }
                }
            }
        }

        private static void ReleaseSessionReservation(string? virtualPath)
        {
            lock (_sessionLock)
            {
                _activeSession?.ReleaseReservation(virtualPath);
            }
        }

        private static CopySession CreateCopySession(List<string> rawCandidates, uint clipboardSequence = 0)
        {
            var session = new CopySession
            {
                ClipboardSequenceNumber = clipboardSequence,
                IsFromClipboard = clipboardSequence != 0
            };
            var addedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasExplicitFiles = rawCandidates.Exists(c => File.Exists(c));

            foreach (var raw in rawCandidates)
            {
                if (File.Exists(raw))
                {
                    if (addedFiles.Add(raw))
                    {
                        session.PendingFiles.Add(raw);
                    }
                }
                else if (Directory.Exists(raw))
                {
                    // Рекурсивное перечисление файлов папки в очередь выполняется ТОЛЬКО если в списке кандидатов
                    // нет явных файлов (т.е. пользователь скопировал папку целиком через Ctrl+C).
                    if (!hasExplicitFiles)
                    {
                        try
                        {
                            foreach (var file in Directory.EnumerateFiles(raw, "*", SearchOption.AllDirectories))
                            {
                                if (addedFiles.Add(file))
                                {
                                    session.PendingFiles.Add(file);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Debug("WinFsp", $"[Сессия] Ошибка перечисления файлов папки '{raw}': {ex.Message}");
                        }
                    }
                }
            }

            session.InitialCount = session.PendingFiles.Count;
            session.LastActivityUtc = DateTime.UtcNow;
            return session;
        }

        /// <summary>
        /// Единый детерминированный метод разрешения источника создаваемого файла (Root Cause First):
        /// 1. Проверяет свежее изменение буфера обмена (GetClipboardSequenceNumber)
        /// 2. Проверяет активную сессию строго для текущей виртуальной родительской папки (VirtualParentDir)
        /// 3. Если виртуальная папка сменилась, файл повторный (коллизия имён) или кэша нет —
        ///    опрашивает открытые файловые дескрипторы процесса callerPid через ядро Windows.
        /// </summary>
        private static (Node? VirtualSource, string? OriginalSource) ResolveFileSource(
            string targetFileName,
            string? relativeVirtualPath,
            int callerPid,
            NodeRepository? repository,
            long expectedSize = -1)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return (null, null);

            if (callerPid <= 0)
            {
                try
                {
                    callerPid = FileSystemBase.GetOperationProcessId();
                }
                catch { }
            }

            string? cleanVirtualPath = string.IsNullOrWhiteSpace(relativeVirtualPath) ? null : NormalizePath(relativeVirtualPath);
            uint currentClipSeq = SafeGetClipboardSequenceNumber();

            AppLogger.Info("WinFsp", $"[Источник] Поиск оригинального файла для '{targetFileName}' (виртуальный путь: '{cleanVirtualPath ?? relativeVirtualPath}')...");

            bool hasNewClipboardSequence = false;
            bool isRepeatInSameSession = false;
            bool hasActiveClipboardSessionForCurrentSeq = false;
            bool hasActiveVirtualClipboardBatch = false;
            bool isSessionMissing = false;
            string diagSessionState = "null";

            lock (_sessionLock)
            {
                if (_activeSession != null)
                {
                    diagSessionState = $"clip={_activeSession.IsFromClipboard},virtClip={_activeSession.IsVirtualClipboard},seq={_activeSession.ClipboardSequenceNumber},vRem={_activeSession.VirtualRemainingCount},pend={_activeSession.PendingFiles.Count},init={_activeSession.InitialCount},consumedV={_activeSession.IsVirtualFileConsumed(cleanVirtualPath)},ageMs={(int)(DateTime.UtcNow - _activeSession.LastActivityUtc).TotalMilliseconds}";
                    if (_activeSession.IsExpired(TimeSpan.FromSeconds(20)))
                    {
                        if (_activeSession.IsFromClipboard && _activeSession.ClipboardSequenceNumber != 0)
                        {
                            _consumedClipboardSeq = _activeSession.ClipboardSequenceNumber;
                        }
                        AppLogger.Info("WinFsp", $"[Источник] Предыдущая сессия копирования завершена по таймауту бездействия (20 сек).");
                        _activeSession = null;
                        isSessionMissing = true;
                    }
                    else
                    {
                        if (currentClipSeq != 0 && currentClipSeq != _activeSession.ClipboardSequenceNumber)
                        {
                            // Буфер обмена изменился с момента инициализации текущей сессии -> новый Ctrl+C имеет приоритет
                            if (_activeSession.IsFromClipboard && _activeSession.ClipboardSequenceNumber != 0)
                            {
                                _consumedClipboardSeq = _activeSession.ClipboardSequenceNumber;
                            }
                            hasNewClipboardSequence = true;
                        }
                        else if (_activeSession.IsVirtualFileConsumed(cleanVirtualPath))
                        {
                            // Тот же виртуальный файл (или имя в той же папке) загружается повторно -> принудительно пересканируем источник
                            isRepeatInSameSession = true;
                            AppLogger.Info("WinFsp", $"[Источник] Повторная запись '{cleanVirtualPath}' в рамках текущей сессии — пропускаем кэш папки и обновляем источник.");
                        }
                        else if (_activeSession.IsVirtualClipboard)
                        {
                            hasActiveVirtualClipboardBatch = _activeSession.VirtualRemainingCount > 0;
                        }
                        else
                        {
                            if (_activeSession.IsFromClipboard && _activeSession.ClipboardSequenceNumber == currentClipSeq)
                            {
                                hasActiveClipboardSessionForCurrentSeq = true;
                            }

                            string? match = _activeSession.FindAndConsume(targetFileName, cleanVirtualPath, expectedSize);
                            if (match != null)
                            {
                                if (_activeSession.InitialCount > 0 && _activeSession.PendingFiles.Count > 0)
                                {
                                    AppLogger.Info("WinFsp", $"[Источник] Найдено совпадение в активной сессии (осталось файлов в очереди: {_activeSession.PendingFiles.Count}/{_activeSession.InitialCount}): '{match}'");
                                }
                                else
                                {
                                    AppLogger.Info("WinFsp", $"[Источник] Найдено совпадение в активной сессии для папки '{GetParentPath(cleanVirtualPath ?? "/")}': '{match}'");
                                }
                                return (null, match);
                            }
                        }
                    }
                }
                else
                {
                    isSessionMissing = true;
                }
            }

            bool isClipboardAlreadyConsumed = currentClipSeq != 0 && currentClipSeq == _consumedClipboardSeq;
            bool shouldCheckFreshClipboardFirst = !isRepeatInSameSession &&
                !isClipboardAlreadyConsumed &&
                !hasActiveClipboardSessionForCurrentSeq &&
                (isSessionMissing || hasNewClipboardSequence || hasActiveVirtualClipboardBatch);

            AppLogger.Info("WinFsp", $"[DIAG Resolve] target='{targetFileName}', vPath='{cleanVirtualPath}', pid={GetDiagProcessName(callerPid)}, expSize={expectedSize}, clipSeq={currentClipSeq}, consumedSeq={_consumedClipboardSeq}, checkFreshClip={shouldCheckFreshClipboardFirst}, flags(missing={isSessionMissing},newSeq={hasNewClipboardSequence},repeat={isRepeatInSameSession},clipCons={isClipboardAlreadyConsumed},actClip={hasActiveClipboardSessionForCurrentSeq},virtBatch={hasActiveVirtualClipboardBatch}), session=[{diagSessionState}]");

            // Шаг 2: Проверяем свежий системный буфер обмена Windows (Ctrl+C / Ctrl+V, CF_HDROP),
            // только если текущий ClipboardSequenceNumber ещё не отработан предыдущей операцией.
            if (shouldCheckFreshClipboardFirst)
            {
                var (clipVirtual, clipOriginal, _) = TryMatchFromClipboard(
                    targetFileName,
                    cleanVirtualPath,
                    expectedSize,
                    currentClipSeq,
                    callerPid,
                    repository,
                    isRetryFallback: false);
                if (clipVirtual != null || clipOriginal != null)
                {
                    return (clipVirtual, clipOriginal);
                }
            }

            // Шаг 3: Опрашиваем открытые файловые дескрипторы (Handles) процесса callerPid через ядро Windows.
            // При уже отработанном буфере обмена (например, перенос мышкой после Ctrl+C -> Ctrl+V) этот шаг
            // выполняется ДО повторного обращения к старому буферу обмена.
            var recentCollidedNames = GetRecentCollisionNames(cleanVirtualPath, callerPid);
            var handleCandidates = CollectOpenFileHandles(
                targetFileName,
                includeMountedDrive: true,
                callerPid: callerPid,
                targetVirtualPath: cleanVirtualPath,
                expectedSize: expectedSize,
                alternatePhysicalNames: recentCollidedNames);
            AppLogger.Info("WinFsp", $"[DIAG Resolve Handles] target='{targetFileName}', handleCandidates={handleCandidates.Count}" + (handleCandidates.Count > 0 ? $": [{string.Join(" | ", handleCandidates.Take(5))}]" : ""));
            if (handleCandidates.Count > 0)
            {
                // Шаг 3.1: Для дескрипторов Drag-and-Drop / бэкаперов: приоритет у физических файлов
                var physicalCandidates = handleCandidates.Where(c => !IsCandidateOnMountedDrive(c)).ToList();
                if (physicalCandidates.Count > 0)
                {
                    string? matchedPhysical = null;
                    foreach (var phys in physicalCandidates)
                    {
                        matchedPhysical = CheckCandidate(phys, targetFileName, cleanVirtualPath, expectedSize);
                        if (matchedPhysical != null)
                            break;
                    }

                    // Если перенос с ПК в TG вызвал переименование (Keep Both: "file (2).ext"), проверяем совпадение с недавней коллизией
                    if (matchedPhysical == null && recentCollidedNames != null)
                    {
                        foreach (var phys in physicalCandidates)
                        {
                            string physName = Path.GetFileName(phys);
                            if (recentCollidedNames.Contains(physName) && File.Exists(phys) && DoesFileSizeMatch(phys, expectedSize))
                            {
                                matchedPhysical = VerifyReadable(phys);
                                if (matchedPhysical != null)
                                    break;
                            }
                        }
                    }

                    if (matchedPhysical != null)
                    {
                        lock (_sessionLock)
                        {
                            if (_activeSession != null && _activeSession.IsFromClipboard && _activeSession.ClipboardSequenceNumber != 0)
                            {
                                _consumedClipboardSeq = _activeSession.ClipboardSequenceNumber;
                            }

                            if (_activeSession == null ||
                                _activeSession.IsExpired(TimeSpan.FromSeconds(20)) ||
                                _activeSession.IsFromClipboard)
                            {
                                _activeSession = new CopySession
                                {
                                    ClipboardSequenceNumber = currentClipSeq,
                                    IsFromClipboard = false
                                };
                            }
                            else if (currentClipSeq != 0)
                            {
                                _activeSession.ClipboardSequenceNumber = currentClipSeq;
                            }

                            _activeSession.UnmarkConsumed(cleanVirtualPath);
                            _activeSession.ReserveVirtualFile(cleanVirtualPath, matchedPhysical);
                            _activeSession.MapVirtualDirectory(cleanVirtualPath, matchedPhysical);
                            _activeSession.LastActivityUtc = DateTime.UtcNow;

                            AppLogger.Info("WinFsp", $"[Handles DragDrop] Привязана папка-источник '{Path.GetDirectoryName(matchedPhysical)}' для виртуального каталога '{GetParentPath(cleanVirtualPath ?? "/")}'");
                        }
                        return (null, matchedPhysical);
                    }
                }

                // Шаг 3.2: ТОЛЬКО если среди дескрипторов физический файл не найден, проверяем виртуальный файл на диске Z:
                if (repository != null)
                {
                    foreach (var cand in handleCandidates)
                    {
                        if (IsCandidateOnMountedDrive(cand))
                        {
                            var vMatch = MatchVirtualCandidate(cand, targetFileName, cleanVirtualPath ?? "", repository);
                            if (vMatch != null)
                            {
                                return (vMatch, null);
                            }
                        }
                    }
                }
            }

            // Шаг 4: Резервный fallback на уже использованный ранее буфер обмена (на случай повторного Ctrl+V
            // того же буфера для мелких файлов, чьи дескрипторы уже закрылись к моменту Create).
            if (!shouldCheckFreshClipboardFirst && !isRepeatInSameSession)
            {
                var (retryVirtual, retryOriginal, _) = TryMatchFromClipboard(
                    targetFileName,
                    cleanVirtualPath,
                    expectedSize,
                    currentClipSeq,
                    callerPid,
                    repository,
                    isRetryFallback: true);
                if (retryVirtual != null || retryOriginal != null)
                {
                    return (retryVirtual, retryOriginal);
                }
            }

            // Шаг 5: Drag-and-Drop копирование внутри диска Z: с автоматическим переименованием при коллизии (Ctrl + Drag),
            // выполняемое строго при отсутствии физических файлов-кандидатов на дисках ПК.
            if (repository != null &&
                !isRepeatInSameSession &&
                !handleCandidates.Any(c => !IsCandidateOnMountedDrive(c) && File.Exists(c)))
            {
                var dragRenamedVirtual = TryMatchRenamedVirtualFromSignals(cleanVirtualPath, targetFileName, callerPid, expectedSize, repository);
                if (dragRenamedVirtual != null)
                {
                    AppLogger.Info("WinFsp", $"[DragDrop Renamed VirtualCopy] Определен виртуальный источник после коллизии: ID {dragRenamedVirtual.Id} ('{dragRenamedVirtual.Name}') -> '{cleanVirtualPath}'.");
                    return (dragRenamedVirtual, null);
                }
            }

            AppLogger.Info("WinFsp", $"[DIAG Resolve Result] target='{targetFileName}', vPath='{cleanVirtualPath}' -> NOT RESOLVED (null, null)");
            return (null, null);
        }

        private static void UpdateVirtualClipboardSession(List<string> clipboardCandidates, uint currentClipSeq, NodeRepository repository, bool isRetryFallback)
        {
            int expectedVirtualFiles = 0;
            foreach (var vClip in clipboardCandidates)
            {
                if (!IsCandidateOnMountedDrive(vClip)) continue;
                string? vp = GetVirtualPathFromMountedPath(vClip);
                if (string.IsNullOrEmpty(vp)) continue;
                var vNode = repository.GetNodeByPath(vp);
                if (vNode == null) continue;
                expectedVirtualFiles += vNode.IsDir
                    ? repository.GetSubtreeNodes(vNode.Id).Count(n => !n.IsDir)
                    : 1;
            }

            lock (_sessionLock)
            {
                if (_activeSession == null ||
                    !_activeSession.IsVirtualClipboard ||
                    _activeSession.ClipboardSequenceNumber != currentClipSeq ||
                    (isRetryFallback && _activeSession.VirtualRemainingCount <= 0))
                {
                    _activeSession = new CopySession
                    {
                        ClipboardSequenceNumber = currentClipSeq,
                        IsFromClipboard = currentClipSeq != 0,
                        IsVirtualClipboard = true,
                        VirtualRemainingCount = Math.Max(1, expectedVirtualFiles),
                        LastActivityUtc = DateTime.UtcNow
                    };
                }
                else
                {
                    _activeSession.LastActivityUtc = DateTime.UtcNow;
                }
            }
        }

        private static (Node? VirtualSource, string? OriginalSource, bool HasFileCandidates) TryMatchFromClipboard(
            string targetFileName,
            string? cleanVirtualPath,
            long expectedSize,
            uint currentClipSeq,
            int callerPid,
            NodeRepository? repository,
            bool isRetryFallback)
        {
            var clipboardCandidates = CollectClipboardCandidates(includeMountedDrive: true);
            AppLogger.Info("WinFsp", $"[DIAG Clipboard] retry={isRetryFallback}, clipSeq={currentClipSeq}, target='{targetFileName}', vPath='{cleanVirtualPath}', mount='{WinFspServer.ActiveMountPoint}', count={clipboardCandidates.Count}" + (clipboardCandidates.Count > 0 ? $": [{string.Join(" | ", clipboardCandidates.Take(5))}]" : ""));
            if (clipboardCandidates.Count > 0)
            {
                string logTag = isRetryFallback ? "Источник: Повторный буфер обмена" : "Источник: Буфер обмена";
                var physicalClip = clipboardCandidates.Where(c => !IsCandidateOnMountedDrive(c)).ToList();

                // 2.1 Проверяем виртуальные кандидаты в буфере обмена (диск Z:)
                if (repository != null)
                {
                    foreach (var clip in clipboardCandidates)
                    {
                        if (IsCandidateOnMountedDrive(clip))
                        {
                            var vMatch = MatchVirtualCandidate(clip, targetFileName, cleanVirtualPath ?? "", repository);
                            if (vMatch != null)
                            {
                                UpdateVirtualClipboardSession(clipboardCandidates, currentClipSeq, repository, isRetryFallback);
                                AppLogger.Info("WinFsp", $"[{logTag}] Найдено прямое совпадение в буфере обмена Windows (Виртуальная копия): '{clip}'");
                                return (vMatch, null, true);
                            }
                        }
                    }

                    // 2.1б Копирование внутри диска в ту же папку (или при коллизии имён), когда Windows переименовала файл
                    // (без хардкода локализованных суффиксов вроде "— копия" / "- Copy" / "(2)"), строго при отсутствии физических файлов в буфере
                    if (physicalClip.Count == 0 && !string.IsNullOrEmpty(cleanVirtualPath))
                    {
                        var renamedMatch = TryMatchRenamedVirtualCandidate(clipboardCandidates, cleanVirtualPath, targetFileName, callerPid, expectedSize, repository);
                        if (renamedMatch != null)
                        {
                            UpdateVirtualClipboardSession(clipboardCandidates, currentClipSeq, repository, isRetryFallback);
                            AppLogger.Info("WinFsp", $"[{logTag}] Определена переименованная виртуальная копия по факту коллизии/чтения: ID {renamedMatch.Id} ('{renamedMatch.Name}') -> '{cleanVirtualPath}'.");
                            return (renamedMatch, null, true);
                        }
                    }
                }

                // 2.2 Проверяем физические кандидаты в буфере обмена (диски D:, C: и др.)
                if (physicalClip.Count > 0)
                {
                    var clipSession = CreateCopySession(physicalClip, currentClipSeq);
                    string? match = clipSession.FindAndConsume(targetFileName, cleanVirtualPath, expectedSize);
                    if (match != null)
                    {
                        lock (_sessionLock)
                        {
                            _activeSession = clipSession;
                        }
                        AppLogger.Info("WinFsp", $"[{logTag}] Найдено прямое совпадение в буфере обмена Windows (Локальный файл): '{match}'");
                        return (null, match, true);
                    }
                }

                return (null, null, true);
            }

            // Если в буфере обмена нет файлов (например, текст или картинка) — фиксируем sequence,
            // чтобы не переоткрывать Clipboard на каждый файл, и пробуем добрать из текущей активной сессии.
            lock (_sessionLock)
            {
                if (currentClipSeq != 0)
                {
                    _consumedClipboardSeq = currentClipSeq;
                }

                if (_activeSession != null && currentClipSeq != 0)
                {
                    _activeSession.ClipboardSequenceNumber = currentClipSeq;
                }

                if (_activeSession != null && !_activeSession.IsVirtualClipboard && !_activeSession.IsVirtualFileConsumed(cleanVirtualPath))
                {
                    string? fallbackMatch = _activeSession.FindAndConsume(targetFileName, cleanVirtualPath, expectedSize);
                    if (fallbackMatch != null)
                    {
                        if (_activeSession.InitialCount > 0 && _activeSession.PendingFiles.Count > 0)
                        {
                            AppLogger.Info("WinFsp", $"[Источник] Найдено совпадение в активной сессии (осталось файлов в очереди: {_activeSession.PendingFiles.Count}/{_activeSession.InitialCount}): '{fallbackMatch}'");
                        }
                        else
                        {
                            AppLogger.Info("WinFsp", $"[Источник] Найдено совпадение в активной сессии для папки '{GetParentPath(cleanVirtualPath ?? "/")}': '{fallbackMatch}'");
                        }
                        return (null, fallbackMatch, false);
                    }
                }
            }

            return (null, null, false);
        }

        private static string? TryFindSourceFile(string targetFileName, string? relativeVirtualPath = null, int callerPid = 0, NodeRepository? repository = null, long expectedSize = -1)
        {
            var (_, originalSource) = ResolveFileSource(targetFileName, relativeVirtualPath, callerPid, repository, expectedSize);
            return originalSource;
        }

        private static List<string> CollectAllCandidates(string? targetFileName = null, int callerPid = 0, bool includeMountedDrive = false, string? targetVirtualPath = null)
        {
            var list = new List<string>();

            // 1. Системный буфер обмена Windows (Ctrl+C / Ctrl+V, CF_HDROP)
            // Быстрое чтение структуры памяти без блокирующих вызовов
            var clipList = CollectClipboardCandidates(includeMountedDrive);
            list.AddRange(clipList);

            // 2. Инспекция файловых дескрипторов (Handles) вызывающего процесса и explorer.exe для Drag-and-Drop / сторонних копировщиков
            // Работает на уровне ядра через NtQuerySystemInformation, без COM/UI сообщений
            var handleList = CollectOpenFileHandles(targetFileName, includeMountedDrive: includeMountedDrive, callerPid: callerPid, targetVirtualPath: targetVirtualPath);
            list.AddRange(handleList);

            return list;
        }

        private static List<string> CollectClipboardCandidates(bool includeMountedDrive = false)
        {
            var results = new List<string>();
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        IntPtr hDrop = GetClipboardData(CF_HDROP);
                        if (hDrop != IntPtr.Zero)
                        {
                            uint fileCount = DragQueryFileCount(hDrop, 0xFFFFFFFFU, IntPtr.Zero, 0);
                            var sb = new StringBuilder(1024);
                            for (uint i = 0; i < fileCount; i++)
                            {
                                sb.Clear();
                                uint len = DragQueryFile(hDrop, i, sb, (uint)sb.Capacity);
                                if (len > 0)
                                {
                                    string clipPath = sb.ToString();
                                    if (includeMountedDrive || !IsCandidateOnMountedDrive(clipPath))
                                    {
                                        results.Add(clipPath);
                                    }
                                }
                            }
                        }
                        break;
                    }
                    catch { }
                    finally
                    {
                        CloseClipboard();
                    }
                }
                Thread.Sleep(10);
            }
            return results;
        }

        private static ushort? _cachedFileObjectTypeIndex = null;
        private static readonly object _fileTypeIndexLock = new object();

        /// <summary>
        /// Безопасно определяет ObjectTypeIndex для типа 'File' в ядре NT.
        /// Создает временный файловый хэндл в собственном процессе и считывает его ObjectTypeIndex из таблицы NT.
        /// Гарантирует, что DuplicateHandle будет вызываться ТОЛЬКО для реальных дисковых файлов,
        /// полностью исключая зависания ядра на IPC/NamedPipe/Socket хэндлах.
        /// </summary>
        private static ushort? GetFileObjectTypeIndex()
        {
            if (_cachedFileObjectTypeIndex.HasValue)
                return _cachedFileObjectTypeIndex.Value;

            lock (_fileTypeIndexLock)
            {
                if (_cachedFileObjectTypeIndex.HasValue)
                    return _cachedFileObjectTypeIndex.Value;

                string tempFile = Path.GetTempFileName();
                try
                {
                    using (var fs = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        IntPtr myHandle = fs.SafeFileHandle.DangerousGetHandle();
                        int myPid = Process.GetCurrentProcess().Id;

                        int bufferSize = 2 * 1024 * 1024;
                        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
                        try
                        {
                            int status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, bufferSize, out int returnLength);
                            if (status != 0 && returnLength > bufferSize)
                            {
                                Marshal.FreeHGlobal(buffer);
                                bufferSize = returnLength + 256 * 1024;
                                buffer = Marshal.AllocHGlobal(bufferSize);
                                status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, bufferSize, out returnLength);
                            }

                            if (status == 0)
                            {
                                long handleCount = Marshal.ReadInt64(buffer);
                                IntPtr currentPtr = IntPtr.Add(buffer, 16);
                                int entrySize = IntPtr.Size == 8 ? 40 : 28;

                                for (long i = 0; i < handleCount; i++)
                                {
                                    IntPtr entryPtr = IntPtr.Add(currentPtr, (int)(i * entrySize));
                                    int pid = (int)Marshal.ReadInt64(IntPtr.Add(entryPtr, 8));
                                    if (pid == myPid)
                                    {
                                        IntPtr val = (IntPtr)Marshal.ReadInt64(IntPtr.Add(entryPtr, 16));
                                        if (val == myHandle)
                                        {
                                            ushort typeIndex = (ushort)Marshal.ReadInt16(IntPtr.Add(entryPtr, 30));
                                            _cachedFileObjectTypeIndex = typeIndex;
                                            // AppLogger.Debug("WinFsp", $"[Handles DragDrop] Системный индекс типа 'File' ядра Windows: {typeIndex}");
                                            return typeIndex;
                                        }
                                    }
                                }
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(buffer);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Debug("WinFsp", $"[Handles DragDrop] Не удалось определить FileObjectTypeIndex: {ex.Message}");
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }

                return null;
            }
        }

        /// <summary>
        /// Безопасное получение пути к файлу по дескриптору ядра с жестким таймаутом (50 мс).
        /// Изолирует системный вызов GetFinalPathNameByHandle в выделенном потоке,
        /// гарантируя, что даже при блокировке чужого дескриптора процесс и драйвер WinFsp НИКОГДА не зависнут.
        /// </summary>
        private static string? GetPathWithTimeout(IntPtr targetHandle, int timeoutMs = 50)
        {
            string? result = null;
            var thread = new Thread(() =>
            {
                try
                {
                    if (GetFileType(targetHandle) == FILE_TYPE_DISK)
                    {
                        var sb = new StringBuilder(1024);
                        uint len = GetFinalPathNameByHandle(targetHandle, sb, (uint)sb.Capacity, VOLUME_NAME_DOS);
                        if (len > 0)
                        {
                            result = sb.ToString();
                        }
                    }
                }
                catch { }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };

            thread.Start();
            if (thread.Join(timeoutMs))
            {
                return result;
            }

            return null; // Таймаут: дескриптор заблокирован ядром, не ждем его
        }

        /// <summary>
        /// Безопасное извлечение путей к файлам, открытым процессами explorer.exe или вызывающим процессом (бэкапером, Total Commander и др.).
        /// Опрашивает открытые файловые дескрипторы через ядро Windows (NtQuerySystemInformation),
        /// не отправляя оконных сообщений и не обращаясь к OLE/COM, что полностью исключает дедлоки.
        /// </summary>
        private static List<string> CollectOpenFileHandles(
            string? targetFileName,
            bool includeMountedDrive = false,
            int callerPid = 0,
            string? targetVirtualPath = null,
            long expectedSize = -1,
            IReadOnlyCollection<string>? alternatePhysicalNames = null)
        {
            var results = new List<string>();
            var dirFallbackResults = new List<string>();
            try
            {
                var targetPids = new HashSet<int>();
                int currentPid = Process.GetCurrentProcess().Id;

                string callerName = "unknown";
                bool isCallerExplorer = false;
                if (callerPid > 0 && callerPid != currentPid)
                {
                    try
                    {
                        callerName = Process.GetProcessById(callerPid).ProcessName;
                        isCallerExplorer = callerName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
                    }
                    catch { }
                }

                if (callerPid > 0 && callerPid != currentPid)
                {
                    // Опрашиваем СТРОГО процесс, инициировавший операцию (callerPid) —
                    // будь то сторонняя утилита (Create Synchronicity) или конкретный рабочий процесс Проводника (explorer.exe),
                    // исключая сотни чужих дескрипторов посторонних фоновых окон Explorer и гарантируя удержание в бюджете времени.
                    targetPids.Add(callerPid);
                }
                else
                {
                    // Резерв: если callerPid неизвестен или совпал с текущим процессом, опрашиваем процессы Проводника
                    var explorerProcesses = Process.GetProcessesByName("explorer");
                    foreach (var p in explorerProcesses)
                    {
                        if (p.Id != currentPid)
                        {
                            targetPids.Add(p.Id);
                        }
                    }
                }

                if (targetPids.Count == 0)
                {
                    AppLogger.Debug("WinFsp", "[Handles DragDrop] Целевые процессы для инспекции дескрипторов не найдены.");
                    return results;
                }

                string callerInfo = callerPid > 0 && callerPid != currentPid ? $", вызывающий процесс: {callerName} (PID: {callerPid})" : "";
                AppLogger.Info("WinFsp", $"[Handles DragDrop] Сканирование хэндлов для {targetPids.Count} процессов (PID: {string.Join(", ", targetPids)}{callerInfo})...");

                // STATUS_INFO_LENGTH_MISMATCH = 0xC0000004
                const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
                int bufferSize = 4 * 1024 * 1024; // 4 MB
                IntPtr buffer = Marshal.AllocHGlobal(bufferSize);

                try
                {
                    int status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, bufferSize, out int returnLength);
                    while ((uint)status == STATUS_INFO_LENGTH_MISMATCH || status != 0)
                    {
                        Marshal.FreeHGlobal(buffer);
                        bufferSize = Math.Max(bufferSize * 2, returnLength + 256 * 1024);
                        if (bufferSize > 64 * 1024 * 1024) // Ограничитель 64 МБ
                        {
                            AppLogger.Warn("WinFsp", $"[Handles DragDrop] Размер буфера превысил лимит ({bufferSize} байт).");
                            return results;
                        }
                        buffer = Marshal.AllocHGlobal(bufferSize);
                        status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, bufferSize, out returnLength);
                        if (status == 0) break;
                        if ((uint)status != STATUS_INFO_LENGTH_MISMATCH)
                        {
                            AppLogger.Warn("WinFsp", $"[Handles DragDrop] NtQuerySystemInformation вернул ошибку NTSTATUS: 0x{status:X8}");
                            return results;
                        }
                    }

                    long handleCount = Marshal.ReadInt64(buffer);
                    ushort? fileTypeIndex = GetFileObjectTypeIndex();
                    AppLogger.Info("WinFsp", $"[Handles DragDrop] В системе обнаружено {handleCount} дескрипторов ядра (File Type Index: {fileTypeIndex?.ToString() ?? "не определен"}).");

                    IntPtr currentPtr = IntPtr.Add(buffer, 16); // Пропуск NumberOfHandles и Reserved (16 байт)
                    int entrySize = IntPtr.Size == 8 ? 40 : 28; // SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX (40 байт на x64, 28 на x86)
                    var processHandles = new Dictionary<int, IntPtr>();
                    var pathSb = new StringBuilder(1024);
                    IntPtr currentProcess = Process.GetCurrentProcess().Handle;

                    int matchedHandles = 0;
                    var scanSw = System.Diagnostics.Stopwatch.StartNew();
                    const int SCAN_TIME_BUDGET_MS = 350; // Увеличенный бюджет времени (350 мс вместо 100 мс)
                    try
                    {
                        for (long i = 0; i < handleCount; i++)
                        {
                            // Оптимизация: опрашиваем таймер раз в 1024 итерации, устраняя оверхед QPC на 85 000 циклов
                            if ((i & 0x3FF) == 0 && scanSw.ElapsedMilliseconds > SCAN_TIME_BUDGET_MS)
                            {
                                AppLogger.Warn("WinFsp", $"[Handles DragDrop] Превышен общий бюджет времени сканирования хэндлов ({SCAN_TIME_BUDGET_MS} мс). Обработано дескрипторов: {i}/{handleCount}, найдено совпадений: {results.Count}");
                                break;
                            }

                            IntPtr entryPtr = IntPtr.Add(currentPtr, (int)(i * entrySize));
                            int processId = (int)Marshal.ReadInt64(IntPtr.Add(entryPtr, 8)); // UniqueProcessId

                            if (targetPids.Contains(processId))
                            {
                                // КРИТИЧЕСКИЙ Root Cause Фильтр: проверяем тип объекта в ядре NT перед любыми DuplicateHandle!
                                // Трогаем ТОЛЬКО файловые дескрипторы (никаких Named Pipe, Socket, ALPC Port, Mutex)
                                ushort objectTypeIndex = (ushort)Marshal.ReadInt16(IntPtr.Add(entryPtr, 30));
                                if (fileTypeIndex.HasValue && objectTypeIndex != fileTypeIndex.Value)
                                {
                                    continue; // Мгновенно пропускаем не-файловый дескриптор в памяти ядра
                                }

                                matchedHandles++;
                                IntPtr handleValue = (IntPtr)Marshal.ReadInt64(IntPtr.Add(entryPtr, 16)); // HandleValue

                                if (!processHandles.TryGetValue(processId, out IntPtr hProcess))
                                {
                                    hProcess = OpenProcess(PROCESS_DUP_HANDLE, false, processId);
                                    if (hProcess == IntPtr.Zero)
                                    {
                                        int err = Marshal.GetLastWin32Error();
                                        AppLogger.Debug("WinFsp", $"[Handles DragDrop] Не удалось открыть процесс PID {processId}: Win32 Error {err}");
                                    }
                                    processHandles[processId] = hProcess;
                                }

                                if (hProcess != IntPtr.Zero)
                                {
                                    if (DuplicateHandle(hProcess, handleValue, currentProcess, out IntPtr targetHandle, 0, false, DUPLICATE_SAME_ACCESS))
                                    {
                                        try
                                        {
                                            string? rawPath = GetPathWithTimeout(targetHandle, 50);
                                            if (!string.IsNullOrEmpty(rawPath))
                                            {
                                                string path = rawPath;
                                                if (path.StartsWith(@"\\?\"))
                                                {
                                                    path = path.Substring(4);
                                                }

                                                bool isOnMounted = IsCandidateOnMountedDrive(path);
                                                if (!includeMountedDrive && isOnMounted)
                                                {
                                                    continue;
                                                }

                                                if (includeMountedDrive && isOnMounted)
                                                {
                                                    string name = Path.GetFileName(path);
                                                    if (string.IsNullOrEmpty(targetFileName) ||
                                                        string.Equals(name, targetFileName, StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        string? vPath = GetVirtualPathFromMountedPath(path);
                                                        // Целевой файл назначения не может быть кандидатом-источником
                                                        if (!string.IsNullOrEmpty(targetVirtualPath) && !string.IsNullOrEmpty(vPath) &&
                                                            string.Equals(NormalizePath(vPath), NormalizePath(targetVirtualPath), StringComparison.OrdinalIgnoreCase))
                                                        {
                                                            continue;
                                                        }

                                                        if (!results.Exists(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
                                                        {
                                                            AppLogger.Info("WinFsp", $"[Handles DragDrop] Обнаружен виртуальный файл на диске в процессе PID {processId}: '{path}'");
                                                            results.Add(path);
                                                            // НЕ выходим через return results: продолжаем поиск, чтобы обязательно обнаружить физические файлы на дисках D:, C:
                                                        }
                                                    }
                                                }
                                                else if (File.Exists(path))
                                                {
                                                    string name = Path.GetFileName(path);
                                                    bool nameMatches = string.IsNullOrEmpty(targetFileName) ||
                                                        string.Equals(name, targetFileName, StringComparison.OrdinalIgnoreCase) ||
                                                        (alternatePhysicalNames != null && alternatePhysicalNames.Contains(name));
                                                    if (nameMatches)
                                                    {
                                                        bool sizeOk = DoesFileSizeMatch(path, expectedSize);

                                                        if (sizeOk && !results.Exists(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
                                                        {
                                                            AppLogger.Info("WinFsp", $"[Handles DragDrop] Обнаружен открытый файл в процессе PID {processId}: '{path}'");
                                                            results.Add(path);

                                                            // Если мы искали конкретный целевой файл и нашли его среди РЕАЛЬНО ОТКРЫТЫХ файлов,
                                                            // МГНОВЕННО прекращаем опрос остальных дескрипторов (Fast-Break)
                                                            if (!string.IsNullOrEmpty(targetFileName) &&
                                                                string.Equals(name, targetFileName, StringComparison.OrdinalIgnoreCase))
                                                            {
                                                                return results;
                                                            }
                                                        }
                                                    }
                                                }
                                                else if (Directory.Exists(path))
                                                {
                                                    // Приоритет открытых ФАЙЛОВ над открытыми ПАПКАМИ:
                                                    // Не возвращаемся сразу на первом совпадении по открытой папке — складываем в резервный список dirFallbackResults,
                                                    // чтобы реально открытый сейчас файл в таблице ядра всегда победил случайно удержанную вкладку/папку того же процесса.
                                                    if (!string.IsNullOrEmpty(targetFileName) && callerPid > 0 && processId == callerPid)
                                                    {
                                                        try
                                                        {
                                                            string candidateFile = Path.Combine(path, targetFileName);
                                                            if (File.Exists(candidateFile))
                                                            {
                                                                bool sizeOk = DoesFileSizeMatch(candidateFile, expectedSize);

                                                                if (sizeOk && !dirFallbackResults.Exists(x => string.Equals(x, candidateFile, StringComparison.OrdinalIgnoreCase)))
                                                                {
                                                                    AppLogger.Debug("WinFsp", $"[Handles DragDrop] Резервный кандидат '{targetFileName}' внутри открытой папки процесса PID {processId}: '{candidateFile}'");
                                                                    dirFallbackResults.Add(candidateFile);
                                                                }
                                                            }
                                                        }
                                                        catch { }
                                                    }
                                                    else if (string.IsNullOrEmpty(targetFileName))
                                                    {
                                                        if (!results.Exists(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)))
                                                        {
                                                            AppLogger.Info("WinFsp", $"[Handles DragDrop] Обнаружена открытая папка в процессе PID {processId}: '{path}'");
                                                            results.Add(path);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        catch { }
                                        finally
                                        {
                                            CloseHandle(targetHandle);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        scanSw.Stop();
                        foreach (var kvp in processHandles)
                        {
                            if (kvp.Value != IntPtr.Zero)
                            {
                                CloseHandle(kvp.Value);
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("WinFsp", $"[Handles DragDrop] Исключение при сканировании хэндлов: {ex.Message}");
            }

            // Если среди открытых дескрипторов самого файла не нашлось (например, очень мелкий файл или бэкапер открыл только каталог),
            // используем резервный список кандидатов из открытых папок процесса callerPid
            if (!results.Exists(r => !IsCandidateOnMountedDrive(r)) && dirFallbackResults.Count > 0)
            {
                foreach (var fallback in dirFallbackResults)
                {
                    if (!results.Exists(x => string.Equals(x, fallback, StringComparison.OrdinalIgnoreCase)))
                    {
                        AppLogger.Info("WinFsp", $"[Handles DragDrop] Используем резервный файл из открытой папки процесса: '{fallback}'");
                        results.Add(fallback);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Проверяет, находится ли кандидат на смонтированном виртуальном диске Telegram (например Y:\).
        /// Все такие пути мгновенно игнорируются для предотвращения дедлоков рекурентного опроса собственных хэндлов WinFsp.
        /// </summary>
        private static bool IsCandidateOnMountedDrive(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string? mountPoint = WinFspServer.ActiveMountPoint;
            if (string.IsNullOrWhiteSpace(mountPoint))
                return false;

            string cleanMount = mountPoint.Trim().TrimEnd('\\', '/').ToUpperInvariant();
            string cleanPath = path.Trim();

            if (cleanPath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            {
                cleanPath = cleanPath.Substring(4);
            }

            cleanPath = cleanPath.ToUpperInvariant();

            if (cleanPath.StartsWith(@"\DEVICE\WINFSP", StringComparison.OrdinalIgnoreCase) ||
                cleanPath.Contains(@"\WINFSP."))
            {
                return true;
            }

            if (cleanPath.StartsWith(cleanMount + @"\") ||
                cleanPath.StartsWith(cleanMount + "/") ||
                string.Equals(cleanPath, cleanMount, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool DoesFileSizeMatch(string filePath, long expectedSize)
        {
            if (expectedSize <= 0)
                return true;
            try
            {
                long actualSize = new System.IO.FileInfo(filePath).Length;
                return DoesSizeMatch(actualSize, expectedSize);
            }
            catch
            {
                return true;
            }
        }

        private static bool DoesSizeMatch(long actualSize, long expectedSize)
        {
            if (expectedSize <= 0)
                return true;
            if (actualSize == expectedSize)
                return true;

            // Windows / WinFsp округляет allocationSize вверх до границы кластера (обычно 4 КБ, реже до 64 КБ)
            return actualSize > 0 && actualSize <= expectedSize && (expectedSize - actualSize) <= 65536;
        }

        private static int ScoreCandidatePathMatch(string candidateFilePath, string? relativeVirtualPath)
        {
            if (string.IsNullOrWhiteSpace(relativeVirtualPath))
                return 0;

            string? physDir = Path.GetDirectoryName(candidateFilePath);
            string virtDir = GetParentPath(relativeVirtualPath);
            if (string.IsNullOrEmpty(physDir) || virtDir == "/")
                return 0;

            var physParts = physDir.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            var virtParts = virtDir.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            int score = 0;
            int pIdx = physParts.Length - 1;
            int vIdx = virtParts.Length - 1;
            while (pIdx >= 0 && vIdx >= 0 &&
                   string.Equals(physParts[pIdx], virtParts[vIdx], StringComparison.OrdinalIgnoreCase))
            {
                score++;
                pIdx--;
                vIdx--;
            }

            return score;
        }

        private static string? CheckCandidate(string candidatePath, string targetFileName, string? relativeVirtualPath, long expectedSize = -1)
        {
            if (IsCandidateOnMountedDrive(candidatePath))
                return null;

            if (File.Exists(candidatePath))
            {
                string candidateName = Path.GetFileName(candidatePath);
                if (string.Equals(candidateName, targetFileName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!DoesFileSizeMatch(candidatePath, expectedSize))
                        return null;
                    return VerifyReadable(candidatePath);
                }
                return null;
            }

            if (Directory.Exists(candidatePath))
            {
                // Сценарий 1: Прямой дочерний файл в скопированной папке
                string directChild = Path.Combine(candidatePath, targetFileName);
                if (File.Exists(directChild) && DoesFileSizeMatch(directChild, expectedSize))
                {
                    return VerifyReadable(directChild);
                }

                // Сценарий 2: По относительному пути, если копировалось дерево папок
                if (!string.IsNullOrEmpty(relativeVirtualPath))
                {
                    string cleanVirtual = relativeVirtualPath.Trim('/', '\\').Replace('/', Path.DirectorySeparatorChar);
                    string candidateDirName = Path.GetFileName(candidatePath);
                    int idx = cleanVirtual.IndexOf(candidateDirName, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        string sub = cleanVirtual.Substring(idx + candidateDirName.Length).TrimStart(Path.DirectorySeparatorChar);
                        string candidateWithSub = Path.Combine(candidatePath, sub);
                        if (File.Exists(candidateWithSub) && DoesFileSizeMatch(candidateWithSub, expectedSize))
                        {
                            return VerifyReadable(candidateWithSub);
                        }
                    }
                }

                // Сценарий 3: Поиск в поддиректориях папки
                try
                {
                    foreach (var file in Directory.EnumerateFiles(candidatePath, targetFileName, SearchOption.AllDirectories))
                    {
                        if (!DoesFileSizeMatch(file, expectedSize))
                            continue;
                        var match = VerifyReadable(file);
                        if (match != null) return match;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Debug("WinFsp", $"[Источник] Ошибка перечисления файлов в папке '{candidatePath}': {ex.Message}");
                }
            }

            return null;
        }

        private static string? VerifyReadable(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                AppLogger.Info("WinFsp", $"[Источник] НАЙДЕНО СОВПАДЕНИЕ! Оригинальный файл '{filePath}' существует и доступен для чтения (размер: {AppLogger.FormatSizeDetailed(fs.Length)}).");
                return filePath;
            }
            catch (Exception fsEx)
            {
                AppLogger.Warn("WinFsp", $"[Источник] НАЙДЕНО СОВПАДЕНИЕ, но файл '{filePath}' заблокирован другим процессом: {fsEx.Message}");
                return filePath;
            }
        }

        #endregion

        #region Virtual Source Detection (Zero-Traffic Internal Copy & Forwarding)

        private static string? GetVirtualPathFromMountedPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return null;
            string? mountPoint = WinFspServer.ActiveMountPoint;
            if (string.IsNullOrWhiteSpace(mountPoint)) return null;

            string cleanPath = fullPath.Trim().Replace('/', '\\');
            if (cleanPath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            {
                cleanPath = cleanPath.Substring(4);
            }

            string cleanMount = mountPoint.Trim().Replace('/', '\\').TrimEnd('\\');
            if (cleanPath.StartsWith(cleanMount + @"\", StringComparison.OrdinalIgnoreCase))
            {
                string sub = cleanPath.Substring(cleanMount.Length).Replace('\\', '/');
                return NormalizePath(sub);
            }
            if (string.Equals(cleanPath, cleanMount, StringComparison.OrdinalIgnoreCase))
            {
                return "/";
            }

            int wIdx = cleanPath.IndexOf(@"\WINFSP", StringComparison.OrdinalIgnoreCase);
            if (wIdx >= 0)
            {
                int nextSlash = cleanPath.IndexOf('\\', wIdx + 7);
                if (nextSlash >= 0)
                {
                    string sub = cleanPath.Substring(nextSlash).Replace('\\', '/');
                    return NormalizePath(sub);
                }
            }

            return null;
        }

        private static Node? TryFindVirtualSourceNode(string targetFileName, string targetVirtualPath, NodeRepository repository)
        {
            var (virtualSource, _) = ResolveFileSource(targetFileName, targetVirtualPath, 0, repository);
            return virtualSource;
        }

        private static Node? MatchVirtualCandidate(string candidatePath, string targetFileName, string targetVirtualPath, NodeRepository repository)
        {
            string? vPath = GetVirtualPathFromMountedPath(candidatePath);
            if (string.IsNullOrEmpty(vPath))
            {
                AppLogger.Info("WinFsp", $"[DIAG VirtualCand] cand='{candidatePath}' -> vPath=null (mount='{WinFspServer.ActiveMountPoint}')");
                return null;
            }

            // КРИТИЧЕСКИ: Целевой файл не может быть источником для самого себя!
            if (!string.IsNullOrEmpty(targetVirtualPath) &&
                string.Equals(NormalizePath(vPath), NormalizePath(targetVirtualPath), StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Info("WinFsp", $"[DIAG VirtualCand] cand='{candidatePath}' (vPath='{vPath}') == target='{targetVirtualPath}' -> SKIP (self)");
                return null;
            }

            // 1. Прямое совпадение имени файла
            string candName = GetFileName(vPath);
            if (string.Equals(candName, targetFileName, StringComparison.OrdinalIgnoreCase))
            {
                var node = repository.GetNodeByPath(vPath);
                if (node != null && !node.IsDir && (node.TgMessageId ?? 0) > 0)
                {
                    AppLogger.Info("WinFsp", $"[DIAG VirtualCand] cand='{candidatePath}' -> MATCH file ID={node.Id}, MsgId={node.TgMessageId}");
                    return node;
                }
                AppLogger.Info("WinFsp", $"[DIAG VirtualCand] cand='{candidatePath}' name matches '{targetFileName}', but node={(node == null ? "null" : $"ID={node.Id},IsDir={node.IsDir},MsgId={node.TgMessageId}")}");
            }

            // 2. Если кандидат - папка (копировалось дерево папок)
            var dirNode = repository.GetNodeByPath(vPath);
            if (dirNode != null && dirNode.IsDir)
            {
                // Сценарий 2а: прямой дочерний элемент
                string childPath = (vPath.TrimEnd('/') + "/" + targetFileName);
                if (!string.IsNullOrEmpty(targetVirtualPath) &&
                    string.Equals(NormalizePath(childPath), NormalizePath(targetVirtualPath), StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var childNode = repository.GetNodeByPath(childPath);
                if (childNode != null && !childNode.IsDir && (childNode.TgMessageId ?? 0) > 0) return childNode;

                // Сценарий 2б: по относительному пути поддерева
                if (!string.IsNullOrEmpty(targetVirtualPath))
                {
                    string dirName = dirNode.Name;
                    int idx = targetVirtualPath.IndexOf("/" + dirName + "/", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        string sub = targetVirtualPath.Substring(idx + dirName.Length + 1);
                        string fullSub = vPath.TrimEnd('/') + sub;
                        if (string.Equals(NormalizePath(fullSub), NormalizePath(targetVirtualPath), StringComparison.OrdinalIgnoreCase))
                        {
                            return null;
                        }

                        var subNode = repository.GetNodeByPath(fullSub);
                        if (subNode != null && !subNode.IsDir && (subNode.TgMessageId ?? 0) > 0) return subNode;
                    }
                }
            }

            AppLogger.Info("WinFsp", $"[DIAG VirtualCand] cand='{candidatePath}' (vPath='{vPath}', candName='{candName}') != targetName='{targetFileName}' -> NO_MATCH");
            return null;
        }

        private static Node? TryMatchRenamedVirtualCandidate(
            List<string> clipboardCandidates,
            string cleanVirtualPath,
            string targetFileName,
            int callerPid,
            long expectedSize,
            NodeRepository repository)
        {
            string targetParentPath = GetParentPath(cleanVirtualPath);
            string targetExt = Path.GetExtension(targetFileName);
            string targetBase = Path.GetFileNameWithoutExtension(targetFileName);
            DateTime cutoff = DateTime.UtcNow.AddSeconds(-20);

            Node? bestNode = null;
            int bestScore = int.MinValue;
            RecentVirtualSignal? bestCollision = null;
            RecentVirtualSignal? bestReadOpen = null;

            foreach (var clip in clipboardCandidates)
            {
                if (!IsCandidateOnMountedDrive(clip))
                    continue;

                string? vPath = GetVirtualPathFromMountedPath(clip);
                if (string.IsNullOrEmpty(vPath))
                    continue;

                if (string.Equals(NormalizePath(vPath), NormalizePath(cleanVirtualPath), StringComparison.OrdinalIgnoreCase))
                    continue;

                var candNode = repository.GetNodeByPath(vPath);
                if (candNode == null || candNode.IsDir || (candNode.TgMessageId ?? 0) <= 0)
                    continue;

                if (!string.Equals(Path.GetExtension(candNode.Name), targetExt, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!DoesSizeMatch(candNode.Size, expectedSize))
                    continue;

                RecentVirtualSignal? matchedCollision = null;
                RecentVirtualSignal? matchedReadOpen = null;
                lock (_virtualSignalsLock)
                {
                    foreach (var c in _recentVirtualCollisions)
                    {
                        if (c.TimestampUtc < cutoff) continue;
                        if (c.SourceNode.Id != candNode.Id) continue;
                        if (callerPid > 0 && c.ProcessId > 0 && c.ProcessId != callerPid) continue;
                        if (!string.Equals(c.ParentPath, targetParentPath, StringComparison.OrdinalIgnoreCase)) continue;
                        if (matchedCollision == null || (!c.Consumed && matchedCollision.Consumed) || c.TimestampUtc > matchedCollision.TimestampUtc)
                        {
                            matchedCollision = c;
                        }
                    }

                    foreach (var r in _recentVirtualReadOpens)
                    {
                        if (r.TimestampUtc < cutoff) continue;
                        if (r.SourceNode.Id != candNode.Id) continue;
                        if (callerPid > 0 && r.ProcessId > 0 && r.ProcessId != callerPid) continue;
                        if (matchedReadOpen == null || (!r.Consumed && matchedReadOpen.Consumed) || r.TimestampUtc > matchedReadOpen.TimestampUtc)
                        {
                            matchedReadOpen = r;
                        }
                    }

                    // Поддержка повторной вставки (Ctrl+V -> Ctrl+V) в ту же папку, когда Windows проверяет коллизию по исходному имени
                    // или по уже существующей первой копии в этой папке
                    if (matchedCollision == null)
                    {
                        foreach (var c in _recentVirtualCollisions)
                        {
                            if (c.TimestampUtc < cutoff) continue;
                            if (callerPid > 0 && c.ProcessId > 0 && c.ProcessId != callerPid) continue;
                            if (!string.Equals(c.ParentPath, targetParentPath, StringComparison.OrdinalIgnoreCase)) continue;
                            if (!string.Equals(Path.GetExtension(c.SourceNode.Name), targetExt, StringComparison.OrdinalIgnoreCase)) continue;
                            if (matchedCollision == null || (!c.Consumed && matchedCollision.Consumed))
                            {
                                matchedCollision = c;
                            }
                        }
                    }
                }

                if (matchedCollision == null && matchedReadOpen == null)
                    continue;

                int score = 0;
                if (matchedCollision != null) score += matchedCollision.Consumed ? 50 : 250;
                if (matchedReadOpen != null) score += matchedReadOpen.Consumed ? 30 : 150;
                if (string.Equals(GetParentPath(vPath), targetParentPath, StringComparison.OrdinalIgnoreCase)) score += 300;

                string candBase = Path.GetFileNameWithoutExtension(candNode.Name);
                if (!string.IsNullOrEmpty(candBase) && targetBase.StartsWith(candBase, StringComparison.OrdinalIgnoreCase))
                {
                    score += 1000 + candBase.Length;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestNode = candNode;
                    bestCollision = matchedCollision;
                    bestReadOpen = matchedReadOpen;
                }
            }

            if (bestNode != null)
            {
                lock (_virtualSignalsLock)
                {
                    if (bestCollision != null) bestCollision.Consumed = true;
                    if (bestReadOpen != null) bestReadOpen.Consumed = true;
                }
            }

            return bestNode;
        }

        private static Node? TryMatchRenamedVirtualFromSignals(
            string? cleanVirtualPath,
            string targetFileName,
            int callerPid,
            long expectedSize,
            NodeRepository repository)
        {
            if (string.IsNullOrWhiteSpace(cleanVirtualPath))
                return null;

            // Защита от конфликта с буфером обмена ПК (Keep Both при копировании с диска ПК)
            var clipCheck = CollectClipboardCandidates(includeMountedDrive: true);
            if (clipCheck.Exists(c => !IsCandidateOnMountedDrive(c)))
                return null;

            string targetParentPath = GetParentPath(cleanVirtualPath);
            string targetExt = Path.GetExtension(targetFileName);
            string targetBase = Path.GetFileNameWithoutExtension(targetFileName);
            DateTime readCutoff = DateTime.UtcNow.AddSeconds(-8);
            DateTime collisionCutoff = DateTime.UtcNow.AddSeconds(-20);

            Node? bestNode = null;
            int bestScore = int.MinValue;
            RecentVirtualSignal? bestReadOpen = null;
            RecentVirtualSignal? bestCollision = null;

            lock (_virtualSignalsLock)
            {
                foreach (var r in _recentVirtualReadOpens)
                {
                    if (r.TimestampUtc < readCutoff || r.Consumed) continue;
                    if (callerPid > 0 && r.ProcessId > 0 && r.ProcessId != callerPid) continue;
                    if (string.Equals(NormalizePath(r.VirtualPath), NormalizePath(cleanVirtualPath), StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(Path.GetExtension(r.SourceNode.Name), targetExt, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!DoesSizeMatch(r.SourceNode.Size, expectedSize)) continue;

                    RecentVirtualSignal? matchedCol = null;
                    foreach (var c in _recentVirtualCollisions)
                    {
                        if (c.TimestampUtc < collisionCutoff) continue;
                        if (c.SourceNode.Id != r.SourceNode.Id) continue;
                        if (!string.Equals(c.ParentPath, targetParentPath, StringComparison.OrdinalIgnoreCase)) continue;
                        if (callerPid > 0 && c.ProcessId > 0 && c.ProcessId != callerPid) continue;
                        matchedCol = c;
                        break;
                    }

                    // Для Drag-and-Drop без буфера обмена требуем либо коллизию в той же папке, либо чтение файла из той же папки
                    if (matchedCol == null && !string.Equals(r.ParentPath, targetParentPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int score = matchedCol != null ? 500 : 100;
                    string candBase = Path.GetFileNameWithoutExtension(r.SourceNode.Name);
                    if (!string.IsNullOrEmpty(candBase) && targetBase.StartsWith(candBase, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 1000 + candBase.Length;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestNode = r.SourceNode;
                        bestReadOpen = r;
                        bestCollision = matchedCol;
                    }
                }

                if (bestNode != null)
                {
                    if (bestReadOpen != null) bestReadOpen.Consumed = true;
                    if (bestCollision != null) bestCollision.Consumed = true;
                }
            }

            if (bestNode == null)
                return null;

            var fresh = repository.GetNodeById(bestNode.Id);
            if (fresh != null && !fresh.IsDir && !fresh.InTrash && (fresh.TgMessageId ?? 0) > 0)
            {
                return fresh;
            }
            return null;
        }

        #endregion

        private class FspNodeContext : IDisposable
        {
            public Node Node { get; set; }
            public bool IsModified { get; set; }
            public bool DeleteOnClose { get; set; }
            public bool CleanupCalled { get; set; }

            // Виртуальный источник для серверного клонирования (Zero-Traffic Copy)
            public Node? VirtualSourceNode { get; set; }
            public Task<FileUploadResult?>? ForwardTask { get; set; }
            public bool IsVirtualCopyPassThrough { get; set; }
            public long VirtualCopyPassThroughTargetSize { get; set; }

            // Прямая потоковая загрузка без временных файлов на диске (Zero-Temp Direct Streaming)
            public StreamingPipeStream? PipeStream { get; set; }
            public Task<FileUploadResult?>? UploadTask { get; set; }
            public CancellationTokenSource? UploadCts { get; set; }
            public MemoryStream? SmallFileBuffer { get; set; }
            public long KnownTargetSize { get; set; } = -1;
            public long TotalBytesWritten { get; set; } = 0;
            public bool IsUploadStarted { get; set; } = false;
            public string? OriginalSourcePath { get; set; }
            public int CreatorProcessId { get; set; }
            public string? VirtualPath { get; set; }
            public AudioMetadataResult? PendingAudioMeta { get; set; }

            public FspNodeContext(Node node) => Node = node;

            public void Dispose()
            {
                UnregisterActiveVirtualCopy(Node.Id);
                if (DeleteOnClose)
                {
                    try { UploadCts?.Cancel(); } catch { }
                    try { PipeStream?.Abort(); } catch { }
                }

                try { SmallFileBuffer?.Dispose(); } catch { }
                SmallFileBuffer = null;
            }
        }

        private class FspDirectoryEnumContext
        {
            public List<Node> Items { get; }
            public int Index { get; set; }
            public FspDirectoryEnumContext(List<Node> items) => Items = items;
        }
    }
}
