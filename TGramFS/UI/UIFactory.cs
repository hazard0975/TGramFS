using System;
using System.Drawing;
using System.Windows.Forms;

namespace TGramFS.UI
{
    /// <summary>
    /// Фабрика компонентов интерфейса Windows Forms.
    /// Гарантирует соблюдение единого стиля, защиту от схлопывания в FlowLayoutPanel и единообразные размеры с поддержкой High-DPI.
    /// </summary>
    public static class UIFactory
    {
        public const int DefaultInnerWidth = 495;

        /// <summary>
        /// Создает рамку GroupBox с защитой от сжатия (MinimumSize) и компактными отступами.
        /// </summary>
        public static GroupBox CreateGroupBox(string title, int width = UITheme.ContentWidth, Action<Action<float>>? registerScaler = null)
        {
            var grp = new GroupBox
            {
                Text = title,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Font = UITheme.BaseFont
            };

            Action<float> applyScale = scale =>
            {
                int scaledW = UITheme.Scale(width, scale);
                grp.Width = scaledW;
                grp.MinimumSize = new Size(scaledW, 0);
                grp.Margin = UITheme.Scale(UITheme.GroupBoxMargin, scale);
                grp.Padding = UITheme.Scale(UITheme.GroupBoxPadding, scale);
            };
            applyScale(1.0f);
            registerScaler?.Invoke(applyScale);

            return grp;
        }

        /// <summary>
        /// Создает внутренний контейнер FlowLayoutPanel для GroupBox с направлением сверху вниз и гарантированным нижним зазором.
        /// </summary>
        public static FlowLayoutPanel CreateVerticalContainer(int width = DefaultInnerWidth, Action<Action<float>>? registerScaler = null)
        {
            var pnl = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = Padding.Empty
            };

            Action<float> applyScale = scale =>
            {
                pnl.Width = UITheme.Scale(width, scale);
                pnl.Padding = new Padding(0, 0, 0, UITheme.Scale(3, scale));
            };
            applyScale(1.0f);
            registerScaler?.Invoke(applyScale);

            return pnl;
        }

        /// <summary>
        /// Создает строку параметра: компактная колонка названия слева + контрол сразу справа.
        /// </summary>
        public static TableLayoutPanel CreateSettingRow(string labelText, Control control, int labelWidth = 140, int width = DefaultInnerWidth, Action<Action<float>>? registerScaler = null)
        {
            var tbl = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Padding = Padding.Empty
            };
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var lbl = new Label
            {
                Text = labelText,
                AutoSize = true,
                Font = UITheme.BaseFont,
                ForeColor = UITheme.TextMain,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            control.Anchor = AnchorStyles.Left;

            tbl.Controls.Add(lbl, 0, 0);
            tbl.Controls.Add(control, 1, 0);

            Action<float> applyScale = scale =>
            {
                int scaledLabelW = UITheme.Scale(labelWidth, scale);
                tbl.Width = UITheme.Scale(width, scale);
                tbl.Margin = UITheme.Scale(UITheme.RowMargin, scale);
                tbl.ColumnStyles[0].Width = scaledLabelW;
                lbl.MaximumSize = new Size(Math.Max(1, scaledLabelW - UITheme.Scale(4, scale)), 0);
                lbl.Margin = UITheme.Scale(new Padding(0, 4, 4, 4), scale);
                control.Margin = UITheme.Scale(new Padding(0, 2, 0, 2), scale);
            };
            applyScale(1.0f);
            registerScaler?.Invoke(applyScale);

            return tbl;
        }

