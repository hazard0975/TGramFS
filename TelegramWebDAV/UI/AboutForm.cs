using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TelegramWebDAV.Config;
using TelegramWebDAV.Services;

namespace TelegramWebDAV.UI
{
    public class AboutForm : Form
    {
        private readonly ConfigManager _configManager;
        private readonly UpdateService _updateService;
        private readonly TelegramService _telegramService;
        private readonly Action _onExitRequested;

        private AppSettings _settings;
        private UpdateInfo? _foundUpdate;
        private CancellationTokenSource? _downloadCts;

        // UI Controls
        private CheckBox _chkAutoCheck = null!;
        private ComboBox _cmbInterval = null!;
        private Label _lblStatus = null!;
        private Button _btnCheckNow = null!;
        private Panel _pnlUpdateCard = null!;
        private Label _lblNewVersionTitle = null!;
        private TextBox _txtReleaseNotes = null!;
        private Button _btnDownloadAndInstall = null!;
        private ProgressBar _progressBar = null!;
        private Label _lblProgress = null!;
        private Button _btnCancelDownload = null!;
        private Button _btnClose = null!;

        public AboutForm(
            ConfigManager configManager,
            UpdateService updateService,
            TelegramService telegramService,
            Action onExitRequested)
        {
            _configManager = configManager;
            _updateService = updateService;
            _telegramService = telegramService;
            _onExitRequested = onExitRequested;
            _settings = _configManager.Load();

            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            Text = "О программе — TGramFS";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(540, 480);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = UITheme.BaseFont;
            BackColor = Color.White;

            try
            {
                string? exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                {
                    Icon = Icon.ExtractAssociatedIcon(exe);
                }
            }
            catch { }

            // Главный вертикальный контейнер
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(16, 16, 16, 12),
                Margin = Padding.Empty
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Шапка
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10)); // Зазор
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Блок настроек обновлений
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Карточка найденного обновления
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Кнопки внизу
            Controls.Add(mainLayout);

