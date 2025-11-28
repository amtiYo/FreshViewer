using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace FreshViewer.Services;

/// <summary>
/// Provides a very small culture switcher by loading prebuilt resource dictionaries.
/// The implementation keeps the default <see cref="CultureInfo"/> synchronized with the selected language.
/// </summary>
public static class LocalizationService
{
    private static readonly Dictionary<string, string> LanguageToCulture = new()
    {
        ["Русский"] = "ru-RU",
        ["English"] = "en-US",
        ["Українська"] = "uk-UA",
        ["Deutsch"] = "de-DE"
    };

    /// <summary>
    /// Applies the requested UI language by loading a corresponding resource dictionary.
    /// </summary>
    public static void ApplyLanguage(string languageName)
    {
        if (!LanguageToCulture.TryGetValue(languageName, out var culture))
        {
            culture = "ru-RU";
        }

        var ci = new CultureInfo(culture);
        CultureInfo.CurrentCulture = ci;
        CultureInfo.CurrentUICulture = ci;

        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var uri = culture switch
        {
            "en-US" => new Uri("avares://FreshViewer/Assets/i18n/Strings.en.axaml"),
            "uk-UA" => new Uri("avares://FreshViewer/Assets/i18n/Strings.uk.axaml"),
            "de-DE" => new Uri("avares://FreshViewer/Assets/i18n/Strings.de.axaml"),
            _ => new Uri("avares://FreshViewer/Assets/i18n/Strings.ru.axaml")
        };

        var dict = AvaloniaXamlLoader.Load(uri) as ResourceDictionary;
        if (dict is null)
        {
            return;
        }

        // Remove previous string dictionaries so only one language is active at a time.
        for (var i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            if (app.Resources.MergedDictionaries[i] is ResourceDictionary existing
                && existing.ContainsKey("Strings.Back"))
            {
                app.Resources.MergedDictionaries.RemoveAt(i);
            }
        }

        app.Resources.MergedDictionaries.Add(dict);
    }
}


