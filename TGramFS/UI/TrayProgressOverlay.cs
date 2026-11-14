using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using TGramFS.Services;

namespace TGramFS.UI
{
    public enum TransferDirection
    {
        Upload,
        Download
    }

    /// <summary>
    /// Интерактивный всплывающий HUD-виджет для отображения живого прогресса передачи данных
    /// (выгрузка в Telegram или скачивание/кэширование из Telegram) при активности или наведении на трей.
    /// </summary>
    public class TrayProgressOverlay : Form
    {
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_TOPMOST = 0x00000008;

        #region Win32 Fullscreen Detection

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE pquns);

        private enum QUERY_USER_NOTIFICATION_STATE
        {
            QUNS_NOT_PRESENT = 1,
            QUNS_BUSY = 2,
            QUNS_RUNNING_D3D_FULL_SCREEN = 3,
            QUNS_PRESENTATION_MODE = 4,
            QUNS_ACCEPTS_NOTIFICATIONS = 5,
            QUNS_QUIET_TIME = 6,
            QUNS_APP = 7
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// Проверяет, активно ли полноэкранное приложение (просмотр фильма, игра, презентация).
        /// В полноэкранном режиме оверлей подавляется и не всплывает поверх видео.
        /// </summary>
        public static bool IsFullScreenAppRunning()
        {
            try
            {
                // 1. Проверка через Shell API (игры, медиаплееры в полноэкранном режиме, презентации)
                if (SHQueryUserNotificationState(out var state) == 0) // S_OK
                {
                    if (state == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN ||
                        state == QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE ||
                        state == QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY)
                    {
                        return true;
                    }
                }

                // 2. Геометрическая проверка переднего активного окна (браузеры в режиме F11, плееры VLC, MPC-HC, PotPlayer)
                var foreground = GetForegroundWindow();
                if (foreground != IntPtr.Zero)
                {
                    var sb = new StringBuilder(256);
                    GetClassName(foreground, sb, 256);
                    string className = sb.ToString();

                    // Исключаем рабочий стол и панель задач Windows
                    if (className is not "Progman" and not "WorkerW" and not "Shell_TrayWnd")
                    {
                        if (GetWindowRect(foreground, out var rect))
                        {
                            var screen = Screen.FromHandle(foreground);
                            if (screen != null)
                            {
                                // Окно развернуто на весь экран без границ (покрывает монитор целиком)
                                if (rect.Left <= screen.Bounds.Left &&
                                    rect.Top <= screen.Bounds.Top &&
                                    rect.Right >= screen.Bounds.Right &&
                                    rect.Bottom >= screen.Bounds.Bottom)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // При ошибках системных вызовов не прерываем работу UI
            }

            return false;
        }

        #endregion

        private readonly System.Windows.Forms.Timer _updateTimer;
        private readonly System.Windows.Forms.Timer _hideCheckTimer;
        private readonly System.Windows.Forms.Timer _completionTimer;
        private readonly SynchronizationContext? _syncContext;

        public bool AutoShowOverlay { get; set; } = true;

        private TransferDirection _direction = TransferDirection.Upload;
        private string _currentFileName = string.Empty;
        private long _currentBytes = 0;
        private long _totalBytes = 0;
        private int _queueCount = 0;
        private bool _isTransferring = false;
        private bool _isFinalizing = false;
        private bool _isCompleted = false;
        private bool _isMetadata = false;
        private DateTime _lastSpeedCalcTime = DateTime.UtcNow;
        private long _lastSpeedBytes = 0;
        private double _bytesPerSecond = 0;
        private DateTime _lastProgressUpdateTime = DateTime.UtcNow;
        private string? _lastMetadataCompletedFile;
        private DateTime _marqueeStartTime = DateTime.UtcNow;
        private string _marqueeLastFileName = string.Empty;

        private const int BaseWidth = 396;
        private const int BaseHeight = 96;
        private const int BasePaddingX = 12;
        private const int BaseTitleY = 10;
        private const int BaseNameY = 28;
        private const int BaseBarY = 52;
        private const int BaseBarHeight = 8;
        private const int BaseStatsY = 68;
        private const int BaseCornerRadius = 8;
        private const int BaseBarCornerRadius = 4;
        private const int BaseMinFillWidth = 8;
        private const int BaseScreenMargin = 16;
        private const float BaseScrollSpeedPxPerSec = 32f;
        private const float BaseMarqueeEndGap = 8f;

        private float _currentDpiScale = 1.0f;

        public TrayProgressOverlay()
        {
            _syncContext = SynchronizationContext.Current;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            UpdateStyles();

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(30, 41, 59); // Slate 800

            // Принудительно создаем дескриптор Win32 HWND и сразу масштабируем форму под текущий DPI
            if (!IsHandleCreated)
            {
                CreateHandle();
            }
            ApplyDpiScale(GetDpiScale());

            _updateTimer = new System.Windows.Forms.Timer { Interval = 35 };
            _updateTimer.Tick += (s, e) => Invalidate();

            _hideCheckTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _hideCheckTimer.Tick += HideCheckTimer_Tick;

            _completionTimer = new System.Windows.Forms.Timer { Interval = 1400 };
            _completionTimer.Tick += (s, e) =>
            {
                AppLogger.Info("TrayProgressOverlay", $"_completionTimer Tick! _isTransferring={_isTransferring}, _isCompleted={_isCompleted}, Visible={Visible}");
                _completionTimer.Stop();
                if (!_isTransferring)
                {
                    _updateTimer.Stop();
                    _hideCheckTimer.Stop();
                    _isCompleted = false;
                    _isFinalizing = false;
                    _isMetadata = false;
                    _currentFileName = string.Empty;
                    Hide();
                    AppLogger.Info("TrayProgressOverlay", "Оверлей успешно скрыт по завершению таймаута.");
                }
            };
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Подавляем системную заливку фона Windows, чтобы не было черного экрана
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        /// <summary>
        /// Попытка показать оверлей с проверкой отсутствия активного полноэкранного режима (фильм, игра)
        /// </summary>
        private bool TryShowOverlay()
        {
            if (IsFullScreenAppRunning())
            {
                // Полноэкранный режим: подавляем появление оверлея
                return false;
            }

            if (!Visible)
            {
                PositionNearTray();
                Show();
                EnsureTopMost();
                _updateTimer.Start();
                _hideCheckTimer.Start();
            }

            return true;
        }

        private void EnsureTopMost()
        {
            if (IsHandleCreated)
            {
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }

        public void UpdateProgress(string fileName, long current, long total, TransferDirection direction = TransferDirection.Upload, int queueCount = 0)
        {
            if (_syncContext != null && SynchronizationContext.Current != _syncContext)
            {
                _syncContext.Post(_ => UpdateProgress(fileName, current, total, direction, queueCount), null);
                return;
            }

            // Если для этого файла передача уже была завершена на 100%, игнорируем любые остаточные/запоздалые обновления
            // только пока активен таймер завершения (_completionTimer.Enabled), чтобы не сбивать финальную плашку
            if (_isCompleted && _currentFileName == fileName && _direction == direction)
            {
                if (_completionTimer.Enabled)
                {
                    return;
                }
                _isCompleted = false;
            }

            _queueCount = queueCount;

            // Отменяем любой таймер закрытия от предыдущих файлов
            if (_completionTimer.Enabled)
            {
                AppLogger.Info("TrayProgressOverlay", $"Остановлен активный _completionTimer для файла {fileName}");
                _completionTimer.Stop();
            }

            _direction = direction;
            _isMetadata = false;

            // При смене файла сбрасываем счетчик
            if (_currentFileName != fileName)
            {
                _currentFileName = fileName;
                _marqueeLastFileName = fileName;
                _marqueeStartTime = DateTime.UtcNow;
                _lastMetadataCompletedFile = null;
                _currentBytes = current;
                _lastSpeedBytes = 0;
                _lastSpeedCalcTime = DateTime.UtcNow;
                _bytesPerSecond = 0;
                _isCompleted = false;
            }
            else
            {
                // Если произошел резкий откат назад (перемотка видео в плеере назад или старт заново)
                if (current < _currentBytes - 2097152L || (current < _currentBytes && _currentBytes >= total))
                {
                    _currentBytes = current;
                    _lastSpeedBytes = current;
                    _lastSpeedCalcTime = DateTime.UtcNow;
                    _bytesPerSecond = 0;
                }
                else
                {
                    _currentBytes = Math.Max(_currentBytes, current);
                }
            }
            _totalBytes = total;
            _isTransferring = true;
            _isCompleted = false;
            _lastProgressUpdateTime = DateTime.UtcNow;

            long metadataLimit = 1572864;
            if (_direction == TransferDirection.Upload && total > metadataLimit && current >= total)
            {
                _isFinalizing = true;
                _bytesPerSecond = 0;
            }
            else if (_direction == TransferDirection.Download && total > 0 && current >= total)
            {
                _isFinalizing = false;
                _bytesPerSecond = 0;
            }
            else
            {
                _isFinalizing = false;
                var now = DateTime.UtcNow;
                var elapsed = (now - _lastSpeedCalcTime).TotalSeconds;
                if (elapsed >= 0.5)
                {
                    long bytesDiff = current - _lastSpeedBytes;
                    if (bytesDiff >= 0)
                    {
                        double instantSpeed = bytesDiff / elapsed;
                        _bytesPerSecond = _bytesPerSecond == 0 ? instantSpeed : (_bytesPerSecond * 0.7 + instantSpeed * 0.3);
                    }
                    _lastSpeedBytes = current;
                    _lastSpeedCalcTime = now;
                }
            }

            // Если окно скрыто — позиционируем и показываем (если включено в настройках и нет полноэкранного режима)
            if (!Visible)
            {
                if (!AutoShowOverlay || !TryShowOverlay())
                {
                    return;
                }
                AppLogger.Info("TrayProgressOverlay", $"Показ окна прогресса [{_direction}] для {fileName} ({current}/{total})");
            }

            Invalidate();
            Update(); // Принудительно запускаем перерисовку очереди WM_PAINT
        }

        public void UpdateMetadataProgress(string fileName, long current, long total)
        {
            if (_syncContext != null && SynchronizationContext.Current != _syncContext)
            {
                _syncContext.Post(_ => UpdateMetadataProgress(fileName, current, total), null);
                return;
            }

            // Если для этого файла передача уже завершилась на 100%, игнорируем любые обновления
            if (_isCompleted && _currentFileName == fileName)
            {
                return;
            }

            if (_completionTimer.Enabled)
            {
                _completionTimer.Stop();
            }

            if (_currentFileName != fileName)
            {
                _currentFileName = fileName;
                _lastMetadataCompletedFile = null;
                _isCompleted = false;
            }

            _direction = TransferDirection.Download;
            _isMetadata = true;
            _currentBytes = current;
            _totalBytes = total;
            _isTransferring = true;
            _isCompleted = false;
            _isFinalizing = false;
            _lastProgressUpdateTime = DateTime.UtcNow;

            Invalidate();
            Update();
        }

        public void UpdateChunkProgress(string fileName, long current, long total) => UpdateMetadataProgress(fileName, current, total);

        public void CompleteMetadata(string fileName)
        {
            if (_syncContext != null && SynchronizationContext.Current != _syncContext)
            {
                _syncContext.Post(_ => CompleteMetadata(fileName), null);
                return;
            }

            // Защита от повторного показа плашки для одного и того же файла
            if (!string.IsNullOrEmpty(fileName) && _lastMetadataCompletedFile == fileName)
            {
                return;
            }
            _lastMetadataCompletedFile = fileName;

            AppLogger.Info("TrayProgressOverlay", $"CompleteMetadata вызван для '{fileName}'. Показ оверлея 'МЕТАДАННЫЕ ПОЛУЧЕНЫ'...");
            _direction = TransferDirection.Download;
            _isTransferring = false;
            _isFinalizing = false;
            _isMetadata = true;
            _isCompleted = true;
            _currentFileName = fileName;
            _bytesPerSecond = 0;

            if (!Visible)
            {
                if (!AutoShowOverlay || !TryShowOverlay())
                {
                    return;
                }
            }

            Invalidate();
            Update();

            _completionTimer.Stop();
            _completionTimer.Start();
        }

        public void CompleteTransfer(string fileName, TransferDirection direction = TransferDirection.Upload)
        {
            if (_syncContext != null && SynchronizationContext.Current != _syncContext)
            {
                _syncContext.Post(_ => CompleteTransfer(fileName, direction), null);
                return;
            }

            AppLogger.Info("TrayProgressOverlay", $"CompleteTransfer [{direction}] вызван для '{fileName}'. Запуск _completionTimer...");
            _currentFileName = fileName;
            _direction = direction;
            _isTransferring = false;
            _isFinalizing = false;
            _isMetadata = false; // При полном скачивании файла сбрасываем флаг метаданных
            _isCompleted = true;
            if (_totalBytes > 0)
            {
                _currentBytes = _totalBytes;
            }
            _bytesPerSecond = 0;

            if (!Visible)
            {
                if (!AutoShowOverlay || !TryShowOverlay())
                {
                    _isCompleted = false;
                    _currentFileName = string.Empty;
                    return;
                }
            }

            Invalidate();
            Update();

            _completionTimer.Stop();
            _completionTimer.Start();
        }

        // Для обратной совместимости
        public void CompleteUpload(string fileName) => CompleteTransfer(fileName, TransferDirection.Upload);

        public void CancelTransfer(string fileName, TransferDirection direction = TransferDirection.Upload)
        {
            if (_syncContext != null && SynchronizationContext.Current != _syncContext)
            {
                _syncContext.Post(_ => CancelTransfer(fileName, direction), null);
                return;
            }

            AppLogger.Info("TrayProgressOverlay", $"CancelTransfer [{direction}] вызван для '{fileName}'. Сброс состояния и скрытие оверлея.");
            _isTransferring = false;
            _isCompleted = false;
            _isFinalizing = false;
            _isMetadata = false;
            _currentFileName = string.Empty;
            _currentBytes = 0;
            _totalBytes = 0;
            _bytesPerSecond = 0;

            _completionTimer.Stop();
            _updateTimer.Stop();
            _hideCheckTimer.Stop();

            Visible = false;
            Invalidate();
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

        private static int ScalePx(int basePx, float dpiScale) => Math.Max(1, (int)Math.Round(basePx * dpiScale));

        private static float ScalePxF(float basePx, float dpiScale) => basePx * dpiScale;

        private void ApplyDpiScale(float dpiScale)
        {
            if (dpiScale <= 0f) dpiScale = 1.0f;
            _currentDpiScale = dpiScale;

            int targetWidth = ScalePx(BaseWidth, dpiScale);
            int targetHeight = ScalePx(BaseHeight, dpiScale);
            if (Size.Width != targetWidth || Size.Height != targetHeight)
            {
                Size = new Size(targetWidth, targetHeight);
            }
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            float scale = e.DeviceDpiNew > 0 ? e.DeviceDpiNew / 96f : GetDpiScale();
            ApplyDpiScale(scale);
            if (Visible)
            {
                PositionNearTray();
                EnsureTopMost();
                Invalidate();
            }
        }

        private void PositionNearTray()
        {
            // Автоматическое позиционирование в правом нижнем углу над панелью задач.
            // Сначала масштабируем размер под актуальный DPI, затем вычисляем координаты.
            float dpiScale = GetDpiScale();
            ApplyDpiScale(dpiScale);

            var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(Point.Empty);
            int margin = ScalePx(BaseScreenMargin, dpiScale);
            int x = workingArea.Right - Width - margin;
            int y = workingArea.Bottom - Height - margin;

            Location = new Point(x, y);

            // Если перенос в область рабочего стола вызвал смену монитора/DPI — синхронизируем размер и позицию повторно
            float postMoveScale = GetDpiScale();
            if (Math.Abs(postMoveScale - dpiScale) > 0.01f)
            {
                ApplyDpiScale(postMoveScale);
                margin = ScalePx(BaseScreenMargin, postMoveScale);
                Location = new Point(workingArea.Right - Width - margin, workingArea.Bottom - Height - margin);
            }
        }

        private void HideCheckTimer_Tick(object? sender, EventArgs e)
        {
            if (!Visible) return;

            // Если прямо во время передачи пользователь включил полноэкранный режим (фильм) - скрываем оверлей
            if (IsFullScreenAppRunning())
            {
                Hide();
                return;
            }

            // Защита от зависания окна при обрыве потока: при передаче данных таймаут 4 сек, при финализации в Telegram - до 12 сек
            var secondsSinceProgress = (DateTime.UtcNow - _lastProgressUpdateTime).TotalSeconds;
            double maxAllowedInactivity = _isFinalizing ? 12.0 : 4.0;
            if ((_isTransferring || _isFinalizing) && secondsSinceProgress >= maxAllowedInactivity)
            {
                if (!_completionTimer.Enabled)
                {
                    AppLogger.Info("TrayProgressOverlay", $"Таймаут неактивности передачи ({secondsSinceProgress:F1} сек). Скрытие оверлея.");
                    _isTransferring = false;
                    _isFinalizing = false;
                    _updateTimer.Stop();
                    _hideCheckTimer.Stop();
                    Hide();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            float dpiScale = g.DpiY > 0 ? g.DpiY / 96f : _currentDpiScale;
            if (dpiScale <= 0f) dpiScale = 1.0f;

            float padX = ScalePxF(BasePaddingX, dpiScale);
            float availableWidth = Math.Max(10f, Width - padX * 2f);

            // 1. Скругленный контур и фон
            int cornerRadius = ScalePx(BaseCornerRadius, dpiScale);
            float borderWidth = Math.Max(1f, dpiScale);
            using (var path = GetRoundedRect(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), cornerRadius))
            {
                using (var brush = new SolidBrush(Color.FromArgb(30, 41, 59))) // Slate 800
                {
                    g.FillPath(brush, path);
                }
                using (var pen = new Pen(Color.FromArgb(71, 85, 105), borderWidth)) // Slate 600
                {
                    g.DrawPath(pen, path);
                }
            }

            using var ellipsisFormat = new StringFormat(StringFormat.GenericTypographic)
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            // 2. Заголовок
            using (var titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            using (var titleBrush = new SolidBrush(Color.FromArgb(148, 163, 184))) // Slate 400
            {
                string header;
                if (_isCompleted)
                {
                    if (_isMetadata)
                    {
                        header = "МЕТАДАННЫЕ ПОЛУЧЕНЫ";
                    }
                    else
                    {
                        header = _direction == TransferDirection.Download ? "СКАЧИВАНИЕ ЗАВЕРШЕНО" : "ЗАГРУЗКА ЗАВЕРШЕНА";
                    }
                }
                else if (_queueCount > 1)
                {
                    header = _direction == TransferDirection.Download 
                        ? $"СКАЧИВАНИЕ ИЗ TELEGRAM (в очереди: {_queueCount})" 
                        : $"ОТПРАВКА В TELEGRAM (в очереди: {_queueCount})";
                }
                else if (_isMetadata)
                {
                    header = "МЕТАДАННЫЕ ПОЛУЧЕНЫ";
                }
                else if (_isFinalizing)
                {
                    header = "СОХРАНЕНИЕ В ОБЛАКЕ";
                }
                else if (_isTransferring)
                {
                    header = _direction == TransferDirection.Download ? "СКАЧИВАНИЕ ИЗ TELEGRAM" : "ОТПРАВКА В TELEGRAM";
                }
                else
                {
                    header = "TGRAMFS";
                }

                float titleY = ScalePxF(BaseTitleY, dpiScale);
                float titleHeight = titleFont.GetHeight(g) + ScalePxF(4f, dpiScale);
                g.DrawString(header, titleFont, titleBrush, new RectangleF(padX, titleY, availableWidth, titleHeight), ellipsisFormat);
            }

            // 3. Имя файла с плавной бегущей строкой (Marquee / Ping-Pong)
            using (var nameFont = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (var nameBrush = new SolidBrush(Color.White))
            {
                string displayName = string.IsNullOrEmpty(_currentFileName) ? "Готов к передаче" : _currentFileName;
                float textX = padX;
                float textY = ScalePxF(BaseNameY, dpiScale);

                if (displayName != _marqueeLastFileName)
                {
                    _marqueeLastFileName = displayName;
                    _marqueeStartTime = DateTime.UtcNow;
                }

                // Замеряем точную физическую ширину текста
                var textSize = g.MeasureString(displayName, nameFont, PointF.Empty, StringFormat.GenericTypographic);
                float textWidth = textSize.Width;

                if (textWidth <= availableWidth || _isCompleted)
                {
                    // Текст полностью помещается либо передача завершена — отрисовываем статично (с аккуратным многоточием при переполнении на финише)
                    float nameHeight = nameFont.GetHeight(g) + ScalePxF(4f, dpiScale);
                    g.DrawString(displayName, nameFont, nameBrush, new RectangleF(textX, textY, availableWidth, nameHeight), ellipsisFormat);
                }
                else
                {
                    // Бегущая строка (Marquee Ping-Pong):
                    // 1. Пауза 1.8 сек в начале (читаем имя)
                    // 2. Плавный скролл влево со скоростью 32 px/сек (масштабируется под DPI)
                    // 3. Пауза 1.8 сек в конце (читаем расширение и часть)
                    // 4. Плавный возврат на старт
                    float maxScroll = textWidth - availableWidth + ScalePxF(BaseMarqueeEndGap, dpiScale);
                    float pauseStartMs = 1800f;
                    float scrollSpeed = ScalePxF(BaseScrollSpeedPxPerSec, dpiScale);
                    float scrollDurationMs = (maxScroll / scrollSpeed) * 1000f;
                    float pauseEndMs = 1800f;
                    float returnDurationMs = (maxScroll / (scrollSpeed * 1.5f)) * 1000f;
                    float totalCycleMs = pauseStartMs + scrollDurationMs + pauseEndMs + returnDurationMs;

                    float elapsedMs = (float)(DateTime.UtcNow - _marqueeStartTime).TotalMilliseconds % totalCycleMs;
                    float scrollOffset;

                    if (elapsedMs < pauseStartMs)
                    {
                        scrollOffset = 0f;
                    }
                    else if (elapsedMs < pauseStartMs + scrollDurationMs)
                    {
                        scrollOffset = ((elapsedMs - pauseStartMs) / scrollDurationMs) * maxScroll;
                    }
                    else if (elapsedMs < pauseStartMs + scrollDurationMs + pauseEndMs)
                    {
                        scrollOffset = maxScroll;
                    }
                    else
                    {
                        float returnProgress = (elapsedMs - (pauseStartMs + scrollDurationMs + pauseEndMs)) / returnDurationMs;
                        scrollOffset = maxScroll * (1f - returnProgress);
                    }

                    var oldClip = g.Clip;
                    float clipTopPad = ScalePxF(2f, dpiScale);
                    float clipHeight = Math.Max(ScalePxF(22f, dpiScale), nameFont.GetHeight(g) + ScalePxF(4f, dpiScale));
                    g.SetClip(new RectangleF(textX, textY - clipTopPad, availableWidth, clipHeight));
                    g.DrawString(displayName, nameFont, nameBrush, textX - scrollOffset, textY, StringFormat.GenericTypographic);
                    g.Clip = oldClip;
                }
            }

            // 4. Шкала прогресса
            int barX = (int)Math.Round(padX);
            int barY = ScalePx(BaseBarY, dpiScale);
            int barWidth = Math.Max(10, Width - barX * 2);
            int barHeight = ScalePx(BaseBarHeight, dpiScale);
            int barRadius = Math.Min(barHeight / 2, ScalePx(BaseBarCornerRadius, dpiScale));
            int minFillWidth = Math.Max(ScalePx(BaseMinFillWidth, dpiScale), barRadius * 2);
            int percent = 0;

            if (_isCompleted)
            {
                percent = 100;
            }
            else if (_isMetadata)
            {
                percent = _totalBytes > 0 ? Math.Max(20, (int)Math.Min(100, (_currentBytes * 100) / Math.Min(_totalBytes, 262144))) : 50;
            }
            else if (_totalBytes > 0)
            {
                percent = (int)Math.Min(100, Math.Max(0, (_currentBytes * 100) / _totalBytes));
            }

            using (var barBgPath = GetRoundedRect(new Rectangle(barX, barY, barWidth, barHeight), barRadius))
            {
                using (var bgBrush = new SolidBrush(Color.FromArgb(51, 65, 85))) // Slate 700
                {
                    g.FillPath(bgBrush, barBgPath);
                }
            }

            if (percent > 0)
            {
                int fillWidth = Math.Min(barWidth, Math.Max(minFillWidth, (barWidth * percent) / 100));
                using (var fillPath = GetRoundedRect(new Rectangle(barX, barY, fillWidth, barHeight), barRadius))
                {
                    Color startColor;
                    Color endColor;

                    if (_isMetadata)
                    {
                        startColor = Color.FromArgb(14, 165, 233); // Sky 500
                        endColor = Color.FromArgb(56, 189, 248);   // Sky 400
                    }
                    else if (_direction == TransferDirection.Download)
                    {
                        startColor = Color.FromArgb(245, 158, 11); // Amber 500
                        endColor = Color.FromArgb(251, 191, 36);   // Amber 400
                    }
                    else
                    {
                        startColor = Color.FromArgb(34, 197, 94);  // Green Emerald 500
                        endColor = Color.FromArgb(56, 189, 248);   // Sky 400
                    }

                    using (var fillBrush = new LinearGradientBrush(
                        new Rectangle(barX, barY, barWidth, barHeight),
                        startColor,
                        endColor,
                        0f))
                    {
                        g.FillPath(fillBrush, fillPath);
                    }
                }
            }

            // 5. Детальная статистика (МБ, процент, скорость)
            using (var statsFont = new Font("Segoe UI", 8.25f, FontStyle.Regular))
            using (var statsBrush = new SolidBrush(Color.FromArgb(203, 213, 225))) // Slate 300
            {
                string stats;
                if (_isCompleted)
                {
                    if (_isMetadata)
                    {
                        stats = "Свойства файла успешно получены ✔";
                    }
                    else
                    {
                        stats = _direction == TransferDirection.Download 
                            ? "Файл успешно получен из Telegram ✔" 
                            : "Файл успешно сохранен в Telegram ✔";
                    }
                }
                else if (_isMetadata)
                {
                    stats = $"Чтение свойств и тегов... ({FormatBytes(_currentBytes)})";
                }
                else if (_isFinalizing)
                {
                    stats = "Финализация сообщения в канале... ⏳";
                }
                else if (_isTransferring && _totalBytes > 0)
                {
                    string currStr = FormatBytes(_currentBytes);
                    string totalStr = FormatBytes(_totalBytes);
                    string speedStr = FormatDualSpeed(_bytesPerSecond);
                    string etaStr = FormatEta(_bytesPerSecond, _totalBytes - _currentBytes);
                    stats = $"{currStr} из {totalStr} ({percent}%) • {speedStr} • {etaStr}";
                }
                else if (_isTransferring)
                {
                    string speedStr = FormatDualSpeed(_bytesPerSecond);
                    stats = $"Передано: {FormatBytes(_currentBytes)} • {speedStr}";
                }
                else
                {
                    stats = "Все файлы синхронизированы ✔";
                }

                float statsY = ScalePxF(BaseStatsY, dpiScale);
                float statsHeight = statsFont.GetHeight(g) + ScalePxF(4f, dpiScale);
                g.DrawString(stats, statsFont, statsBrush, new RectangleF(padX, statsY, availableWidth, statsHeight), ellipsisFormat);
            }
        }

        private static string FormatEta(double bytesPerSecond, long remainingBytes)
        {
            if (bytesPerSecond < 1024 || remainingBytes <= 0) return string.Empty;

            double totalSeconds = remainingBytes / bytesPerSecond;
            if (totalSeconds < 0 || double.IsNaN(totalSeconds) || double.IsInfinity(totalSeconds)) return string.Empty;

            long sec = (long)Math.Round(totalSeconds);
            long hours = sec / 3600;
            long minutes = (sec % 3600) / 60;
            long seconds = sec % 60;

            return $"~{hours:D2}:{minutes:D2}:{seconds:D2}";
        }

        private static string FormatDualSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond < 1024) return "вычисление...";

            // 1. Сетевая скорость в битах/с (Мбит/с, кбит/с)
            double bitsPerSec = bytesPerSecond * 8.0;
            string bitsStr = bitsPerSec >= 1_000_000
                ? $"{(bitsPerSec / 1_000_000.0):F1} Мбит/с"
                : $"{(bitsPerSec / 1_000.0):F0} кбит/с";

            // 2. Скорость полезных данных в байтах/с (МБ/с, КБ/с)
            string bytesStr = bytesPerSecond >= 1024 * 1024
                ? $"{(bytesPerSecond / (1024.0 * 1024.0)):F1} МБ/с"
                : $"{(bytesPerSecond / 1024.0):F1} КБ/с";

            return $"{bitsStr} ({bytesStr})";
        }

        private static string FormatBytes(long bytes)
        {
            return AppLogger.FormatBytes(bytes);
        }

        private static GraphicsPath GetRoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _updateTimer.Dispose();
                _hideCheckTimer.Dispose();
                _completionTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