        /// <summary>
        /// Создает строку для текстового поля ввода на всю оставшуюся ширину.
        /// </summary>
        public static TableLayoutPanel CreateInputRow(string labelText, Control control, int labelWidth = 90, int width = DefaultInnerWidth, Action<Action<float>>? registerScaler = null)
        {
            var tbl = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Padding = Padding.Empty
            };
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var lbl = new Label
            {
                Text = labelText,
                AutoSize = true,
                Font = UITheme.BaseFont,
                ForeColor = UITheme.TextMain,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            control.Dock = DockStyle.Fill;

            tbl.Controls.Add(lbl, 0, 0);
            tbl.Controls.Add(control, 1, 0);

            Action<float> applyScale = scale =>
            {
                int scaledW = UITheme.Scale(width, scale);
                int scaledLabelW = UITheme.Scale(labelWidth, scale);
                tbl.Width = scaledW;
                tbl.MinimumSize = new Size(scaledW, 0);
                tbl.Margin = UITheme.Scale(UITheme.RowMargin, scale);
                tbl.ColumnStyles[0].Width = scaledLabelW;
                lbl.MaximumSize = new Size(Math.Max(1, scaledLabelW - UITheme.Scale(4, scale)), 0);
                lbl.Margin = UITheme.Scale(new Padding(0, 4, 4, 4), scale);
                control.Margin = UITheme.Scale(new Padding(0, 2, 0, 2), scale);
            };
            applyScale(1.0f);
            registerScaler?.Invoke(applyScale);

            return tbl;
        }

