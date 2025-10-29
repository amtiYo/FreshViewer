using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FreshViewer.Models;
using FreshViewer.Services;

namespace FreshViewer.Controls;

/// <summary>
/// Displays still images and animations with kinetic panning, zooming, and rotation support.
/// </summary>
public sealed class ImageViewport : Control, IDisposable
{
    private const double LerpFactor = 0.18;
    private const double PanFriction = 0.85;
    private const double ZoomFactor = 1.2;
    private const double ZoomStep = 0.15;
    private const double MinScale = 0.05;
    private const double MaxScale = 20.0;
    private static readonly TimeSpan TimerInterval = TimeSpan.FromMilliseconds(8);

    private readonly DispatcherTimer _timer;
    private readonly ImageLoader _loader = new();

    private LoadedImage? _currentImage;
    private CancellationTokenSource? _loadingCts;
    private ImageMetadata? _currentMetadata;

    private double _currentScale = 1.0;
    private double _targetScale = 1.0;
    private double _fitScale = 1.0;
    private Vector _currentOffset;
    private Vector _targetOffset;
    private Vector _panVelocity;
    private double _backgroundOpacity;
    private double _targetBackgroundOpacity = 0.68;

    private bool _isPanning;
    private Point _lastPointerPosition;

    private bool _openingAnimationActive;
    private double _openingScale = 0.8;
    private double _openingOpacity;

    private bool _closingAnimationActive;
    private double _closingScale = 1.0;
    private double _closingOpacity = 1.0;

    private bool _needsRedraw;
    private bool _fitPending;

    private double _rotation;
    private double _targetRotation;

    private int _animationFrameIndex;
    private TimeSpan _animationFrameElapsed;
    private int _completedLoops;
    private bool _isFullscreen;
    private ImageTransition _pendingTransition = ImageTransition.None;

    private DateTime _lastTick = DateTime.UtcNow;

    /// <summary>
    /// Raised when an image or animation finishes loading.
    /// </summary>
    public event EventHandler<ImagePresentedEventArgs>? ImagePresented;
    /// <summary>
    /// Raised when the viewport transform (zoom, offset, or rotation) changes.
    /// </summary>
    public event EventHandler? ViewStateChanged;
    /// <summary>
    /// Raised when loading an image fails with an exception.
    /// </summary>
    public event EventHandler<ImageFailedEventArgs>? ImageFailed;
    /// <summary>
    /// Raised when the user clicks the background instead of the image content.
    /// </summary>
    public event EventHandler? BackgroundClicked;

