using System;
using System.Diagnostics;

namespace FreshViewer.Effects.LiquidGlass;

/// <summary>
/// Provides environment checks and feature flags for the Liquid Glass renderer.
/// </summary>
internal static class LiquidGlassSupport
{
    private const string EnvironmentSwitch = "FRESHVIEWER_FORCE_LIQUID_GLASS";

    private static readonly Lazy<SupportState> CachedState = new(Evaluate, isThreadSafe: true);

    /// <summary>
    /// Gets a value indicating whether the advanced Liquid Glass effect can be used.
    /// </summary>
    public static bool IsSupported => CachedState.Value.IsSupported;

    /// <summary>
    /// Gets an optional diagnostic message describing why the effect is unavailable.
    /// </summary>
    public static string? DiagnosticMessage => CachedState.Value.Message;

    private static SupportState Evaluate()
    {
        var overrideValue = Environment.GetEnvironmentVariable(EnvironmentSwitch);
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            if (bool.TryParse(overrideValue, out var parsed))
            {
                Debug.WriteLine($"LiquidGlass: support forced to {parsed} via environment switch.");
                return parsed
                    ? new SupportState(true, "Forced on by environment variable.")
                    : new SupportState(false, "Forced off by environment variable.");
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            return new SupportState(false, "Liquid Glass requires the Windows compositor.");
        }

        return new SupportState(true, null);
    }

    private readonly record struct SupportState(bool IsSupported, string? Message);
}
