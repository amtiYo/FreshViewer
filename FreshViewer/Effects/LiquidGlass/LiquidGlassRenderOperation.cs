using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Platform;
using Avalonia.Skia;
using SkiaSharp;

namespace FreshViewer.Effects.LiquidGlass;

/// <summary>
/// Custom draw operation that feeds the Liquid Glass shader with the framebuffer snapshot.
/// </summary>
internal sealed class LiquidGlassRenderOperation : ICustomDrawOperation
{
    private readonly Rect _bounds;
    private readonly LiquidGlassRenderParameters _parameters;
    private readonly SKRuntimeEffect _effect;

    public LiquidGlassRenderOperation(Rect bounds, LiquidGlassRenderParameters parameters, SKRuntimeEffect effect)
    {
        _bounds = bounds;
        _parameters = parameters;
        _effect = effect;
    }

    public Rect Bounds => _bounds;

    public void Dispose()
    {
    }

    public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

    public bool HitTest(Point p) => _bounds.Contains(p);

    public void Render(ImmediateDrawingContext context)
    {
        var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (leaseFeature is null)
        {
            return;
        }

        using var lease = leaseFeature.Lease();
        var canvas = lease.SkCanvas;
        if (canvas is null || lease.SkSurface is null)
        {
            return;
        }

        using var snapshot = lease.SkSurface.Snapshot();
        if (snapshot is null)
        {
            return;
        }

        if (!canvas.TotalMatrix.TryInvert(out var invertedMatrix))
        {
            return;
        }

        using var backgroundShader = SKShader.CreateImage(
            snapshot,
            SKShaderTileMode.Clamp,
            SKShaderTileMode.Clamp,
            invertedMatrix);

        var uniforms = new SKRuntimeEffectUniforms(_effect)
        {
            ["resolution"] = new[] { (float)_bounds.Width, (float)_bounds.Height },
            ["intensity"] = (float)_parameters.Intensity,
            ["blurAmount"] = (float)_parameters.Blur,
            ["chromaticAberration"] = (float)_parameters.ChromaticAberration,
            ["saturation"] = (float)_parameters.Saturation
        };

        var children = new SKRuntimeEffectChildren(_effect)
        {
            ["backgroundTexture"] = backgroundShader
        };

        using var shader = _effect.ToShader(false, uniforms, children);
        if (shader is null)
        {
            return;
        }

        using var paint = new SKPaint
        {
            Shader = shader,
            IsAntialias = true
        };

        var rect = SKRect.Create(0, 0, (float)_bounds.Width, (float)_bounds.Height);
        var cornerRadius = (float)Math.Clamp(_parameters.Radius, 0, Math.Min(_bounds.Width, _bounds.Height) * 0.5);

        using var path = new SKPath();
        path.AddRoundRect(rect, cornerRadius, cornerRadius);

        canvas.Save();
        canvas.ClipPath(path, SKClipOperation.Intersect, true);
        canvas.DrawRect(rect, paint);
        canvas.Restore();
    }
}
