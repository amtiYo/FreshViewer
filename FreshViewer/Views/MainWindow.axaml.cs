using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Styling;
using FreshViewer.Controls;
using FreshViewer.Models;
using FreshViewer.ViewModels;
using ImageMagick;
using FreshViewer.Services;

namespace FreshViewer.Views;

/// <summary>
/// Main window hosting the viewer UI and coordinating user interactions.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".mng", ".webp", ".tiff", ".tif", ".ico", ".svg",
        ".pbm", ".pgm", ".ppm", ".xbm", ".xpm", ".heic", ".heif", ".avif", ".jxl", ".fits", ".hdr",
        ".exr", ".pic", ".psd", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".orf", ".rw2",
        ".pef", ".srw", ".x3f", ".mrw", ".dcr", ".kdc", ".erf", ".mef", ".mos", ".ptx", ".r3d", ".fff",
        ".iiq"
    };

    private readonly ImageViewport _viewport;
    private readonly Border _dragOverlay;
    private readonly MainWindowViewModel _viewModel;
    private readonly Grid? _uiChrome;
    private readonly Border? _summaryCard;
    private readonly Border? _infoPanel;
    private readonly Border? _settingsPanel;
    private readonly ShortcutManager _shortcuts = new();

    private TranslateTransform? _uiChromeTransform;
    private TranslateTransform? _summaryCardTransform;

    private CancellationTokenSource? _chromeAnimationCts;
    private CancellationTokenSource? _summaryAnimationCts;
    private CancellationTokenSource? _infoPanelAnimationCts;
    private CancellationTokenSource? _settingsPanelAnimationCts;

    private static readonly SplineEasing SlideEasing = new(0.18, 0.88, 0.32, 1.08);
    private static readonly TimeSpan ChromeAnimationDuration = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan PanelAnimationDuration = TimeSpan.FromMilliseconds(320);
    private const double ChromeHiddenOffset = -28;
    private const double SummaryHiddenOffset = -22;
    private const double InfoPanelHiddenOffset = -80;
    private const double SettingsPanelHiddenOffset = 80;

    private string? _pendingInitialPath;
    private string? _currentDirectory;
    private List<string> _directoryFiles = new();
    private int _currentIndex = -1;
    private bool _currentAnimated;
    private ImageMetadata? _currentMetadata;

    private WindowState _previousWindowState = WindowState.Normal;
    private bool _uiVisibleBeforeFullscreen = true;
    private PixelPoint? _restoreWindowPosition;
    private Size? _restoreWindowSize;
    private bool _wasMaximizedBeforeFullscreen;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureWindowChrome();

        _viewport = this.FindControl<ImageViewport>("ImageViewport")
                    ?? throw new InvalidOperationException("Viewport control not found");
        _dragOverlay = this.FindControl<Border>("DragOverlay")
                       ?? throw new InvalidOperationException("Drag overlay not found");
        _uiChrome = this.FindControl<Grid>("UiChrome");
        _summaryCard = this.FindControl<Border>("SummaryCard");
        _infoPanel = this.FindControl<Border>("InfoPanel");
        _settingsPanel = this.FindControl<Border>("SettingsPanel");

        InitializeChromeState();

        if (DataContext is MainWindowViewModel existingVm)
        {
            _viewModel = existingVm;
        }
        else
        {
            _viewModel = new MainWindowViewModel();
            DataContext = _viewModel;
        }

        _viewport.ImagePresented += OnImagePresented;
        _viewport.ImageFailed += OnImageFailed;
        _viewport.ViewStateChanged += (_, _) => UpdateResolutionLabel();

        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        PropertyChanged += OnWindowPropertyChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        InitializePanelState(_infoPanel, -80);
        InitializePanelState(_settingsPanel, 80);

        // Apply the persisted theme and language.
        LocalizationService.ApplyLanguage(_viewModel.SelectedLanguage);
        ThemeManager.Apply(_viewModel.SelectedTheme);

        _viewModel.StatusText = "Откройте изображение или перетащите файл";
    }

    private void ConfigureWindowChrome()
    {
        // Simplified chrome: no Mica/Acrylic effects, just a transparent surface.
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        Background = Brushes.Transparent; // Leave the window transparent; draw the background via XAML.
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.PreferSystemChrome;
        ExtendClientAreaTitleBarHeightHint = 32;
    }

    /// <summary>
    /// Applies command-line arguments captured by the desktop lifetime.
    /// </summary>
    public void InitializeFromArguments(string[]? args)
    {
        if (args is { Length: > 0 })
        {
            _pendingInitialPath = args[0];
        }
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (WindowState != WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }

        if (!string.IsNullOrWhiteSpace(_pendingInitialPath) && File.Exists(_pendingInitialPath))
        {
            await OpenImageAsync(_pendingInitialPath);
        }
        else
        {
            _viewModel.ShowMetadataPlaceholder = true;
            await Dispatcher.UIThread.InvokeAsync(() => _viewport.Focus());
        }

        Dispatcher.UIThread.Post(() =>
        {
            _ = AnimateUiChromeAsync(_viewModel.IsUiVisible, immediate: true);
            if (_viewModel.IsMetadataCardVisible)
            {
                _ = AnimateSummaryCardAsync(true, immediate: true);
            }
        });
    }

    private static void InitializePanelState(Border? panel, double initialOffset)
    {
        if (panel is null)
        {
            return;
        }

        panel.IsVisible = false;
        panel.IsHitTestVisible = false;
        panel.Opacity = 0;
        panel.RenderTransform = new TranslateTransform(initialOffset, 0);
    }

    private void InitializeChromeState()
    {
        if (_uiChrome is { } chrome)
        {
            _uiChromeTransform = EnsureTranslateTransform(chrome);
            _uiChromeTransform.Y = ChromeHiddenOffset;
            chrome.Opacity = 0;
            chrome.IsVisible = false;
            chrome.IsHitTestVisible = false;
        }

        if (_summaryCard is { } card)
        {
            _summaryCardTransform = EnsureTranslateTransform(card);
            _summaryCardTransform.Y = SummaryHiddenOffset;
            card.Opacity = 0;
            card.IsVisible = false;
            card.IsHitTestVisible = false;
        }
    }

    private static TranslateTransform EnsureTranslateTransform(Visual visual)
    {
        if (visual.RenderTransform is TranslateTransform translate)
        {
            return translate;
        }

        translate = new TranslateTransform();
        visual.RenderTransform = translate;
        return translate;
    }

    private async Task PromptOpenFileAsync()
    {
        if (StorageProvider is null)
        {
            return;
        }

        var fileTypes = new List<FilePickerFileType>
        {
            new("Поддерживаемые форматы")
            {
                Patterns = SupportedExtensions.Select(ext => "*" + ext).ToArray()
            }
        };

        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Открыть изображение",
            AllowMultiple = false,
            FileTypeFilter = fileTypes
        });

        var file = result?.FirstOrDefault();
        if (file?.TryGetLocalPath() is { } localPath && File.Exists(localPath))
        {
            await OpenImageAsync(localPath);
        }
    }

    private async Task OpenImageAsync(string path, ImageTransition transition = ImageTransition.FadeIn)
    {
        _viewModel.StatusText = "Загрузка изображения...";
        Title = $"FreshViewer — {Path.GetFileName(path)}";
        _currentMetadata = null;
        _viewModel.ResetMetadata();
        _viewModel.HideError();

        if (_viewport.IsFullscreen && transition != ImageTransition.Instant)
        {
            transition = ImageTransition.Instant;
        }

        await _viewport.LoadImageAsync(path, transition);
        UpdateDirectoryContext(path);
    }

    private void UpdateDirectoryContext(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            _currentDirectory = directory;
            _directoryFiles = Directory.EnumerateFiles(directory)
                .Where(f => IsSupported(Path.GetExtension(f)))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();
            _currentIndex = _directoryFiles.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = ex.Message;
        }
    }

    private static bool IsSupported(string? extension)
        => extension is not null && SupportedExtensions.Contains(extension);

    private async void OnImagePresented(object? sender, ImagePresentedEventArgs e)
    {
        var fileName = Path.GetFileName(e.Path);
        _currentAnimated = e.IsAnimated;
        _currentMetadata = e.Metadata;
        _viewModel.ApplyMetadata(fileName, FormatResolution(e.Dimensions, e.IsAnimated), "Готово", e.Metadata);
        Title = $"FreshViewer — {fileName}";
        await Dispatcher.UIThread.InvokeAsync(UpdateResolutionLabel);
        _viewModel.StatusText = e.IsAnimated ? "Анимация загружена" : "Изображение загружено";
    }

    private static string FormatResolution((int Width, int Height) size, bool animated)
    {
        var resolution = $"{size.Width} × {size.Height}";
        return animated ? resolution + " · Анимация" : resolution;
    }

    private void UpdateResolutionLabel()
    {
        if (!_viewport.HasImage)
        {
            _viewModel.HasMetadataDetails = false;
            _viewModel.ResolutionText = null;
            _viewModel.GalleryPositionText = null;
            return;
        }

        var (width, height) = _viewport.GetEffectivePixelDimensions();
        if (width <= 0 || height <= 0)
        {
            _viewModel.ResolutionText = null;
            _viewModel.GalleryPositionText = null;
            return;
        }

        var resolution = $"{width} × {height}" + (_currentAnimated ? " · Анимация" : string.Empty);
        _viewModel.ResolutionText = resolution;

        if (!string.IsNullOrWhiteSpace(_currentDirectory) && _currentIndex >= 0 &&
            _currentIndex < _directoryFiles.Count)
        {
            _viewModel.GalleryPositionText = $"{_currentIndex + 1}/{_directoryFiles.Count}";
        }
        else
        {
            _viewModel.GalleryPositionText = null;
        }
    }

    private void OnImageFailed(object? sender, ImageFailedEventArgs e)
    {
        var (title, description) = DescribeLoadFailure(e);
        _viewModel.StatusText = title;
        _viewModel.ResetMetadata();
        _viewModel.ShowError(title, description);
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled)
        {
            return;
        }

        if (_shortcuts.TryMatch(e, out var action))
        {
            switch (action)
            {
                case ShortcutAction.OpenFile:
                    await PromptOpenFileAsync();
                    e.Handled = true;
                    return;
                case ShortcutAction.CopyFrame:
                    await CopyCurrentFrameToClipboardAsync();
                    e.Handled = true;
                    return;
                case ShortcutAction.Fullscreen:
                    ToggleFullscreen();
                    e.Handled = true;
                    return;
                case ShortcutAction.Fit:
                    _viewport.FitToView();
                    _viewModel.StatusText = "Изображение подогнано по экрану";
                    e.Handled = true;
                    return;
                case ShortcutAction.ZoomIn:
                    _viewport.ZoomIncrement(_viewport.ViewportCenterPoint, zoomIn: true);
                    _viewModel.StatusText = "Приближение";
                    e.Handled = true;
                    return;
                case ShortcutAction.ZoomOut:
                    _viewport.ZoomIncrement(_viewport.ViewportCenterPoint, zoomIn: false);
                    _viewModel.StatusText = "Отдаление";
                    e.Handled = true;
                    return;
                case ShortcutAction.RotateClockwise:
                    _viewport.RotateClockwise();
                    _viewModel.StatusText = "Поворот по часовой";
                    e.Handled = true;
                    return;
                case ShortcutAction.RotateCounterClockwise:
                    _viewport.RotateCounterClockwise();
                    _viewModel.StatusText = "Поворот против часовой";
                    e.Handled = true;
                    return;
                case ShortcutAction.ToggleUi:
                    ToggleInterfaceVisibility();
                    e.Handled = true;
                    return;
                case ShortcutAction.ToggleInfo:
                    ToggleInfoPanel();
                    e.Handled = true;
                    return;
                case ShortcutAction.ToggleSettings:
                    ToggleSettingsPanel();
                    e.Handled = true;
                    return;
                case ShortcutAction.Next:
                    await NavigateAsync(1);
                    e.Handled = true;
                    return;
                case ShortcutAction.Previous:
                    await NavigateAsync(-1);
                    e.Handled = true;
                    return;
                default:
                    break;
            }
        }

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private async Task NavigateAsync(int direction)
    {
        if (_directoryFiles.Count == 0 || _currentIndex < 0)
        {
            return;
        }

        var newIndex = (_currentIndex + direction + _directoryFiles.Count) % _directoryFiles.Count;
        if (newIndex == _currentIndex)
        {
            return;
        }

        _currentIndex = newIndex;
        var path = _directoryFiles[_currentIndex];
        var transition = direction > 0 ? ImageTransition.SlideFromRight : ImageTransition.SlideFromLeft;
        if (_viewport.IsFullscreen)
        {
            transition = ImageTransition.Instant;
        }

        await OpenImageAsync(path, transition);
        _viewModel.StatusText = direction > 0 ? "Следующее изображение" : "Предыдущее изображение";
    }

    private void ToggleFullscreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            var targetState = _wasMaximizedBeforeFullscreen
                ? WindowState.Maximized
                : (_previousWindowState == WindowState.FullScreen ? WindowState.Maximized : _previousWindowState);

            WindowState = WindowState.Normal;

            if (targetState == WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
            else
            {
                if (_restoreWindowSize is { } size)
                {
                    Width = size.Width;
                    Height = size.Height;
                }

                if (_restoreWindowPosition is { } position)
                {
                    Position = position;
                }

                WindowState = targetState;
            }

            _viewModel.IsUiVisible = _uiVisibleBeforeFullscreen;
            _viewModel.StatusText = "Полноэкранный режим выключен";
            UpdateResolutionLabel();
            _wasMaximizedBeforeFullscreen = false;
            return;
        }

        _previousWindowState = WindowState;
        _wasMaximizedBeforeFullscreen = WindowState == WindowState.Maximized;
        if (!_wasMaximizedBeforeFullscreen)
        {
            _restoreWindowSize = new Size(Width, Height);
            _restoreWindowPosition = Position;
        }
        else
        {
            _restoreWindowSize = null;
            _restoreWindowPosition = null;
        }

        _uiVisibleBeforeFullscreen = _viewModel.IsUiVisible;
        WindowState = WindowState.FullScreen;

        _viewModel.IsInfoPanelVisible = false;
        _viewModel.IsSettingsPanelVisible = false;
        _viewModel.IsUiVisible = false;

        _viewModel.StatusText = "Полноэкранный режим";
        UpdateResolutionLabel();
    }

    private async Task CopyCurrentFrameToClipboardAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        if (_viewport.GetCurrentFrameBitmap() is { } bitmap)
        {
            await using var stream = new MemoryStream();
            bitmap.Save(stream);
            var pngBytes = stream.ToArray();

            var dataObject = new DataObject();
            dataObject.Set("image/png", pngBytes);
            await clipboard.SetDataObjectAsync(dataObject);
            _viewModel.StatusText = "Кадр скопирован в буфер обмена";
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            _dragOverlay.IsVisible = true;
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        _dragOverlay.IsVisible = false;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        _dragOverlay.IsVisible = false;
        if (!e.Data.Contains(DataFormats.Files))
        {
            return;
        }

        var files = e.Data.GetFiles();
        if (files is null)
        {
            return;
        }

        var first = files.FirstOrDefault();
        if (first?.TryGetLocalPath() is { } localPath && File.Exists(localPath))
        {
            await OpenImageAsync(localPath);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        PropertyChanged -= OnWindowPropertyChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
        DisposeAnimationCts();
        _viewport.Dispose();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty && e.NewValue is WindowState state)
        {
            _viewport.IsFullscreen = state == WindowState.FullScreen;
            if (state != WindowState.FullScreen)
            {
                _previousWindowState = state;
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _viewModel))
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.IsUiVisible):
                _ = AnimateUiChromeAsync(_viewModel.IsUiVisible);
                if (_viewModel.IsUiVisible)
                {
                    _ = AnimateSummaryCardAsync(_viewModel.IsMetadataCardVisible);
                }
                else
                {
                    _ = AnimateSummaryCardAsync(false);
                }

                break;
            case nameof(MainWindowViewModel.SelectedTheme):
                ThemeManager.Apply(_viewModel.SelectedTheme);
                break;
            case nameof(MainWindowViewModel.SelectedLanguage):
                LocalizationService.ApplyLanguage(_viewModel.SelectedLanguage);
                break;
            case nameof(MainWindowViewModel.SelectedShortcutProfile):
                _shortcuts.ResetToProfile(_viewModel.SelectedShortcutProfile);
                break;
            case nameof(MainWindowViewModel.IsMetadataCardVisible):
                if (_viewModel.IsUiVisible)
                {
                    _ = AnimateSummaryCardAsync(_viewModel.IsMetadataCardVisible);
                }

                break;
            case nameof(MainWindowViewModel.IsInfoPanelVisible):
                CancelAnimation(ref _infoPanelAnimationCts);
                var infoCts = new CancellationTokenSource();
                _infoPanelAnimationCts = infoCts;
                _ = AnimatePanelAsync(
                    _infoPanel,
                    _viewModel.IsInfoPanelVisible,
                    InfoPanelHiddenOffset,
                    infoCts,
                    completedCts =>
                    {
                        if (ReferenceEquals(_infoPanelAnimationCts, completedCts))
                        {
                            _infoPanelAnimationCts = null;
                        }

                        completedCts.Dispose();
                    });
                break;
            case nameof(MainWindowViewModel.IsSettingsPanelVisible):
                CancelAnimation(ref _settingsPanelAnimationCts);
                var settingsCts = new CancellationTokenSource();
                _settingsPanelAnimationCts = settingsCts;
                _ = AnimatePanelAsync(
                    _settingsPanel,
                    _viewModel.IsSettingsPanelVisible,
                    SettingsPanelHiddenOffset,
                    settingsCts,
                    completedCts =>
                    {
                        if (ReferenceEquals(_settingsPanelAnimationCts, completedCts))
                        {
                            _settingsPanelAnimationCts = null;
                        }

                        completedCts.Dispose();
                    });
                break;
        }
    }

    private Task AnimateUiChromeAsync(bool show, bool immediate = false)
    {
        if (_uiChrome is not { } chrome)
        {
            return Task.CompletedTask;
        }

        _uiChromeTransform ??= EnsureTranslateTransform(chrome);

        if (immediate)
        {
            _uiChromeTransform.Y = show ? 0 : ChromeHiddenOffset;
            chrome.Opacity = show ? 1 : 0;
            chrome.IsVisible = show;
            chrome.IsHitTestVisible = show;
            return Task.CompletedTask;
        }

        CancelAnimation(ref _chromeAnimationCts);
        var cts = new CancellationTokenSource();
        _chromeAnimationCts = cts;

        if (show)
        {
            chrome.IsVisible = true;
            chrome.IsHitTestVisible = true;
        }
        else
        {
            chrome.IsHitTestVisible = false;
        }

        var fromY = _uiChromeTransform.Y;
        var toY = show ? 0 : ChromeHiddenOffset;
        var fromOpacity = chrome.Opacity;
        var toOpacity = show ? 1.0 : 0.0;

        var translationAnimation = new Animation
        {
            Duration = ChromeAnimationDuration,
            FillMode = FillMode.Both,
            Easing = SlideEasing,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(TranslateTransform.YProperty, fromY) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(TranslateTransform.YProperty, toY) }
                }
            }
        };

        var opacityAnimation = new Animation
        {
            Duration = ChromeAnimationDuration,
            FillMode = FillMode.Both,
            Easing = SlideEasing,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(Visual.OpacityProperty, fromOpacity) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(Visual.OpacityProperty, toOpacity) }
                }
            }
        };

        return RunCompositeAnimationAsync(
            chrome,
            _uiChromeTransform!,
            show,
            cts,
            completedCts =>
            {
                if (ReferenceEquals(_chromeAnimationCts, completedCts))
                {
                    _chromeAnimationCts = null;
                }

                completedCts.Dispose();
            },
            translationAnimation,
            opacityAnimation);
    }

    private Task AnimateSummaryCardAsync(bool show, bool immediate = false)
    {
        if (_summaryCard is not { } card)
        {
            return Task.CompletedTask;
        }

        _summaryCardTransform ??= EnsureTranslateTransform(card);

        if (immediate)
        {
            _summaryCardTransform.Y = show ? 0 : SummaryHiddenOffset;
            card.Opacity = show ? 1 : 0;
            card.IsVisible = show;
            card.IsHitTestVisible = show;
            return Task.CompletedTask;
        }

        CancelAnimation(ref _summaryAnimationCts);
        var cts = new CancellationTokenSource();
        _summaryAnimationCts = cts;

        if (show)
        {
            card.IsVisible = true;
            card.IsHitTestVisible = true;
        }
        else
        {
            card.IsHitTestVisible = false;
        }

        var fromY = _summaryCardTransform.Y;
        var toY = show ? 0 : SummaryHiddenOffset;
        var fromOpacity = card.Opacity;
        var toOpacity = show ? 1.0 : 0.0;

        var translationAnimation = new Animation
        {
            Duration = ChromeAnimationDuration,
            FillMode = FillMode.Both,
            Easing = SlideEasing,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(TranslateTransform.YProperty, fromY) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(TranslateTransform.YProperty, toY) }
                }
            }
        };

        var opacityAnimation = new Animation
        {
            Duration = ChromeAnimationDuration,
            FillMode = FillMode.Both,
            Easing = SlideEasing,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(Visual.OpacityProperty, fromOpacity) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(Visual.OpacityProperty, toOpacity) }
                }
            }
        };

        return RunCompositeAnimationAsync(
            card,
            _summaryCardTransform!,
            show,
            cts,
            completedCts =>
            {
                if (ReferenceEquals(_summaryAnimationCts, completedCts))
                {
                    _summaryAnimationCts = null;
                }

                completedCts.Dispose();
            },
            translationAnimation,
            opacityAnimation);
    }

    private Task AnimatePanelAsync(
        Border? panel,
        bool show,
        double hiddenOffset,
        CancellationTokenSource cts,
        Action<CancellationTokenSource> finalize,
        bool immediate = false)
    {
        if (panel is null)
        {
            finalize(cts);
            return Task.CompletedTask;
        }

        var transform = panel.RenderTransform as TranslateTransform ?? EnsureTranslateTransform(panel);

        if (immediate)
        {
            transform.X = show ? 0 : hiddenOffset;
            panel.Opacity = show ? 1 : 0;
            panel.IsVisible = show;
            panel.IsHitTestVisible = show;
            finalize(cts);
            return Task.CompletedTask;
        }

        if (show)
        {
            panel.IsVisible = true;
            panel.IsHitTestVisible = true;
        }
        else
        {
            panel.IsHitTestVisible = false;
        }

        var fromX = transform.X;
        var toX = show ? 0 : hiddenOffset;
        var fromOpacity = panel.Opacity;
        var toOpacity = show ? 1.0 : 0.0;

        var translationAnimation = new Animation
        {
            Duration = PanelAnimationDuration,
            FillMode = FillMode.Both,
            Easing = SlideEasing,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(TranslateTransform.XProperty, fromX) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(TranslateTransform.XProperty, toX) }
                }
            }
        };

        var opacityAnimation = new Animation
        {
            Duration = PanelAnimationDuration,
            FillMode = FillMode.Both,
            Easing = SlideEasing,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(Visual.OpacityProperty, fromOpacity) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(Visual.OpacityProperty, toOpacity) }
                }
            }
        };

        return RunCompositeAnimationAsync(panel, transform, show, cts, finalize, translationAnimation,
            opacityAnimation);
    }

    private static Task RunCompositeAnimationAsync(
        Control control,
        TranslateTransform transform,
        bool show,
        CancellationTokenSource cts,
        Action<CancellationTokenSource> finalize,
        Animation translation,
        Animation opacity)
    {
        return RunAsync();

        async Task RunAsync()
        {
            try
            {
                await Task.WhenAll(
                    translation.RunAsync(transform, cts.Token),
                    opacity.RunAsync(control, cts.Token));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                finalize(cts);
            }

            if (!show && !cts.IsCancellationRequested)
            {
                control.IsHitTestVisible = false;
                control.IsVisible = false;
            }
        }
    }

    private static void CancelAnimation(ref CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        if (!cts.IsCancellationRequested)
        {
            cts.Cancel();
        }

        cts.Dispose();
        cts = null;
    }

    private void DisposeAnimationCts()
    {
        CancelAnimation(ref _chromeAnimationCts);
        CancelAnimation(ref _summaryAnimationCts);
        CancelAnimation(ref _infoPanelAnimationCts);
        CancelAnimation(ref _settingsPanelAnimationCts);
    }

    private void ToggleInterfaceVisibility()
    {
        var isVisible = !_viewModel.IsUiVisible;
        _viewModel.IsUiVisible = isVisible;

        if (!isVisible && _viewModel.IsInfoPanelVisible)
        {
            _viewModel.IsInfoPanelVisible = false;
        }

        if (!isVisible && _viewModel.IsSettingsPanelVisible)
        {
            _viewModel.IsSettingsPanelVisible = false;
        }

        _viewModel.StatusText = isVisible
            ? "Интерфейс отображён"
            : "Интерфейс скрыт — нажмите Q для возврата";
    }

    private void ToggleSettingsPanel()
    {
        if (!_viewModel.IsUiVisible)
        {
            _viewModel.IsUiVisible = true;
        }

        var show = !_viewModel.IsSettingsPanelVisible;
        if (show && _viewModel.IsInfoPanelVisible)
        {
            _viewModel.IsInfoPanelVisible = false;
        }

        _viewModel.IsSettingsPanelVisible = show;

        _viewModel.StatusText = show
            ? "Панель настроек открыта"
            : "Панель настроек скрыта";
    }

    private void ToggleInfoPanel()
    {
        if (!_viewModel.IsUiVisible)
        {
            _viewModel.IsUiVisible = true;
        }

        var infoVisible = !_viewModel.IsInfoPanelVisible;
        if (infoVisible && _viewModel.IsSettingsPanelVisible)
        {
            _viewModel.IsSettingsPanelVisible = false;
        }

        _viewModel.IsInfoPanelVisible = infoVisible;

        if (infoVisible && _currentMetadata is null)
        {
            _viewModel.StatusText = "Нет метаданных для отображения";
        }
        else
        {
            _viewModel.StatusText = infoVisible
                ? "Панель информации открыта"
                : "Панель информации скрыта";
        }
    }

    private static (string Title, string Description) DescribeLoadFailure(ImageFailedEventArgs args)
    {
        var path = args.Path;
        var fileName = string.IsNullOrWhiteSpace(path) ? "Изображение" : Path.GetFileName(path);
        var ex = args.Exception;

        return ex switch
        {
            FileNotFoundException => ($"Файл не найден",
                $"Не удалось найти файл \"{fileName}\". Проверьте путь и попробуйте снова."),
            UnauthorizedAccessException => ($"Нет доступа",
                $"У FreshViewer нет доступа к файлу \"{fileName}\". Разрешите чтение или переместите файл."),
            MagickMissingDelegateErrorException or MagickException =>
                ($"Не удалось открыть {fileName}",
                    $"Magick.NET вернул ошибку: {ex.Message}\nПроверьте, поддерживается ли формат и не повреждён ли файл."),
            InvalidDataException or FormatException => ($"Файл повреждён",
                $"Файл \"{fileName}\" нельзя прочитать: {ex.Message}"),
            NotSupportedException => ($"Формат не поддерживается",
                $"Формат \"{Path.GetExtension(fileName)}\" пока не поддерживается или отключён."),
            OperationCanceledException => ($"Загрузка прервана", "Операция чтения изображения была отменена."),
            _ => ($"Ошибка загрузки", $"Не удалось открыть \"{fileName}\": {ex.Message}")
        };
    }

    private void OnErrorOverlayDismissed(object? sender, RoutedEventArgs e)
    {
        _viewModel.HideError();
    }

    private async void OnErrorOverlayOpenAnother(object? sender, RoutedEventArgs e)
    {
        _viewModel.HideError();
        await PromptOpenFileAsync();
    }

    private async void OnOpenButtonClicked(object? sender, RoutedEventArgs e)
    {
        await PromptOpenFileAsync();
    }

    private async void OnPreviousClicked(object? sender, RoutedEventArgs e)
    {
        await NavigateAsync(-1);
    }

    private async void OnNextClicked(object? sender, RoutedEventArgs e)
    {
        await NavigateAsync(1);
    }

    // Fit button removed from UI; keep keyboard shortcuts via ShortcutManager

    private void OnRotateClicked(object? sender, RoutedEventArgs e)
    {
        _viewport.RotateClockwise();
        _viewModel.StatusText = "Поворот по часовой";
    }

    private void OnToggleSettingsClicked(object? sender, RoutedEventArgs e)
    {
        ToggleSettingsPanel();
    }

    private void OnToggleInfoClicked(object? sender, RoutedEventArgs e)
    {
        ToggleInfoPanel();
    }

    // Fullscreen button removed from UI; F11 shortcut remains

    private async void OnExportShortcutsClicked(object? sender, RoutedEventArgs e)
    {
        var json = await _shortcuts.ExportToJsonAsync();
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(json);
        _viewModel.StatusText = "Профиль шорткатов скопирован в буфер обмена";
    }

    private async void OnImportShortcutsClicked(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        var text = await clipboard.GetTextAsync();
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                _shortcuts.ImportFromJson(text);
                _viewModel.StatusText = "Профиль шорткатов импортирован";
            }
            catch (Exception ex)
            {
                _viewModel.StatusText = $"Ошибка импорта: {ex.Message}";
            }
        }
    }

    private void OnResetShortcutsClicked(object? sender, RoutedEventArgs e)
    {
        _shortcuts.ResetToProfile(_viewModel.SelectedShortcutProfile);
        _viewModel.StatusText = "Шорткаты сброшены к профилю";
    }

    // Helper methods are unnecessary; property setters trigger PropertyChanged immediately.
}