    public ImageViewport()
    {
        ClipToBounds = false;
        Focusable = true;

        _timer = new DispatcherTimer(TimerInterval, DispatcherPriority.Render, (_, _) => OnTick());
        _timer.Start();

        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnPointerWheelChanged, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BoundsProperty)
        {
            RecalculateFitScale();
            if (_fitPending)
            {
                ApplyFitToView();
            }

            if (_pendingTransition != ImageTransition.None)
            {
                if (ApplyPendingTransition())
                {
                    _needsRedraw = true;
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the viewport is rendered in fullscreen mode.
    /// </summary>
    public bool IsFullscreen
    {
        get => _isFullscreen;
        set
        {
            if (_isFullscreen == value)
            {
                return;
            }

            _isFullscreen = value;
            _targetBackgroundOpacity = value ? 1.0 : 0.68;
            AnimateViewportRealignment(value);
            _needsRedraw = true;
        }
    }

    /// <summary>
    /// Gets a value indicating whether an image is currently loaded.
    /// </summary>
    public bool HasImage => _currentImage is not null;

    /// <summary>
    /// Determines whether the specified point is within the projected image bounds.
    /// </summary>
    public bool IsPointWithinImage(Point point) => IsPointOnImage(point);

    /// <summary>
    /// Gets the current rotation angle in degrees.
    /// </summary>
    public double Rotation => _rotation;

    /// <summary>
    /// Gets the current zoom factor applied to the image.
    /// </summary>
    public double CurrentScale => _currentScale;

    /// <summary>
    /// Gets the current pan offset in device-independent units.
    /// </summary>
    public Vector CurrentOffset => _currentOffset;

    /// <summary>
    /// Gets the geometric center of the viewport.
    /// </summary>
    public Point ViewportCenterPoint => new Point(Bounds.Width / 2.0, Bounds.Height / 2.0);

    /// <summary>
    /// Loads an image asynchronously and optionally applies a transition animation.
    /// </summary>
    public async Task LoadImageAsync(string path, ImageTransition transition = ImageTransition.FadeIn)
    {
        CancelLoading();
        _loadingCts = new CancellationTokenSource();
        _pendingTransition = transition;
        var requestedPath = path;

        try
        {
            var loaded = await _loader.LoadAsync(path, _loadingCts.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => PresentLoadedImage(loaded), DispatcherPriority.Render);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ImageFailed?.Invoke(this, new ImageFailedEventArgs(requestedPath, ex));
            });
        }
    }

    /// <summary>
    /// Resets zoom, rotation, and offset to fit the current image.
    /// </summary>
    public void ResetView()
    {
        if (!HasImage)
        {
            return;
        }

        _rotation = 0;
        RecalculateFitScale();
        ApplyFitToView();
    }

    /// <summary>
    /// Sets the target zoom so that the image fits inside the viewport bounds.
    /// </summary>
    public void FitToView()
    {
        if (!HasImage)
        {
            return;
        }

        RecalculateFitScale();
        _targetScale = _fitScale;
        _panVelocity = Vector.Zero;

        if (!double.IsNaN(_fitScale) && _fitScale > 0)
        {
            var scaleDiff = _targetScale - _currentScale;
            if (Math.Abs(scaleDiff) > 0.01)
            {
                _currentScale += scaleDiff * 0.35;
            }
            else
            {
                _currentScale = _targetScale;
            }
        }

        var center = GetViewportCenter();
        var offsetDiff = center - _targetOffset;
        _targetOffset = center;

        if (offsetDiff.Length > 1.0)
        {
            _currentOffset += offsetDiff * 0.4;
        }
        else
        {
            _currentOffset = center;
        }

        _needsRedraw = true;
    }

    public void ZoomTo(double newScale, Point focusPoint)
    {
        if (!HasImage)
        {
            return;
        }

        var clamped = Math.Clamp(newScale, MinScale, MaxScale);
        var referenceScale = _targetScale;
        if (referenceScale <= 0)
        {
            referenceScale = 0.001;
        }

        var focusVector = new Vector(focusPoint.X, focusPoint.Y);
        var delta = focusVector - _targetOffset;
        var ratio = clamped / referenceScale;
        _targetOffset = focusVector - delta * ratio;
        _targetScale = clamped;
        _needsRedraw = true;
    }

    /// <summary>
    /// Adjusts the zoom by a fixed step around the supplied focus point.
    /// </summary>
    public void ZoomIncrement(Point focusPoint, bool zoomIn)
    {
        var factor = zoomIn ? ZoomFactor : (1.0 / ZoomFactor);
        ZoomTo(_targetScale * factor, focusPoint);
    }

    /// <summary>
    /// Applies mouse wheel zooming using the configured step factor.
    /// </summary>
    public void ZoomWithWheel(Point focusPoint, double wheelDelta)
    {
        var zoomFactor = 1.0 + wheelDelta * ZoomStep;
        if (zoomFactor < 0.05)
        {
            zoomFactor = 0.05;
        }
        ZoomTo(_targetScale * zoomFactor, focusPoint);
    }

    /// <summary>
    /// Rotates the image clockwise by 90 degrees.
    /// </summary>
    public void RotateClockwise()
    {
        if (!HasImage)
        {
            return;
        }

        _panVelocity = Vector.Zero;
        _targetRotation = NormalizeAngle(_targetRotation + 90);
        RecalculateFitScale();
        _needsRedraw = true;
        ViewStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Rotates the image counter-clockwise by 90 degrees.
    /// </summary>
    public void RotateCounterClockwise()
    {
        if (!HasImage)
        {
            return;
        }

        _panVelocity = Vector.Zero;
        _targetRotation = NormalizeAngle(_targetRotation - 90);
        RecalculateFitScale();
        _needsRedraw = true;
        ViewStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns the pixel dimensions taking rotation into account.
    /// </summary>
    public (int width, int height) GetEffectivePixelDimensions()
    {
        if (!HasImage)
        {
            return (0, 0);
        }

        var pixelSize = _currentImage!.PixelSize;
        return IsRotationSwappingDimensions()
            ? (pixelSize.Height, pixelSize.Width)
            : (pixelSize.Width, pixelSize.Height);
    }

    /// <summary>
    /// Retrieves the bitmap backing the current static image or animation frame.
    /// </summary>
    public Bitmap? GetCurrentFrameBitmap()
    {
        if (_currentImage is null)
        {
            return null;
        }

        if (_currentImage.IsAnimated)
        {
            var frames = _currentImage.Animation!.Frames;
            if (frames.Count == 0)
            {
                return null;
            }

            return frames[Math.Clamp(_animationFrameIndex, 0, frames.Count - 1)].Bitmap;
        }

        return _currentImage.Bitmap;
    }

    /// <summary>
    /// Gets the metadata associated with the currently loaded image.
    /// </summary>
    public ImageMetadata? CurrentMetadata => _currentMetadata;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var alpha = (byte)Math.Clamp(_backgroundOpacity * 255.0, 0, 255);
        var r = _isFullscreen ? (byte)0 : (byte)10;
        var g = _isFullscreen ? (byte)0 : (byte)12;
        var b = _isFullscreen ? (byte)0 : (byte)18;
        var backgroundColor = Color.FromArgb(alpha, r, g, b);
        context.FillRectangle(new SolidColorBrush(backgroundColor), bounds);

        var bitmap = GetCurrentFrameBitmap();
        if (bitmap is null)
        {
            return;
        }

        var finalScale = _currentScale;
        if (_openingAnimationActive)
        {
            finalScale *= _openingScale;
        }
        else if (_closingAnimationActive)
        {
            finalScale *= _closingScale;
        }

        var centerOffset = _currentOffset;
        var destRect = new Rect(-bitmap.PixelSize.Width / 2.0, -bitmap.PixelSize.Height / 2.0,
            bitmap.PixelSize.Width, bitmap.PixelSize.Height);

        using var translate = context.PushTransform(Matrix.CreateTranslation(centerOffset.X, centerOffset.Y));
        IDisposable? rotate = null;
        IDisposable? scale = null;

        if (Math.Abs(_rotation) > 0.01)
        {
            var radians = Math.PI * _rotation / 180.0;
            rotate = context.PushTransform(Matrix.CreateRotation(radians));
        }

        if (Math.Abs(finalScale - 1.0) > 0.001)
        {
            scale = context.PushTransform(Matrix.CreateScale(finalScale, finalScale));
        }

        var opacity = 1.0;
        if (_openingAnimationActive)
        {
            opacity *= _openingOpacity;
        }
        else if (_closingAnimationActive)
        {
            opacity *= _closingOpacity;
        }

        var sourceRect = new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        using (var opacityScope = context.PushOpacity(opacity))
        {
            context.DrawImage(bitmap, sourceRect, destRect);
        }

        scale?.Dispose();
        rotate?.Dispose();
    }

    private void PresentLoadedImage(LoadedImage loaded)
    {
        _currentImage?.Dispose();
        _currentImage = loaded;
        _currentMetadata = loaded.Metadata;

        _rotation = 0.0;
        _targetRotation = 0.0;
        _animationFrameIndex = 0;
        _animationFrameElapsed = TimeSpan.Zero;
        _completedLoops = 0;
        _panVelocity = Vector.Zero;
        _openingAnimationActive = false;
        _openingScale = 1.0;
        _openingOpacity = 1.0;
        _closingAnimationActive = false;
        _closingOpacity = 1.0;
        _closingScale = 1.0;

        if (_pendingTransition == ImageTransition.None)
        {
            _pendingTransition = ImageTransition.FadeIn;
        }

        _backgroundOpacity = _pendingTransition == ImageTransition.FadeIn
            ? 0
            : _targetBackgroundOpacity;

        _fitPending = Bounds.Width <= 0 || Bounds.Height <= 0;
        RecalculateFitScale();
        ApplyFitToView();

        if (!ApplyPendingTransition())
        {
            _needsRedraw = true;
        }

        ImagePresented?.Invoke(this, new ImagePresentedEventArgs(loaded.Path, GetEffectivePixelDimensions(), loaded.IsAnimated, _currentMetadata));
        ViewStateChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void ApplyFitToView()
    {
        if (!HasImage)
        {
            return;
        }

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            _fitPending = true;
            return;
        }

        _fitPending = false;
        var center = GetViewportCenter();
        _targetOffset = center;
        _currentOffset = center;
        _targetScale = _fitScale;
        _currentScale = _isFullscreen ? _fitScale : _fitScale * 0.85;
        _needsRedraw = true;
    }

    private bool ApplyPendingTransition()
    {
        if (_pendingTransition == ImageTransition.None)
        {
            return false;
        }

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        var center = GetViewportCenter();

        switch (_pendingTransition)
        {
            case ImageTransition.SlideFromLeft:
                PrepareSlideTransition(center, new Vector(-bounds.Width * 0.55, 0));
                break;
            case ImageTransition.SlideFromRight:
                PrepareSlideTransition(center, new Vector(bounds.Width * 0.55, 0));
                break;
            case ImageTransition.Instant:
                _openingAnimationActive = false;
                _openingOpacity = 1.0;
                _currentOffset = center;
                _targetOffset = center;
                _currentScale = _targetScale;
                _backgroundOpacity = _targetBackgroundOpacity;
                break;
            case ImageTransition.FadeIn:
            default:
                _openingAnimationActive = true;
                _openingScale = 0.92;
                _openingOpacity = 0.0;
                break;
        }

        _pendingTransition = ImageTransition.None;
        _needsRedraw = true;
        return true;
    }

    private void PrepareSlideTransition(Vector center, Vector offset)
    {
        _openingAnimationActive = false;
        _openingOpacity = 1.0;
        _currentOffset = center + offset;
        _targetOffset = center;
        _currentScale = _targetScale;
        _backgroundOpacity = _targetBackgroundOpacity;
    }

    private bool IsPointOnImage(Point point)
    {
        if (!HasImage)
        {
            return false;
        }

        var bitmap = GetCurrentFrameBitmap();
        if (bitmap is null)
        {
            return false;
        }

        if (!TryGetBitmapSize(bitmap, out var pixelSize))
        {
            return false;
        }

        var finalScale = _currentScale;
        var centerPoint = new Point(_currentOffset.X, _currentOffset.Y);
        var local = new Vector(point.X - centerPoint.X, point.Y - centerPoint.Y);

        if (Math.Abs(_rotation) > 0.001)
        {
            var radians = Math.PI * _rotation / 180.0;
            var cos = Math.Cos(-radians);
            var sin = Math.Sin(-radians);
            var rotatedX = local.X * cos - local.Y * sin;
            var rotatedY = local.X * sin + local.Y * cos;
            local = new Vector(rotatedX, rotatedY);
        }

        if (Math.Abs(finalScale) < 0.0001)
        {
            return false;
        }

        local /= finalScale;
        var halfWidth = pixelSize.Width / 2.0;
        var halfHeight = pixelSize.Height / 2.0;

        return Math.Abs(local.X) <= halfWidth && Math.Abs(local.Y) <= halfHeight;
    }

    private static bool TryGetBitmapSize(Bitmap bitmap, out PixelSize size)
    {
        try
        {
            size = bitmap.PixelSize;
            return size.Width > 0 && size.Height > 0;
        }
        catch (Exception ex) when (ex is NullReferenceException or ObjectDisposedException)
        {
            size = default;
            return false;
        }
    }

    private void AnimateViewportRealignment(bool enteringFullscreen)
    {
        _panVelocity = Vector.Zero;
        _pendingTransition = ImageTransition.None;

        if (!HasImage)
        {
            _backgroundOpacity = enteringFullscreen ? 1.0 : _targetBackgroundOpacity;
            return;
        }

        RecalculateFitScale();
        _fitPending = false;

        var center = GetViewportCenter();
        _targetOffset = center;
        _currentOffset = center;
        _targetScale = _fitScale;

        if (enteringFullscreen)
        {
            _currentScale = Math.Min(_currentScale, Math.Max(_targetScale, 0.001) * 0.96);
        }
        else
        {
            _currentScale = Math.Min(_currentScale, Math.Max(_targetScale, 0.001) * 1.02);
        }

        _openingAnimationActive = true;
        _openingOpacity = 0.0;
        _openingScale = enteringFullscreen ? 0.96 : 1.04;
        _backgroundOpacity = enteringFullscreen ? 0.0 : _targetBackgroundOpacity;
        _needsRedraw = true;
    }

    private void RecalculateFitScale()
    {
        if (!HasImage)
        {
            return;
        }

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            _fitPending = true;
            return;
        }

        var (width, height) = GetEffectivePixelDimensions();
        if (width <= 0 || height <= 0)
        {
            _fitScale = 1.0;
            return;
        }

        var padding = _isFullscreen ? 1.0 : 0.9;
        var scaleX = (bounds.Width * padding) / width;
        var scaleY = (bounds.Height * padding) / height;
        var fitScale = Math.Min(scaleX, scaleY);
        _fitScale = _isFullscreen ? fitScale : Math.Min(1.0, fitScale);
        if (double.IsNaN(_fitScale) || double.IsInfinity(_fitScale))
        {
            _fitScale = 1.0;
        }
    }

    private Vector GetViewportCenter()
    {
        var bounds = Bounds;
        return new Vector(bounds.Width / 2.0, bounds.Height / 2.0);
    }

    private bool IsRotationSwappingDimensions()
    {
        var rotated = Math.Abs((NormalizeAngle(_targetRotation) % 180)) > 0.01;
        return rotated;
    }

    private void OnTick()
    {
        var now = DateTime.UtcNow;
        var delta = now - _lastTick;
        if (delta > TimeSpan.FromMilliseconds(50))
        {
            delta = TimeSpan.FromMilliseconds(16);
        }
        _lastTick = now;

        var changed = false;

        if (_currentImage?.IsAnimated == true)
        {
            var animation = _currentImage.Animation!;
            if (animation.Frames.Count > 0)
            {
                _animationFrameElapsed += delta;
                var frame = animation.Frames[_animationFrameIndex];
                var duration = frame.Duration.TotalMilliseconds < 5 ? TimeSpan.FromMilliseconds(16) : frame.Duration;
                if (_animationFrameElapsed >= duration)
                {
                    _animationFrameElapsed -= duration;
                    _animationFrameIndex++;
                    if (_animationFrameIndex >= animation.Frames.Count)
                    {
                        _animationFrameIndex = 0;
                        _completedLoops++;
                        if (animation.LoopCount > 0 && _completedLoops >= animation.LoopCount)
                        {
                            _animationFrameIndex = animation.Frames.Count - 1;
                        }
                    }
                    changed = true;
                }
            }
        }

        if (_openingAnimationActive)
        {
            _openingScale = LerpTo(_openingScale, 1.0, 0.15);
            _openingOpacity = LerpTo(_openingOpacity, 1.0, 0.2);
            if (Math.Abs(_openingScale - 1.0) < 0.01 && Math.Abs(_openingOpacity - 1.0) < 0.01)
            {
                _openingScale = 1.0;
                _openingOpacity = 1.0;
                _openingAnimationActive = false;
            }
            changed = true;
        }

        if (_closingAnimationActive)
        {
            _closingScale = LerpTo(_closingScale, 0.7, 0.25);
            _closingOpacity = LerpTo(_closingOpacity, 0.0, 0.25);
            changed = true;
        }

        if (Math.Abs(_targetBackgroundOpacity - _backgroundOpacity) > 0.01)
        {
            _backgroundOpacity = LerpTo(_backgroundOpacity, _targetBackgroundOpacity, 0.15);
            changed = true;
        }

        if (!_isPanning && (_panVelocity.X != 0 || _panVelocity.Y != 0))
        {
            _targetOffset += _panVelocity;
            _panVelocity *= PanFriction;
            if (Math.Abs(_panVelocity.X) < 0.1 && Math.Abs(_panVelocity.Y) < 0.1)
            {
                _panVelocity = Vector.Zero;
            }
            changed = true;
        }

        var scaleDiff = _targetScale - _currentScale;
        if (Math.Abs(scaleDiff) > 0.0005)
        {
            _currentScale += scaleDiff * LerpFactor;
            changed = true;
        }

        var offsetDiff = _targetOffset - _currentOffset;
        if (Math.Abs(offsetDiff.X) > 0.1 || Math.Abs(offsetDiff.Y) > 0.1)
        {
            _currentOffset += offsetDiff * LerpFactor;
            changed = true;
        }

        var rotationDiff = GetShortestRotationDelta(_rotation, _targetRotation);
        if (Math.Abs(rotationDiff) > 0.05)
        {
            _rotation = NormalizeAngle(_rotation + rotationDiff * 0.18);
            changed = true;
        }
        else if (Math.Abs(rotationDiff) > 0.001)
        {
            _rotation = NormalizeAngle(_targetRotation);
            changed = true;
        }

        if (_needsRedraw || changed)
        {
            _needsRedraw = false;
            InvalidateVisual();
        }
    }

    private static double LerpTo(double current, double target, double factor)
        => current + (target - current) * factor;

    private static double NormalizeAngle(double angle)
    {
        angle %= 360;
        if (angle < 0)
        {
            angle += 360;
        }

        return angle;
    }

    private static double GetShortestRotationDelta(double current, double target)
    {
        var diff = target - current;
        diff %= 360;
        if (diff > 180)
        {
            diff -= 360;
        }
        else if (diff < -180)
        {
            diff += 360;
        }

        return diff;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this) is { Properties.IsLeftButtonPressed: true } point)
        {
            if (!HasImage || !IsPointOnImage(point.Position))
            {
                BackgroundClicked?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            _isPanning = true;
            _lastPointerPosition = point.Position;
            _panVelocity = Vector.Zero;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!HasImage || !_isPanning)
        {
            return;
        }

        var position = e.GetPosition(this);
        var delta = position - _lastPointerPosition;
        _currentOffset += delta;
        _targetOffset = _currentOffset;
        _panVelocity = delta * 0.6;
        _lastPointerPosition = position;
        _needsRedraw = true;
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isPanning = false;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!HasImage)
        {
            return;
        }

