using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace FreshViewer.Effects.LiquidGlass;

/// <summary>
/// Provides the shader-backed rendering pipeline for the Liquid Glass decorator.
/// </summary>
internal static class LiquidGlassRenderer
{
    private static readonly object SyncRoot = new();
    private static SKRuntimeEffect? _cachedEffect;
    private static bool _shaderLoadAttempted;

    /// <summary>
    /// Attempts to render the Liquid Glass shader. Returns <c>true</c> when the shader was executed.
    /// </summary>
    /// <param name="context">The drawing context provided by Avalonia.</param>
    /// <param name="bounds">The bounds of the decorator.</param>
    /// <param name="parameters">Uniform values controlling the shader.</param>
    public static bool TryRender(DrawingContext context, Rect bounds, in LiquidGlassRenderParameters parameters)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        if (!LiquidGlassSupport.IsSupported)
        {
            LogOnce("Liquid Glass shader disabled because the platform is not supported.");
            return false;
        }

        var effect = EnsureEffect();
        if (effect is null)
        {
            return false;
        }

        context.Custom(new LiquidGlassRenderOperation(bounds, parameters, effect));
        return true;
    }

    private static SKRuntimeEffect? EnsureEffect()
    {
        if (_cachedEffect is not null)
        {
            return _cachedEffect;
        }

        lock (SyncRoot)
        {
            if (_cachedEffect is not null || _shaderLoadAttempted)
            {
                return _cachedEffect;
            }

            _shaderLoadAttempted = true;

            try
            {
                var assetUri = new Uri("avares://FreshViewer/Effects/LiquidGlass/Assets/Shaders/LiquidGlassShader.sksl");
                using var stream = Avalonia.Platform.AssetLoader.Open(assetUri);
                using var reader = new System.IO.StreamReader(stream);
                var shaderCode = reader.ReadToEnd();

                _cachedEffect = SKRuntimeEffect.Create(shaderCode, out var errorText);
                if (_cachedEffect is null)
                {
                    Debug.WriteLine($"LiquidGlass: failed to compile shader - {errorText}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LiquidGlass: exception while loading shader - {ex.Message}");
                _cachedEffect = null;
            }

            return _cachedEffect;
        }
    }

    private static bool _loggedDiagnostic;

    private static void LogOnce(string message)
    {
        if (_loggedDiagnostic)
        {
            return;
        }

        _loggedDiagnostic = true;
        Debug.WriteLine($"LiquidGlass: {message} {LiquidGlassSupport.DiagnosticMessage}");
    }
}

/// <summary>
/// Lightweight struct describing the uniforms used by the shader pipeline.
/// </summary>
internal readonly record struct LiquidGlassRenderParameters(
    double Radius,
    double Intensity,
    double Blur,
    double ChromaticAberration,
    double Saturation);
