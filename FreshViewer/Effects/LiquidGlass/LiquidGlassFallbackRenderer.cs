using System;
using Avalonia;
using Avalonia.Media;

namespace FreshViewer.Effects.LiquidGlass;

/// <summary>
/// Draws a lightweight brush-based fallback whenever the shader is unavailable.
/// </summary>
internal static class LiquidGlassFallbackRenderer
{
    private static readonly IBrush BaseBrush = new SolidColorBrush(Color.FromArgb(0x34, 0xFF, 0xFF, 0xFF));

    private static readonly IBrush HighlightBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new GradientStop(Color.FromArgb(0xAA, 0xFF, 0xFF, 0xFF), 0),
            new GradientStop(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF), 1)
        }
    };

    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.FromArgb(0x4A, 0xC5, 0xD9, 0xFF)), 1);

    /// <summary>
    /// Renders the fallback glass with a static gradient and border.
    /// </summary>
    /// <param name="context">The drawing context provided by Avalonia.</param>
    /// <param name="bounds">The area to paint.</param>
    /// <param name="radius">Corner radius applied to the background card.</param>
    public static void Render(DrawingContext context, Rect bounds, double radius)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var rounded = new RoundedRect(bounds, new CornerRadius(radius));
        context.DrawRectangle(BaseBrush, null, rounded);

        var highlightRect = new Rect(bounds.X + 6, bounds.Y + 6, Math.Max(0, bounds.Width - 12), Math.Max(0, bounds.Height / 2));
        var highlightRounded = new RoundedRect(highlightRect, new CornerRadius(Math.Max(0, radius - 6)));
        context.DrawRectangle(HighlightBrush, null, highlightRounded);

        context.DrawRectangle(null, BorderPen, rounded);
    }
}
