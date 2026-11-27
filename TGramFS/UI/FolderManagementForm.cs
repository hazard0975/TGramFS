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

                int targetW = UITheme.Scale(BaseClientWidth, dpiScale);
                int targetH = UITheme.Scale(BaseClientHeight, dpiScale);
                Rectangle workingArea = IsHandleCreated
                    ? Screen.FromHandle(Handle).WorkingArea
                    : Screen.FromPoint(Cursor.Position).WorkingArea;

                Size nonClient = Size - ClientSize;
                int maxW = Math.Max(420, workingArea.Width - Math.Max(0, nonClient.Width));
                int maxH = Math.Max(360, workingArea.Height - Math.Max(0, nonClient.Height));

                MinimumSize = new Size(
                    UITheme.Scale(520, dpiScale),
                    UITheme.Scale(420, dpiScale));
                ClientSize = new Size(Math.Min(targetW, maxW), Math.Min(targetH, maxH));
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
            RegisterDpiScaler(scale =>
            {
                lblHint.Margin = UITheme.Scale(new Padding(0, 0, 0, 8), scale);
                lblHint.MaximumSize = new Size(UITheme.Scale(BaseClientWidth - 60, scale), 0);
            });

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
            RegisterDpiScaler(scale => _lblBoundEmptyHint.Margin = UITheme.Scale(new Padding(0, 6, 0, 2), scale));

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
                isPrimary: true,
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
            RegisterDpiScaler(scale =>
            {
                lblHint.Margin = UITheme.Scale(new Padding(0, 0, 0, 8), scale);
                lblHint.MaximumSize = new Size(UITheme.Scale(BaseClientWidth - 60, scale), 0);
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
                errorMessage = "Укажите имя или путь папки на виртуальном диске (например, /Музыка).";
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
            using var form = new Form
            {
                Text = "Новая папка",
                Icon = AppIconHelper.GetAppIcon(),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                Font = UITheme.BaseFont,
                ClientSize = new Size(380, 125)
            };

            var lbl = new Label
            {
                Text = $"Создать папку внутри «{parentPath}»:",
                AutoSize = true,
                Location = new Point(14, 14),
                MaximumSize = new Size(350, 0)
            };

            var txt = new TextBox
            {
                Location = new Point(14, 42),
                Width = 350
            };

            var btnOk = new Button
            {
                Text = "Создать",
                DialogResult = DialogResult.OK,
                Location = new Point(204, 82),
                Size = new Size(76, 28)
            };

            var btnCancel = new Button
            {
                Text = "Отмена",
                DialogResult = DialogResult.Cancel,
                Location = new Point(288, 82),
                Size = new Size(76, 28)
            };

            form.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCancel });
            form.AcceptButton = btnOk;
            form.CancelButton = btnCancel;

            while (form.ShowDialog(owner) == DialogResult.OK)
            {
                string name = txt.Text.Trim();
                if (string.IsNullOrWhiteSpace(name) ||
                    name == "." ||
                    name == ".." ||
                    name.Equals(".Trash", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains('/') ||
                    name.Contains('\\') ||
                    name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    MessageBox.Show(form, "Введите корректное имя папки без слэшей и спецсимволов.", "Некорректное имя", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }
                return name;
            }

            return null;
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
            private readonly NodeRepository _repository;
            private readonly bool _isPreviewRule;
            private readonly int _targetMode;

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

            private void InitializeDialog()
            {
                string ruleName = _isPreviewRule ? "фото-превью" : "подписей в Telegram";
                string actionName = _targetMode == 1 ? "Разрешить" : "Добавить исключение";
                Text = $"{actionName} — {ruleName}";
                Icon = AppIconHelper.GetAppIcon();
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.Sizable;
                MaximizeBox = false;
                MinimizeBox = false;
                Font = UITheme.BaseFont;
                ClientSize = new Size(490, 420);
                MinimumSize = new Size(420, 340);

                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 5,
                    Padding = new Padding(12)
                };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var topBar = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    ColumnCount = 2,
                    RowCount = 1,
                    Margin = new Padding(0, 0, 0, 6)
                };
                topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                var lblPrompt = new Label
                {
                    Text = "Выберите папку из базы или введите путь (несуществующая папка будет создана):",
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                var btnNewSubfolder = new Button
                {
                    Text = "+ Новая папка",
                    AutoSize = true,
                    Height = 26,
                    Padding = new Padding(8, 2, 8, 2)
                };
                btnNewSubfolder.Click += (s, e) => CreateSubfolderInTree();

                topBar.Controls.Add(lblPrompt, 0, 0);
                topBar.Controls.Add(btnNewSubfolder, 1, 0);

                _tree = new TreeView
                {
                    Dock = DockStyle.Fill,
                    HideSelection = false,
                    Font = UITheme.BaseFont
                };
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
                    RowCount = 1,
                    Margin = new Padding(0, 8, 0, 4)
                };
                pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

                var lblPath = new Label
                {
                    Text = "Путь папки:",
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(0, 0, 8, 0)
                };
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
                    ForeColor = UITheme.TextMuted,
                    Margin = new Padding(0, 2, 0, 8),
                    MaximumSize = new Size(450, 0)
                };

                var bottomBtns = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    FlowDirection = FlowDirection.RightToLeft,
                    WrapContents = false
                };

                var btnCancel = new Button { Text = "Отмена", Size = new Size(96, 28), DialogResult = DialogResult.Cancel };
                var btnApply = new Button { Text = "Применить", Size = new Size(106, 28), Font = UITheme.BoldFont, Margin = new Padding(0, 0, 8, 0) };
                btnApply.Click += (s, e) => ApplyAndClose();

                bottomBtns.Controls.Add(btnCancel);
                bottomBtns.Controls.Add(btnApply);

                root.Controls.Add(topBar, 0, 0);
                root.Controls.Add(_tree, 0, 1);
                root.Controls.Add(pathPanel, 0, 2);
                root.Controls.Add(_lblStatus, 0, 3);
                root.Controls.Add(bottomBtns, 0, 4);

                Controls.Add(root);
                AcceptButton = btnApply;
                CancelButton = btnCancel;
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

                            var tags = new List<string>();
                            Color? color = null;

                            if (explicitMode == 1)
                            {
                                tags.Add("[Вкл]");
                                color = UITheme.TextSuccess;
                            }
                            else if (explicitMode == 0)
                            {
                                tags.Add("[Исключение]");
                                color = UITheme.TextDanger;
                            }
                            else if (effective)
                            {
                                tags.Add("(насл.: Вкл)");
                                color = Color.FromArgb(40, 125, 85);
                            }

                            if (info.IsCustomBoundChannel)
                            {
                                tags.Add("[канал]");
                            }

                            return (string.Join(" ", tags), color);
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
            private readonly ConfigManager _configManager;
            private readonly NodeRepository _repository;
            private readonly TelegramService _telegramService;
            private readonly VirtualDriveManager? _virtualDriveManager;

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

            private void InitializeDialog()
            {
                Text = "Создать / привязать папку к каналу";
                Icon = AppIconHelper.GetAppIcon();
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.Sizable;
                MaximizeBox = false;
                MinimizeBox = false;
                Font = UITheme.BaseFont;
                ClientSize = new Size(510, 470);
                MinimumSize = new Size(440, 390);

                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 6,
                    Padding = new Padding(12)
                };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var topBar = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    ColumnCount = 2,
                    RowCount = 1,
                    Margin = new Padding(0, 0, 0, 6)
                };
                topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
                topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                var lblPrompt = new Label
                {
                    Text = "Выберите существующую папку или введите путь новой папки (например, /Музыка):",
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _btnNewSubfolder = new Button
                {
                    Text = "+ Новая папка",
                    AutoSize = true,
                    Height = 26,
                    Padding = new Padding(8, 2, 8, 2)
                };
                _btnNewSubfolder.Click += (s, e) => CreateSubfolderInTree();

                topBar.Controls.Add(lblPrompt, 0, 0);
                topBar.Controls.Add(_btnNewSubfolder, 1, 0);

                _tree = new TreeView
                {
                    Dock = DockStyle.Fill,
                    HideSelection = false,
                    Font = UITheme.BaseFont
                };
                _tree.AfterSelect += (s, e) =>
                {
                    if (_suppressTreeSync) return;
                    if (e.Node?.Tag is FolderTreeNodeInfo info)
                    {
                        _txtPath.Text = info.FullPath == "/" ? "/" : info.FullPath;
                        UpdateStatusHint();
                    }
                };

                var pathPanel = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    ColumnCount = 2,
                    RowCount = 1,
                    Margin = new Padding(0, 8, 0, 4)
                };
                pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

                var lblPath = new Label
                {
                    Text = "Путь на диске:",
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(0, 0, 8, 0)
                };
                _txtPath = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Font = UITheme.BaseFont
                };
                _txtPath.TextChanged += (s, e) => UpdateStatusHint();

                pathPanel.Controls.Add(lblPath, 0, 0);
                pathPanel.Controls.Add(_txtPath, 1, 0);

                var optsPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    FlowDirection = FlowDirection.TopDown,
                    WrapContents = false,
                    Margin = new Padding(0, 4, 0, 4)
                };
                _chkPreview = new CheckBox
                {
                    Text = "Включить фото-превью для галереи Telegram (для этой папки и подпапок)",
                    AutoSize = true,
                    Margin = new Padding(0, 2, 0, 4)
                };
                _chkCaptions = new CheckBox
                {
                    Text = "Включить текстовые подписи в Telegram (для этой папки и подпапок)",
                    AutoSize = true,
                    Margin = new Padding(0, 2, 0, 2)
                };
                optsPanel.Controls.Add(_chkPreview);
                optsPanel.Controls.Add(_chkCaptions);

                _lblStatus = new Label
                {
                    AutoSize = true,
                    Font = UITheme.SmallFont,
                    ForeColor = UITheme.TextMuted,
                    Margin = new Padding(0, 2, 0, 8),
                    MaximumSize = new Size(470, 0)
                };

                var bottomBtns = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    FlowDirection = FlowDirection.RightToLeft,
                    WrapContents = false
                };

                _btnCancel = new Button { Text = "Отмена", Size = new Size(96, 28), DialogResult = DialogResult.Cancel };
                _btnCreateBind = new Button { Text = "Создать и привязать", Size = new Size(155, 28), Font = UITheme.BoldFont, Margin = new Padding(0, 0, 8, 0) };
                _btnCreateBind.Click += async (s, e) => await CreateAndBindAsync();

                bottomBtns.Controls.Add(_btnCancel);
                bottomBtns.Controls.Add(_btnCreateBind);

                root.Controls.Add(topBar, 0, 0);
                root.Controls.Add(_tree, 0, 1);
                root.Controls.Add(pathPanel, 0, 2);
                root.Controls.Add(optsPanel, 0, 3);
                root.Controls.Add(_lblStatus, 0, 4);
                root.Controls.Add(bottomBtns, 0, 5);

                Controls.Add(root);
                AcceptButton = _btnCreateBind;
                CancelButton = _btnCancel;
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
                            ? ($"[канал: {info.ChannelTitle ?? info.TgChannelId?.ToString()}]", UITheme.TextMuted)
                            : (string.Empty, null),
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
                if (!TryNormalizeVirtualFolderPath(_txtPath.Text, allowRoot: false, out string normalized, out string err))
                {
                    _lblStatus.ForeColor = UITheme.TextDanger;
                    _lblStatus.Text = err;
                    return;
                }

                var existing = _folderItems.FirstOrDefault(f => string.Equals(f.FullPath, normalized, StringComparison.OrdinalIgnoreCase));
                if (existing != null && existing.IsCustomBoundChannel)
                {
                    _lblStatus.ForeColor = UITheme.TextDanger;
                    _lblStatus.Text = $"Папка «{normalized}» уже привязана к каналу «{existing.ChannelTitle ?? existing.TgChannelId?.ToString()}». Введите имя подпапки или выберите другую папку.";
                }
                else if (existing != null)
                {
                    _lblStatus.ForeColor = UITheme.TextMuted;
                    _lblStatus.Text = $"Будет создан новый приватный канал «{existing.Name}» и запущена фоновая миграция существующих файлов.";
                }
                else
                {
                    _lblStatus.ForeColor = UITheme.TextMuted;
                    _lblStatus.Text = $"Папка «{normalized}» будет создана на диске и привязана к новому каналу Telegram.";
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
                _btnCreateBind.Text = busy ? "Создание канала..." : "Создать и привязать";
                UseWaitCursor = busy;
            }
        }
    }
}
