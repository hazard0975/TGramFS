using System;
using System.Drawing;
using System.Windows.Forms;

namespace TGramFS.UI
{
    /// <summary>
    /// Единые константы оформления, шрифтов, цветов и размеров (UI Design System).
    /// </summary>
    public static class UITheme
    {
        // === Шрифты ===
        public static readonly Font BaseFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font BoldFont = new Font("Segoe UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font HeaderFont = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font SmallFont = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font MonoFont = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point);

        // === Цвета ===
        public static readonly Color TextMain = Color.FromArgb(30, 30, 30);
        public static readonly Color TextMuted = Color.FromArgb(110, 110, 110);
        public static readonly Color TextDanger = Color.FromArgb(190, 40, 40);
        public static readonly Color TextSuccess = Color.FromArgb(35, 140, 35);
        public static readonly Color BorderLight = Color.FromArgb(215, 215, 215);
        public static readonly Color BackgroundLight = Color.FromArgb(248, 249, 250);

        // === Размеры контролов (px при базовых 96 DPI / 100%) ===
        public const int ContentWidth = 515;
        public const int ButtonHeightDefault = 28; // Стандартная высота кнопок действий Windows 10/11 (28-30 px)
        public const int ButtonHeightPrimary = 30; // Акцентная кнопка диалога
        public const int ButtonHeightSmall = 23;   // 14 DLU — канонический стандарт Microsoft Windows UX для однострочных элементов ввода и ассоциированных кнопок при 96 DPI
        public const int InputHeight = 23;         // 14 DLU — системная высота однострочного поля ввода (TextBox / ComboBox) при 96 DPI
        public const int InputNumberWidth = 90;
        public const int InputPortWidth = 90;
        public const int DropDownWidthDefault = 380;

        // === Отступы (Margins & Paddings) ===
        public static readonly Padding GroupBoxMargin = new Padding(0, 0, 0, 10);
        public static readonly Padding GroupBoxPadding = new Padding(8, 6, 8, 4);
        public static readonly Padding RowMargin = new Padding(0, 1, 0, 3);
        public static readonly Padding ControlMargin = new Padding(0, 2, 0, 2);

        // === Хелперы масштабирования High-DPI (от базовых 96 DPI) ===
        public static int Scale(int basePx, float dpiScale) =>
            basePx <= 0 ? 0 : Math.Max(1, (int)Math.Round(basePx * dpiScale));

        public static Padding Scale(Padding basePadding, float dpiScale) =>
            new Padding(
                Scale(basePadding.Left, dpiScale),
                Scale(basePadding.Top, dpiScale),
                Scale(basePadding.Right, dpiScale),
                Scale(basePadding.Bottom, dpiScale));

        public static Size Scale(Size baseSize, float dpiScale) =>
            new Size(Scale(baseSize.Width, dpiScale), Scale(baseSize.Height, dpiScale));
    }
}
