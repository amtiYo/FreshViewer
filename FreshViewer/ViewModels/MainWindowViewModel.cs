using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using FreshViewer.Models;

namespace FreshViewer.ViewModels;

/// <summary>
/// Exposes the UI state for the main window, including metadata and UI toggle flags.
/// </summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private string? _fileName;
    private string? _resolutionText;
    private string _statusText = "Откройте изображение";
    private bool _isMetadataVisible;
    private bool _isUiVisible = true;
    private bool _isMetadataCardVisible;
    private bool _isInfoPanelVisible;
    private IReadOnlyList<MetadataSectionViewModel>? _metadataSections;
    private IReadOnlyList<MetadataItemViewModel>? _summaryItems;
    private bool _hasSummaryItems;
    private bool _hasMetadataDetails;
    private bool _showMetadataPlaceholder = true;
    private bool _isErrorVisible;
    private string? _errorTitle;
    private string? _errorDescription;
    private bool _isSettingsPanelVisible;
    private string? _galleryPositionText;
    private string _selectedTheme = "Liquid Dawn";
    private string _selectedLanguage = "Русский";
    private string _selectedShortcutProfile = "Стандартный";
    private bool _enableLiquidGlass = true;
    private bool _enableAmbientAnimations = true;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets or sets the name of the currently opened file.
    /// </summary>
    public string? FileName
    {
        get => _fileName;
        set => SetField(ref _fileName, value);
    }

    /// <summary>
    /// Gets or sets the resolution label shown to the user.
    /// </summary>
    public string? ResolutionText
    {
        get => _resolutionText;
        set => SetField(ref _resolutionText, value);
    }

    /// <summary>
    /// Gets or sets the status message displayed in the footer panel.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether metadata should be visible.
    /// </summary>
    public bool IsMetadataVisible
    {
        get => _isMetadataVisible;
        set
        {
            if (SetField(ref _isMetadataVisible, value))
            {
                UpdateMetadataCardVisibility();
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the main UI chrome is visible.
    /// </summary>
    public bool IsUiVisible
    {
        get => _isUiVisible;
        set
        {
            if (SetField(ref _isUiVisible, value))
            {
                UpdateMetadataCardVisibility();

                if (!value)
                {
                    if (IsInfoPanelVisible)
                    {
                        IsInfoPanelVisible = false;
                    }

                    if (IsSettingsPanelVisible)
                    {
                        IsSettingsPanelVisible = false;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the summary metadata card should be shown.
    /// </summary>
    public bool IsMetadataCardVisible
    {
        get => _isMetadataCardVisible;
        private set => SetField(ref _isMetadataCardVisible, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the detailed metadata panel is open.
    /// </summary>
    public bool IsInfoPanelVisible
    {
        get => _isInfoPanelVisible;
        set
        {
            if (SetField(ref _isInfoPanelVisible, value) && value)
            {
                if (IsSettingsPanelVisible)
                {
                    IsSettingsPanelVisible = false;
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the settings panel is open.
    /// </summary>
    public bool IsSettingsPanelVisible
    {
        get => _isSettingsPanelVisible;
        set
        {
            if (SetField(ref _isSettingsPanelVisible, value) && value)
            {
                if (IsInfoPanelVisible)
                {
                    IsInfoPanelVisible = false;
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the metadata sections displayed in the detail panel.
    /// </summary>
    public IReadOnlyList<MetadataSectionViewModel>? MetadataSections
    {
        get => _metadataSections;
        set => SetField(ref _metadataSections, value);
    }

    /// <summary>
    /// Gets the summary metadata items shown in the floating card.
    /// </summary>
    public IReadOnlyList<MetadataItemViewModel>? SummaryItems
    {
        get => _summaryItems;
        private set
        {
            if (SetField(ref _summaryItems, value))
            {
                HasSummaryItems = value is { Count: > 0 };
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the summary card has data to display.
    /// </summary>
    public bool HasSummaryItems
    {
        get => _hasSummaryItems;
        private set => SetField(ref _hasSummaryItems, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the detail panel contains metadata.
    /// </summary>
    public bool HasMetadataDetails
    {
        get => _hasMetadataDetails;
        set => SetField(ref _hasMetadataDetails, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the metadata placeholder should be shown.
    /// </summary>
    public bool ShowMetadataPlaceholder
    {
        get => _showMetadataPlaceholder;
        set => SetField(ref _showMetadataPlaceholder, value);
    }

    /// <summary>
    /// Gets or sets the text representing the current file index within its folder.
    /// </summary>
    public string? GalleryPositionText
    {
        get => _galleryPositionText;
        set => SetField(ref _galleryPositionText, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the error overlay is displayed.
    /// </summary>
    public bool IsErrorVisible
    {
        get => _isErrorVisible;
        set => SetField(ref _isErrorVisible, value);
    }

    /// <summary>
    /// Gets or sets the title used by the error overlay.
    /// </summary>
    public string? ErrorTitle
    {
        get => _errorTitle;
        set => SetField(ref _errorTitle, value);
    }

    /// <summary>
    /// Gets or sets the description displayed in the error overlay.
    /// </summary>
    public string? ErrorDescription
    {
        get => _errorDescription;
        set => SetField(ref _errorDescription, value);
    }

    /// <summary>
    /// Gets the predefined theme names available to the user.
    /// </summary>
    public IReadOnlyList<string> ThemeOptions { get; } = new[]
    {
        "Liquid Dawn",
        "Midnight Flow",
        "Frosted Steel"
    };

    /// <summary>
    /// Gets the language options exposed in the UI.
    /// </summary>
    public IReadOnlyList<string> LanguageOptions { get; } = new[]
    {
        "Русский",
        "English",
        "Українська",
        "Deutsch"
    };

    /// <summary>
    /// Gets the names of the available shortcut profiles.
    /// </summary>
    public IReadOnlyList<string> ShortcutProfiles { get; } = new[]
    {
        "Стандартный",
        "Photoshop",
        "Lightroom"
    };

    /// <summary>
    /// Gets or sets the currently active theme name.
    /// </summary>
    public string SelectedTheme
    {
        get => _selectedTheme;
        set => SetField(ref _selectedTheme, value);
    }

    /// <summary>
    /// Gets or sets the selected language option.
    /// </summary>
    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set => SetField(ref _selectedLanguage, value);
    }

    /// <summary>
    /// Gets or sets the active shortcut profile name.
    /// </summary>
    public string SelectedShortcutProfile
    {
        get => _selectedShortcutProfile;
        set => SetField(ref _selectedShortcutProfile, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the Liquid Glass effect is enabled.
    /// </summary>
    public bool EnableLiquidGlass
    {
        get => _enableLiquidGlass;
        set => SetField(ref _enableLiquidGlass, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether ambient UI animations are enabled.
    /// </summary>
    public bool EnableAmbientAnimations
    {
        get => _enableAmbientAnimations;
        set => SetField(ref _enableAmbientAnimations, value);
    }

    /// <summary>
    /// Updates the view model with metadata from a successfully loaded image.
    /// </summary>
    /// <param name="fileName">The file name to display.</param>
    /// <param name="resolution">The human readable resolution text.</param>
    /// <param name="statusMessage">A status message to present to the user.</param>
    /// <param name="metadata">The structured metadata extracted from the file.</param>
    public void ApplyMetadata(string? fileName, string? resolution, string statusMessage, ImageMetadata? metadata)
    {
        FileName = fileName;
        ResolutionText = resolution;
        StatusText = statusMessage;
        IsMetadataVisible = !string.IsNullOrWhiteSpace(fileName);
        MetadataSections = MetadataViewModelFactory.Create(metadata);
        HasMetadataDetails = MetadataSections is { Count: > 0 };
        SummaryItems = MetadataSections?.FirstOrDefault()?.Items?.Take(6).ToList();
        ShowMetadataPlaceholder = !HasSummaryItems;
        GalleryPositionText = null;
        IsErrorVisible = false;
        ErrorTitle = null;
        ErrorDescription = null;
        UpdateMetadataCardVisibility();
    }

    /// <summary>
    /// Clears all metadata state and hides associated panels.
    /// </summary>
    public void ResetMetadata()
    {
        FileName = null;
        ResolutionText = null;
        MetadataSections = null;
        SummaryItems = null;
        HasMetadataDetails = false;
        ShowMetadataPlaceholder = true;
        IsMetadataVisible = false;
        GalleryPositionText = null;
        UpdateMetadataCardVisibility();
    }

    /// <summary>
    /// Shows an error overlay with the provided title and description.
    /// </summary>
    public void ShowError(string title, string description)
    {
        ErrorTitle = title;
        ErrorDescription = description;
        IsErrorVisible = true;
    }

    /// <summary>
    /// Hides the error overlay and clears the stored messages.
    /// </summary>
    public void HideError()
    {
        IsErrorVisible = false;
        ErrorTitle = null;
        ErrorDescription = null;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        return false;
    }

    private void UpdateMetadataCardVisibility()
    {
        IsMetadataCardVisible = _isUiVisible && _isMetadataVisible;
    }
}