        ZoomWithWheel(e.GetPosition(this), e.Delta.Y);
        e.Handled = true;
    }

    private void CancelLoading()
    {
        _loadingCts?.Cancel();
        _loadingCts?.Dispose();
        _loadingCts = null;
    }

    /// <summary>
    /// Releases image resources and stops the internal animation timer.
    /// </summary>
    public void Dispose()
    {
        CancelLoading();
        _currentImage?.Dispose();
        _timer.Stop();
    }
}

/// <summary>
/// Event arguments describing the result of presenting an image.
/// </summary>
public sealed class ImagePresentedEventArgs : EventArgs
{
    public ImagePresentedEventArgs(string path, (int width, int height) dimensions, bool isAnimated, ImageMetadata? metadata)
    {
        Path = path;
        Dimensions = dimensions;
        IsAnimated = isAnimated;
        Metadata = metadata;
    }

    /// <summary>
    /// Gets the path of the presented image.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the effective pixel dimensions of the image or animation.
    /// </summary>
    public (int Width, int Height) Dimensions { get; }

    /// <summary>
    /// Gets a value indicating whether the presented resource is animated.
    /// </summary>
    public bool IsAnimated { get; }

    /// <summary>
    /// Gets the metadata extracted during the load phase.
    /// </summary>
    public ImageMetadata? Metadata { get; }
}

/// <summary>
/// Defines the supported transition animations when presenting images.
/// </summary>
public enum ImageTransition
{
    None,
    FadeIn,
    SlideFromLeft,
    SlideFromRight,
    Instant
}

/// <summary>
/// Event arguments describing a failure while loading an image.
/// </summary>
public sealed class ImageFailedEventArgs : EventArgs
{
    public ImageFailedEventArgs(string path, Exception exception)
    {
        Path = path;
        Exception = exception;
    }

    /// <summary>
    /// Gets the path of the image that failed to load.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the exception describing the failure.
    /// </summary>
    public Exception Exception { get; }
}
