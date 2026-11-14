using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using TGramFS.Config;
using TGramFS.Services;
using TGramFS.Utils;

namespace TGramFS.UI
{
    /// <summary>
    /// Нативное окно настроек сервиса и пошаговой авторизации в Telegram (WinForms).
    /// </summary>
    public class AuthSettingsForm : Form
    {
        private readonly ConfigManager _configManager;
        private readonly TelegramService _telegramService;
        private readonly Database.NodeRepository _repository;
        private readonly DatabaseBackupService? _backupService;
        private readonly VirtualDriveManager? _virtualDriveManager;
        private AppSettings _settings;

        private const int BaseClientWidth = 595;
        private const int BaseClientHeight = 660;
        private readonly List<Action<float>> _dpiScalers = new();
        private float _currentDpiScale = 1.0f;

        public event Action? SettingsSaved;

        // UI Controls
        private TabControl _tabControl = null!;
        private TabPage _tabGeneral = null!;
        private TabPage _tabTelegram = null!;
        private TabPage _tabBackup = null!;
        private TabPage _tabRegistry = null!;
        private TabPage _tabLogs = null!;

        // Backup Tab
        private GroupBox _grpBackupChannel = null!;
        private TextBox _txtBackupChannelTitle = null!;
        private Button _btnRenameBackupChannel = null!;
        private CheckBox _chkEnableAutoBackup = null!;
        private NumericUpDown _numDebounceMinutes = null!;
        private CheckBox _chkBackupOnExit = null!;
        private CheckBox _chkCheckOnStartup = null!;
        private NumericUpDown _numRemoteCheckInterval = null!;
        private Label _lblBackupStatus = null!;

        // General Tab
        private CheckBox _chkWebDavEnabled = null!;
        private NumericUpDown _numPort = null!;
        private ComboBox _cmbEngine = null!;
        private CheckBox _chkMountDrive = null!;
        private CheckBox _chkMountForAdministrator = null!;
        private ComboBox _cmbDriveLetter = null!;
        private TextBox _txtVolumeName = null!;
        private NumericUpDown _numCapacityGb = null!;
        private CheckBox _chkAutoExpand = null!;
        private CheckBox _chkIncludeTrashInSpace = null!;
        private CheckBox _chkAutoStart = null!;
        private CheckBox _chkHideTrash = null!;
        private CheckBox _chkContextMenu = null!;
        private CheckBox _chkAutoShowProgressOverlay = null!;
        private CheckBox _chkGalleryPreview = null!;
        private CheckBox _chkAutoVacuum = null!;
        private CheckBox _chkEnableDiskCache = null!;
        private NumericUpDown _numDownloadWorkers = null!;
        private NumericUpDown _numPacingDelayMs = null!;
        private NumericUpDown _numMemoryCacheMb = null!;
        private NumericUpDown _numChunkTtlMinutes = null!;
        private NumericUpDown _numStreamingActivationMb = null!;
        private NumericUpDown _numAudioWindowMb = null!;
        private NumericUpDown _numStreamingWindowMb = null!;

        // Telegram Tab
        private Label _lblStatus = null!;
        private TableLayoutPanel _pnlUserDetails = null!;
        private Label _lblUserName = null!;
        private Label _lblUserTag = null!;
        private Label _lblUserId = null!;
        private Label _lblUserPhone = null!;
        private Label _lblUserPremium = null!;
        private Label _lblUserDc = null!;
        private Label _lblInstruction = null!;
        private TextBox _txtApiId = null!;
        private TextBox _txtApiHash = null!;
        private Button _btnSaveApi = null!;
        private GroupBox _grpPrimaryChannel = null!;
        private TextBox _txtChannelTitle = null!;
        private Button _btnRenamePrimaryChannel = null!;
        private TextBox _txtInput = null!;
        private Button _btnAction = null!;
        private Button _btnResetAuth = null!;
        private Button _btnLogout = null!;
        private System.Windows.Forms.Timer? _floodTimer;

        // Registry Tab
        private Label _lblRegStatus = null!;
        private Label _lblRegCurrentState = null!;
        private Button _btnApplyRegFix = null!;

        // Logs Tab
        private CheckBox _chkLogDebug = null!;
        private CheckBox _chkLogInfo = null!;
        private CheckBox _chkLogWarn = null!;
        private CheckBox _chkLogError = null!;
        private NumericUpDown _numMaxLogMb = null!;
        private NumericUpDown _numMaxLogFiles = null!;
        private Label _lblLogStats = null!;

        public AuthSettingsForm(
            ConfigManager configManager, 
            TelegramService telegramService, 
            Database.NodeRepository repository, 
            DatabaseBackupService? backupService = null,
            VirtualDriveManager? virtualDriveManager = null)
        {
            _configManager = configManager;
            _telegramService = telegramService;
            _repository = repository;
            _backupService = backupService;
            _virtualDriveManager = virtualDriveManager;
            _settings = _configManager.Load();

            InitializeComponents();
            UpdateUiState();
        }

        private void RegisterDpiScaler(Action<float> scaler) => _dpiScalers.Add(scaler);

        private FlowLayoutPanel CreateTabScrollPanel()
        {
            var pnl = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            RegisterDpiScaler(scale => pnl.Padding = UITheme.Scale(new Padding(12), scale));
            return pnl;
        }

        private Panel CreateBottomSpacer()
        {
            var spacer = new Panel { Margin = Padding.Empty };
            RegisterDpiScaler(scale => spacer.Size = UITheme.Scale(new Size(10, 10), scale));
            return spacer;
        }

        private float GetDpiScale()
        {
            int dpi = DeviceDpi;
            if (dpi <= 0 && IsHandleCreated)
            {
                using var g = CreateGraphics();
                dpi = (int)Math.Round(g.DpiX);
            }
            return dpi > 0 ? dpi / 96f : 1.0f;
        }

        private void UpdateFormClientSize(float dpiScale)
        {
            int targetClientWidth = UITheme.Scale(BaseClientWidth, dpiScale);
            int targetClientHeight = UITheme.Scale(BaseClientHeight, dpiScale);

            Rectangle workingArea = IsHandleCreated
                ? Screen.FromHandle(Handle).WorkingArea
                : (Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080));

            Size nonClientSize = Size - ClientSize;
            int nonClientWidth = Math.Max(0, nonClientSize.Width);
            int nonClientHeight = Math.Max(0, nonClientSize.Height);

            int maxClientWidth = Math.Max(320, workingArea.Width - nonClientWidth);
            int maxClientHeight = Math.Max(320, workingArea.Height - nonClientHeight);

            ClientSize = new Size(
                Math.Min(targetClientWidth, maxClientWidth),
                Math.Min(targetClientHeight, maxClientHeight));
        }

        private void ApplyDpiScale(float dpiScale)
        {
            if (dpiScale <= 0f) dpiScale = 1.0f;
            _currentDpiScale = dpiScale;

            SuspendLayout();
            try
            {
                foreach (var scaler in _dpiScalers)
                {
                    scaler(dpiScale);
                }

                UpdateFormClientSize(dpiScale);
            }
            finally
            {
                ResumeLayout(true);
                PerformLayout();
            }
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            float scale = e.DeviceDpiNew > 0 ? e.DeviceDpiNew / 96f : GetDpiScale();
            ApplyDpiScale(scale);
        }

        private void InitializeComponents()
        {
            SuspendLayout();

            this.Text = "TGramFS — Настройки";
            this.AutoScaleMode = AutoScaleMode.None;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = UITheme.BaseFont;

            this.Icon = AppIconHelper.GetAppIcon();

            if (!IsHandleCreated)
            {
                CreateHandle();
            }

            // Нижняя фиксированная панель действий формы со стандартными кнопками
            var btnSaveAll = UIFactory.CreateButton("Сохранить", (s, e) => SaveAllSettings());
            var btnClose = UIFactory.CreateButton("Закрыть", (s, e) => this.Close());
            var pnlBottomBar = UIFactory.CreateBottomBar(btnSaveAll, btnClose, RegisterDpiScaler);

            _tabControl = new TabControl { Dock = DockStyle.Fill };

            // =========================================================================
            // === Вкладка 1: Основные настройки (Сервер и Диск) =======================
            // =========================================================================
            _tabGeneral = new TabPage("Сервер и Диск");
            var pnlGeneral = CreateTabScrollPanel();

            // 1. Рамка: Сетевой диск и служба
            var grpServerDrive = UIFactory.CreateGroupBox("Сетевой диск и служба", registerScaler: RegisterDpiScaler);
            var pnlServerInner = UIFactory.CreateVerticalContainer(registerScaler: RegisterDpiScaler);

            _chkWebDavEnabled = UIFactory.CreateCheckBox("Включить встроенный WebDAV сервер", _settings.Server.WebDavEnabled, registerScaler: RegisterDpiScaler);
            _numPort = UIFactory.CreateNumericInput(1024, 65535, _settings.Server.Port, 80, registerScaler: RegisterDpiScaler);
            var rowPort = UIFactory.CreateSettingRow("HTTP Порт:", _numPort, 140, registerScaler: RegisterDpiScaler);

            _chkMountDrive = UIFactory.CreateCheckBox("Автоматически монтировать сетевой диск в Windows", _settings.Server.MountDrive, registerScaler: RegisterDpiScaler);
            _chkMountForAdministrator = UIFactory.CreateCheckBox("Подключать диск и для Администратора (UAC)", _settings.Server.MountForAdministrator, registerScaler: RegisterDpiScaler);
            _chkMountForAdministrator.Enabled = _chkMountDrive.Checked;
            _chkMountDrive.CheckedChanged += (s, e) =>
            {
                _chkMountForAdministrator.Enabled = _chkMountDrive.Checked;
                if (!_chkMountDrive.Checked)
                {
                    _chkMountForAdministrator.Checked = false;
                }
            };

            _cmbEngine = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = UITheme.BaseFont };
            RegisterDpiScaler(scale => _cmbEngine.Width = UITheme.Scale(260, scale));
            _cmbEngine.Items.Add("WinFsp (Прямой стриминг в ОЗУ)");
            _cmbEngine.Items.Add("WebDAV (Служба Windows WebClient)");
            _cmbEngine.SelectedIndex = _settings.Server.Engine == DriveEngine.WinFsp ? 0 : 1;
            var rowEngine = UIFactory.CreateSettingRow("Драйвер диска:", _cmbEngine, 140, registerScaler: RegisterDpiScaler);

