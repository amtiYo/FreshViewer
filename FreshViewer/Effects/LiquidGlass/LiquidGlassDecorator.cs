using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FreshViewer.Effects.LiquidGlass;

/// <summary>
/// Decorator that renders a Liquid Glass background underneath its content.
/// When the runtime shader is not available, a static fallback is used instead.
/// </summary>
public class LiquidGlassDecorator : Decorator
{
    /// <summary>
    /// Identifies the <see cref="Radius"/> styled property.
    /// </summary>
    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<LiquidGlassDecorator, double>(nameof(Radius), 24);

    /// <summary>
    /// Identifies the <see cref="Intensity"/> styled property.
    /// </summary>
    public static readonly StyledProperty<double> IntensityProperty =
        AvaloniaProperty.Register<LiquidGlassDecorator, double>(nameof(Intensity), 0.85);

    /// <summary>
    /// Identifies the <see cref="BlurAmount"/> styled property.
    /// </summary>
    public static readonly StyledProperty<double> BlurAmountProperty =
        AvaloniaProperty.Register<LiquidGlassDecorator, double>(nameof(BlurAmount), 0.35);

    /// <summary>
    /// Identifies the <see cref="ChromaticAberration"/> styled property.
    /// </summary>
    public static readonly StyledProperty<double> ChromaticAberrationProperty =
        AvaloniaProperty.Register<LiquidGlassDecorator, double>(nameof(ChromaticAberration), 0.45);

    /// <summary>
    /// Identifies the <see cref="Saturation"/> styled property.
    /// </summary>
    public static readonly StyledProperty<double> SaturationProperty =
        AvaloniaProperty.Register<LiquidGlassDecorator, double>(nameof(Saturation), 1.2);

    /// <summary>
    /// Identifies the <see cref="IsEffectEnabled"/> styled property.
    /// </summary>
    public static readonly StyledProperty<bool> IsEffectEnabledProperty =
        AvaloniaProperty.Register<LiquidGlassDecorator, bool>(nameof(IsEffectEnabled), true);

    static LiquidGlassDecorator()
    {
        AffectsRender<LiquidGlassDecorator>(
            RadiusProperty,
            IntensityProperty,
            BlurAmountProperty,
            ChromaticAberrationProperty,
            SaturationProperty,
            IsEffectEnabledProperty);
    }

    /// <summary>
    /// Gets or sets the corner radius used when rendering the glass card.
    /// </summary>
    public double Radius
    {
        get => GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets the distortion strength supplied to the shader.
    /// </summary>
    public double Intensity
    {
        get => GetValue(IntensityProperty);
        set => SetValue(IntensityProperty, value);
    }

    /// <summary>
    /// Gets or sets the soft blur intensity. Value is clamped between 0 and 1.
    /// </summary>
    public double BlurAmount
    {
        get => GetValue(BlurAmountProperty);
        set => SetValue(BlurAmountProperty, value);
    }

    /// <summary>
    /// Gets or sets the chromatic aberration amount used by the shader.
    /// </summary>
    public double ChromaticAberration
    {
        get => GetValue(ChromaticAberrationProperty);
        set => SetValue(ChromaticAberrationProperty, value);
    }

    /// <summary>
    /// Gets or sets the saturation multiplier applied by the shader.
    /// </summary>
    public double Saturation
    {
        get => GetValue(SaturationProperty);
        set => SetValue(SaturationProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the shader should be executed.
    /// </summary>
    public bool IsEffectEnabled
    {
        get => GetValue(IsEffectEnabledProperty);
        set => SetValue(IsEffectEnabledProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var parameters = new LiquidGlassRenderParameters(
            Radius,
            Clamp01(Intensity),
            Clamp01(BlurAmount),
            Clamp01(ChromaticAberration),
            ClampSaturation(Saturation));

        var rendered = false;
        if (IsEffectEnabled)
        {
            rendered = LiquidGlassRenderer.TryRender(context, bounds, parameters);
        }

        if (!rendered)
        {
            LiquidGlassFallbackRenderer.Render(context, bounds, Radius);
        }

        base.Render(context);
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static double ClampSaturation(double value) => Math.Clamp(value, 0, 2);
}