            // 1. Шапка (Иконка + Название + Версия + Дата сборки)
            var headerPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var picIcon = new PictureBox
            {
                Size = new Size(40, 40),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0, 2, 8, 0)
            };
            if (Icon != null) picIcon.Image = Icon.ToBitmap();
            headerPanel.Controls.Add(picIcon, 0, 0);

            var infoPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            var lblTitle = new Label
            {
                Text = "TGramFS",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 102, 204),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 2)
            };
            infoPanel.Controls.Add(lblTitle);

            var lblSubtitle = new Label
            {
                Text = "Telegram Virtual Drive & Cloud Filesystem",
                Font = UITheme.SmallFont,
                ForeColor = UITheme.TextMuted,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            infoPanel.Controls.Add(lblSubtitle);

            var curVer = _updateService.GetCurrentVersion();
            var curDate = _updateService.GetCurrentBuildDate();

            var lblVersion = new Label
            {
                Text = $"Версия: v{curVer.Major}.{curVer.Minor}.{curVer.Build}    •    Дата выхода: {curDate:dd.MM.yyyy HH:mm}",
                Font = UITheme.BaseFont,
                ForeColor = UITheme.TextMain,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            infoPanel.Controls.Add(lblVersion);

            headerPanel.Controls.Add(infoPanel, 1, 0);
            mainLayout.Controls.Add(headerPanel, 0, 0);

            // 2. Линия-разделитель
            var sep = new Label
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = UITheme.BorderLight,
                Margin = new Padding(0, 4, 0, 8)
            };
            mainLayout.Controls.Add(sep, 0, 1);

            // 3. Блок настроек обновлений
            var updateSettingsGroup = new GroupBox
            {
                Text = "Обновления программы",
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = UITheme.BaseFont,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(12, 8, 12, 10)
            };

            var settingsLayout = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Dock = DockStyle.Top,
                AutoSize = true,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            var rowCheck = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };

            _chkAutoCheck = new CheckBox
            {
                Text = "Проверять обновления автоматически",
                AutoSize = true,
                Font = UITheme.BaseFont,
                Margin = new Padding(0, 3, 10, 0)
            };
            _chkAutoCheck.CheckedChanged += ChkAutoCheck_CheckedChanged;
            rowCheck.Controls.Add(_chkAutoCheck);

            _cmbInterval = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 140,
                Font = UITheme.BaseFont,
                Margin = new Padding(0, 0, 0, 0)
            };
            _cmbInterval.Items.AddRange(new object[] { "Ежедневно", "Еженедельно", "Ежемесячно" });
            _cmbInterval.SelectedIndexChanged += CmbInterval_SelectedIndexChanged;
            rowCheck.Controls.Add(_cmbInterval);

            settingsLayout.Controls.Add(rowCheck);

            var rowBtn = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0)
            };

            _btnCheckNow = new Button
            {
                Text = "Проверить сейчас",
                Height = UITheme.ButtonHeightDefault,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(14, 0, 14, 0),
                Font = UITheme.BaseFont,
                UseVisualStyleBackColor = true,
                Margin = new Padding(0, 0, 12, 0)
            };
            _btnCheckNow.Click += async (s, e) => await CheckUpdatesAsync(userInitiated: true);
            rowBtn.Controls.Add(_btnCheckNow);

            _lblStatus = new Label
            {
                Text = GetLastCheckStatusText(),
                AutoSize = true,
                ForeColor = UITheme.TextMuted,
                Font = UITheme.SmallFont,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 0, 0)
            };
            rowBtn.Controls.Add(_lblStatus);

            settingsLayout.Controls.Add(rowBtn);
            updateSettingsGroup.Controls.Add(settingsLayout);
            mainLayout.Controls.Add(updateSettingsGroup, 0, 2);

            // 4. Карточка найденного обновления (динамическая панель)
            _pnlUpdateCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(245, 248, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(12),
                Margin = new Padding(0, 4, 0, 8),
                Visible = false
            };

            var updateCardLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            updateCardLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Заголовок релиза
            updateCardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Текст Release Notes
            updateCardLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Прогресс-бар
            updateCardLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Текст прогресса
            updateCardLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Кнопки действия
            _pnlUpdateCard.Controls.Add(updateCardLayout);

            _lblNewVersionTitle = new Label
            {
                Text = "Доступна новая версия!",
                Font = UITheme.HeaderFont,
                ForeColor = Color.FromArgb(0, 102, 204),
                AutoSize = true,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 0, 0, 6)
            };
            updateCardLayout.Controls.Add(_lblNewVersionTitle, 0, 0);

            _txtReleaseNotes = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Font = UITheme.SmallFont,
                Margin = new Padding(0, 0, 0, 6)
            };
            updateCardLayout.Controls.Add(_txtReleaseNotes, 0, 1);

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 16,
                Minimum = 0,
                Maximum = 100,
                Visible = false,
                Margin = new Padding(0, 0, 0, 4)
            };
            updateCardLayout.Controls.Add(_progressBar, 0, 2);

            _lblProgress = new Label
            {
                Text = "",
                Font = UITheme.SmallFont,
                ForeColor = UITheme.TextMuted,
                AutoSize = true,
                Visible = false,
                Margin = new Padding(0, 0, 0, 6)
            };
            updateCardLayout.Controls.Add(_lblProgress, 0, 3);

            var actionButtonsLayout = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                Margin = Padding.Empty
            };

            _btnDownloadAndInstall = new Button
            {
                Text = "Скачать и обновить",
                Height = UITheme.ButtonHeightPrimary,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(16, 0, 16, 0),
                Font = UITheme.BoldFont,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(0, 0, 10, 0)
            };
            _btnDownloadAndInstall.FlatAppearance.BorderSize = 0;
            _btnDownloadAndInstall.Click += async (s, e) => await StartDownloadAndInstallAsync();
            actionButtonsLayout.Controls.Add(_btnDownloadAndInstall);

            _btnCancelDownload = new Button
            {
                Text = "Отмена",
                Height = UITheme.ButtonHeightPrimary,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(14, 0, 14, 0),
                Font = UITheme.BaseFont,
                Visible = false,
                Margin = Padding.Empty
            };
            _btnCancelDownload.Click += (s, e) => _downloadCts?.Cancel();
            actionButtonsLayout.Controls.Add(_btnCancelDownload);

            updateCardLayout.Controls.Add(actionButtonsLayout, 0, 4);
            mainLayout.Controls.Add(_pnlUpdateCard, 0, 3);

            // 5. Нижняя панель с кнопкой закрыть
            var bottomLayout = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = Padding.Empty
            };

            _btnClose = new Button
            {
                Text = "Закрыть",
                Height = UITheme.ButtonHeightDefault,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(22, 0, 22, 0),
                Font = UITheme.BaseFont,
                DialogResult = DialogResult.OK
            };
            _btnClose.Click += (s, e) => Close();
            bottomLayout.Controls.Add(_btnClose);

            mainLayout.Controls.Add(bottomLayout, 0, 4);
        }

        private void LoadSettings()
        {
            _chkAutoCheck.Checked = _settings.Updates.AutoCheckUpdates;
            string interval = _settings.Updates.CheckInterval?.ToLowerInvariant() ?? "daily";

            _cmbInterval.SelectedIndex = interval switch
            {
                "weekly" or "еженедельно" => 1,
                "monthly" or "ежемесячно" => 2,
                _ => 0
            };
            _cmbInterval.Enabled = _chkAutoCheck.Checked;
        }

        private void ChkAutoCheck_CheckedChanged(object? sender, EventArgs e)
        {
            _cmbInterval.Enabled = _chkAutoCheck.Checked;
            _settings.Updates.AutoCheckUpdates = _chkAutoCheck.Checked;
            _configManager.Save(_settings);
        }

        private void CmbInterval_SelectedIndexChanged(object? sender, EventArgs e)
        {
            _settings.Updates.CheckInterval = _cmbInterval.SelectedIndex switch
            {
                1 => "Weekly",
                2 => "Monthly",
                _ => "Daily"
            };
            _configManager.Save(_settings);
        }

        private string GetLastCheckStatusText()
        {
            if (_settings.Updates.LastCheckTime.HasValue)
            {
                return $"Последняя проверка: {_settings.Updates.LastCheckTime.Value:dd.MM.yyyy HH:mm}";
            }
            return "Проверка еще не выполнялась";
        }

        public async Task CheckUpdatesAsync(bool userInitiated = false)
        {
            _btnCheckNow.Enabled = false;
            _btnCheckNow.Text = "Проверка...";
            _lblStatus.Text = "Связь с сервером обновлений...";
            _lblStatus.ForeColor = UITheme.TextMuted;

            try
            {
                var info = await _updateService.CheckForUpdatesAsync();
                _settings = _configManager.Load();
                _lblStatus.Text = GetLastCheckStatusText();

                if (info == null)
                {
                    _lblStatus.Text = "Не удалось связаться с сервером обновлений.";
                    _lblStatus.ForeColor = UITheme.TextDanger;
                    return;
                }

                _foundUpdate = info;

                if (info.IsNewer)
                {
                    ShowUpdateCard(info);
                }
                else
                {
                    _pnlUpdateCard.Visible = false;
                    _lblStatus.Text = $"У вас установлена самая свежая версия (v{info.Version}) ✔";
                    _lblStatus.ForeColor = UITheme.TextSuccess;

                    if (userInitiated)
                    {
                        MessageBox.Show(
                            $"У вас уже установлена последняя доступная версия TGramFS v{info.Version}.\nОбновление не требуется.",
                            "Обновление программы",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"Ошибка проверки: {ex.Message}";
                _lblStatus.ForeColor = UITheme.TextDanger;
            }
            finally
            {
                _btnCheckNow.Text = "Проверить сейчас";
                _btnCheckNow.Enabled = true;
            }
        }

        private void ShowUpdateCard(UpdateInfo info)
        {
            string sizeStr = info.SizeBytes > 0 ? $"{info.SizeBytes / (1024.0 * 1024.0):F1} МБ" : "Размер уточняется";
            string dateStr = info.PublishedAt.HasValue ? info.PublishedAt.Value.ToString("dd.MM.yyyy") : "";

            _lblNewVersionTitle.Text = $"Найдена новая версия TGramFS {info.TagName}! (Релиз: {dateStr}, {sizeStr})";
            _txtReleaseNotes.Text = string.IsNullOrWhiteSpace(info.ReleaseNotes) ? "Описание релиза отсутствует." : info.ReleaseNotes;

            _progressBar.Visible = false;
            _lblProgress.Visible = false;
            _btnCancelDownload.Visible = false;
            _btnDownloadAndInstall.Visible = true;
            _btnDownloadAndInstall.Enabled = true;
            _btnDownloadAndInstall.Text = "Скачать и обновить";

            _pnlUpdateCard.Visible = true;
        }

        private async Task StartDownloadAndInstallAsync()
        {
            if (_foundUpdate == null || string.IsNullOrEmpty(_foundUpdate.DownloadUrl))
            {
                MessageBox.Show("Ссылка на файл обновления не найдена.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Проверяем, идут ли активные выгрузки в Telegram
            if (_telegramService.PendingUploadsCount > 0)
            {
                var askActive = MessageBox.Show(
                    $"В данный момент в Telegram загружаются файлы ({_telegramService.PendingUploadsCount} в очереди).\n" +
                    "Перезапуск приложения для обновления прервет текущие выгрузки.\n\nПродолжить обновление?",
                    "Внимание: идут активные загрузки",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (askActive != DialogResult.Yes) return;
            }

            _downloadCts = new CancellationTokenSource();
            _btnDownloadAndInstall.Enabled = false;
            _btnCheckNow.Enabled = false;
            _btnCancelDownload.Visible = true;
            _progressBar.Visible = true;
            _progressBar.Value = 0;
            _lblProgress.Visible = true;
            _lblProgress.Text = "Подготовка к загрузке...";

            var progress = new Progress<UpdateDownloadProgress>(p =>
            {
                _progressBar.Value = Math.Min(100, Math.Max(0, p.ProgressPercentage));
                double mbReceived = p.BytesReceived / (1024.0 * 1024.0);
                double mbTotal = p.TotalBytesToReceive / (1024.0 * 1024.0);
                _lblProgress.Text = $"Загрузка: {mbReceived:F1} МБ из {mbTotal:F1} МБ ({p.ProgressPercentage}%)";
            });

            string tempFile;
            try
            {
                tempFile = await _updateService.DownloadUpdateAsync(_foundUpdate, progress, _downloadCts.Token);
            }
            catch (OperationCanceledException)
            {
                _lblProgress.Text = "Загрузка обновления отменена.";
                _progressBar.Visible = false;
                _lblProgress.Visible = false;
                _btnCancelDownload.Visible = false;
                _btnDownloadAndInstall.Enabled = true;
                _btnCheckNow.Enabled = true;
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при скачивании обновления: {ex.Message}", "Ошибка скачивания", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _lblProgress.Text = $"Ошибка загрузки: {ex.Message}";
                _btnCancelDownload.Visible = false;
                _btnDownloadAndInstall.Enabled = true;
                _btnCheckNow.Enabled = true;
                return;
            }

            _lblProgress.Text = "Загрузка завершена! Ожидание подтверждения...";

            var confirm = MessageBox.Show(
                $"Обновление {_foundUpdate.TagName} успешно загружено!\n\n" +
                "Для завершения установки старая версия программы будет закрыта и запустится новая.\n\n" +
                "Перезапустить программу прямо сейчас?",
                "Обновление готово",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                try
                {
                    _lblProgress.Text = "Запуск новой версии...";
                    _updateService.ApplyUpdateAndRestart(tempFile, _onExitRequested);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("AboutForm", $"Ошибка запуска новой версии: {ex.Message}", ex);
                    MessageBox.Show(
                        $"Не удалось запустить скрипт обновления:\n{ex.Message}\n\nФайл новой версии сохранен по пути:\n{tempFile}",
                        "Ошибка применения обновления",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    _lblProgress.Text = "Ошибка применения обновления.";
                    _btnDownloadAndInstall.Enabled = true;
                    _btnCancelDownload.Visible = false;
                    _btnCheckNow.Enabled = true;
                }
            }
            else
            {
                _lblProgress.Text = "Обновление отложено пользователем.";
                _btnDownloadAndInstall.Enabled = true;
                _btnCancelDownload.Visible = false;
                _btnCheckNow.Enabled = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _downloadCts?.Cancel();
                _downloadCts?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
