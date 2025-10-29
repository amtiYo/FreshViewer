using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FreshViewer.Services;

/// <summary>
/// Loads the themed resource dictionaries that provide the Liquid Glass color palettes.
/// </summary>
public static class ThemeManager
{
    /// <summary>
    /// Applies a theme by name by replacing the Liquid Glass resource dictionaries.
    /// </summary>
    public static void Apply(string themeName)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var uri = themeName switch
        {
            "Midnight Flow" => new Uri("avares://FreshViewer/Styles/LiquidGlass.Windows.axaml"),
            "Frosted Steel" => new Uri("avares://FreshViewer/Styles/LiquidGlass.Windows.axaml"),
            _ => new Uri("avares://FreshViewer/Styles/LiquidGlass.axaml")
        };

        var dict = AvaloniaXamlLoader.Load(uri) as ResourceDictionary;
        if (dict is null)
        {
            return;
        }

        // Remove previous Liquid Glass dictionaries before adding the new palette.
        for (var i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            if (app.Resources.MergedDictionaries[i] is ResourceDictionary existing
                && existing.ContainsKey("LiquidGlass.WindowBackground"))
            {
                app.Resources.MergedDictionaries.RemoveAt(i);
            }
        }

        app.Resources.MergedDictionaries.Add(dict);
    }
}


