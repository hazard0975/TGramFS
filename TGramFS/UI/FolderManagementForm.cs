using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TGramFS.Config;
using TGramFS.Database;
using TGramFS.Services;
using TGramFS.Utils;

namespace TGramFS.UI
{
    /// <summary>
    /// Окно централизованного управления папками виртуального диска:
    /// 1) Привязанные к отдельным каналам Telegram папки;
    /// 2) Папки и исключения для создания фото-превью галереи Telegram;
    /// 3) Папки и исключения для текстовых подписей сообщений в Telegram.
    /// </summary>
    public class FolderManagementForm : Form
    {
        private readonly ConfigManager _configManager;
        private readonly NodeRepository _repository;
        private readonly TelegramService _telegramService;
        private readonly VirtualDriveManager? _virtualDriveManager;

        private const int BaseClientWidth = 660;
        private const int BaseClientHeight = 520;
        private readonly List<Action<float>> _dpiScalers = new();
        private float _currentDpiScale = 1.0f;

        private TabControl _tabControl = null!;

        // Tab 1: Bound Folders
        private ListView _lvBoundFolders = null!;
        private Button _btnCreateBindFolder = null!;
        private Button _btnOpenBoundFolder = null!;
        private Button _btnRefreshBound = null!;
        private Label _lblBoundEmptyHint = null!;

        // Tab 2: Photo Previews
        private ListView _lvPreviewAllowed = null!;
        private ListView _lvPreviewExcluded = null!;
        private Button _btnPreviewRemoveAllowed = null!;
        private Button _btnPreviewRemoveExcluded = null!;

        // Tab 3: Captions
        private ListView _lvCaptionAllowed = null!;
        private ListView _lvCaptionExcluded = null!;
        private Button _btnCaptionRemoveAllowed = null!;
        private Button _btnCaptionRemoveExcluded = null!;

        public FolderManagementForm(
            ConfigManager configManager,
            NodeRepository repository,
            TelegramService telegramService,
            VirtualDriveManager? virtualDriveManager = null)
        {
            _configManager = configManager;
            _repository = repository;
            _telegramService = telegramService;
            _virtualDriveManager = virtualDriveManager;

            InitializeComponents();
            RefreshAllTabs();
        }

        private void RegisterDpiScaler(Action<float> scaler) => _dpiScalers.Add(scaler);

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
            int targetW = UITheme.Scale(BaseClientWidth, dpiScale);
            int targetH = UITheme.Scale(BaseClientHeight, dpiScale);
            Rectangle workingArea = IsHandleCreated
                ? Screen.FromHandle(Handle).WorkingArea
                : Screen.FromPoint(Cursor.Position).WorkingArea;

            Size nonClient = Size - ClientSize;
            int maxW = Math.Max(420, workingArea.Width - Math.Max(0, nonClient.Width));
            int maxH = Math.Max(360, workingArea.Height - Math.Max(0, nonClient.Height));

            MinimumSize = new Size(
                Math.Min(UITheme.Scale(520, dpiScale), maxW),
                Math.Min(UITheme.Scale(420, dpiScale), maxH));
            ClientSize = new Size(Math.Min(targetW, maxW), Math.Min(targetH, maxH));
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

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            float actualScale = GetDpiScale();
            if (Math.Abs(actualScale - _currentDpiScale) > 0.01f)
            {
                ApplyDpiScale(actualScale);
            }
            else
            {
                UpdateFormClientSize(actualScale);
            }
            ResizeBoundColumns();
            ResizeRuleListColumns();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            float newScale = e.DeviceDpiNew > 0 ? e.DeviceDpiNew / 96f : GetDpiScale();
            if (Math.Abs(newScale - _currentDpiScale) > 0.01f)
            {
                ApplyDpiScale(newScale);
            }
        }

        private void InitializeComponents()
        {
            Text = "TGramFS — Управление папками";
            Icon = AppIconHelper.GetAppIcon();
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            Font = UITheme.BaseFont;
            MaximizeBox = true;
            MinimizeBox = true;

            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = UITheme.BaseFont
            };
            RegisterDpiScaler(scale => _tabControl.Padding = new Point(UITheme.Scale(8, scale), UITheme.Scale(4, scale)));

            _tabControl.TabPages.Add(BuildBoundFoldersTab());
            _tabControl.TabPages.Add(BuildRuleTab(
                tabTitle: "Фото-превью",
                description: "По умолчанию создание фото-превью для галереи Telegram отключено для всего диска.\nДобавьте папки, в которых нужно создавать фото-превью, и при необходимости задайте исключения.",
                isPreviewRule: true,
                out _lvPreviewAllowed,
                out _lvPreviewExcluded,
                out _btnPreviewRemoveAllowed,
                out _btnPreviewRemoveExcluded));
            _tabControl.TabPages.Add(BuildRuleTab(
                tabTitle: "Подписи в Telegram",
                description: "По умолчанию текстовые подписи в Telegram отключены для всего диска (включая многотомные файлы).\nДобавьте папки, где нужны подписи со статусом и путём (#latest / #trash), и при необходимости задайте исключения.",
                isPreviewRule: false,
                out _lvCaptionAllowed,
                out _lvCaptionExcluded,
                out _btnCaptionRemoveAllowed,
                out _btnCaptionRemoveExcluded));

            _tabControl.SelectedIndexChanged += (s, e) => RefreshAllTabs();