            _txtVolumeName = new TextBox { Text = _settings.Server.DriveName ?? "TGramFS Drive", Font = UITheme.BaseFont };
            RegisterDpiScaler(scale => _txtVolumeName.Width = UITheme.Scale(260, scale));
            var rowVolumeName = UIFactory.CreateSettingRow("Имя тома:", _txtVolumeName, 140, registerScaler: RegisterDpiScaler);

            _cmbDriveLetter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = UITheme.BaseFont,
                MaxDropDownItems = 12
            };
            RegisterDpiScaler(scale => _cmbDriveLetter.Width = UITheme.Scale(80, scale));
            string currentConfigLetter = _settings.Server.DriveLetter?.Trim().ToUpper() ?? "Z:";
            if (!currentConfigLetter.EndsWith(":") && currentConfigLetter != "AUTO")
            {
                currentConfigLetter += ":";
            }

            char currentConfigChar = (!string.IsNullOrEmpty(currentConfigLetter) && currentConfigLetter != "AUTO" && char.IsLetter(currentConfigLetter[0]))
                ? char.ToUpperInvariant(currentConfigLetter[0])
                : '\0';

            char activeMountChar = (!string.IsNullOrEmpty(WinFspServer.ActiveMountPoint) && char.IsLetter(WinFspServer.ActiveMountPoint[0]))
                ? char.ToUpperInvariant(WinFspServer.ActiveMountPoint[0])
                : '\0';

            var occupiedLetters = new HashSet<char>();
            try
            {
                foreach (string drive in Environment.GetLogicalDrives())
                {
                    if (!string.IsNullOrEmpty(drive) && char.IsLetter(drive[0]))
                    {
                        occupiedLetters.Add(char.ToUpperInvariant(drive[0]));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("AuthSettingsForm", $"Не удалось получить список занятых дисков: {ex.Message}");
            }

            for (char c = 'Z'; c >= 'D'; c--)
            {
                // Добавляем букву, только если она свободна в системе,
                // либо если это наш собственный активный виртуальный диск/текущая настройка
                if (!occupiedLetters.Contains(c) || c == activeMountChar || c == currentConfigChar)
                {
                    _cmbDriveLetter.Items.Add($"{c}:");
                }
            }
            _cmbDriveLetter.Items.Add("AUTO");

            if (!_cmbDriveLetter.Items.Contains(currentConfigLetter))
            {
                _cmbDriveLetter.Items.Insert(0, currentConfigLetter);
            }
            _cmbDriveLetter.SelectedItem = currentConfigLetter;
            if (_cmbDriveLetter.SelectedIndex == -1)
            {
                _cmbDriveLetter.SelectedItem = _cmbDriveLetter.Items.Count > 0 ? _cmbDriveLetter.Items[0] : "AUTO";
            }
            var rowDriveLetter = UIFactory.CreateSettingRow("Буква диска:", _cmbDriveLetter, 140, registerScaler: RegisterDpiScaler);

            _numCapacityGb = UIFactory.CreateNumericInput(10, 1048576, _settings.Server.VirtualDiskCapacityGb > 0 ? _settings.Server.VirtualDiskCapacityGb : 1024, 80, registerScaler: RegisterDpiScaler);
            var rowCapacity = UIFactory.CreateSettingRow("Размер диска (ГБ):", _numCapacityGb, 140, registerScaler: RegisterDpiScaler);

            _chkAutoExpand = UIFactory.CreateCheckBox("Автоматически расширять диск при заполнении > 70%", _settings.Server.AutoExpandDiskCapacity, registerScaler: RegisterDpiScaler);
            _chkIncludeTrashInSpace = UIFactory.CreateCheckBox("Учитывать файлы в корзине (.Trash) в занятом месте", _settings.Server.IncludeTrashInUsedSpace, registerScaler: RegisterDpiScaler);

            pnlServerInner.Controls.AddRange(new Control[] {
                _chkWebDavEnabled, rowPort, _chkMountDrive, _chkMountForAdministrator, rowEngine, rowVolumeName, rowDriveLetter, rowCapacity, _chkAutoExpand, _chkIncludeTrashInSpace
            });
            grpServerDrive.Controls.Add(pnlServerInner);

            // 2. Рамка: Интеграция с Windows и проводником
            var grpIntegration = UIFactory.CreateGroupBox("Интеграция с Windows и проводником", registerScaler: RegisterDpiScaler);
            var pnlIntegrationInner = UIFactory.CreateVerticalContainer(registerScaler: RegisterDpiScaler);

            bool isAutoStart = AutoStartHelper.IsAutoStartEnabled();
            _chkAutoStart = UIFactory.CreateCheckBox("Автозапуск сервиса при входе в Windows", isAutoStart || _settings.Server.AutoStartWithWindows, registerScaler: RegisterDpiScaler);
            _chkHideTrash = UIFactory.CreateCheckBox("Скрывать папку .Trash из корня диска (чистый корень)", _settings.Server.HideTrashFromRoot, registerScaler: RegisterDpiScaler);
            _chkContextMenu = UIFactory.CreateCheckBox("Пункт «Открыть корзину TGramFS» в контекстном меню Windows", _settings.Server.AddTrashToContextMenu, registerScaler: RegisterDpiScaler);
            _chkAutoShowProgressOverlay = UIFactory.CreateCheckBox("Отображать индикатор прогресса операций с файлами", _settings.Server.AutoShowProgressOverlay, registerScaler: RegisterDpiScaler);
            _chkGalleryPreview = UIFactory.CreateCheckBox("Создавать фото-превью для галереи Telegram", _settings.Server.CreatePhotoGalleryPreview, registerScaler: RegisterDpiScaler);

            pnlIntegrationInner.Controls.AddRange(new Control[] {
                _chkAutoStart, _chkHideTrash, _chkContextMenu, _chkAutoShowProgressOverlay, _chkGalleryPreview
            });
            grpIntegration.Controls.Add(pnlIntegrationInner);

            // 3. Рамка: Кэширование и стриминг
            var grpCache = UIFactory.CreateGroupBox("Кэширование, стриминг и скорость MTProto", registerScaler: RegisterDpiScaler);
            var pnlCacheInner = UIFactory.CreateVerticalContainer(registerScaler: RegisterDpiScaler);

            _chkEnableDiskCache = UIFactory.CreateCheckBox("Сохранять прочитанные файлы в дисковый кэш (%TEMP%)", _settings.Server.EnableDiskReadCache, registerScaler: RegisterDpiScaler);

            _numDownloadWorkers = UIFactory.CreateNumericInput(
                ServerSettings.MinDownloadWorkerCount,
                ServerSettings.MaxDownloadWorkerCount,
                Math.Clamp(_settings.Server.DownloadWorkerCount, ServerSettings.MinDownloadWorkerCount, ServerSettings.MaxDownloadWorkerCount),
                80,
                registerScaler: RegisterDpiScaler);
            var rowDownloadWorkers = UIFactory.CreateSettingRow($"Параллельных воркеров MTProto ({ServerSettings.MinDownloadWorkerCount}-{ServerSettings.MaxDownloadWorkerCount}):", _numDownloadWorkers, 270, registerScaler: RegisterDpiScaler);

            _numPacingDelayMs = UIFactory.CreateNumericInput(
                ServerSettings.MinPacingDelayMs,
                ServerSettings.MaxPacingDelayMs,
                Math.Clamp(_settings.Server.PacingDelayMs, ServerSettings.MinPacingDelayMs, ServerSettings.MaxPacingDelayMs),
                80,
                registerScaler: RegisterDpiScaler);
            var rowPacingDelay = UIFactory.CreateSettingRow("Задержка между запросами (мс):", _numPacingDelayMs, 270, registerScaler: RegisterDpiScaler);

            _numMemoryCacheMb = UIFactory.CreateNumericInput(32, 4096, _settings.Server.MemoryCacheSizeMb > 0 ? _settings.Server.MemoryCacheSizeMb : 128, 80, registerScaler: RegisterDpiScaler);
            var rowMemCache = UIFactory.CreateSettingRow("Буфер кэша в ОЗУ (МБ):", _numMemoryCacheMb, 270, registerScaler: RegisterDpiScaler);

            _numChunkTtlMinutes = UIFactory.CreateNumericInput(1, 120, _settings.Server.ChunkMemoryCacheTtlMinutes > 0 ? _settings.Server.ChunkMemoryCacheTtlMinutes : 10, 80, registerScaler: RegisterDpiScaler);
            var rowChunkTtl = UIFactory.CreateSettingRow("Время жизни кэша в ОЗУ (мин):", _numChunkTtlMinutes, 270, registerScaler: RegisterDpiScaler);

            _numStreamingActivationMb = UIFactory.CreateNumericInput(1, 50, _settings.Server.StreamingActivationThresholdMb > 0 ? _settings.Server.StreamingActivationThresholdMb : 1, 80, registerScaler: RegisterDpiScaler);
            var rowStreamingActivation = UIFactory.CreateSettingRow("Старт упреждения после (МБ):", _numStreamingActivationMb, 270, registerScaler: RegisterDpiScaler);

            _numAudioWindowMb = UIFactory.CreateNumericInput(1, 100, _settings.Server.AudioPrefetchWindowMb > 0 ? _settings.Server.AudioPrefetchWindowMb : 2, 80, registerScaler: RegisterDpiScaler);
            var rowAudioWindow = UIFactory.CreateSettingRow("Буфер упреждения для аудио (МБ):", _numAudioWindowMb, 270, registerScaler: RegisterDpiScaler);

            _numStreamingWindowMb = UIFactory.CreateNumericInput(5, 200, _settings.Server.StreamingPrefetchWindowMb > 0 ? _settings.Server.StreamingPrefetchWindowMb : 20, 80, registerScaler: RegisterDpiScaler);
            var rowStreamingWindow = UIFactory.CreateSettingRow("Буфер упреждения для видео/файлов (МБ):", _numStreamingWindowMb, 270, registerScaler: RegisterDpiScaler);

            pnlCacheInner.Controls.AddRange(new Control[] {
                _chkEnableDiskCache, rowDownloadWorkers, rowPacingDelay, rowMemCache, rowChunkTtl, rowStreamingActivation, rowAudioWindow, rowStreamingWindow
            });
            grpCache.Controls.Add(pnlCacheInner);

            pnlGeneral.Controls.AddRange(new Control[] {
                grpServerDrive,
                grpIntegration,
                grpCache,
                CreateBottomSpacer()
            });
            _tabGeneral.Controls.Add(pnlGeneral);

            // =========================================================================
            // === Вкладка 2: Авторизация Telegram =====================================
            // =========================================================================
            _tabTelegram = new TabPage("Авторизация Telegram");
            var pnlTg = CreateTabScrollPanel();

            // 1. Рамка: Параметры приложения (my.telegram.org)
            var grpTelegramApi = UIFactory.CreateGroupBox("Параметры приложения (my.telegram.org)", registerScaler: RegisterDpiScaler);
            var pnlTgApiInner = UIFactory.CreateVerticalContainer(registerScaler: RegisterDpiScaler);

            var currentAccount = _repository.GetActiveTelegramAccount();
            var primaryChannel = _repository.GetPrimaryTelegramChannel(currentAccount?.Id);

            int apiInputWidth = UIFactory.DefaultInnerWidth - 90;

            _txtApiId = new TextBox
            {
                Text = currentAccount != null && currentAccount.ApiId > 0 ? currentAccount.ApiId.ToString() : "",
                Font = UITheme.BaseFont
            };
            RegisterDpiScaler(scale => _txtApiId.Width = UITheme.Scale(apiInputWidth, scale));
            var rowApiId = UIFactory.CreateSettingRow("API ID:", _txtApiId, 90, UIFactory.DefaultInnerWidth, RegisterDpiScaler);

            _txtApiHash = new TextBox
            {
                Text = currentAccount?.ApiHash ?? "",
                UseSystemPasswordChar = true,
                Font = UITheme.BaseFont
            };
            RegisterDpiScaler(scale => _txtApiHash.Width = UITheme.Scale(apiInputWidth, scale));
            var rowApiHash = UIFactory.CreateSettingRow("API Hash:", _txtApiHash, 90, UIFactory.DefaultInnerWidth, RegisterDpiScaler);

            _btnSaveApi = UIFactory.CreateButton("Сохранить параметры Telegram", (s, e) => SaveTelegramApiKeys(), width: apiInputWidth, registerScaler: RegisterDpiScaler);
            _btnSaveApi.Enabled = false;
            var rowSaveApi = UIFactory.CreateSettingRow("", _btnSaveApi, 90, UIFactory.DefaultInnerWidth, RegisterDpiScaler);

            _txtApiId.TextChanged += (s, e) => UpdateSaveApiButtonState();
            _txtApiHash.TextChanged += (s, e) => UpdateSaveApiButtonState();

            pnlTgApiInner.Controls.AddRange(new Control[] { rowApiId, rowApiHash, rowSaveApi });
            grpTelegramApi.Controls.Add(pnlTgApiInner);

            // 2. Рамка: Основной канал хранилища
            _grpPrimaryChannel = UIFactory.CreateGroupBox("Основной канал хранилища", registerScaler: RegisterDpiScaler);
            var pnlPrimaryChannelInner = UIFactory.CreateVerticalContainer(registerScaler: RegisterDpiScaler);

            _txtChannelTitle = new TextBox
            {
                Text = primaryChannel != null && !string.IsNullOrWhiteSpace(primaryChannel.Title) ? primaryChannel.Title : "TGramFS Storage",
                Font = UITheme.BaseFont
            };

            _btnRenamePrimaryChannel = UIFactory.CreateButton("Переименовать", async (s, e) => await RenamePrimaryChannelAsync(), width: 135, registerScaler: RegisterDpiScaler);
            _btnRenamePrimaryChannel.Enabled = false;

            _txtChannelTitle.TextChanged += (s, e) => UpdatePrimaryRenameButtonState();

            var rowPrimaryChannel = UIFactory.CreateActionInputRow("Название:", _txtChannelTitle, _btnRenamePrimaryChannel, 90, 135, UIFactory.DefaultInnerWidth, RegisterDpiScaler);

            pnlPrimaryChannelInner.Controls.Add(rowPrimaryChannel);
            _grpPrimaryChannel.Controls.Add(pnlPrimaryChannelInner);
            UpdatePrimaryChannelControls();

            // 3. Рамка: Текущая сессия Telegram
            var grpTelegramSession = UIFactory.CreateGroupBox("Текущая сессия Telegram", registerScaler: RegisterDpiScaler);
            var pnlTgSessionInner = UIFactory.CreateVerticalContainer(registerScaler: RegisterDpiScaler);

            _lblStatus = new Label
            {
                Text = "Статус: Проверка сессии...",
                AutoSize = true,
                Font = UITheme.HeaderFont
            };
            RegisterDpiScaler(scale => _lblStatus.Margin = new Padding(0, 0, 0, UITheme.Scale(8, scale)));

            // Информационная карточка профиля пользователя Telegram
            int profileCardWidth = UIFactory.DefaultInnerWidth - 135; // 460 px при 96 DPI: выравнивание по правому краю поля ввода (левому краю кнопки [Переименовать])
            _pnlUserDetails = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowOnly,
                ColumnCount = 2,
                RowCount = 6,
                BackColor = UITheme.BackgroundLight
            };
            _pnlUserDetails.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _pnlUserDetails.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            RegisterDpiScaler(scale =>
            {
                int cardW = UITheme.Scale(profileCardWidth, scale);
                _pnlUserDetails.Width = cardW;
                _pnlUserDetails.MinimumSize = new Size(cardW, 0);
                _pnlUserDetails.Margin = new Padding(0, 0, 0, UITheme.Scale(8, scale));
                _pnlUserDetails.Padding = UITheme.Scale(new Padding(8, 6, 8, 6), scale);
            });

            Label createDetailHeader(string title)
            {
                var lbl = new Label
                {
                    Text = title,
                    AutoSize = true,
                    Font = UITheme.BoldFont,
                    ForeColor = UITheme.TextMain,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                RegisterDpiScaler(scale => lbl.Margin = UITheme.Scale(new Padding(0, 3, 10, 3), scale));
                return lbl;
            }

            Label createDetailValue()
            {
                var lbl = new Label
                {
                    Text = "—",
                    AutoSize = true,
                    Font = UITheme.BaseFont,
                    ForeColor = UITheme.TextMain,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                RegisterDpiScaler(scale => lbl.Margin = UITheme.Scale(new Padding(0, 3, 0, 3), scale));
                return lbl;
            }

            _lblUserName = createDetailValue();
            _lblUserTag = createDetailValue();
            _lblUserId = createDetailValue();
            _lblUserPhone = createDetailValue();
            _lblUserPremium = createDetailValue();
            _lblUserDc = createDetailValue();

            _pnlUserDetails.Controls.Add(createDetailHeader("👤 Имя и фамилия:"), 0, 0);
            _pnlUserDetails.Controls.Add(_lblUserName, 1, 0);

            _pnlUserDetails.Controls.Add(createDetailHeader("🏷 Никнейм:"), 0, 1);
            _pnlUserDetails.Controls.Add(_lblUserTag, 1, 1);

            _pnlUserDetails.Controls.Add(createDetailHeader("🆔 Telegram User ID:"), 0, 2);
            _pnlUserDetails.Controls.Add(_lblUserId, 1, 2);

            _pnlUserDetails.Controls.Add(createDetailHeader("📱 Номер телефона:"), 0, 3);
            _pnlUserDetails.Controls.Add(_lblUserPhone, 1, 3);

            _pnlUserDetails.Controls.Add(createDetailHeader("⭐ Подписка:"), 0, 4);
            _pnlUserDetails.Controls.Add(_lblUserPremium, 1, 4);

            _pnlUserDetails.Controls.Add(createDetailHeader("🌐 Дата-центр (DC):"), 0, 5);
            _pnlUserDetails.Controls.Add(_lblUserDc, 1, 5);

            _lblInstruction = new Label
            {
                Text = "Введите номер телефона в международном формате (+3...):",
                AutoSize = true,
                Font = UITheme.BaseFont
            };
            RegisterDpiScaler(scale =>
            {
                _lblInstruction.MaximumSize = new Size(UITheme.Scale(UIFactory.DefaultInnerWidth, scale), 0);
                _lblInstruction.Margin = new Padding(0, 0, 0, UITheme.Scale(6, scale));
            });

            _txtInput = new TextBox { Font = UITheme.BaseFont };
            RegisterDpiScaler(scale =>
            {
                _txtInput.Width = UITheme.Scale(UIFactory.DefaultInnerWidth, scale);
                _txtInput.Margin = new Padding(0, 0, 0, UITheme.Scale(8, scale));
            });

            _btnAction = UIFactory.CreateButton("Отправить", async (s, e) => await HandleTelegramActionAsync(), width: 180, registerScaler: RegisterDpiScaler);
            _btnResetAuth = UIFactory.CreateButton("Изменить номер", (s, e) => HandleResetAuth(), width: 140, foreColor: UITheme.TextMuted, registerScaler: RegisterDpiScaler);
            _btnLogout = UIFactory.CreateButton("Выйти из аккаунта", (s, e) => HandleTelegramLogout(), width: 160, foreColor: UITheme.TextDanger, registerScaler: RegisterDpiScaler);

            var pnlTgButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            RegisterDpiScaler(scale => pnlTgButtons.Margin = UITheme.Scale(new Padding(0, 4, 0, 4), scale));
            pnlTgButtons.Controls.AddRange(new Control[] { _btnAction, _btnResetAuth, _btnLogout });

            pnlTgSessionInner.Controls.AddRange(new Control[] { _lblStatus, _pnlUserDetails, _lblInstruction, _txtInput, pnlTgButtons });
            grpTelegramSession.Controls.Add(pnlTgSessionInner);

            pnlTg.Controls.AddRange(new Control[] {
                grpTelegramApi,
                _grpPrimaryChannel,
                grpTelegramSession,
                CreateBottomSpacer()
            });
            _tabTelegram.Controls.Add(pnlTg);

            // =========================================================================
            // === Вкладка 3: База данных (SQLite и бэкап) =============================
            // =========================================================================
            _tabBackup = new TabPage("База");
            var pnlBackup = CreateTabScrollPanel();

            // 1. Рамка: База данных SQLite (base.db)
            var grpDb = UIFactory.CreateGroupBox("База данных SQLite (base.db)", registerScaler: RegisterDpiScaler);
            var pnlDbInner = UIFactory.CreateVerticalContainer(registerScaler: RegisterDpiScaler);

            var lblDbStats = new Label
            {
                AutoSize = true,
                Font = UITheme.SmallFont
            };
            RegisterDpiScaler(scale => lblDbStats.Margin = UITheme.Scale(new Padding(0, 2, 0, 4), scale));

            Action updateDbStats = () =>
            {
                try
                {
                    string dbPath = _repository.DatabasePath;
                    if (File.Exists(dbPath))
                    {
                        var fi = new FileInfo(dbPath);
                        double sizeMb = (double)fi.Length / (1024 * 1024);
                        lblDbStats.Text = $"Размер базы на диске: {sizeMb:F2} МБ   |   Файл: {Path.GetFileName(dbPath)}";
                    }
                    else
                    {
                        lblDbStats.Text = $"Файл базы данных: {dbPath} (еще не создан)";
                    }
                }
                catch (Exception ex)
                {
                    lblDbStats.Text = $"Ошибка получения статуса БД: {ex.Message}";
                }
            };
            updateDbStats();

            var btnVacuum = UIFactory.CreateButton("🗜 Сжать базу (VACUUM)", registerScaler: RegisterDpiScaler);
            btnVacuum.Click += async (s, e) =>
            {
                btnVacuum.Enabled = false;
                btnVacuum.Text = "Сжатие базы...";
                try
                {
                    await System.Threading.Tasks.Task.Run(() => _repository.VacuumDatabase());
                    updateDbStats();
                    MessageBox.Show("Оптимизация базы данных (VACUUM) успешно завершена!\nНеиспользуемое дисковое пространство освобождено.", "База данных", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка сжатия базы данных:\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    btnVacuum.Text = "🗜 Сжать базу (VACUUM)";
                    btnVacuum.Enabled = true;
                }
            };

            var btnOpenDbFolder = UIFactory.CreateButton("📁 Папка с базой данных", registerScaler: RegisterDpiScaler);
            btnOpenDbFolder.Click += (s, e) =>
            {
                try
                {
                    string fullPath = Path.GetFullPath(_repository.DatabasePath);
                    string? dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", dir);
                    }
                    else
                    {
                        System.Diagnostics.Process.Start("explorer.exe", AppDomain.CurrentDomain.BaseDirectory);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось открыть папку: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            _chkAutoVacuum = UIFactory.CreateCheckBox(
                "Автоматически сжимать базу (VACUUM) при очистке корзины",
                _settings.Database.AutoVacuumOnTrashDelete,
                registerScaler: RegisterDpiScaler
            );

            var rowDbButtons = UIFactory.CreateActionRow(RegisterDpiScaler, btnVacuum, btnOpenDbFolder);
            pnlDbInner.Controls.AddRange(new Control[] { lblDbStats, _chkAutoVacuum, rowDbButtons });
            grpDb.Controls.Add(pnlDbInner);

            // 2. Рамка: Канал для резервных копий
            _grpBackupChannel = UIFactory.CreateGroupBox("Канал для резервных копий", registerScaler: RegisterDpiScaler);
            var pnlBackupChannelInner = UIFactory.CreateVerticalContainer(registerScaler: RegisterDpiScaler);

            var currentBackupCh = _repository?.GetBackupChannel();

            _txtBackupChannelTitle = new TextBox
            {
                Text = currentBackupCh?.Title ?? "TGramFS DB Backups",
                Font = UITheme.BaseFont
            };

            _btnRenameBackupChannel = UIFactory.CreateButton("Переименовать", async (s, e) => await RenameBackupChannelAsync(), width: 135, registerScaler: RegisterDpiScaler);
            _btnRenameBackupChannel.Enabled = false;

            _txtBackupChannelTitle.TextChanged += (s, e) => UpdateBackupRenameButtonState();

            var rowBackupChannel = UIFactory.CreateActionInputRow("Название:", _txtBackupChannelTitle, _btnRenameBackupChannel, 90, 135, UIFactory.DefaultInnerWidth, RegisterDpiScaler);

            pnlBackupChannelInner.Controls.Add(rowBackupChannel);
            _grpBackupChannel.Controls.Add(pnlBackupChannelInner);
            UpdateBackupChannelControls();

            // 2. Рамка: Автоматический бэкап
            var grpAutoBackup = UIFactory.CreateGroupBox("Политика автоматического бэкапа", registerScaler: RegisterDpiScaler);
            var pnlAutoBackupInner = UIFactory.CreateVerticalContainer(registerScaler: RegisterDpiScaler);

            _chkBackupOnExit = UIFactory.CreateCheckBox("Создавать резервную копию при выходе из программы", _settings.Database.Backup.BackupOnExit, registerScaler: RegisterDpiScaler);

            _chkEnableAutoBackup = UIFactory.CreateCheckBox("Включить автоматическую выгрузку бэкапа в Telegram", _settings.Database.Backup.EnableAutoBackup, registerScaler: RegisterDpiScaler);

            _numDebounceMinutes = UIFactory.CreateNumericInput(1, 120, _settings.Database.Backup.DebounceMinutes > 0 ? _settings.Database.Backup.DebounceMinutes : 15, 80, registerScaler: RegisterDpiScaler);
            var rowDebounce = UIFactory.CreateSettingRow("Таймер тишины после записи (мин):", _numDebounceMinutes, 240, registerScaler: RegisterDpiScaler);
            _numDebounceMinutes.Enabled = _chkEnableAutoBackup.Checked;
            _chkEnableAutoBackup.CheckedChanged += (s, e) => _numDebounceMinutes.Enabled = _chkEnableAutoBackup.Checked;

            _chkCheckOnStartup = UIFactory.CreateCheckBox("Проверять наличие свежей базы в Telegram (сценарий Дом/Работа)", _settings.Database.Backup.CheckForRemoteOnStartup, registerScaler: RegisterDpiScaler);

            _numRemoteCheckInterval = UIFactory.CreateNumericInput(1, 120, _settings.Database.Backup.RemoteCheckIntervalMinutes > 0 ? _settings.Database.Backup.RemoteCheckIntervalMinutes : 5, 80, registerScaler: RegisterDpiScaler);
            var rowRemoteCheck = UIFactory.CreateSettingRow("Интервал фоновой проверки (мин):", _numRemoteCheckInterval, 240, registerScaler: RegisterDpiScaler);
            _numRemoteCheckInterval.Enabled = _chkCheckOnStartup.Checked;
            _chkCheckOnStartup.CheckedChanged += (s, e) => _numRemoteCheckInterval.Enabled = _chkCheckOnStartup.Checked;

            pnlAutoBackupInner.Controls.AddRange(new Control[] { _chkBackupOnExit, _chkEnableAutoBackup, rowDebounce, _chkCheckOnStartup, rowRemoteCheck });
            grpAutoBackup.Controls.Add(pnlAutoBackupInner);

            // 3. Рамка: Ручные действия
            var grpBackupActions = UIFactory.CreateGroupBox("Действия с резервными копиями", registerScaler: RegisterDpiScaler);
            var pnlBackupActionsInner = UIFactory.CreateVerticalContainer(registerScaler: RegisterDpiScaler);

            _lblBackupStatus = new Label
            {
                AutoSize = true,
                Font = UITheme.SmallFont
            };
            RegisterDpiScaler(scale => _lblBackupStatus.Margin = UITheme.Scale(new Padding(0, 2, 0, 6), scale));

            Action updateBackupStatusText = () =>
            {
                string statusText = "Статус: ";
                if (_backupService != null)
                {
                    if (_backupService.HasConflict)
                    {
                        statusText += "⚠️ Конфликт синхронизации";
                    }
                    else if (_backupService.HasUnsavedChanges)
                    {
                        statusText += "Есть локальные изменения";
                    }
                    else
                    {
                        statusText += "Синхронизировано";
                    }

                    if (_backupService.LastBackupTimeUtc.HasValue)
                    {
                        var localTime = _backupService.LastBackupTimeUtc.Value.ToLocalTime();
                        string timeStr = localTime.Date == DateTime.Today
                            ? localTime.ToString("HH:mm:ss")
                            : localTime.ToString("dd.MM HH:mm");
                        statusText += $" (бэкап в {timeStr})";
                    }
                }
                else
                {
                    statusText += "Активен";
                }
                _lblBackupStatus.Text = statusText;
            };
            updateBackupStatusText();

            var btnManualBackup = UIFactory.CreateButton("📤 Отправить бэкап в Telegram сейчас", registerScaler: RegisterDpiScaler);
            btnManualBackup.Click += async (s, e) =>
            {
                if (_backupService == null)
                {
                    MessageBox.Show("Служба бэкапа не инициализирована.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                bool force = false;
                if (_backupService.HasConflict)
                {
                    string conflictText = _backupService.ConflictDetails ?? "В Telegram доступна более свежая копия базы от другого устройства.";
                    var conflictAnswer = MessageBox.Show(
                        $"⚠️ Обнаружен конфликт синхронизации базы данных!\n\n" +
                        $"{conflictText}\n\n" +
                        $"Вы уверены, что хотите ПРИНУДИТЕЛЬНО отправить локальную базу в Telegram и перезаписать статус синхронизации?",
                        "Конфликт версий базы данных",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (conflictAnswer != DialogResult.Yes)
                    {
                        return;
                    }
                    force = true;
                }

                btnManualBackup.Enabled = false;
                btnManualBackup.Text = "Создание и отправка...";
                try
                {
                    bool ok = await _backupService.PerformBackupAsync("Запуск из окна настроек", force: force);
                    updateBackupStatusText();
                    if (ok)
                    {
                        MessageBox.Show("Резервная копия базы данных успешно создана и отправлена в Telegram!", "Бэкап базы", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        if (_backupService.HasConflict)
                        {
                            MessageBox.Show($"Выгрузка заблокирована из-за конфликта версий:\n{_backupService.ConflictDetails}", "Конфликт синхронизации", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                        else
                        {
                            MessageBox.Show("Не удалось отправить бэкап. Проверьте авторизацию в Telegram (вкладка Telegram).", "Предупреждение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка отправки бэкапа:\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    btnManualBackup.Text = "📤 Отправить бэкап в Telegram сейчас";
                    btnManualBackup.Enabled = true;
                }
            };

            var btnOpenBackupsDir = UIFactory.CreateButton("📁 Папка локальных бэкапов", registerScaler: RegisterDpiScaler);
            btnOpenBackupsDir.Click += (s, e) =>
            {
                try
                {
                    string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
                    Directory.CreateDirectory(dir);
                    System.Diagnostics.Process.Start("explorer.exe", dir);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось открыть папку: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            var btnRestoreFromTelegram = UIFactory.CreateButton("📥 Восстановить базу из Telegram", registerScaler: RegisterDpiScaler);
            btnRestoreFromTelegram.Click += async (s, e) =>
            {
                if (_backupService == null)
                {
                    MessageBox.Show("Служба бэкапа не инициализирована.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (!_telegramService.IsAuthorized)
                {
                    MessageBox.Show("Для восстановления базы данных необходимо сначала авторизоваться в Telegram (вкладка Telegram).", "Требуется авторизация", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                btnRestoreFromTelegram.Enabled = false;
                btnRestoreFromTelegram.Text = "Поиск бэкапа в Telegram...";
                try
                {
                    var backupInfo = await _backupService.GetLatestBackupInfoAsync();
                    if (!backupInfo.Found || backupInfo.Document == null)
                    {
                        MessageBox.Show("В канале Telegram не найдено резервных копий базы данных (#db_backup).", "Резервная копия не найдена", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    double sizeMb = (double)backupInfo.Size / (1024 * 1024);
                    string promptMessage = 
                        $"Найдена последняя резервная копия базы данных в Telegram:\n\n" +
                        $"• Файл: {backupInfo.FileName}\n" +
                        $"• Дата: {backupInfo.Date.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n" +
                        $"• Размер: {sizeMb:F2} МБ\n\n" +
                        $"Внимание: Текущая база данных будет замещена версией из Telegram (перед заменой будет создан защитный локальный снимок base_pre_restore_*.db).\n\n" +
                        $"Вы уверены, что хотите восстановить базу данных?";

                    var confirmResult = MessageBox.Show(promptMessage, "Подтверждение восстановления", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (confirmResult != DialogResult.Yes)
                    {
                        return;
                    }

                    btnRestoreFromTelegram.Text = "Восстановление...";
                    bool restored = await _backupService.RestoreFromTelegramAsync(backupInfo.Document, status =>
                    {
                        if (!IsDisposed && IsHandleCreated)
                        {
                            BeginInvoke(new Action(() => _lblBackupStatus.Text = status));
                        }
                    }, messageId: backupInfo.MessageId);

                    updateBackupStatusText();
                    if (restored)
                    {
                        updateDbStats();
                        MessageBox.Show("База данных успешно восстановлена из Telegram!\nФайловая структура обновлена.", "Восстановление завершено", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("Не удалось восстановить базу данных. Подробности записаны в журнал логов.", "Ошибка восстановления", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при восстановлении базы данных:\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    btnRestoreFromTelegram.Text = "📥 Восстановить базу из Telegram";
                    btnRestoreFromTelegram.Enabled = true;
                }
            };

            var rowBackupBtns = UIFactory.CreateActionRow(RegisterDpiScaler, btnManualBackup, btnRestoreFromTelegram);
            var rowFolderBtn = UIFactory.CreateActionRow(RegisterDpiScaler, btnOpenBackupsDir);
            pnlBackupActionsInner.Controls.AddRange(new Control[] { _lblBackupStatus, rowBackupBtns, rowFolderBtn });
            grpBackupActions.Controls.Add(pnlBackupActionsInner);

            pnlBackup.Controls.AddRange(new Control[] {
                grpDb,
                _grpBackupChannel,
                grpAutoBackup,
                grpBackupActions,
                CreateBottomSpacer()
            });
            _tabBackup.Controls.Add(pnlBackup);

            // =========================================================================
            // === Вкладка 4: Патч реестра Windows =====================================
            // =========================================================================
            _tabRegistry = new TabPage("Патч реестра Windows");
            var pnlReg = CreateTabScrollPanel();

            var grpRegistry = UIFactory.CreateGroupBox("Системные твики службы Windows WebClient и UAC", registerScaler: RegisterDpiScaler);
            var pnlRegInner = UIFactory.CreateVerticalContainer(registerScaler: RegisterDpiScaler);

            _lblRegStatus = new Label
            {
                Text = "Комплексный фикс реестра Windows для служб WebDAV и виртуальных дисков:\n\n" +
                       "1. BasicAuthLevel = 2 — разрешение подключения WebDAV по HTTP в локальной сети.\n" +
                       "2. FileSizeLimitInBytes = 4 GB — снятие системного лимита в 50 МБ на файл (ошибка 0x800700DF).\n" +
                       "3. EnableLinkedConnections = 1 — сквозная видимость дисков между сессиями пользователя и Администратора.\n" +
                       "4. ZoneMap (Местная интрасеть) — устранение системных предупреждений безопасности при копировании файлов.",
                AutoSize = true,
                Font = UITheme.BaseFont
            };
            RegisterDpiScaler(scale =>
            {
                int w = UITheme.Scale(UIFactory.DefaultInnerWidth, scale);
                _lblRegStatus.Width = w;
                _lblRegStatus.MaximumSize = new Size(w, 0);
                _lblRegStatus.Margin = UITheme.Scale(new Padding(0, 2, 0, 10), scale);
            });

            _lblRegCurrentState = new Label
            {
                AutoSize = true,
                Font = UITheme.HeaderFont
            };
            RegisterDpiScaler(scale =>
            {
                _lblRegCurrentState.MaximumSize = new Size(UITheme.Scale(UIFactory.DefaultInnerWidth, scale), 0);
                _lblRegCurrentState.Margin = UITheme.Scale(new Padding(0, 0, 0, 10), scale);
            });

            Action refreshRegState = () =>
            {
                bool isApplied = WindowsRegistryFixer.IsAllFixesApplied();
                if (isApplied)
                {
                    _lblRegCurrentState.Text = "Статус: Все системные патчи успешно применены ✔";
                    _lblRegCurrentState.ForeColor = UITheme.TextSuccess;
                }
                else
                {
                    _lblRegCurrentState.Text = "Статус: Рекомендуется применить системные патчи реестра ⚠";
                    _lblRegCurrentState.ForeColor = Color.DarkOrange;
                }
            };
            refreshRegState();

            _btnApplyRegFix = UIFactory.CreateButton("🛡 Применить комплексный фикс реестра (с запросом UAC)", null, height: 34, width: UIFactory.DefaultInnerWidth, registerScaler: RegisterDpiScaler);
            RegisterDpiScaler(scale => _btnApplyRegFix.Margin = UITheme.Scale(new Padding(0, 6, 0, 8), scale));
            _btnApplyRegFix.Click += (s, e) =>
            {
                _btnApplyRegFix.Enabled = false;
                try
                {
                    bool success = WindowsRegistryFixer.ApplyAllFixesWithElevation(out string message);
                    refreshRegState();
                    if (success)
                    {
                        MessageBox.Show(
                            "Патчи реестра Windows успешно применены:\n\n" +
                            "• BasicAuthLevel = 2 (HTTP WebDAV разрешен)\n" +
                            "• FileSizeLimitInBytes = 4 GB (лимит 50 МБ снят)\n" +
                            "• EnableLinkedConnections = 1 (диск виден и от Администратора)\n" +
                            "• Местная интрасеть для localhost/дисков настроена\n\n" +
                            "Для полного вступления изменений в силу рекомендуется перезапустить службу WebClient или перезагрузить ПК.",
                            "Патч реестра Windows",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show(message, "Патч реестра Windows", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                finally
                {
                    _btnApplyRegFix.Enabled = true;
                }
            };

            pnlRegInner.Controls.AddRange(new Control[] { _lblRegStatus, _lblRegCurrentState, _btnApplyRegFix });
            grpRegistry.Controls.Add(pnlRegInner);

            pnlReg.Controls.AddRange(new Control[] {
                grpRegistry,
                CreateBottomSpacer()
            });
            _tabRegistry.Controls.Add(pnlReg);

            // =========================================================================
            // === Вкладка 5: Логирование ==============================================
            // =========================================================================
            _tabLogs = new TabPage("Логирование");
            var pnlLogs = CreateTabScrollPanel();

            // 1. Рамка: Уровни логирования
            var grpLogLevels = UIFactory.CreateGroupBox("Уровни логирования", registerScaler: RegisterDpiScaler);
            var pnlLogLevels = UIFactory.CreateVerticalContainer(registerScaler: RegisterDpiScaler);

            _chkLogDebug = UIFactory.CreateCheckBox("Debug — подробная техническая отладка (COM-интерфейсы, внутренние вызовы)", _settings.Logging.EnableDebug, registerScaler: RegisterDpiScaler);
            _chkLogInfo = UIFactory.CreateCheckBox("Info — стандартные информационные события (файлы, загрузки, запуски)", _settings.Logging.EnableInfo, registerScaler: RegisterDpiScaler);
            _chkLogWarn = UIFactory.CreateCheckBox("Warn — предупреждения и некритичные отклонения", _settings.Logging.EnableWarn, registerScaler: RegisterDpiScaler);
            _chkLogError = UIFactory.CreateCheckBox("Error — ошибки приложения и сбои операций", _settings.Logging.EnableError, registerScaler: RegisterDpiScaler);

            pnlLogLevels.Controls.AddRange(new Control[] { _chkLogDebug, _chkLogInfo, _chkLogWarn, _chkLogError });
            grpLogLevels.Controls.Add(pnlLogLevels);

            // 2. Рамка: Параметры хранения и ротации
            var grpRotation = UIFactory.CreateGroupBox("Параметры хранения и ротации лог-файлов", registerScaler: RegisterDpiScaler);
            var pnlRotation = UIFactory.CreateVerticalContainer(registerScaler: RegisterDpiScaler);

            _numMaxLogMb = UIFactory.CreateNumericInput(1, 50, Math.Clamp(_settings.Logging.MaxLogFileSizeMb > 0 ? _settings.Logging.MaxLogFileSizeMb : 5, 1, 50), 90, registerScaler: RegisterDpiScaler);
            var rowMaxMb = UIFactory.CreateSettingRow("Максимальный размер одного файла лога (МБ):", _numMaxLogMb, 330, registerScaler: RegisterDpiScaler);

            _numMaxLogFiles = UIFactory.CreateNumericInput(1, 10, Math.Clamp(_settings.Logging.MaxArchivedFiles > 0 ? _settings.Logging.MaxArchivedFiles : 3, 1, 10), 90, registerScaler: RegisterDpiScaler);
            var rowMaxFiles = UIFactory.CreateSettingRow("Количество хранящихся архивных файлов:", _numMaxLogFiles, 330, registerScaler: RegisterDpiScaler);

            pnlRotation.Controls.AddRange(new Control[] { rowMaxMb, rowMaxFiles });
            grpRotation.Controls.Add(pnlRotation);

            // 3. Рамка: Текущая информация и действия
            var grpStats = UIFactory.CreateGroupBox("Текущая информация и действия", registerScaler: RegisterDpiScaler);
            var pnlStatsInner = UIFactory.CreateVerticalContainer(registerScaler: RegisterDpiScaler);

            _lblLogStats = new Label
            {
                AutoSize = true,
                Font = UITheme.SmallFont
            };
            RegisterDpiScaler(scale =>
            {
                _lblLogStats.MaximumSize = new Size(UITheme.Scale(UIFactory.DefaultInnerWidth, scale), 0);
                _lblLogStats.Margin = UITheme.Scale(new Padding(0, 2, 0, 4), scale);
            });

            Action updateLogStats = () =>
            {
                try
                {
                    string logPath = AppLogger.CurrentLogFilePath;
                    long mainSize = File.Exists(logPath) ? new FileInfo(logPath).Length : 0;
                    long archiveTotal = 0;
                    int archiveCount = 0;
                    for (int i = 1; i <= 10; i++)
                    {
                        string arch = Path.Combine(AppLogger.LogDirectory, $"app.{i}.log");
                        if (File.Exists(arch))
                        {
                            archiveCount++;
                            archiveTotal += new FileInfo(arch).Length;
                        }
                    }
                    double mainMb = mainSize / (1024.0 * 1024.0);
                    double totalMb = (mainSize + archiveTotal) / (1024.0 * 1024.0);
                    _lblLogStats.Text = $"Текущий файл: {Path.GetFileName(logPath)} ({mainMb:F2} МБ)\n" +
                                       $"Архивы ротации: {archiveCount} файлов (общий объём {totalMb:F2} МБ)\n" +
                                       $"Папка: {AppLogger.LogDirectory}";
                }
                catch
                {
                    _lblLogStats.Text = $"Папка логов: {AppLogger.LogDirectory}";
                }
            };
            updateLogStats();

            var btnOpenLogDir = UIFactory.CreateButton("📁 Папка с логами", registerScaler: RegisterDpiScaler);
            btnOpenLogDir.Click += (s, e) =>
            {
                try
                {
                    Directory.CreateDirectory(AppLogger.LogDirectory);
                    System.Diagnostics.Process.Start("explorer.exe", AppLogger.LogDirectory);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось открыть папку: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            var btnOpenLogFile = UIFactory.CreateButton("📄 Открыть app.log", registerScaler: RegisterDpiScaler);
            btnOpenLogFile.Click += (s, e) =>
            {
                try
                {
                    if (File.Exists(AppLogger.CurrentLogFilePath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = AppLogger.CurrentLogFilePath,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        MessageBox.Show("Файл app.log пока еще не создан.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Не удалось открыть файл лога: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            var btnClearLogs = UIFactory.CreateButton("🗑 Очистить логи", foreColor: UITheme.TextDanger, registerScaler: RegisterDpiScaler);
            btnClearLogs.Click += (s, e) =>
            {
                if (MessageBox.Show("Вы действительно хотите очистить текущий лог и все его архивы?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    AppLogger.ClearLogs();
                    updateLogStats();
                    MessageBox.Show("Все логи успешно очищены!", "Логирование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            var rowLogActions = UIFactory.CreateActionRow(RegisterDpiScaler, btnOpenLogDir, btnOpenLogFile, btnClearLogs);
            pnlStatsInner.Controls.AddRange(new Control[] { _lblLogStats, rowLogActions });
            grpStats.Controls.Add(pnlStatsInner);

            pnlLogs.Controls.AddRange(new Control[] {
                grpLogLevels,
                grpRotation,
                grpStats,
                CreateBottomSpacer()
            });
            _tabLogs.Controls.Add(pnlLogs);

            _tabControl.TabPages.AddRange(new TabPage[] { _tabGeneral, _tabTelegram, _tabBackup, _tabRegistry, _tabLogs });
            this.Controls.Add(_tabControl);
            this.Controls.Add(pnlBottomBar);

            ResumeLayout(false);
            ApplyDpiScale(GetDpiScale());
        }

        private void SaveTelegramApiKeys()
        {
            if (!int.TryParse(_txtApiId.Text.Trim(), out int apiId) || apiId <= 0)
            {
                MessageBox.Show("Введите корректный числовой API ID (например, 12345678).", "Ошибка валидации", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string apiHash = _txtApiHash.Text.Trim();
            if (string.IsNullOrEmpty(apiHash) || apiHash.Length < 10)
            {
                MessageBox.Show("Введите корректный API Hash (строка из 32 шестнадцатеричных символов).", "Ошибка валидации", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _telegramService.UpdateApiCredentials(apiId, apiHash);
            UpdateUiState();
            UpdateSaveApiButtonState();

            MessageBox.Show("Параметры Telegram успешно сохранены!", "TGramFS", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async System.Threading.Tasks.Task RenamePrimaryChannelAsync()
        {
            string cleanTitle = _txtChannelTitle.Text.Trim();
            if (string.IsNullOrEmpty(cleanTitle))
            {
                cleanTitle = "TGramFS Storage";
                _txtChannelTitle.Text = cleanTitle;
            }
            if (cleanTitle.Length > 128)
            {
                cleanTitle = cleanTitle.Substring(0, 128);
                _txtChannelTitle.Text = cleanTitle;
            }

            var account = _repository?.GetActiveTelegramAccount();
            var primary = _repository?.GetPrimaryTelegramChannel(account?.Id);

            _btnRenamePrimaryChannel.Enabled = false;
            _btnRenamePrimaryChannel.Text = "Сохранение...";
            try
            {
                if (primary != null && primary.ChannelId != 0)
                {
                    _repository?.UpdateChannelTitle(primary.ChannelId, cleanTitle);
                    if (_telegramService.IsAuthorized)
                    {
                        try
                        {
                            await _telegramService.EditChannelTitleAsync(primary.ChannelId, cleanTitle);
                            MessageBox.Show($"Канал в Telegram переименован в «{cleanTitle}».", "Основной канал", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("AuthSettingsForm", $"Не удалось синхронизировать заголовок основного канала: {ex.Message}");
                            MessageBox.Show($"Имя канала сохранено в базе, но Telegram вернул ошибку:\n{ex.Message}", "Основной канал", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    else
                    {
                        _telegramService.UpdateStorageChannelTitle(cleanTitle);
                        MessageBox.Show($"Имя канала сохранено. В Telegram оно обновится при подключении к сети.", "Основной канал", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    _telegramService.UpdateStorageChannelTitle(cleanTitle);
                    MessageBox.Show($"Канал «{cleanTitle}» будет создан при первой авторизации.", "Основной канал", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            finally
            {
                _btnRenamePrimaryChannel.Text = "Переименовать";
                UpdatePrimaryChannelControls();
            }
        }

        private async System.Threading.Tasks.Task RenameBackupChannelAsync()
        {
            string newTitle = _txtBackupChannelTitle.Text.Trim();
            if (string.IsNullOrEmpty(newTitle))
            {
                newTitle = "TGramFS DB Backups";
                _txtBackupChannelTitle.Text = newTitle;
            }
            if (newTitle.Length > 128)
            {
                newTitle = newTitle.Substring(0, 128);
                _txtBackupChannelTitle.Text = newTitle;
            }

            var currentBk = _repository?.GetBackupChannel();

            _btnRenameBackupChannel.Enabled = false;
            _btnRenameBackupChannel.Text = "Сохранение...";
            try
            {
                if (currentBk != null && currentBk.ChannelId != 0)
                {
                    _repository?.UpdateChannelTitle(currentBk.ChannelId, newTitle);
                    if (_telegramService.IsAuthorized)
                    {
                        try
                        {
                            await _telegramService.EditChannelTitleAsync(currentBk.ChannelId, newTitle);
                            MessageBox.Show($"Канал бэкапов в Telegram переименован в «{newTitle}».", "Резервное копирование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("AuthSettingsForm", $"Не удалось синхронизировать заголовок бэкап-канала: {ex.Message}");
                            MessageBox.Show($"Имя канала сохранено в базе, но Telegram вернул ошибку:\n{ex.Message}", "Резервное копирование", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    else
                    {
                        MessageBox.Show($"Имя канала бэкапов сохранено. В Telegram оно обновится при подключении к сети.", "Резервное копирование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                else
                {
                    if (_telegramService.IsAuthorized)
                    {
                        try
                        {
                            await _telegramService.GetBackupStoragePeerAsync(newTitle);
                            MessageBox.Show($"Создан канал бэкапов в Telegram с именем «{newTitle}».", "Резервное копирование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("AuthSettingsForm", $"Не удалось создать бэкап-канал в Telegram: {ex.Message}");
                            MessageBox.Show($"Канал бэкапов «{newTitle}» будет создан при первом бэкапе.", "Резервное копирование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                    else
                    {
                        MessageBox.Show($"Канал бэкапов «{newTitle}» будет создан при первом бэкапе.", "Резервное копирование", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            finally
            {
                _btnRenameBackupChannel.Text = "Переименовать";
                UpdateBackupChannelControls();
            }
        }

        private void UpdateSaveApiButtonState()
        {
            var currentAccount = _repository?.GetActiveTelegramAccount();
            string savedApiId = currentAccount != null && currentAccount.ApiId > 0 ? currentAccount.ApiId.ToString() : "";
            string savedApiHash = currentAccount?.ApiHash ?? "";

            string currentApiId = _txtApiId.Text.Trim();
            string currentApiHash = _txtApiHash.Text.Trim();

            bool hasInput = !string.IsNullOrEmpty(currentApiId) && !string.IsNullOrEmpty(currentApiHash);
            bool isChanged = !string.Equals(currentApiId, savedApiId, StringComparison.Ordinal) ||
                             !string.Equals(currentApiHash, savedApiHash, StringComparison.Ordinal);

            _btnSaveApi.Enabled = hasInput && isChanged;
        }

        private void UpdatePrimaryChannelControls()
        {
            var account = _repository?.GetActiveTelegramAccount();
            var primary = _repository?.GetPrimaryTelegramChannel(account?.Id);
            bool isPrimaryReady = primary != null && primary.ChannelId != 0;

            if (_grpPrimaryChannel != null)
            {
                _grpPrimaryChannel.Text = isPrimaryReady 
                    ? "Основной канал хранилища (✓ подключен)" 
                    : "Основной канал хранилища (будет создан при авторизации)";
            }

            UpdatePrimaryRenameButtonState();
        }

        private void UpdatePrimaryRenameButtonState()
        {
            var account = _repository?.GetActiveTelegramAccount();
            var primary = _repository?.GetPrimaryTelegramChannel(account?.Id);
            string savedTitle = primary != null && !string.IsNullOrWhiteSpace(primary.Title) ? primary.Title : "TGramFS Storage";
            string currentText = _txtChannelTitle.Text.Trim();

            _btnRenamePrimaryChannel.Enabled = !string.IsNullOrWhiteSpace(currentText) &&
                                               !string.Equals(currentText, savedTitle, StringComparison.Ordinal);
        }

        private void UpdateBackupChannelControls()
        {
            var currentBk = _repository?.GetBackupChannel();
            bool isBackupReady = currentBk != null && currentBk.ChannelId != 0;

            if (_grpBackupChannel != null)
            {
                _grpBackupChannel.Text = isBackupReady 
                    ? "Канал для резервных копий (✓ подключен)" 
                    : "Канал для резервных копий (будет создан при первом бэкапе)";
            }

            UpdateBackupRenameButtonState();
        }

        private void UpdateBackupRenameButtonState()
        {
            var currentBk = _repository?.GetBackupChannel();
            string savedTitle = currentBk != null && !string.IsNullOrWhiteSpace(currentBk.Title) ? currentBk.Title : "TGramFS DB Backups";
            string currentText = _txtBackupChannelTitle.Text.Trim();

            _btnRenameBackupChannel.Enabled = !string.IsNullOrWhiteSpace(currentText) &&
                                              !string.Equals(currentText, savedTitle, StringComparison.Ordinal);
        }

        private void UpdateUiState()
        {
            var account = _repository.GetActiveTelegramAccount();
            bool hasApiKeys = account != null && account.ApiId > 0 && !string.IsNullOrEmpty(account.ApiHash);

            if (_telegramService.IsAuthorized)
            {
                var user = _telegramService.CurrentUser;
                _lblStatus.Text = "Статус: Авторизован ✔";
                _lblStatus.ForeColor = UITheme.TextSuccess;

                if (user != null)
                {
                    _lblUserName.Text = user.FullName;
                    _lblUserTag.Text = user.FormattedUsername;
                    _lblUserId.Text = user.Id.ToString();
                    _lblUserPhone.Text = user.FormattedPhone;
                    _lblUserPremium.Text = user.PremiumDescription;
                    if (user.IsPremium)
                    {
                        _lblUserPremium.ForeColor = Color.FromArgb(170, 95, 0);
                    }
                    else
                    {
                        _lblUserPremium.ForeColor = UITheme.TextMain;
                    }
                    _lblUserDc.Text = user.DcDescription;
                }

                _pnlUserDetails.Visible = true;
                _lblInstruction.Visible = false;
                _txtInput.Visible = false;
                _btnAction.Visible = false;
                _btnResetAuth.Visible = false;
                _btnLogout.Visible = true;
                StopFloodTimer();
            }
            else
            {
                _lblStatus.Text = "Статус: Не авторизован ❌";
                _lblStatus.ForeColor = UITheme.TextDanger;
                _pnlUserDetails.Visible = false;
                _txtInput.Visible = hasApiKeys;
                _btnAction.Visible = hasApiKeys;
                _btnLogout.Visible = false;

                int floodSec = TelegramService.GetRemainingFloodWaitSeconds();
                if (floodSec > 0)
                {
                    _btnAction.Enabled = false;
                    _btnAction.Text = $"Подождите ({floodSec} с)";
                    _btnResetAuth.Text = "Изменить номер";
                    _btnResetAuth.Visible = hasApiKeys;
                    _lblInstruction.Text = TelegramService.FormatFloodWaitMessage(floodSec);
                    _lblInstruction.ForeColor = Color.OrangeRed;
                    _lblInstruction.Visible = true;
                    StartOrEnsureFloodTimer();
                }
                else
                {
                    StopFloodTimer();
                    _btnAction.Enabled = true;
                    _btnResetAuth.Visible = hasApiKeys && (_telegramService.CurrentStep != AuthStep.NeedsPhone);

                    if (!hasApiKeys)
                    {
                        _lblInstruction.Text = "Сначала укажите и сохраните API ID и API Hash (получить на my.telegram.org).";
                        _lblInstruction.ForeColor = Color.DarkOrange;
                        _lblInstruction.Visible = true;
                    }
                    else
                    {
                        _lblInstruction.ForeColor = UITheme.TextMain;
                        _lblInstruction.Visible = true;
                        switch (_telegramService.CurrentStep)
                        {
                            case AuthStep.NeedsPhone:
                                _lblInstruction.Text = "Введите номер телефона в международном формате (+3...):";
                                _btnAction.Text = "Получить код в Telegram";
                                _txtInput.UseSystemPasswordChar = false;
                                break;
                            case AuthStep.NeedsCode:
                                _lblInstruction.Text = "Введите проверочный код из Telegram (пришел в приложение Telegram):";
                                _btnAction.Text = "Подтвердить код";
                                _btnResetAuth.Text = "Изменить номер";
                                _txtInput.UseSystemPasswordChar = false;
                                break;
                            case AuthStep.Needs2FA:
                                _lblInstruction.Text = "Введите облачный пароль 2FA (двухфакторной аутентификации):";
                                _btnAction.Text = "Войти";
                                _btnResetAuth.Text = "Сбросить ввод";
                                _txtInput.UseSystemPasswordChar = true;
                                break;
                        }
                    }
                }
            }

            UpdateSaveApiButtonState();
            UpdatePrimaryChannelControls();
            UpdateBackupChannelControls();
        }

        private async System.Threading.Tasks.Task HandleTelegramActionAsync()
        {
            string input = _txtInput.Text.Trim();
            if (string.IsNullOrEmpty(input)) return;

            _btnAction.Enabled = false;
            try
            {
                string? nextRequirement = await _telegramService.LoginStepAsync(input);
                _txtInput.Clear();
                UpdateUiState();

                if (_telegramService.IsAuthorized)
                {
                    MessageBox.Show("Авторизация в Telegram успешно завершена!", "TGramFS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Авторизация Telegram", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdateUiState();
            }
            finally
            {
                if (TelegramService.GetRemainingFloodWaitSeconds() == 0)
                {
                    _btnAction.Enabled = true;
                }
            }
        }

        private void HandleResetAuth()
        {
            _telegramService.ResetAuthToPhone();
            _txtInput.Clear();
            UpdateUiState();
        }

        private void StartOrEnsureFloodTimer()
        {
            if (_floodTimer == null)
            {
                _floodTimer = new System.Windows.Forms.Timer { Interval = 1000 };
                _floodTimer.Tick += (s, e) =>
                {
                    if (IsDisposed || !IsHandleCreated) return;
                    int rem = TelegramService.GetRemainingFloodWaitSeconds();
                    if (rem > 0)
                    {
                        _lblInstruction.Text = TelegramService.FormatFloodWaitMessage(rem);
                        _btnAction.Text = $"Подождите ({rem} с)";
                    }
                    else
                    {
                        UpdateUiState();
                    }
                };
            }
            if (!_floodTimer.Enabled)
            {
                _floodTimer.Start();
            }
        }

        private void StopFloodTimer()
        {
            if (_floodTimer != null && _floodTimer.Enabled)
            {
                _floodTimer.Stop();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopFloodTimer();
            _floodTimer?.Dispose();
            _floodTimer = null;
            base.OnFormClosed(e);
        }

        private void HandleTelegramLogout()
        {
            if (MessageBox.Show("Вы уверены, что хотите сбросить сессию Telegram?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _telegramService.Logout();
                UpdateUiState();
            }
        }

        private void SaveAllSettings()
        {
            string chosenLetter = _cmbDriveLetter.SelectedItem?.ToString() ?? "Z:";
            if (chosenLetter != "AUTO" && chosenLetter.Length >= 2 && char.IsLetter(chosenLetter[0]))
            {
                char chosenChar = char.ToUpperInvariant(chosenLetter[0]);
                char activeChar = (!string.IsNullOrEmpty(WinFspServer.ActiveMountPoint) && char.IsLetter(WinFspServer.ActiveMountPoint[0]))
                    ? char.ToUpperInvariant(WinFspServer.ActiveMountPoint[0])
                    : '\0';

                bool isConflict = false;
                try
                {
                    foreach (string d in Environment.GetLogicalDrives())
                    {
                        if (!string.IsNullOrEmpty(d) && char.ToUpperInvariant(d[0]) == chosenChar && chosenChar != activeChar)
                        {
                            isConflict = true;
                            break;
                        }
                    }
                }
                catch { }

                if (isConflict)
                {
                    MessageBox.Show(
                        $"Буква {chosenLetter} уже занята другим диском или сетевым подключением в Windows.\nПожалуйста, выберите свободную букву диска или режим AUTO.",
                        "Конфликт буквы диска",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            var oldEngine = _settings.Server.Engine;
            var oldDriveLetter = _settings.Server.DriveLetter;
            var oldMountDrive = _settings.Server.MountDrive;
            _settings.Server.Engine = _cmbEngine.SelectedIndex == 0 ? DriveEngine.WinFsp : DriveEngine.WebDav;
            _settings.Server.WebDavEnabled = _chkWebDavEnabled.Checked;
            _settings.Server.Port = (int)_numPort.Value;
            _settings.Server.MountDrive = _chkMountDrive.Checked;
            _settings.Server.MountForAdministrator = _chkMountForAdministrator.Checked;
            _settings.Server.DriveLetter = chosenLetter;
            _settings.Server.DriveName = _txtVolumeName.Text.Trim();
            _settings.Server.VirtualDiskCapacityGb = (long)_numCapacityGb.Value;
            _settings.Server.AutoExpandDiskCapacity = _chkAutoExpand.Checked;
            _settings.Server.IncludeTrashInUsedSpace = _chkIncludeTrashInSpace.Checked;
            _settings.Server.AutoStartWithWindows = _chkAutoStart.Checked;
            _settings.Server.HideTrashFromRoot = _chkHideTrash.Checked;
            _settings.Server.AddTrashToContextMenu = _chkContextMenu.Checked;
            _settings.Server.AutoShowProgressOverlay = _chkAutoShowProgressOverlay.Checked;
            _settings.Server.CreatePhotoGalleryPreview = _chkGalleryPreview.Checked;
            _settings.Server.EnableDiskReadCache = _chkEnableDiskCache.Checked;
            _settings.Server.DownloadWorkerCount = (int)_numDownloadWorkers.Value;
            _settings.Server.PacingDelayMs = (int)_numPacingDelayMs.Value;
            TelegramService.SetPacingDelay(_settings.Server.PacingDelayMs);
            _settings.Server.MemoryCacheSizeMb = (int)_numMemoryCacheMb.Value;
            _settings.Server.ChunkMemoryCacheTtlMinutes = (int)_numChunkTtlMinutes.Value;
            _settings.Server.StreamingActivationThresholdMb = (int)_numStreamingActivationMb.Value;
            _settings.Server.AudioPrefetchWindowMb = (int)_numAudioWindowMb.Value;
            _settings.Server.StreamingPrefetchWindowMb = (int)_numStreamingWindowMb.Value;

            _settings.Logging.EnableDebug = _chkLogDebug.Checked;
            _settings.Logging.EnableInfo = _chkLogInfo.Checked;
            _settings.Logging.EnableWarn = _chkLogWarn.Checked;
            _settings.Logging.EnableError = _chkLogError.Checked;
            _settings.Logging.MaxLogFileSizeMb = (int)_numMaxLogMb.Value;
            _settings.Logging.MaxArchivedFiles = (int)_numMaxLogFiles.Value;
            _settings.Database.AutoVacuumOnTrashDelete = _chkAutoVacuum.Checked;

            _settings.Database.Backup.EnableAutoBackup = _chkEnableAutoBackup.Checked;
            _settings.Database.Backup.DebounceMinutes = (int)_numDebounceMinutes.Value;
            _settings.Database.Backup.BackupOnExit = _chkBackupOnExit.Checked;
            _settings.Database.Backup.CheckForRemoteOnStartup = _chkCheckOnStartup.Checked;
            _settings.Database.Backup.RemoteCheckIntervalMinutes = (int)_numRemoteCheckInterval.Value;

            if (_chkContextMenu.Checked)
            {
                ShellContextMenuHelper.RegisterTrashContextMenu(_settings.Server.DriveLetter);
            }
            else
            {
                ShellContextMenuHelper.UnregisterTrashContextMenu();
            }

            AutoStartHelper.SetAutoStart(_chkAutoStart.Checked);

            _configManager.Save(_settings);
            _backupService?.StartCloudSyncTimer();
            _telegramService.UpdateSettings(_settings);
            string msg = "Все настройки успешно сохранены!";
            bool engineChanged = oldEngine != _settings.Server.Engine;
            bool letterChanged = !string.Equals(oldDriveLetter, _settings.Server.DriveLetter, StringComparison.OrdinalIgnoreCase);
            bool mountChanged = oldMountDrive != _settings.Server.MountDrive;

            if (_virtualDriveManager != null)
            {
                if (!_settings.Server.MountDrive && oldMountDrive)
                {
                    _virtualDriveManager.Unmount();
                    msg += "\n\nВиртуальный диск отключен.";
                }
                else if (_settings.Server.MountDrive && (engineChanged || letterChanged || mountChanged))
                {
                    if (_virtualDriveManager.Remount(out string remountError))
                    {
                        string activeEngine = _virtualDriveManager.ActiveEngine == DriveEngine.WinFsp ? "WinFsp" : "WebDAV";
                        string activeLetter = _virtualDriveManager.MountedLetter ?? _settings.Server.DriveLetter ?? "Z:";
                        msg += $"\n\nДиск успешно переподключен: {activeLetter} [{activeEngine}].";
                    }
                    else
                    {
                        msg += $"\n\nНе удалось автоматически переподключить диск: {remountError}\nВы можете повторить попытку через меню трея.";
                    }
                }
            }

            SettingsSaved?.Invoke();
            MessageBox.Show(msg, "TGramFS", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