        /// <summary>
        /// Создает строку из названия, поля ввода и кнопки действия в одну линию.
        /// Использует TableLayoutPanel с гибкой средней колонкой для надежной DPI-адаптации и выравнивания базовой линии шрифта.
        /// </summary>
        public static TableLayoutPanel CreateActionInputRow(string labelText, TextBox textBox, Button button, int labelWidth = 90, int buttonWidth = 135, int width = DefaultInnerWidth, Action<Action<float>>? registerScaler = null)
        {
            var tbl = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Padding = Padding.Empty
            };
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, labelWidth));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, buttonWidth));

            var lbl = new Label
            {
                Text = labelText,
                AutoSize = true,
                Font = UITheme.BaseFont,
                ForeColor = UITheme.TextMain,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            textBox.Font = UITheme.BaseFont;
            textBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;

            button.Anchor = AnchorStyles.Left;

            float activeScale = 1.0f;

            // Динамическая привязка высоты кнопки к системной высоте TextBox при любом DPI масштабировании
            tbl.Layout += (s, e) =>
            {
                int scaledBtnW = UITheme.Scale(buttonWidth, activeScale);
                int currentBoxHeight = textBox.Height > 0 ? textBox.Height : textBox.PreferredHeight;
                if (currentBoxHeight > 0 && (button.Height != currentBoxHeight || button.Width != scaledBtnW || button.MinimumSize.Width != scaledBtnW))
                {
                    button.Width = scaledBtnW;
                    button.Height = currentBoxHeight;
                    button.MinimumSize = new Size(scaledBtnW, currentBoxHeight);
                    button.MaximumSize = new Size(scaledBtnW * 2, currentBoxHeight);
                }
            };

            tbl.Controls.Add(lbl, 0, 0);
            tbl.Controls.Add(textBox, 1, 0);
            tbl.Controls.Add(button, 2, 0);

            Action<float> applyScale = scale =>
            {
                activeScale = scale;
                int scaledW = UITheme.Scale(width, scale);
                int scaledLabelW = UITheme.Scale(labelWidth, scale);
                int scaledBtnW = UITheme.Scale(buttonWidth, scale);

                tbl.Width = scaledW;
                tbl.MinimumSize = new Size(scaledW, 0);
                tbl.Margin = UITheme.Scale(UITheme.RowMargin, scale);
                tbl.ColumnStyles[0].Width = scaledLabelW;
                tbl.ColumnStyles[2].Width = scaledBtnW;

                lbl.MaximumSize = new Size(Math.Max(1, scaledLabelW - UITheme.Scale(4, scale)), 0);
                lbl.Margin = UITheme.Scale(new Padding(0, 2, 4, 2), scale);
                textBox.Margin = UITheme.Scale(new Padding(0, 2, 8, 2), scale);
                button.Margin = UITheme.Scale(new Padding(0, 2, 0, 2), scale);

                int targetHeight = textBox.Height > 0
                    ? textBox.Height
                    : (textBox.PreferredHeight > 0 ? textBox.PreferredHeight : UITheme.Scale(UITheme.ButtonHeightSmall, scale));
                button.Width = scaledBtnW;
                button.Height = targetHeight;
                button.MinimumSize = new Size(scaledBtnW, targetHeight);
                button.MaximumSize = new Size(scaledBtnW * 2, targetHeight);
            };
            applyScale(1.0f);
            registerScaler?.Invoke(applyScale);

            return tbl;
        }

        /// <summary>
        /// Создает числовое поле NumericUpDown с компактной шириной и единым шрифтом.
        /// </summary>
        public static NumericUpDown CreateNumericInput(decimal min, decimal max, decimal val, int width = UITheme.InputNumberWidth, int decimalPlaces = 0, Action<Action<float>>? registerScaler = null)
        {
            var num = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, val)),
                DecimalPlaces = decimalPlaces,
                Font = UITheme.BaseFont
            };

            Action<float> applyScale = scale =>
            {
                num.Width = UITheme.Scale(width, scale);
                num.Margin = UITheme.Scale(UITheme.ControlMargin, scale);
            };
            applyScale(1.0f);
            registerScaler?.Invoke(applyScale);

            return num;
        }

        /// <summary>
        /// Создает стандартную кнопку действия через единую фабрику с защитой от сплющивания (MinimumSize).
        /// </summary>
        public static Button CreateButton(
            string text,
            EventHandler? onClick = null,
            bool isPrimary = false,
            int height = UITheme.ButtonHeightDefault,
            int? width = null,
            bool autoSize = false,
            DockStyle dock = DockStyle.None,
            Color? foreColor = null,
            Action<Action<float>>? registerScaler = null)
        {
            var btn = new Button
            {
                Text = text,
                Font = isPrimary ? UITheme.BoldFont : UITheme.BaseFont,
                UseVisualStyleBackColor = true,
                AutoSize = autoSize,
                Dock = dock
            };
            if (foreColor.HasValue)
            {
                btn.ForeColor = foreColor.Value;
            }
            if (onClick != null)
            {
                btn.Click += onClick;
            }

            Action<float> applyScale = scale =>
            {
                int scaledH = UITheme.Scale(height, scale);
                int scaledW = width.HasValue ? UITheme.Scale(width.Value, scale) : 0;

                btn.Height = scaledH;
                btn.MinimumSize = new Size(scaledW, scaledH);
                if (width.HasValue)
                {
                    btn.Width = scaledW;
                }
                btn.Padding = autoSize ? UITheme.Scale(new Padding(12, 4, 12, 4), scale) : Padding.Empty;
                btn.Margin = UITheme.Scale(new Padding(0, 2, 0, 2), scale);
            };
            applyScale(1.0f);
            registerScaler?.Invoke(applyScale);

            return btn;
        }

        /// <summary>
        /// Создает чекбокс со стандартным шрифтом и отступами.
        /// </summary>
        public static CheckBox CreateCheckBox(string text, bool isChecked, EventHandler? onCheckedChanged = null, Action<Action<float>>? registerScaler = null)
        {
            var chk = new CheckBox
            {
                Text = text,
                Checked = isChecked,
                AutoSize = true,
                Font = UITheme.BaseFont,
                ForeColor = UITheme.TextMain
            };
            if (onCheckedChanged != null)
            {
                chk.CheckedChanged += onCheckedChanged;
            }

            Action<float> applyScale = scale =>
            {
                chk.Margin = UITheme.Scale(new Padding(0, 3, 0, 5), scale);
            };
            applyScale(1.0f);
            registerScaler?.Invoke(applyScale);

            return chk;
        }

        /// <summary>
        /// Создает информационную подпись или подсказку мелким приглушенным шрифтом.
        /// </summary>
        public static Label CreateHintLabel(string text, int width = DefaultInnerWidth, Action<Action<float>>? registerScaler = null)
        {
            var lbl = new Label
            {
                Text = text,
                AutoSize = true,
                Font = UITheme.SmallFont,
                ForeColor = UITheme.TextMuted
            };

            Action<float> applyScale = scale =>
            {
                int scaledW = UITheme.Scale(width, scale);
                lbl.Width = scaledW;
                lbl.MaximumSize = new Size(scaledW, 0);
                lbl.Margin = UITheme.Scale(new Padding(0, 1, 0, 6), scale);
            };
            applyScale(1.0f);
            registerScaler?.Invoke(applyScale);

            return lbl;
        }

        /// <summary>
        /// Создает панель кнопок с равномерным распределением колонок на всю ширину блока.
        /// </summary>
        public static TableLayoutPanel CreateActionRow(params Control[] buttons) =>
            CreateActionRow(null, buttons);

        /// <summary>
        /// Создает панель кнопок с равномерным распределением колонок на всю ширину блока и поддержкой High-DPI.
        /// </summary>
        public static TableLayoutPanel CreateActionRow(Action<Action<float>>? registerScaler, params Control[] buttons)
        {
            var tbl = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = buttons.Length,
                RowCount = 1,
                Padding = Padding.Empty
            };
            float percentPerCol = 100f / buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
            {
                tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, percentPerCol));
                buttons[i].Dock = DockStyle.Fill;
                tbl.Controls.Add(buttons[i], i, 0);
            }

            Action<float> applyScale = scale =>
            {
                int scaledW = UITheme.Scale(DefaultInnerWidth, scale);
                int scaledH = UITheme.Scale(UITheme.ButtonHeightDefault + 4, scale);
                tbl.Width = scaledW;
                tbl.Height = scaledH;
                tbl.MinimumSize = new Size(scaledW, scaledH);
                tbl.Margin = UITheme.Scale(new Padding(0, 2, 0, 2), scale);

                int gap = UITheme.Scale(3, scale);
                int vMargin = UITheme.Scale(1, scale);
                for (int i = 0; i < buttons.Length; i++)
                {
                    int leftMargin = i == 0 ? 0 : gap;
                    int rightMargin = i == buttons.Length - 1 ? 0 : gap;
                    buttons[i].Margin = new Padding(leftMargin, vMargin, rightMargin, vMargin);
                }
            };
            applyScale(1.0f);
            registerScaler?.Invoke(applyScale);

            return tbl;
        }

        /// <summary>
        /// Создает нижнюю фиксированную панель окна с компактным центрированием кнопок.
        /// </summary>
        public static TableLayoutPanel CreateBottomBar(Button saveBtn, Button? closeBtn = null, Action<Action<float>>? registerScaler = null)
        {
            var pnl = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = SystemColors.Control
            };
            pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115f));
            if (closeBtn != null)
            {
                pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115f));
                closeBtn.Dock = DockStyle.Fill;
                closeBtn.Font = UITheme.BaseFont;
                pnl.Controls.Add(closeBtn, 2, 0);
            }
            else
            {
                pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0f));
            }

            saveBtn.Dock = DockStyle.Fill;
            saveBtn.Font = UITheme.BaseFont;
            pnl.Controls.Add(saveBtn, 1, 0);

            Action<float> applyScale = scale =>
            {
                pnl.Height = UITheme.Scale(44, scale);
                pnl.Padding = UITheme.Scale(new Padding(12, 6, 22, 6), scale);

                int colW = UITheme.Scale(115, scale);
                int btnMinW = UITheme.Scale(110, scale);
                int btnH = UITheme.Scale(28, scale);
                int hGap = UITheme.Scale(4, scale);

                pnl.ColumnStyles[1].Width = colW;
                saveBtn.Height = btnH;
                saveBtn.MinimumSize = new Size(btnMinW, btnH);
                saveBtn.Margin = new Padding(0, 0, hGap, 0);

                if (closeBtn != null)
                {
                    pnl.ColumnStyles[2].Width = colW;
                    closeBtn.Height = btnH;
                    closeBtn.MinimumSize = new Size(btnMinW, btnH);
                    closeBtn.Margin = new Padding(hGap, 0, 0, 0);
                }
            };
            applyScale(1.0f);
            registerScaler?.Invoke(applyScale);

            return pnl;
        }
    }
}