            var btnClose = UIFactory.CreateButton("Закрыть", (s, e) => Close(), registerScaler: RegisterDpiScaler);
            var bottomBar = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = SystemColors.Control
            };
            bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            bottomBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115f));
            btnClose.Dock = DockStyle.Fill;
            bottomBar.Controls.Add(btnClose, 1, 0);
            RegisterDpiScaler(scale =>
            {
                bottomBar.Height = UITheme.Scale(44, scale);
                bottomBar.Padding = UITheme.Scale(new Padding(12, 6, 16, 6), scale);
                bottomBar.ColumnStyles[1].Width = UITheme.Scale(115, scale);
                btnClose.Height = UITheme.Scale(28, scale);
                btnClose.MinimumSize = new Size(UITheme.Scale(110, scale), UITheme.Scale(28, scale));
            });

            Controls.Add(_tabControl);
            Controls.Add(bottomBar);

            CancelButton = btnClose;
            ApplyDpiScale(GetDpiScale());
        }

        private TabPage BuildBoundFoldersTab()
        {
            var tab = new TabPage("Привязанные папки");

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            RegisterDpiScaler(scale => root.Padding = UITheme.Scale(new Padding(12), scale));

            var lblHint = new Label
            {
                Text = "Папки виртуального диска (включая вложенные), привязанные к отдельным приватным каналам Telegram.\nФайлы внутри привязанной папки и её подпапок загружаются в соответствующий канал.",
                AutoSize = true,
                Font = UITheme.SmallFont,
                ForeColor = UITheme.TextMuted,
                Dock = DockStyle.Top
            };

            _lvBoundFolders = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                Font = UITheme.BaseFont
            };
            _lvBoundFolders.Columns.Add("Путь на диске", 280);
            _lvBoundFolders.Columns.Add("Канал Telegram", 190);
            _lvBoundFolders.Columns.Add("ID канала", 120);
            _lvBoundFolders.SelectedIndexChanged += (s, e) =>
            {
                _btnOpenBoundFolder.Enabled = _lvBoundFolders.SelectedItems.Count > 0;
            };
            _lvBoundFolders.DoubleClick += (s, e) => OpenSelectedBoundFolderInExplorer();
            _lvBoundFolders.SizeChanged += (s, e) => ResizeBoundColumns();

            _lblBoundEmptyHint = new Label
            {
                Text = "Привязанные папки пока не настроены. Нажмите «Создать / привязать папку...» или выберите «Привязать папку к каналу» в меню Проводника.",
                AutoSize = true,
                Font = UITheme.SmallFont,
                ForeColor = UITheme.TextMuted,
                Visible = false
            };

            void UpdateBoundTabWrapWidths()
            {
                int availW = root.ClientSize.Width > 40
                    ? root.ClientSize.Width - root.Padding.Horizontal - UITheme.Scale(6, _currentDpiScale)
                    : UITheme.Scale(BaseClientWidth - 60, _currentDpiScale);
                availW = Math.Max(UITheme.Scale(200, _currentDpiScale), availW);
                lblHint.MaximumSize = new Size(availW, 0);
                _lblBoundEmptyHint.MaximumSize = new Size(availW, 0);
            }

            root.SizeChanged += (s, e) => UpdateBoundTabWrapWidths();
            RegisterDpiScaler(scale =>
            {
                lblHint.Margin = UITheme.Scale(new Padding(0, 0, 0, 8), scale);
                _lblBoundEmptyHint.Margin = UITheme.Scale(new Padding(0, 6, 0, 2), scale);
                UpdateBoundTabWrapWidths();
            });

            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            RegisterDpiScaler(scale => btnRow.Margin = UITheme.Scale(new Padding(0, 8, 0, 0), scale));

            _btnCreateBindFolder = UIFactory.CreateButton(
                "Создать / привязать папку...",
                (s, e) => OpenCreateOrBindFolderDialog(),
                autoSize: true,
                registerScaler: RegisterDpiScaler);

            _btnOpenBoundFolder = UIFactory.CreateButton(
                "Открыть в Проводнике",
                (s, e) => OpenSelectedBoundFolderInExplorer(),
                autoSize: true,
                registerScaler: RegisterDpiScaler);
            _btnOpenBoundFolder.Enabled = false;

            _btnRefreshBound = UIFactory.CreateButton(
                "Обновить",
                (s, e) => RefreshAllTabs(),
                autoSize: true,
                registerScaler: RegisterDpiScaler);

            RegisterDpiScaler(scale =>
            {
                int gap = UITheme.Scale(8, scale);
                _btnCreateBindFolder.Margin = new Padding(0, 0, gap, 0);
                _btnOpenBoundFolder.Margin = new Padding(0, 0, gap, 0);
                _btnRefreshBound.Margin = Padding.Empty;
            });

            btnRow.Controls.AddRange(new Control[] { _btnCreateBindFolder, _btnOpenBoundFolder, _btnRefreshBound });

            root.Controls.Add(lblHint, 0, 0);
            root.Controls.Add(_lvBoundFolders, 0, 1);
            root.Controls.Add(_lblBoundEmptyHint, 0, 2);
            root.Controls.Add(btnRow, 0, 3);

            tab.Controls.Add(root);
            return tab;
        }

        private TabPage BuildRuleTab(
            string tabTitle,
            string description,
            bool isPreviewRule,
            out ListView lvAllowed,
            out ListView lvExcluded,
            out Button btnRemoveAllowed,
            out Button btnRemoveExcluded)
        {
            var tab = new TabPage(tabTitle);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            RegisterDpiScaler(scale => root.Padding = UITheme.Scale(new Padding(12), scale));

            var lblHint = new Label
            {
                Text = description,
                AutoSize = true,
                Font = UITheme.SmallFont,
                ForeColor = UITheme.TextMuted,
                Dock = DockStyle.Top
            };

            void UpdateRuleTabWrapWidth()
            {
                int availW = root.ClientSize.Width > 40
                    ? root.ClientSize.Width - root.Padding.Horizontal - UITheme.Scale(6, _currentDpiScale)
                    : UITheme.Scale(BaseClientWidth - 60, _currentDpiScale);
                lblHint.MaximumSize = new Size(Math.Max(UITheme.Scale(200, _currentDpiScale), availW), 0);
            }

            root.SizeChanged += (s, e) => UpdateRuleTabWrapWidth();
            RegisterDpiScaler(scale =>
            {
                lblHint.Margin = UITheme.Scale(new Padding(0, 0, 0, 8), scale);
                UpdateRuleTabWrapWidth();
            });

            var splitGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            splitGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            splitGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            splitGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            var grpAllowed = BuildRuleSectionGroup(
                "Разрешено для папок (и их подпапок)",
                isPreviewRule,
                targetMode: 1,
                out lvAllowed,
                out btnRemoveAllowed);

            var grpExcluded = BuildRuleSectionGroup(
                "Исключения (запрещено внутри разрешённых)",
                isPreviewRule,
                targetMode: 0,
                out lvExcluded,
                out btnRemoveExcluded);

            RegisterDpiScaler(scale =>
            {
                grpAllowed.Margin = UITheme.Scale(new Padding(0, 0, 0, 6), scale);
                grpExcluded.Margin = UITheme.Scale(new Padding(0, 4, 0, 0), scale);
            });

            splitGrid.Controls.Add(grpAllowed, 0, 0);
            splitGrid.Controls.Add(grpExcluded, 0, 1);

            root.Controls.Add(lblHint, 0, 0);
            root.Controls.Add(splitGrid, 0, 1);

            tab.Controls.Add(root);
            return tab;
        }

        private GroupBox BuildRuleSectionGroup(
            string title,
            bool isPreviewRule,
            int targetMode,
            out ListView listView,
            out Button btnRemove)
        {
            var grp = new GroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                Font = UITheme.BaseFont
            };

            var inner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            RegisterDpiScaler(scale => inner.Padding = UITheme.Scale(new Padding(8, 6, 8, 8), scale));

            var lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = true,
                HeaderStyle = ColumnHeaderStyle.None,
                Font = UITheme.BaseFont
            };
            lv.Columns.Add("Путь на диске", 420);
            lv.SizeChanged += (s, e) =>
            {
                if (lv.Columns.Count > 0 && lv.ClientSize.Width > 20)
                {
                    lv.Columns[0].Width = Math.Max(100, lv.ClientSize.Width - 6);
                }
            };

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };
            RegisterDpiScaler(scale => btnPanel.Margin = UITheme.Scale(new Padding(8, 0, 0, 0), scale));

            var btnAdd = UIFactory.CreateButton(
                "Добавить...",
                (s, e) => OpenRulePickerDialog(isPreviewRule, targetMode),
                width: 115,
                registerScaler: RegisterDpiScaler);

            var localBtnRemove = UIFactory.CreateButton(
                "Убрать",
                (s, e) => RemoveSelectedRules(lv, isPreviewRule),
                width: 115,
                registerScaler: RegisterDpiScaler);
            localBtnRemove.Enabled = false;

            lv.SelectedIndexChanged += (s, e) => localBtnRemove.Enabled = lv.SelectedItems.Count > 0;
            lv.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete && lv.SelectedItems.Count > 0)
                {
                    RemoveSelectedRules(lv, isPreviewRule);
                    e.Handled = true;
                }
            };

            RegisterDpiScaler(scale =>
            {
                btnAdd.Margin = UITheme.Scale(new Padding(0, 0, 0, 6), scale);
                localBtnRemove.Margin = Padding.Empty;
            });

            btnPanel.Controls.Add(btnAdd);
            btnPanel.Controls.Add(localBtnRemove);

            inner.Controls.Add(lv, 0, 0);
            inner.Controls.Add(btnPanel, 1, 0);

            grp.Controls.Add(inner);

            listView = lv;
            btnRemove = localBtnRemove;
            return grp;
        }

        private void ResizeBoundColumns()
        {
            if (_lvBoundFolders == null || _lvBoundFolders.Columns.Count < 3) return;
            int totalW = _lvBoundFolders.ClientSize.Width - 6;
            if (totalW <= 200) return;

            int idColW = UITheme.Scale(110, _currentDpiScale);
            int channelColW = Math.Max(UITheme.Scale(150, _currentDpiScale), (int)(totalW * 0.30));
            int pathColW = Math.Max(UITheme.Scale(180, _currentDpiScale), totalW - idColW - channelColW);

            _lvBoundFolders.Columns[0].Width = pathColW;
            _lvBoundFolders.Columns[1].Width = channelColW;
            _lvBoundFolders.Columns[2].Width = idColW;
        }

        private void ResizeRuleListColumns()
        {
            foreach (var lv in new[] { _lvPreviewAllowed, _lvPreviewExcluded, _lvCaptionAllowed, _lvCaptionExcluded })
            {
                if (lv != null && lv.Columns.Count > 0 && lv.ClientSize.Width > 20)
                {
                    lv.Columns[0].Width = Math.Max(100, lv.ClientSize.Width - 6);
                }
            }
        }

        public void RefreshAllTabs()
        {
            RefreshBoundFoldersTab();
            RefreshRuleTabs();
        }

        private void RefreshBoundFoldersTab()
        {
            var bound = _repository.GetBoundChannelFolders();
            _lvBoundFolders.BeginUpdate();
            try
            {
                _lvBoundFolders.Items.Clear();
                foreach (var item in bound)
                {
                    var lvi = new ListViewItem(item.FullPath) { Tag = item };
                    lvi.SubItems.Add(item.ChannelTitle);
                    lvi.SubItems.Add(item.ChannelId.ToString());
                    _lvBoundFolders.Items.Add(lvi);
                }
            }
            finally
            {
                _lvBoundFolders.EndUpdate();
            }

            _lblBoundEmptyHint.Visible = bound.Count == 0;
            _btnOpenBoundFolder.Enabled = _lvBoundFolders.SelectedItems.Count > 0;
            ResizeBoundColumns();
        }

        private void RefreshRuleTabs()
        {
            var previewRules = _repository.GetFoldersWithExplicitPhotoPreviewMode();
            PopulateRuleList(_lvPreviewAllowed, previewRules.Where(x => x.Mode == 1), _btnPreviewRemoveAllowed);
            PopulateRuleList(_lvPreviewExcluded, previewRules.Where(x => x.Mode == 0), _btnPreviewRemoveExcluded);

            var captionRules = _repository.GetFoldersWithExplicitCaptionMode();
            PopulateRuleList(_lvCaptionAllowed, captionRules.Where(x => x.Mode == 1), _btnCaptionRemoveAllowed);
            PopulateRuleList(_lvCaptionExcluded, captionRules.Where(x => x.Mode == 0), _btnCaptionRemoveExcluded);

            ResizeRuleListColumns();
        }

        private static void PopulateRuleList(ListView lv, IEnumerable<FolderRuleItem> items, Button removeBtn)
        {
            lv.BeginUpdate();
            try
            {
                lv.Items.Clear();
                foreach (var item in items)
                {
                    var lvi = new ListViewItem(item.FullPath) { Tag = item };
                    lv.Items.Add(lvi);
                }
            }
            finally
            {
                lv.EndUpdate();
            }
            removeBtn.Enabled = lv.SelectedItems.Count > 0;
        }

        private void RemoveSelectedRules(ListView lv, bool isPreviewRule)
        {
            if (lv.SelectedItems.Count == 0) return;

            var idsToReset = lv.SelectedItems
                .Cast<ListViewItem>()
                .Select(i => i.Tag as FolderRuleItem)
                .Where(r => r != null)
                .Select(r => r!.NodeId)
                .Distinct()
                .ToList();

            foreach (int nodeId in idsToReset)
            {
                if (isPreviewRule)
                {
                    _repository.SetFolderPhotoPreviewMode(nodeId, null);
                }
                else
                {
                    _repository.SetFolderCaptionMode(nodeId, null);
                }
            }

            RefreshRuleTabs();
        }

        private void OpenRulePickerDialog(bool isPreviewRule, int targetMode)
        {
            using var dlg = new FolderRulePickerDialog(_repository, isPreviewRule, targetMode);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                RefreshRuleTabs();
            }
        }

        private void OpenCreateOrBindFolderDialog()
        {
            if (!_telegramService.IsAuthorized)
            {
                MessageBox.Show(
                    this,
                    "Для создания канала Telegram необходимо сначала выполнить вход в аккаунт в окне «Настройки».",
                    "Telegram не авторизован",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            using var dlg = new CreateOrBindFolderDialog(_configManager, _repository, _telegramService, _virtualDriveManager);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                RefreshAllTabs();
            }
        }

        private void OpenSelectedBoundFolderInExplorer()
        {
            if (_lvBoundFolders.SelectedItems.Count == 0) return;
            if (_lvBoundFolders.SelectedItems[0].Tag is not BoundFolderInfo info) return;

            var settings = _configManager.CurrentSettings;
            string drive = _virtualDriveManager?.MountedLetter ?? settings.Server.DriveLetter ?? "Z:";
            if (string.Equals(drive, "AUTO", StringComparison.OrdinalIgnoreCase)) drive = "Z:";
            drive = drive.TrimEnd('\\', '/');
            if (!drive.EndsWith(":")) drive += ":";

            string sub = info.FullPath.Replace('/', '\\').TrimStart('\\');
            string fullWinPath = string.IsNullOrEmpty(sub) ? drive + "\\" : $"{drive}\\{sub}";

            try
            {
                if (Directory.Exists(fullWinPath))
                {
                    Process.Start("explorer.exe", $"\"{fullWinPath}\"");
                }
                else
                {
                    string escapedRel = string.Join("/", info.FullPath
                        .Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Select(Uri.EscapeDataString));
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = $"http://localhost:{settings.Server.Port}/{escapedRel}",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Не удалось открыть папку: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        internal static bool TryNormalizeVirtualFolderPath(string rawPath, bool allowRoot, out string normalizedPath, out string errorMessage)
        {
            normalizedPath = "/";
            errorMessage = string.Empty;

            string trimmed = (rawPath ?? string.Empty).Trim().Replace('\\', '/');
            if (trimmed.Length >= 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':')
            {
                trimmed = trimmed.Substring(2);
            }

            var segments = trimmed
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToList();

            if (segments.Count == 0)
            {
                if (allowRoot)
                {
                    normalizedPath = "/";
                    return true;
                }
                errorMessage = "Укажите имя или путь папки на виртуальном диске (например, Музыка).";
                return false;
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (string seg in segments)
            {
                if (seg == "." || seg == ".." || seg.IndexOfAny(invalidChars) >= 0)
                {
                    errorMessage = $"Недопустимое имя папки: '{seg}'.";
                    return false;
                }
            }

            if (segments[0].Equals(".Trash", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Системную папку .Trash нельзя использовать в правилах.";
                return false;
            }

            normalizedPath = "/" + string.Join("/", segments);
            return true;
        }

        private static string? PromptNewSubfolderName(IWin32Window owner, string parentPath)
        {
            using var dlg = new NewSubfolderDialog(parentPath);
            return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.FolderName : null;
        }

        private sealed class NewSubfolderDialog : Form
        {
            private const int BaseWidth = 400;
            private const int BaseHeight = 135;
            private readonly List<Action<float>> _dpiScalers = new();
            private float _currentDpiScale = 1.0f;

            private readonly string _parentPath;
            private TextBox _txtName = null!;
            public string? FolderName { get; private set; }

            public NewSubfolderDialog(string parentPath)
            {
                _parentPath = parentPath;
                InitializeDialog();
            }

            private void RegisterDpiScaler(Action<float> scaler) => _dpiScalers.Add(scaler);

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
                    ClientSize = new Size(UITheme.Scale(BaseWidth, dpiScale), UITheme.Scale(BaseHeight, dpiScale));
                }
                finally
                {
                    ResumeLayout(true);
                    PerformLayout();
                }
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                float actualScale = GetDpiScale();
                if (Math.Abs(actualScale - _currentDpiScale) > 0.01f)
                {
                    ApplyDpiScale(actualScale);
                }
            }

            protected override void OnDpiChanged(DpiChangedEventArgs e)
            {
                base.OnDpiChanged(e);
                float newScale = e.DeviceDpiNew > 0 ? e.DeviceDpiNew / 96f : GetDpiScale();
                if (Math.Abs(newScale - _currentDpiScale) > 0.01f)
                {
                    ApplyDpiScale(newScale);
                }
            }

            private void InitializeDialog()
            {
                Text = "Новая папка";
                Icon = AppIconHelper.GetAppIcon();
                AutoScaleMode = AutoScaleMode.None;
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                Font = UITheme.BaseFont;

                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 3
                };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                RegisterDpiScaler(scale => root.Padding = UITheme.Scale(new Padding(14), scale));

                var lbl = new Label
                {
                    Text = $"Создать папку внутри «{_parentPath}»:",
                    AutoSize = true,
                    Dock = DockStyle.Top
                };
                RegisterDpiScaler(scale =>
                {
                    lbl.Margin = UITheme.Scale(new Padding(0, 0, 0, 8), scale);
                    lbl.MaximumSize = new Size(UITheme.Scale(BaseWidth - 32, scale), 0);
                });

                _txtName = new TextBox
                {
                    Dock = DockStyle.Top,
                    Font = UITheme.BaseFont
                };
                RegisterDpiScaler(scale => _txtName.Margin = UITheme.Scale(new Padding(0, 0, 0, 10), scale));

                var btnRow = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    ColumnCount = 3,
                    RowCount = 1
                };
                btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                var btnOk = UIFactory.CreateButton(
                    "Создать",
                    (s, e) => TryAccept(),
                    width: 105,
                    registerScaler: RegisterDpiScaler);

                var btnCancel = UIFactory.CreateButton(
                    "Отмена",
                    width: 105,
                    registerScaler: RegisterDpiScaler);
                btnCancel.DialogResult = DialogResult.Cancel;

                RegisterDpiScaler(scale =>
                {
                    btnOk.Margin = UITheme.Scale(new Padding(0, 0, 8, 0), scale);
                    btnCancel.Margin = Padding.Empty;
                });

                btnRow.Controls.Add(btnOk, 1, 0);
                btnRow.Controls.Add(btnCancel, 2, 0);

                root.Controls.Add(lbl, 0, 0);
                root.Controls.Add(_txtName, 0, 1);
                root.Controls.Add(btnRow, 0, 2);

                Controls.Add(root);
                AcceptButton = btnOk;
                CancelButton = btnCancel;

                ApplyDpiScale(GetDpiScale());
            }

            private void TryAccept()
            {
                string name = _txtName.Text.Trim();
                if (string.IsNullOrWhiteSpace(name) ||
                    name == "." ||
                    name == ".." ||
                    name.Equals(".Trash", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains('/') ||
                    name.Contains('\\') ||
                    name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    MessageBox.Show(this, "Введите корректное имя папки без слэшей и спецсимволов.", "Некорректное имя", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                FolderName = name;
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private const string BoundSuffixTag = "[привязано]";
        private const string BoundSuffixWithPad = "  " + BoundSuffixTag;

        private static void ConfigureBoundTagTreeDrawing(TreeView tree)
        {
            tree.DrawMode = TreeViewDrawMode.OwnerDrawText;
            tree.DrawNode += (s, e) =>
            {
                if (e.Node == null) return;
                e.DrawDefault = false;

                string fullText = e.Node.Text ?? string.Empty;
                Font defaultFont = tree.Font ?? UITheme.BaseFont;
                bool isBoundNode = e.Node.Tag is FolderTreeNodeInfo info && info.IsCustomBoundChannel;
                bool isSelected = (e.State & TreeNodeStates.Selected) != 0;
                bool isFocused = tree.Focused;

                bool hasBoundSuffix = isBoundNode
                    && fullText.EndsWith(BoundSuffixWithPad, StringComparison.Ordinal);

                string namePart = hasBoundSuffix
                    ? fullText.Substring(0, fullText.Length - BoundSuffixWithPad.Length)
                    : fullText;
                string suffixPart = hasBoundSuffix ? BoundSuffixWithPad : string.Empty;

                Font nameFont = isBoundNode ? UITheme.BoldFont : (e.Node.NodeFont ?? defaultFont);
                Font suffixFont = defaultFont;

                const TextFormatFlags flags = TextFormatFlags.NoPrefix
                    | TextFormatFlags.NoPadding
                    | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine;

                Size nameSize = TextRenderer.MeasureText(e.Graphics, namePart, nameFont, Size.Empty, flags);
                Size suffixSize = string.IsNullOrEmpty(suffixPart)
                    ? Size.Empty
                    : TextRenderer.MeasureText(e.Graphics, suffixPart, suffixFont, Size.Empty, flags);

                const int leftPad = 2;
                int drawWidth = Math.Max(e.Bounds.Width, leftPad + nameSize.Width + suffixSize.Width + 4);
                Rectangle bgRect = new Rectangle(e.Bounds.X, e.Bounds.Y, drawWidth, e.Bounds.Height);

                Color bgColor = isSelected
                    ? (isFocused ? SystemColors.Highlight : SystemColors.Control)
                    : (tree.BackColor.IsEmpty ? SystemColors.Window : tree.BackColor);

                using (var bgBrush = new SolidBrush(bgColor))
                {
                    e.Graphics.FillRectangle(bgBrush, bgRect);
                }

                Color baseFore = e.Node.ForeColor.IsEmpty
                    ? (tree.ForeColor.IsEmpty ? UITheme.TextMain : tree.ForeColor)
                    : e.Node.ForeColor;
                Color nameColor = (isSelected && isFocused) ? SystemColors.HighlightText : baseFore;
                Color suffixColor = (isSelected && isFocused) ? SystemColors.HighlightText : UITheme.TextMuted;

                Rectangle nameRect = new Rectangle(e.Bounds.X + leftPad, e.Bounds.Y, nameSize.Width + 2, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, namePart, nameFont, nameRect, nameColor, flags);

                if (!string.IsNullOrEmpty(suffixPart))
                {
                    Rectangle suffixRect = new Rectangle(e.Bounds.X + leftPad + nameSize.Width, e.Bounds.Y, suffixSize.Width + 4, e.Bounds.Height);
                    TextRenderer.DrawText(e.Graphics, suffixPart, suffixFont, suffixRect, suffixColor, flags);
                }

                if ((e.State & TreeNodeStates.Focused) != 0 && isFocused)
                {
                    ControlPaint.DrawFocusRectangle(e.Graphics, bgRect, nameColor, bgColor);
                }
            };
        }

        private static void PopulateFolderTree(
            TreeView tree,
            List<FolderTreeNodeInfo> nodes,
            Func<FolderTreeNodeInfo, (string Label, Color? Color)>? formatter = null,
            string? pathToSelect = null)
        {
            tree.BeginUpdate();
            try
            {
                tree.Nodes.Clear();
                var treeNodeById = new Dictionary<int, TreeNode>();

                foreach (var info in nodes)
                {
                    string display = info.ParentId == null ? "/ (Корень диска)" : info.Name;
                    Color? color = null;
                    if (formatter != null)
                    {
                        var (suffix, c) = formatter(info);
                        if (!string.IsNullOrEmpty(suffix))
                        {
                            display += "  " + suffix;
                        }
                        color = c;
                    }

                    var tn = new TreeNode(display) { Tag = info };
                    if (info.IsCustomBoundChannel)
                    {
                        tn.NodeFont = UITheme.BoldFont;
                    }
                    if (color.HasValue)
                    {
                        tn.ForeColor = color.Value;
                    }
                    treeNodeById[info.Id] = tn;

                    if (!info.ParentId.HasValue)
                    {
                        tree.Nodes.Add(tn);
                    }
                    else if (treeNodeById.TryGetValue(info.ParentId.Value, out var parentTn))
                    {
                        parentTn.Nodes.Add(tn);
                    }
                    else
                    {
                        tree.Nodes.Add(tn);
                    }
                }

                if (tree.Nodes.Count > 0)
                {
                    tree.Nodes[0].Expand();
                }

                TreeNode? selected = null;
                if (!string.IsNullOrEmpty(pathToSelect))
                {
                    selected = FindTreeNodeByPath(tree.Nodes, pathToSelect);
                }
                selected ??= tree.Nodes.Count > 0 ? tree.Nodes[0] : null;
                if (selected != null)
                {
                    tree.SelectedNode = selected;
                    selected.EnsureVisible();
                }
            }
            finally
            {
                tree.EndUpdate();
            }
        }

        private static TreeNode? FindTreeNodeByPath(TreeNodeCollection nodes, string targetPath)
        {
            foreach (TreeNode tn in nodes)
            {
                if (tn.Tag is FolderTreeNodeInfo info &&
                    string.Equals(info.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return tn;
                }
                var childMatch = FindTreeNodeByPath(tn.Nodes, targetPath);
                if (childMatch != null) return childMatch;
            }
            return null;
        }

        /// <summary>
        /// Диалог выбора или создания папки для добавления в список разрешённых / исключений.
        /// </summary>
        private sealed class FolderRulePickerDialog : Form
        {
            private const int BaseClientWidth = 560;
            private const int BaseClientHeight = 450;

            private readonly NodeRepository _repository;
            private readonly bool _isPreviewRule;
            private readonly int _targetMode;
            private readonly List<Action<float>> _dpiScalers = new();
            private float _currentDpiScale = 1.0f;

            private TreeView _tree = null!;
            private TextBox _txtPath = null!;
            private Label _lblStatus = null!;
            private List<FolderTreeNodeInfo> _folderItems = new();
            private bool _suppressTreeSync;

            public FolderRulePickerDialog(NodeRepository repository, bool isPreviewRule, int targetMode)
            {
                _repository = repository;
                _isPreviewRule = isPreviewRule;
                _targetMode = targetMode;

                InitializeDialog();
                ReloadTree("/");
            }

            private void RegisterDpiScaler(Action<float> scaler) => _dpiScalers.Add(scaler);

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

            private void UpdateDialogClientSize(float dpiScale)
            {
                int targetW = UITheme.Scale(BaseClientWidth, dpiScale);
                int targetH = UITheme.Scale(BaseClientHeight, dpiScale);
                Rectangle workingArea = IsHandleCreated
                    ? Screen.FromHandle(Handle).WorkingArea
                    : Screen.FromPoint(Cursor.Position).WorkingArea;

                Size nonClient = Size - ClientSize;
                int maxW = Math.Max(420, workingArea.Width - Math.Max(0, nonClient.Width));
                int maxH = Math.Max(340, workingArea.Height - Math.Max(0, nonClient.Height));

                MinimumSize = new Size(
                    Math.Min(UITheme.Scale(460, dpiScale), maxW),
                    Math.Min(UITheme.Scale(360, dpiScale), maxH));
                ClientSize = new Size(Math.Min(targetW, maxW), Math.Min(targetH, maxH));
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
                    UpdateDialogClientSize(dpiScale);
                }
                finally
                {
                    ResumeLayout(true);
                    PerformLayout();
                }
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                float actualScale = GetDpiScale();
                if (Math.Abs(actualScale - _currentDpiScale) > 0.01f)
                {
                    ApplyDpiScale(actualScale);
                }
                else
                {
                    UpdateDialogClientSize(actualScale);
                }
            }

            protected override void OnDpiChanged(DpiChangedEventArgs e)
            {
                base.OnDpiChanged(e);
                float newScale = e.DeviceDpiNew > 0 ? e.DeviceDpiNew / 96f : GetDpiScale();
                if (Math.Abs(newScale - _currentDpiScale) > 0.01f)
                {
                    ApplyDpiScale(newScale);
                }
            }

            private void InitializeDialog()
            {
                string ruleName = _isPreviewRule ? "фото-превью" : "подписи в Telegram";
                string actionName = _targetMode == 1 ? "Разрешить" : "Добавить исключение";
                Text = $"{actionName} — {ruleName}";
                Icon = AppIconHelper.GetAppIcon();
                AutoScaleMode = AutoScaleMode.None;
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.Sizable;
                MaximizeBox = false;
                MinimizeBox = false;
                Font = UITheme.BaseFont;

                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 5
                };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                RegisterDpiScaler(scale => root.Padding = UITheme.Scale(new Padding(12), scale));

                var lblPrompt = new Label
                {
                    Text = "Выберите папку из базы или введите путь (несуществующая папка будет создана):",
                    AutoSize = true,
                    Dock = DockStyle.Top
                };

                _tree = new TreeView
                {
                    Dock = DockStyle.Fill,
                    HideSelection = false,
                    Font = UITheme.BaseFont
                };
                ConfigureBoundTagTreeDrawing(_tree);
                _tree.AfterSelect += (s, e) =>
                {
                    if (_suppressTreeSync) return;
                    if (e.Node?.Tag is FolderTreeNodeInfo info)
                    {
                        _txtPath.Text = info.FullPath;
                        UpdatePathStatusHint();
                    }
                };
                _tree.NodeMouseDoubleClick += (s, e) =>
                {
                    if (e.Node?.Tag is FolderTreeNodeInfo)
                    {
                        ApplyAndClose();
                    }
                };

                var pathPanel = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    ColumnCount = 2,
                    RowCount = 1
                };
                pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                RegisterDpiScaler(scale => pathPanel.Margin = UITheme.Scale(new Padding(0, 8, 0, 4), scale));

                var lblPath = new Label
                {
                    Text = "Путь папки:",
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                RegisterDpiScaler(scale => lblPath.Margin = UITheme.Scale(new Padding(0, 0, 8, 0), scale));

                _txtPath = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Font = UITheme.BaseFont
                };
                _txtPath.TextChanged += (s, e) => UpdatePathStatusHint();

                pathPanel.Controls.Add(lblPath, 0, 0);
                pathPanel.Controls.Add(_txtPath, 1, 0);

                _lblStatus = new Label
                {
                    AutoSize = true,
                    Font = UITheme.SmallFont,
                    ForeColor = UITheme.TextMuted
                };

                void UpdateWrapWidths()
                {
                    int availW = root.ClientSize.Width > 40
                        ? root.ClientSize.Width - root.Padding.Horizontal - UITheme.Scale(6, _currentDpiScale)
                        : UITheme.Scale(BaseClientWidth - 40, _currentDpiScale);
                    availW = Math.Max(UITheme.Scale(200, _currentDpiScale), availW);
                    lblPrompt.MaximumSize = new Size(availW, 0);
                    _lblStatus.MaximumSize = new Size(availW, 0);
                }

                root.SizeChanged += (s, e) => UpdateWrapWidths();
                RegisterDpiScaler(scale =>
                {
                    lblPrompt.Margin = UITheme.Scale(new Padding(0, 0, 0, 6), scale);
                    _lblStatus.Margin = UITheme.Scale(new Padding(0, 2, 0, 8), scale);
                    UpdateWrapWidths();
                });

                var bottomBtns = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    ColumnCount = 4,
                    RowCount = 1
                };
                bottomBtns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                bottomBtns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                bottomBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                bottomBtns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                var btnNewSubfolder = UIFactory.CreateButton(
                    "Новая папка",
                    (s, e) => CreateSubfolderInTree(),
                    width: 115,
                    registerScaler: RegisterDpiScaler);

                var btnApply = UIFactory.CreateButton(
                    "Применить",
                    (s, e) => ApplyAndClose(),
                    width: 115,
                    registerScaler: RegisterDpiScaler);

                var btnCancel = UIFactory.CreateButton(
                    "Отмена",
                    width: 115,
                    registerScaler: RegisterDpiScaler);
                btnCancel.DialogResult = DialogResult.Cancel;

                RegisterDpiScaler(scale =>
                {
                    btnNewSubfolder.Margin = UITheme.Scale(new Padding(0, 0, 8, 0), scale);
                    btnApply.Margin = Padding.Empty;
                    btnCancel.Margin = Padding.Empty;
                });

                bottomBtns.Controls.Add(btnNewSubfolder, 0, 0);
                bottomBtns.Controls.Add(btnApply, 1, 0);
                bottomBtns.Controls.Add(btnCancel, 3, 0);

                root.Controls.Add(lblPrompt, 0, 0);
                root.Controls.Add(_tree, 0, 1);
                root.Controls.Add(pathPanel, 0, 2);
                root.Controls.Add(_lblStatus, 0, 3);
                root.Controls.Add(bottomBtns, 0, 4);

                Controls.Add(root);
                AcceptButton = btnApply;
                CancelButton = btnCancel;

                ApplyDpiScale(GetDpiScale());
            }

            private void ReloadTree(string? selectPath)
            {
                _folderItems = _repository.GetActiveFolderTree();
                _suppressTreeSync = true;
                try
                {
                    PopulateFolderTree(
                        _tree,
                        _folderItems,
                        info =>
                        {
                            int? explicitMode = _isPreviewRule ? info.PhotoPreviewMode : info.CaptionMode;
                            bool effective = _isPreviewRule ? info.EffectivePhotoPreview : info.EffectiveCaption;

                            Color? color = null;
                            if (explicitMode == 1)
                            {
                                color = UITheme.TextSuccess;
                            }
                            else if (explicitMode == 0)
                            {
                                color = UITheme.TextDanger;
                            }
                            else if (effective)
                            {
                                color = Color.FromArgb(40, 125, 85);
                            }

                            string suffix = info.IsCustomBoundChannel ? BoundSuffixTag : string.Empty;
                            return (suffix, color);
                        },
                        selectPath);
                }
                finally
                {
                    _suppressTreeSync = false;
                }

                if (_tree.SelectedNode?.Tag is FolderTreeNodeInfo selectedInfo)
                {
                    _txtPath.Text = selectedInfo.FullPath;
                }
                UpdatePathStatusHint();
            }

            private void CreateSubfolderInTree()
            {
                string parentPath = "/";
                if (_tree.SelectedNode?.Tag is FolderTreeNodeInfo info)
                {
                    parentPath = info.FullPath;
                }

                string? newName = PromptNewSubfolderName(this, parentPath);
                if (string.IsNullOrEmpty(newName)) return;

                string targetPath = parentPath == "/" ? "/" + newName : parentPath.TrimEnd('/') + "/" + newName;
                var created = _repository.EnsureDirectoryPathExists(targetPath);
                if (created == null)
                {
                    MessageBox.Show(this, $"Не удалось создать папку '{targetPath}'. Проверьте, нет ли файла с таким именем.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ReloadTree(targetPath);
            }

            private void UpdatePathStatusHint()
            {
                if (!TryNormalizeVirtualFolderPath(_txtPath.Text, allowRoot: true, out string normalized, out string err))
                {
                    _lblStatus.ForeColor = UITheme.TextDanger;
                    _lblStatus.Text = err;
                    return;
                }

                var existing = _folderItems.FirstOrDefault(f => string.Equals(f.FullPath, normalized, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    _lblStatus.ForeColor = UITheme.TextMuted;
                    _lblStatus.Text = $"Папка «{normalized}» будет автоматически создана в базе и добавлена в список.";
                    return;
                }

                int? explicitMode = _isPreviewRule ? existing.PhotoPreviewMode : existing.CaptionMode;
                bool effective = _isPreviewRule ? existing.EffectivePhotoPreview : existing.EffectiveCaption;

                if (_targetMode == 0 && !effective && explicitMode != 1)
                {
                    _lblStatus.ForeColor = UITheme.TextMuted;
                    _lblStatus.Text = $"Для «{normalized}» правило сейчас и так выключено. Исключение имеет смысл внутри разрешённой родительской папки.";
                }
                else
                {
                    _lblStatus.ForeColor = UITheme.TextMuted;
                    string stateText = explicitMode == 1
                        ? "явно включено"
                        : (explicitMode == 0 ? "явно исключено" : (effective ? "включено по наследованию" : "выключено по умолчанию"));
                    _lblStatus.Text = $"Текущее состояние для «{normalized}»: {stateText}.";
                }
            }

            private void ApplyAndClose()
            {
                if (!TryNormalizeVirtualFolderPath(_txtPath.Text, allowRoot: true, out string normalized, out string err))
                {
                    MessageBox.Show(this, err, "Некорректный путь", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var node = _repository.EnsureDirectoryPathExists(normalized);
                if (node == null)
                {
                    MessageBox.Show(this, $"Не удалось создать или найти папку '{normalized}'. Возможно, по этому пути уже существует файл.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (_isPreviewRule)
                {
                    _repository.SetFolderPhotoPreviewMode(node.Id, _targetMode);
                }
                else
                {
                    _repository.SetFolderCaptionMode(node.Id, _targetMode);
                }

                DialogResult = DialogResult.OK;
                Close();
            }
        }

        /// <summary>
        /// Модальный диалог создания/выбора папки и привязки её к новому каналу Telegram с опциональным включением превью и подписей.
        /// </summary>
        private sealed class CreateOrBindFolderDialog : Form
        {
            private const int BaseClientWidth = 600;
            private const int BaseClientHeight = 490;

            private readonly ConfigManager _configManager;
            private readonly NodeRepository _repository;
            private readonly TelegramService _telegramService;
            private readonly VirtualDriveManager? _virtualDriveManager;
            private readonly List<Action<float>> _dpiScalers = new();
            private float _currentDpiScale = 1.0f;

            private TreeView _tree = null!;
            private TextBox _txtPath = null!;
            private CheckBox _chkPreview = null!;
            private CheckBox _chkCaptions = null!;
            private Label _lblStatus = null!;
            private Button _btnCreateBind = null!;
            private Button _btnCancel = null!;
            private Button _btnNewSubfolder = null!;
            private List<FolderTreeNodeInfo> _folderItems = new();
            private bool _suppressTreeSync;

            public CreateOrBindFolderDialog(
                ConfigManager configManager,
                NodeRepository repository,
                TelegramService telegramService,
                VirtualDriveManager? virtualDriveManager)
            {
                _configManager = configManager;
                _repository = repository;
                _telegramService = telegramService;
                _virtualDriveManager = virtualDriveManager;

                InitializeDialog();
                ReloadTree("/");
            }

            private void RegisterDpiScaler(Action<float> scaler) => _dpiScalers.Add(scaler);

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

            private void UpdateDialogClientSize(float dpiScale)
            {
                int targetW = UITheme.Scale(BaseClientWidth, dpiScale);
                int targetH = UITheme.Scale(BaseClientHeight, dpiScale);
                Rectangle workingArea = IsHandleCreated
                    ? Screen.FromHandle(Handle).WorkingArea
                    : Screen.FromPoint(Cursor.Position).WorkingArea;

                Size nonClient = Size - ClientSize;
                int maxW = Math.Max(420, workingArea.Width - Math.Max(0, nonClient.Width));
                int maxH = Math.Max(360, workingArea.Height - Math.Max(0, nonClient.Height));

                MinimumSize = new Size(
                    Math.Min(UITheme.Scale(480, dpiScale), maxW),
                    Math.Min(UITheme.Scale(400, dpiScale), maxH));
                ClientSize = new Size(Math.Min(targetW, maxW), Math.Min(targetH, maxH));
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
                    UpdateDialogClientSize(dpiScale);
                }
                finally
                {
                    ResumeLayout(true);
                    PerformLayout();
                }
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                float actualScale = GetDpiScale();
                if (Math.Abs(actualScale - _currentDpiScale) > 0.01f)
                {
                    ApplyDpiScale(actualScale);
                }
                else
                {
                    UpdateDialogClientSize(actualScale);
                }
            }

            protected override void OnDpiChanged(DpiChangedEventArgs e)
            {
                base.OnDpiChanged(e);
                float newScale = e.DeviceDpiNew > 0 ? e.DeviceDpiNew / 96f : GetDpiScale();
                if (Math.Abs(newScale - _currentDpiScale) > 0.01f)
                {
                    ApplyDpiScale(newScale);
                }
            }

            private void InitializeDialog()
            {
                Text = "Создать / привязать папку к каналу";
                Icon = AppIconHelper.GetAppIcon();
                AutoScaleMode = AutoScaleMode.None;
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.Sizable;
                MaximizeBox = false;
                MinimizeBox = false;
                Font = UITheme.BaseFont;

                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 6
                };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                RegisterDpiScaler(scale => root.Padding = UITheme.Scale(new Padding(12), scale));

                var lblPrompt = new Label
                {
                    Text = "Выберите существующую папку или введите путь новой папки (например, Музыка):",
                    AutoSize = true,
                    Dock = DockStyle.Top
                };

                _tree = new TreeView
                {
                    Dock = DockStyle.Fill,
                    HideSelection = false,
                    Font = UITheme.BaseFont
                };
                ConfigureBoundTagTreeDrawing(_tree);
                _tree.AfterSelect += (s, e) =>
                {
                    if (_suppressTreeSync) return;
                    if (e.Node?.Tag is FolderTreeNodeInfo info)
                    {
                        _txtPath.Text = info.FullPath == "/" ? string.Empty : info.FullPath.TrimStart('/');
                        UpdateStatusHint();
                    }
                };

                var pathPanel = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    ColumnCount = 2,
                    RowCount = 1
                };
                pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                RegisterDpiScaler(scale => pathPanel.Margin = UITheme.Scale(new Padding(0, 8, 0, 4), scale));

                var lblPath = new Label
                {
                    Text = "Путь на диске:",
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                RegisterDpiScaler(scale => lblPath.Margin = UITheme.Scale(new Padding(0, 0, 8, 0), scale));

                _txtPath = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Font = UITheme.BaseFont,
                    PlaceholderText = "Например: Музыка или Сборник/Рок"
                };
                _txtPath.TextChanged += (s, e) => UpdateStatusHint();

                pathPanel.Controls.Add(lblPath, 0, 0);
                pathPanel.Controls.Add(_txtPath, 1, 0);

                var optsPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false
                };
                RegisterDpiScaler(scale => optsPanel.Margin = UITheme.Scale(new Padding(0, 4, 0, 4), scale));

                _chkPreview = UIFactory.CreateCheckBox(
                    "Включить фото-превью для галереи Telegram (для этой папки и подпапок)",
                    isChecked: false,
                    registerScaler: RegisterDpiScaler);

                _chkCaptions = UIFactory.CreateCheckBox(
                    "Включить текстовые подписи в Telegram (для этой папки и подпапок)",
                    isChecked: false,
                    registerScaler: RegisterDpiScaler);

                optsPanel.Controls.Add(_chkPreview);
                optsPanel.Controls.Add(_chkCaptions);

                _lblStatus = new Label
                {
                    AutoSize = true,
                    Font = UITheme.SmallFont,
                    ForeColor = UITheme.TextMuted
                };

                void UpdateWrapWidths()
                {
                    int availW = root.ClientSize.Width > 40
                        ? root.ClientSize.Width - root.Padding.Horizontal - UITheme.Scale(6, _currentDpiScale)
                        : UITheme.Scale(BaseClientWidth - 40, _currentDpiScale);
                    availW = Math.Max(UITheme.Scale(200, _currentDpiScale), availW);
                    int checkBoxMaxW = Math.Max(UITheme.Scale(180, _currentDpiScale), availW - UITheme.Scale(24, _currentDpiScale));

                    lblPrompt.MaximumSize = new Size(availW, 0);
                    _lblStatus.MaximumSize = new Size(availW, 0);
                    _chkPreview.MaximumSize = new Size(checkBoxMaxW, 0);
                    _chkCaptions.MaximumSize = new Size(checkBoxMaxW, 0);
                }

                root.SizeChanged += (s, e) => UpdateWrapWidths();
                RegisterDpiScaler(scale =>
                {
                    lblPrompt.Margin = UITheme.Scale(new Padding(0, 0, 0, 6), scale);
                    _lblStatus.Margin = UITheme.Scale(new Padding(0, 2, 0, 8), scale);
                    UpdateWrapWidths();
                });

                var bottomBtns = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    ColumnCount = 4,
                    RowCount = 1
                };
                bottomBtns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                bottomBtns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                bottomBtns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                bottomBtns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                _btnNewSubfolder = UIFactory.CreateButton(
                    "Новая папка",
                    (s, e) => CreateSubfolderInTree(),
                    width: 115,
                    registerScaler: RegisterDpiScaler);

                _btnCreateBind = UIFactory.CreateButton(
                    "Привязать",
                    async (s, e) => await CreateAndBindAsync(),
                    width: 115,
                    registerScaler: RegisterDpiScaler);

                _btnCancel = UIFactory.CreateButton(
                    "Отмена",
                    width: 115,
                    registerScaler: RegisterDpiScaler);
                _btnCancel.DialogResult = DialogResult.Cancel;

                RegisterDpiScaler(scale =>
                {
                    _btnNewSubfolder.Margin = UITheme.Scale(new Padding(0, 0, 8, 0), scale);
                    _btnCreateBind.Margin = Padding.Empty;
                    _btnCancel.Margin = Padding.Empty;
                });

                bottomBtns.Controls.Add(_btnNewSubfolder, 0, 0);
                bottomBtns.Controls.Add(_btnCreateBind, 1, 0);
                bottomBtns.Controls.Add(_btnCancel, 3, 0);

                root.Controls.Add(lblPrompt, 0, 0);
                root.Controls.Add(_tree, 0, 1);
                root.Controls.Add(pathPanel, 0, 2);
                root.Controls.Add(optsPanel, 0, 3);
                root.Controls.Add(_lblStatus, 0, 4);
                root.Controls.Add(bottomBtns, 0, 5);

                Controls.Add(root);
                AcceptButton = _btnCreateBind;
                CancelButton = _btnCancel;

                ApplyDpiScale(GetDpiScale());
            }

            private void ReloadTree(string? selectPath)
            {
                _folderItems = _repository.GetActiveFolderTree();
                _suppressTreeSync = true;
                try
                {
                    PopulateFolderTree(
                        _tree,
                        _folderItems,
                        info => info.IsCustomBoundChannel
                            ? (BoundSuffixTag, null)
                            : (string.Empty, null),
                        selectPath);
                }
                finally
                {
                    _suppressTreeSync = false;
                }

                if (_tree.SelectedNode?.Tag is FolderTreeNodeInfo selectedInfo)
                {
                    _txtPath.Text = selectedInfo.FullPath == "/" ? string.Empty : selectedInfo.FullPath.TrimStart('/');
                }
                UpdateStatusHint();
            }

            private void CreateSubfolderInTree()
            {
                string parentPath = "/";
                if (_tree.SelectedNode?.Tag is FolderTreeNodeInfo info)
                {
                    parentPath = info.FullPath;
                }

                string? newName = PromptNewSubfolderName(this, parentPath);
                if (string.IsNullOrEmpty(newName)) return;

                string targetPath = parentPath == "/" ? "/" + newName : parentPath.TrimEnd('/') + "/" + newName;
                var created = _repository.EnsureDirectoryPathExists(targetPath);
                if (created == null)
                {
                    MessageBox.Show(this, $"Не удалось создать папку '{targetPath}'.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ReloadTree(targetPath);
            }

            private void UpdateStatusHint()
            {
                string raw = _txtPath.Text.Trim();
                if (string.IsNullOrEmpty(raw) || raw == "/" || raw == "\\")
                {
                    _lblStatus.ForeColor = UITheme.TextMuted;
                    _lblStatus.Text = "Выберите существующую папку в дереве или введите имя новой папки (например, Музыка).";
                    return;
                }

                if (!TryNormalizeVirtualFolderPath(raw, allowRoot: false, out string normalized, out string err))
                {
                    _lblStatus.ForeColor = UITheme.TextDanger;
                    _lblStatus.Text = err;
                    return;
                }

                string displayPath = normalized.TrimStart('/');
                var existing = _folderItems.FirstOrDefault(f => string.Equals(f.FullPath, normalized, StringComparison.OrdinalIgnoreCase));
                if (existing != null && existing.IsCustomBoundChannel)
                {
                    _lblStatus.ForeColor = UITheme.TextDanger;
                    _lblStatus.Text = $"Папка «{displayPath}» уже привязана к каналу «{existing.ChannelTitle ?? existing.TgChannelId?.ToString()}». Введите имя подпапки или выберите другую папку.";
                }
                else if (existing != null)
                {
                    _lblStatus.ForeColor = UITheme.TextMuted;
                    _lblStatus.Text = $"Будет создан новый приватный канал «{existing.Name}» и запущена фоновая миграция существующих файлов.";
                }
                else
                {
                    _lblStatus.ForeColor = UITheme.TextMuted;
                    _lblStatus.Text = $"Папка «{displayPath}» будет создана на диске и привязана к новому каналу Telegram.";
                }
            }

            private async Task CreateAndBindAsync()
            {
                if (!TryNormalizeVirtualFolderPath(_txtPath.Text, allowRoot: false, out string normalized, out string err))
                {
                    MessageBox.Show(this, err, "Некорректный путь", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!_telegramService.IsAuthorized)
                {
                    MessageBox.Show(this, "Служба Telegram не авторизована.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var existing = _repository.GetNodeByPath(normalized);
                if (existing != null && !existing.IsDir)
                {
                    MessageBox.Show(this, $"По пути '{normalized}' уже находится файл.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                long primaryId = _repository.GetPrimaryTelegramChannel()?.ChannelId ?? 0;
                if (existing != null && existing.TgChannelId.HasValue && existing.TgChannelId.Value != 0 && (primaryId == 0 || existing.TgChannelId.Value != primaryId))
                {
                    var currentCh = _repository.GetTelegramChannel(existing.TgChannelId.Value);
                    MessageBox.Show(
                        this,
                        $"Папка '{existing.Name}' уже привязана к каналу Telegram: '{currentCh?.Title ?? existing.TgChannelId.Value.ToString()}'.",
                        "Уже привязана",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                SetBusy(true);
                try
                {
                    var folderNode = _repository.EnsureDirectoryPathExists(normalized);
                    if (folderNode == null)
                    {
                        MessageBox.Show(this, $"Не удалось создать папку '{normalized}'.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var newChannel = await _telegramService.CreateCustomChannelAsync(folderNode.Name);
                    if (newChannel == null)
                    {
                        MessageBox.Show(this, $"Не удалось создать канал Telegram для папки '{folderNode.Name}'.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    if (_chkPreview.Checked)
                    {
                        _repository.SetFolderPhotoPreviewMode(folderNode.Id, 1);
                    }
                    if (_chkCaptions.Checked)
                    {
                        _repository.SetFolderCaptionMode(folderNode.Id, 1);
                    }

                    await _telegramService.MigrateSubtreeToChannelAsync(folderNode.Id, newChannel.ChannelId);

                    string exactRelPath = _repository.GetNodeFullPath(folderNode.Id);
                    if (string.IsNullOrWhiteSpace(exactRelPath) || exactRelPath == "/")
                    {
                        exactRelPath = normalized;
                    }

                    string? driveLetter = _virtualDriveManager?.MountedLetter ?? _configManager.CurrentSettings.Server.DriveLetter;
                    BoundFolderShellHelper.ApplyBoundFolderAttributesThroughVfs(null, exactRelPath, driveLetter);
                    _virtualDriveManager?.NotifyBoundFolderUpdated(exactRelPath);
                    BoundFolderShellHelper.NotifyExplorerFolderIconChanged(null, exactRelPath, driveLetter);

                    DialogResult = DialogResult.OK;
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Ошибка при привязке папки:\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    if (!IsDisposed)
                    {
                        SetBusy(false);
                    }
                }
            }

            private void SetBusy(bool busy)
            {
                _tree.Enabled = !busy;
                _txtPath.Enabled = !busy;
                _chkPreview.Enabled = !busy;
                _chkCaptions.Enabled = !busy;
                _btnNewSubfolder.Enabled = !busy;
                _btnCreateBind.Enabled = !busy;
                _btnCancel.Enabled = !busy;
                _btnCreateBind.Text = busy ? "Привязка..." : "Привязать";
                UseWaitCursor = busy;
            }
        }
    }
}
