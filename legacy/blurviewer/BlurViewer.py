"""
BlurViewer v0.8.1
Professional image viewer with advanced format support and smooth animations
"""

import sys
import os
from pathlib import Path
from typing import Optional
import math

from PySide6.QtCore import Qt, QTimer, QPointF, QRectF, QThread, Signal, QEasingCurve
from PySide6.QtGui import (QPixmap, QImageReader, QPainter, QWheelEvent, QMouseEvent,
                           QColor, QImage, QGuiApplication, QMovie)
from PySide6.QtWidgets import QApplication, QWidget, QFileDialog


class ImageLoader(QThread):
    """Background thread for loading heavy image formats"""

    imageLoaded = Signal(str, QPixmap)
    animatedImageLoaded = Signal(str)
    loadFailed = Signal(str, str)

    _plugins_registered = False
    
    def __init__(self, path):
        super().__init__()
        self.path = path
    
    def run(self):
        try:
            # Quick format validation for common misnamed files
            if not os.path.exists(self.path):
                self.loadFailed.emit(self.path, "File does not exist")
                return
            
            # Check file header for common format issues
            try:
                with open(self.path, 'rb') as f:
                    header = f.read(16)
                    
                    # Check for video files with wrong extensions
                    if self.path.lower().endswith(('.gif', '.png', '.jpg')) and header[4:8] == b'ftyp':
                        self.loadFailed.emit(self.path, "This is a video file (MP4), not an image. Use a video player instead.")
                        return
            except Exception:
                pass
            
            # Check if it's an animated format first
            if self.isInterruptionRequested():
                return

            if Path(self.path).suffix.lower() in BlurViewer.ANIMATED_EXTENSIONS:
                movie = self._try_load_animated(self.path)
                if movie and movie.isValid():
                    # Try to start movie to verify it works
                    try:
                        movie.jumpToFrame(0)
                        first_frame = movie.currentPixmap()
                        if not first_frame.isNull():
                            if self.isInterruptionRequested():
                                return
                            self.animatedImageLoaded.emit(self.path)
                            return
                    except Exception:
                        pass

            # Load as static image
            pixmap = self._load_image_comprehensive(self.path)
            if pixmap and not pixmap.isNull():
                if self.isInterruptionRequested():
                    return
                self.imageLoaded.emit(self.path, pixmap)
            else:
                self.loadFailed.emit(self.path, "Failed to load image")
        except Exception as e:
            self.loadFailed.emit(self.path, str(e))
    
    def _try_load_animated(self, path: str) -> QMovie:
        """Try to load animated image formats using QMovie"""
        try:
            normalized_path = os.path.normpath(path)
            movie = QMovie(normalized_path)
            if movie.isValid():
                return movie
        except Exception:
            pass
        return None

    def _register_all_plugins(self):
        """Register all available image format plugins"""
        if ImageLoader._plugins_registered:
            return

        try:
            import pillow_heif  # type: ignore

            pillow_heif.register_heif_opener()
        except Exception:
            pass

        try:
            import pillow_avif  # type: ignore

            if hasattr(pillow_avif, "register_avif_opener"):
                pillow_avif.register_avif_opener()
        except Exception:
            pass

        ImageLoader._plugins_registered = True
    
    def _load_image_comprehensive(self, path: str) -> QPixmap:
        """Comprehensive image loader supporting all formats"""
        self._register_all_plugins()

        if self.isInterruptionRequested():
            return None

        # Try Qt native formats first
        normalized_path = os.path.normpath(path)

        reader = QImageReader(normalized_path)
        reader.setAutoTransform(True)
        if reader.canRead():
            qimg = reader.read()
            if qimg and not qimg.isNull():
                if qimg.format() != QImage.Format_RGBA8888:
                    qimg = qimg.convertToFormat(QImage.Format_RGBA8888)
                return QPixmap.fromImage(qimg)

        # Try with Pillow for other formats
        try:
            from PIL import Image

            with open(normalized_path, 'rb') as f:
                if self.isInterruptionRequested():
                    return None

                im = Image.open(f)
                im.load()

                if im.mode != 'RGBA':
                    im = im.convert('RGBA')

                data = im.tobytes('raw', 'RGBA')
                bytes_per_line = im.width * 4
                qimg = QImage(data, im.width, im.height, bytes_per_line, QImage.Format_RGBA8888)
                if not qimg.isNull():
                    return QPixmap.fromImage(qimg.copy())
        except Exception:
            pass

        return None


class BlurViewer(QWidget):
    # Animation constants - optimized values
    LERP_FACTOR = 0.18  # Slightly faster interpolation
    PAN_FRICTION = 0.85  # Slightly less friction for smoother panning
    NAVIGATION_SPEED = 0.045  # Slower for smoother transitions
    ZOOM_FACTOR = 1.2
    ZOOM_STEP = 0.15
    
    # Scale limits
    MIN_SCALE = 0.1
    MAX_SCALE = 20.0
    MIN_REFRESH_INTERVAL = 8  # 125 FPS max
    
    # Opacity values
    WINDOWED_BG_OPACITY = 200.0
    FULLSCREEN_BG_OPACITY = 250.0
    
    # Supported file extensions
    ANIMATED_EXTENSIONS = {'.gif', '.mng'}
    RAW_EXTENSIONS = {'.cr2', '.cr3', '.nef', '.arw', '.dng', '.raf', '.orf', 
                     '.rw2', '.pef', '.srw', '.x3f', '.mrw', '.dcr', '.kdc', 
                     '.erf', '.mef', '.mos', '.ptx', '.r3d', '.fff', '.iiq'}
    
    def __init__(self, image_path: Optional[str] = None):
        super().__init__()
        
        # Window setup
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.Window)
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setFocusPolicy(Qt.StrongFocus)
        self.setAcceptDrops(True)
        
        # Cache screen geometry to avoid repeated calls
        self._screen_geom = None
        self._screen_center = None
        self._current_pixmap_cache = None  # Cache for current pixmap

        # Image state
        self.pixmap: Optional[QPixmap] = None
        self.image_path = None
        self.movie: Optional[QMovie] = None
        self.rotation = 0.0
        
        # Directory navigation
        self.current_directory = None
        self.image_files = []
        self.current_index = -1

        # Transform state
        self.target_scale = 1.0
        self.current_scale = 1.0
        self.target_offset = QPointF(0, 0)
        self.current_offset = QPointF(0, 0)
        self.fit_scale = 1.0
        
        # Animation parameters
        self.lerp_factor = self.LERP_FACTOR
        self.pan_friction = self.PAN_FRICTION
        
        # Navigation animation
        self.navigation_animation = False
        self.navigation_direction = 0
        self.navigation_progress = 0.0
        self.old_pixmap = None
        self.new_pixmap = None
        
        # Interaction state
        self.is_panning = False
        self.last_mouse_pos = QPointF(0, 0)
        self.pan_velocity = QPointF(0, 0)
        
        # Opening animation
        self.opening_animation = True
        self.opening_scale = 0.8
        self.opening_opacity = 0.0
        
        # Closing animation
        self.closing_animation = False
        self.closing_scale = 1.0
        self.closing_opacity = 1.0
        
        # Background fade
        self.background_opacity = 0.0
        self.target_background_opacity = self.WINDOWED_BG_OPACITY
        
        # Fullscreen state
        self.is_fullscreen = False
        self.saved_scale = 1.0
        self.saved_offset = QPointF(0, 0)
        
        # Performance optimization
        self.update_pending = False
        self.loading_thread: Optional[ImageLoader] = None
        self._needs_cache_update = True  # Flag for pixmap cache
        self._active_request_path: Optional[str] = None

        # Main animation timer with adaptive FPS
        self.timer = QTimer(self)
        refresh_interval = self._get_monitor_refresh_interval()
        self.timer.setInterval(refresh_interval)
        self.timer.timeout.connect(self.animate)
        self.timer.start()

        # Load image
        if image_path:
            self.load_image(image_path)
        else:
            self.open_dialog_and_load()

    def _get_monitor_refresh_interval(self) -> int:
        """Get monitor refresh interval in milliseconds"""
        try:
            screen = QApplication.primaryScreen()
            if screen:
                refresh_rate = screen.refreshRate()
                if refresh_rate > 0:
                    interval = int(1000.0 / refresh_rate)
                    return max(interval, self.MIN_REFRESH_INTERVAL)
        except Exception:
            pass
        
        return 16  # Fallback to 60 FPS

    def _get_current_pixmap(self) -> QPixmap:
        """Get current pixmap with caching"""
        if self._needs_cache_update:
            if self.pixmap:
                self._current_pixmap_cache = self.pixmap
            elif self.movie:
                self._current_pixmap_cache = self.movie.currentPixmap()
            else:
                self._current_pixmap_cache = QPixmap()
            self._needs_cache_update = False
        
        return self._current_pixmap_cache or QPixmap()

    def _invalidate_pixmap_cache(self):
        """Invalidate the pixmap cache"""
        self._needs_cache_update = True

    def _get_screen_info(self):
        """Get cached screen geometry and center"""
        if self._screen_geom is None:
            self._screen_geom = QApplication.primaryScreen().availableGeometry()
            self._screen_center = QPointF(self._screen_geom.center())
        return self._screen_geom, self._screen_center

    def _clear_screen_cache(self):
        """Clear cached screen info"""
        self._screen_geom = None
        self._screen_center = None

    def _stop_loading_thread(self):
        """Stop the active loading thread if it is running"""
        if self.loading_thread:
            self.loading_thread.requestInterruption()
            self.loading_thread.wait()
            self.loading_thread.deleteLater()
            self.loading_thread = None
        self._active_request_path = None

    def _on_loader_finished(self):
        """Cleanup when the loading thread finishes"""
        thread = self.sender()
        if thread is self.loading_thread:
            self.loading_thread = None
        if thread:
            thread.deleteLater()

    def _start_loading_thread(self, path: str, static_slot, animated_slot):
        """Helper to start a loading thread for the given path"""
        normalized_path = os.path.normpath(path)
        self._stop_loading_thread()
        self._active_request_path = normalized_path

        thread = ImageLoader(normalized_path)
        thread.imageLoaded.connect(static_slot)
        thread.animatedImageLoaded.connect(animated_slot)
        thread.loadFailed.connect(self._on_load_failed)
        thread.finished.connect(self._on_loader_finished)

        self.loading_thread = thread
        thread.start()

    def get_image_files_in_directory(self, directory_path: str):
        """Get list of supported image files in directory"""
        if not directory_path:
            return []
        
        directory = Path(directory_path)
        if not directory.is_dir():
            return []
        
        # Supported extensions
        supported_exts = {
            '.png', '.jpg', '.jpeg', '.bmp', '.gif', '.mng', '.webp', '.tiff', '.tif', '.ico', '.svg',
            '.pbm', '.pgm', '.ppm', '.xbm', '.xpm',
            '.heic', '.heif', '.avif', '.jxl',
            '.fits', '.hdr', '.exr', '.pic', '.psd'
        } | self.RAW_EXTENSIONS
        
        image_files = []
        try:
            for file_path in directory.iterdir():
                if file_path.is_file() and file_path.suffix.lower() in supported_exts:
                    image_files.append(str(file_path))
        except (OSError, PermissionError):
            return []
        
        return sorted(image_files, key=lambda x: Path(x).name.lower())

    def setup_directory_navigation(self, image_path: str):
        """Setup directory navigation for the current image"""
        if not image_path:
            return
        
        path_obj = Path(image_path)
        self.current_directory = str(path_obj.parent)
        self.image_files = self.get_image_files_in_directory(self.current_directory)
        
        try:
            self.current_index = self.image_files.index(str(path_obj))
        except ValueError:
            self.current_index = -1

    def navigate_to_image(self, direction: int):
        """Navigate to next/previous image in directory"""
        if not self.image_files or self.current_index == -1 or self.navigation_animation:
            return
        
        new_index = (self.current_index + direction) % len(self.image_files)
        if new_index == self.current_index:
            return
        
        # Setup slide animation only in windowed mode
        if not self.is_fullscreen:
            self.old_pixmap = self._get_current_pixmap()
            self.navigation_direction = direction
            self.navigation_progress = 0.0
            self.navigation_animation = True
        
        self.current_index = new_index
        new_path = self.image_files[self.current_index]
        
        # Load new image in background
        self._start_loading_thread(new_path, self._on_navigation_image_loaded, self._on_navigation_animated_loaded)
    
    def _on_navigation_image_loaded(self, path: str, pixmap: QPixmap):
        """Handle successful navigation image loading"""
        normalized_path = os.path.normpath(path)
        if self._active_request_path and normalized_path != self._active_request_path:
            return

        self._active_request_path = None
        self._invalidate_pixmap_cache()

        if self.is_fullscreen:
            # Stop any movie
            if self.movie:
                self.movie.stop()
                self.movie.deleteLater()
                self.movie = None

            self.pixmap = pixmap
            self.image_path = normalized_path
            self.rotation = 0.0
            self._fit_to_fullscreen_instant()
        else:
            # Use slide animation in windowed mode
            self.new_pixmap = pixmap
            self.image_path = normalized_path
            self.rotation = 0.0

    def _on_navigation_animated_loaded(self, path: str):
        """Handle successful navigation animated image loading"""
        normalized_path = os.path.normpath(path)
        if self._active_request_path and normalized_path != self._active_request_path:
            return

        self._active_request_path = None
        self._invalidate_pixmap_cache()

        if self.is_fullscreen:
            # Stop any existing movie
            if self.movie:
                self.movie.stop()
                self.movie.deleteLater()
                self.movie = None

            # Create new movie
            self.movie = QMovie(normalized_path)

            if self.movie and self.movie.isValid():
                self.pixmap = None
                self.image_path = normalized_path
                self.rotation = 0.0

                self.movie.frameChanged.connect(self._on_movie_frame_changed)
                self.movie.jumpToFrame(0)
                first_frame = self.movie.currentPixmap()
                if not first_frame.isNull():
                    self.pixmap = first_frame
                    self._fit_to_fullscreen_instant()
                    self.pixmap = None
                    self.movie.start()
        else:
            # Use slide animation in windowed mode
            temp_movie = QMovie(normalized_path)

            if temp_movie and temp_movie.isValid():
                temp_movie.jumpToFrame(0)
                first_frame = temp_movie.currentPixmap()
                if not first_frame.isNull():
                    self.new_pixmap = first_frame
                    self.image_path = normalized_path
                    self.rotation = 0.0
                temp_movie.deleteLater()

    def _on_movie_frame_changed(self):
        """Handle movie frame change"""
        self._invalidate_pixmap_cache()
        self.schedule_update()

    def close_application(self):
        """Start closing animation and exit"""
        if not self.closing_animation:
            self.closing_animation = True
            self.target_background_opacity = 0.0
            QTimer.singleShot(300, QApplication.instance().quit)

    def get_supported_formats(self):
        """Get comprehensive list of supported formats"""
        base_formats = [
            "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.mng", "*.webp", "*.tiff", "*.tif", 
            "*.ico", "*.svg", "*.pbm", "*.pgm", "*.ppm", "*.xbm", "*.xpm"
        ]
        
        raw_formats = ["*" + ext for ext in self.RAW_EXTENSIONS]
        modern_formats = ["*.heic", "*.heif", "*.avif", "*.jxl"]
        scientific_formats = ["*.fits", "*.hdr", "*.exr", "*.pic", "*.psd"]
        
        return base_formats + raw_formats + modern_formats + scientific_formats

    def open_dialog_and_load(self):
        formats = self.get_supported_formats()
        filter_str = f"All Supported Images ({' '.join(formats)})"
        
        fname, _ = QFileDialog.getOpenFileName(
            self, "Open image", str(Path.home()), filter_str
        )
        if fname:
            self.load_image(fname)
        else:
            QTimer.singleShot(0, QApplication.instance().quit)

    def load_image(self, path: str):
        """Load image with comprehensive format support"""
        normalized_path = os.path.normpath(path)
        self.image_path = normalized_path
        self.setup_directory_navigation(normalized_path)

        # Reset animations
        if self.pixmap:
            self.opening_animation = True
            self.opening_scale = 0.95
            self.opening_opacity = 0.2

        # Stop any existing movie
        if self.movie:
            self.movie.stop()
            self.movie = None

        self._invalidate_pixmap_cache()
        self._stop_loading_thread()

        if not os.path.isfile(normalized_path):
            self._on_load_failed(normalized_path, "File does not exist")
            return

        # Background loading
        self._start_loading_thread(normalized_path, self._on_image_loaded, self._on_animated_image_loaded)

    def _on_image_loaded(self, path: str, pixmap: QPixmap):
        """Handle successful image loading"""
        normalized_path = os.path.normpath(path)
        if self._active_request_path and normalized_path != self._active_request_path:
            return

        self._active_request_path = None
        # Stop any existing movie
        if self.movie:
            self.movie.stop()
            self.movie = None

        self.pixmap = pixmap
        self.image_path = normalized_path
        self.rotation = 0.0
        self._invalidate_pixmap_cache()
        self._setup_image_display()

    def _on_animated_image_loaded(self, path: str):
        """Handle successful animated image loading"""
        normalized_path = os.path.normpath(path)
        if self._active_request_path and normalized_path != self._active_request_path:
            return

        self._active_request_path = None
        # Stop any existing movie
        if self.movie:
            self.movie.stop()
            self.movie.deleteLater()
            self.movie = None

        # Create new movie
        self.movie = QMovie(normalized_path)

        if self.movie.isValid():
            self.pixmap = None
            self.image_path = normalized_path
            self.rotation = 0.0
            self._invalidate_pixmap_cache()
            
            # Setup movie for display
            self.movie.frameChanged.connect(self._on_movie_frame_changed)
            # Get first frame for sizing
            self.movie.jumpToFrame(0)
            first_frame = self.movie.currentPixmap()
            
            if not first_frame.isNull():
                self.pixmap = first_frame  # Temporary for sizing calculations
                self._setup_image_display()
                self.pixmap = None  # Clear static pixmap, use movie instead
                self.movie.start()
        else:
            self._on_load_failed(normalized_path, "QMovie invalid")

    def _on_load_failed(self, path: str, error: str):
        """Handle loading failure"""
        normalized_path = os.path.normpath(path) if path else None
        if self._active_request_path and normalized_path != self._active_request_path:
            return

        self._active_request_path = None

        if self.navigation_animation:
            self.navigation_animation = False
            self.old_pixmap = None
            self.new_pixmap = None

        if not self.pixmap and not self.movie:
            if "video file" not in error:
                print(f"Failed to load image: {error}")
            QTimer.singleShot(3000, QApplication.instance().quit)

    def _setup_image_display(self):
        """Setup display parameters after image is loaded"""
        current_pixmap = self._get_current_pixmap()
        if not current_pixmap or current_pixmap.isNull():
            return

        screen_geom, screen_center = self._get_screen_info()
        
        # Calculate fit-to-screen scale
        if current_pixmap.width() > 0 and current_pixmap.height() > 0:
            scale_x = (screen_geom.width() * 0.9) / current_pixmap.width()
            scale_y = (screen_geom.height() * 0.9) / current_pixmap.height()
            self.fit_scale = min(scale_x, scale_y, 1.0)
        else:
            self.fit_scale = 1.0

        # Set initial transform
        self.target_scale = self.fit_scale
        self.current_scale = self.fit_scale * 0.8
        
        # Center the image
        self.target_offset = screen_center
        self.current_offset = screen_center

        # Setup window
        self.resize(screen_geom.width(), screen_geom.height())
        self.move(screen_geom.topLeft())

        # Reset animation states
        self.opening_animation = True
        self.opening_scale = 0.8
        self.opening_opacity = 0.0
        self.background_opacity = 0.0
        self.target_background_opacity = self.WINDOWED_BG_OPACITY
        self.pan_velocity = QPointF(0, 0)

        self.schedule_update()

    def get_image_bounds(self) -> QRectF:
        """Get the bounds of the image in screen coordinates"""
        current_pixmap = self._get_current_pixmap()
        if not current_pixmap or current_pixmap.isNull():
            return QRectF()
        
        img_w = current_pixmap.width() * self.current_scale
        img_h = current_pixmap.height() * self.current_scale
        
        return QRectF(
            self.current_offset.x() - img_w / 2,
            self.current_offset.y() - img_h / 2,
            img_w,
            img_h
        )

    def point_in_image(self, point: QPointF) -> bool:
        """Check if point is inside the image"""
        bounds = self.get_image_bounds()
        return bounds.contains(point)

    def _calculate_effective_dimensions(self):
        """Calculate effective image dimensions considering rotation"""
        current_pixmap = self._get_current_pixmap()
        if not current_pixmap or current_pixmap.isNull():
            return 1, 1
        
        if self.rotation % 180 == 90:  # 90° or 270°
            return current_pixmap.height(), current_pixmap.width()
        else:  # 0° or 180°
            return current_pixmap.width(), current_pixmap.height()

    def zoom_to(self, new_scale: float, focus_point: QPointF = None):
        """Zoom to specific scale with focus point"""
        if not self.pixmap and not self.movie:
            return
        
        # Restrict zoom in fullscreen mode
        if self.is_fullscreen:
            screen_geom = QApplication.primaryScreen().geometry()
            effective_width, effective_height = self._calculate_effective_dimensions()
            if effective_width > 0 and effective_height > 0:
                scale_x = screen_geom.width() / effective_width
                scale_y = screen_geom.height() / effective_height
                min_fullscreen_scale = min(scale_x, scale_y)
                new_scale = max(min_fullscreen_scale, new_scale)
        
        # Clamp scale
        new_scale = max(self.MIN_SCALE, min(self.MAX_SCALE, new_scale))
        
        # Determine focus point
        if focus_point is None:
            focus_point = self.current_offset
        elif not self.point_in_image(focus_point):
            focus_point = self.current_offset
        
        # Calculate the point in image space that should stay under the focus
        old_scale = self.current_scale
        if old_scale > 0:
            dx = focus_point.x() - self.current_offset.x()
            dy = focus_point.y() - self.current_offset.y()
            
            scale_ratio = new_scale / old_scale
            new_dx = dx * scale_ratio
            new_dy = dy * scale_ratio
            
            self.target_offset = QPointF(
                focus_point.x() - new_dx,
                focus_point.y() - new_dy
            )
        
        self.target_scale = new_scale

    def fit_to_screen(self):
        """Fit image to screen - FIXED centering bug"""
        if not self.pixmap and not self.movie:
            return
        
        screen_geom, screen_center = self._get_screen_info()
        
        # Recalculate fit scale in case of rotation changes
        current_pixmap = self._get_current_pixmap()
        if current_pixmap and not current_pixmap.isNull():
            effective_width, effective_height = self._calculate_effective_dimensions()
            if effective_width > 0 and effective_height > 0:
                scale_x = (screen_geom.width() * 0.9) / effective_width
                scale_y = (screen_geom.height() * 0.9) / effective_height
                self.fit_scale = min(scale_x, scale_y, 1.0)
        
        self.target_scale = self.fit_scale
        self.target_offset = screen_center
        # Reset current offset to ensure smooth centering
        self.current_offset = QPointF(self.current_offset)  # Create a copy to force update

    def toggle_fullscreen(self):
        """Toggle fullscreen mode"""
        self.is_fullscreen = not self.is_fullscreen
        self._clear_screen_cache()
        
        if self.is_fullscreen:
            self.saved_scale = self.target_scale
            self.saved_offset = QPointF(self.target_offset)
            self.showFullScreen()
            self.target_background_opacity = self.FULLSCREEN_BG_OPACITY
            self._fit_to_fullscreen()
        else:
            self.showNormal()
            screen_geom, _ = self._get_screen_info()
            self.resize(screen_geom.width(), screen_geom.height())
            self.move(screen_geom.topLeft())
            self.target_scale = self.saved_scale
            self.target_offset = self.saved_offset
            self.target_background_opacity = self.WINDOWED_BG_OPACITY

    def _fit_to_fullscreen(self):
        """Fit image to fullscreen with animation"""
        if not self.pixmap and not self.movie:
            return
        
        screen_geom = QApplication.primaryScreen().geometry()
        screen_center = QPointF(screen_geom.center())
        
        effective_width, effective_height = self._calculate_effective_dimensions()
        if effective_width > 0 and effective_height > 0:
            scale_x = screen_geom.width() / effective_width
            scale_y = screen_geom.height() / effective_height
            fit_scale = min(scale_x, scale_y)
        else:
            fit_scale = 1.0
        
        self.target_scale = fit_scale
        self.target_offset = screen_center

    def _fit_to_fullscreen_instant(self):
        """Fit image to fullscreen instantly without animation"""
        if not self.pixmap and not self.movie:
            return
        
        screen_geom = QApplication.primaryScreen().geometry()
        screen_center = QPointF(screen_geom.center())
        
        effective_width, effective_height = self._calculate_effective_dimensions()
        if effective_width > 0 and effective_height > 0:
            scale_x = screen_geom.width() / effective_width
            scale_y = screen_geom.height() / effective_height
            fit_scale = min(scale_x, scale_y)
        else:
            fit_scale = 1.0
        
        self.target_scale = fit_scale
        self.current_scale = fit_scale
        self.target_offset = screen_center
        self.current_offset = screen_center
        self.schedule_update()

    def wheelEvent(self, e: QWheelEvent):
        """Handle zoom with mouse wheel"""
        if not self.pixmap and not self.movie:
            return
        
        delta = e.angleDelta().y() / 120.0
        zoom_factor = 1.0 + (delta * self.ZOOM_STEP)
        new_scale = self.target_scale * zoom_factor
        self.zoom_to(new_scale, e.position())
        e.accept()

    def mousePressEvent(self, e: QMouseEvent):
        """Handle mouse press"""
        if e.button() == Qt.LeftButton:
            if self.point_in_image(e.position()):
                self.is_panning = True
                self.last_mouse_pos = e.position()
                self.pan_velocity = QPointF(0, 0)
                e.accept()
            else:
                # Exit only in windowed mode
                if not self.is_fullscreen:
                    self.close_application()
        elif e.button() == Qt.RightButton:
            self.close_application()

    def mouseMoveEvent(self, e: QMouseEvent):
        """Handle mouse move"""
        if self.is_panning:
            delta = e.position() - self.last_mouse_pos
            self.current_offset += delta
            self.target_offset = QPointF(self.current_offset)
            self.pan_velocity = delta * 0.6
            self.last_mouse_pos = e.position()
            self.schedule_update()
            e.accept()

    def mouseReleaseEvent(self, e: QMouseEvent):
        """Handle mouse release"""
        if e.button() == Qt.LeftButton:
            self.is_panning = False
            e.accept()

    def mouseDoubleClickEvent(self, e: QMouseEvent):
        """Handle double click - toggle between fit and 100%"""
        if not self.pixmap and not self.movie:
            return
        
        if self.is_fullscreen:
            screen_geom = QApplication.primaryScreen().geometry()
            effective_width, effective_height = self._calculate_effective_dimensions()
            if effective_width > 0 and effective_height > 0:
                scale_x = screen_geom.width() / effective_width
                scale_y = screen_geom.height() / effective_height
                fullscreen_fit_scale = min(scale_x, scale_y)
                
                if abs(self.current_scale - fullscreen_fit_scale) < 0.01:
                    self.zoom_to(1.0, e.position())
                else:
                    self._fit_to_fullscreen()
            e.accept()
            return
        
        if abs(self.target_scale - self.fit_scale) < 0.01:
            self.zoom_to(1.0, e.position())
        else:
            self.fit_to_screen()
        
        e.accept()

    def animate(self):
        """Main animation loop - optimized"""
        needs_update = False
        
        # Navigation slide animation with improved easing
        if self.navigation_animation:
            # Use smoother easing curve
            self.navigation_progress = min(1.0, self.navigation_progress + self.NAVIGATION_SPEED)
            
            if self.navigation_progress >= 1.0:
                self.navigation_animation = False
                
                # Handle animated images
                if self.new_pixmap:
                    # Stop any existing movie
                    if self.movie:
                        self.movie.stop()
                        self.movie.deleteLater()
                        self.movie = None
                    
                    self.pixmap = self.new_pixmap
                    self.new_pixmap = None
                    self._invalidate_pixmap_cache()
                
                self.old_pixmap = None
                
                # Smooth transition to centered position
                _, screen_center = self._get_screen_info()
                self.target_offset = screen_center
                
                # Reset to fit scale for new image
                current_pixmap = self._get_current_pixmap()
                if current_pixmap and not current_pixmap.isNull():
                    screen_geom, _ = self._get_screen_info()
                    scale_x = (screen_geom.width() * 0.9) / current_pixmap.width()
                    scale_y = (screen_geom.height() * 0.9) / current_pixmap.height()
                    self.fit_scale = min(scale_x, scale_y, 1.0)
                    self.target_scale = self.fit_scale
                
            needs_update = True
        
        # Opening animation
        if self.opening_animation:
            self.opening_scale = min(1.0, self.opening_scale + (1.0 - self.opening_scale) * 0.15)
            self.opening_opacity = min(1.0, self.opening_opacity + (1.0 - self.opening_opacity) * 0.2)
            
            if self.opening_scale > 0.99 and self.opening_opacity > 0.99:
                self.opening_scale = 1.0
                self.opening_opacity = 1.0
                self.opening_animation = False
            
            needs_update = True
        
        # Closing animation
        if self.closing_animation:
            self.closing_scale += (0.7 - self.closing_scale) * 0.25
            self.closing_opacity += (0.0 - self.closing_opacity) * 0.25
            needs_update = True
        
        # Background fade animation
        bg_diff = self.target_background_opacity - self.background_opacity
        if abs(bg_diff) > 1.0:
            self.background_opacity += bg_diff * 0.15
            needs_update = True
        
        # Pan inertia
        if not self.is_panning and (abs(self.pan_velocity.x()) > 0.1 or abs(self.pan_velocity.y()) > 0.1):
            self.target_offset += self.pan_velocity
            self.pan_velocity *= self.pan_friction
            needs_update = True
        
        # Smooth interpolation to target values
        scale_diff = self.target_scale - self.current_scale
        offset_diff = self.target_offset - self.current_offset
        
        if abs(scale_diff) > 0.001:
            self.current_scale += scale_diff * self.lerp_factor
            needs_update = True
        
        if abs(offset_diff.x()) > 0.1 or abs(offset_diff.y()) > 0.1:
            self.current_offset += offset_diff * self.lerp_factor
            needs_update = True
        
        if needs_update:
            self.schedule_update()

    def schedule_update(self):
        """Schedule update to avoid excessive redraws"""
        if not self.update_pending:
            self.update_pending = True
            QTimer.singleShot(0, self._do_update)

    def _do_update(self):
        """Perform the actual update"""
        self.update_pending = False
        self.update()

    def dragEnterEvent(self, event):
        """Handle drag enter"""
        if event.mimeData().hasUrls():
            event.accept()
        else:
            event.ignore()

    def dropEvent(self, event):
        """Handle file drop"""
        urls = event.mimeData().urls()
        if urls:
            path = urls[0].toLocalFile()
            if Path(path).is_file():
                self.load_image(path)

    def keyPressEvent(self, e):
        """Handle keyboard input"""
        if e.key() == Qt.Key_Escape:
            self.close_application()
            return

        if e.key() == Qt.Key_F11:
            self.toggle_fullscreen()
            e.accept()
            return

        # Directory navigation with A and D keys
        key_text = e.text().lower()
        if e.key() == Qt.Key_A or key_text == 'a' or key_text == 'ф':
            self.navigate_to_image(-1)
            e.accept()
            return
        elif e.key() == Qt.Key_D or key_text == 'd' or key_text == 'в':
            self.navigate_to_image(1)
            e.accept()
            return

        # Zoom with +/- keys
        if e.key() == Qt.Key_Plus or e.key() == Qt.Key_Equal:
            self._keyboard_zoom(self.ZOOM_FACTOR)
            e.accept()
            return
        elif e.key() == Qt.Key_Minus:
            self._keyboard_zoom(1.0 / self.ZOOM_FACTOR)
            e.accept()
            return

        # Copy to clipboard
        if e.modifiers() & Qt.ControlModifier:
            if e.key() == Qt.Key_C or key_text == 'c' or key_text == 'с':
                current_pixmap = self._get_current_pixmap()
                if current_pixmap and not current_pixmap.isNull():
                    QGuiApplication.clipboard().setPixmap(current_pixmap)
                return

        # Rotate
        if e.key() == Qt.Key_R or key_text == 'r' or key_text == 'к':
            self.rotation = (self.rotation + 90) % 360
            self._invalidate_pixmap_cache()
            if self.is_fullscreen:
                self._fit_to_fullscreen_instant()
            else:
                # Recalculate fit scale after rotation
                self.fit_to_screen()
            return
        
        # Fit to screen
        if (e.key() == Qt.Key_F or key_text == 'f' or key_text == 'а' or 
            e.key() == Qt.Key_Space):
            if self.is_fullscreen:
                self._fit_to_fullscreen()
            else:
                self.fit_to_screen()
            return

        super().keyPressEvent(e)

    def _keyboard_zoom(self, factor: float):
        """Handle keyboard zoom with given factor"""
        if self.pixmap or self.movie:
            _, screen_center = self._get_screen_info()
            new_scale = self.target_scale * factor
            self.zoom_to(new_scale, screen_center)

    def _ease_in_out_cubic(self, t: float) -> float:
        """Smooth easing function for animations"""
        if t < 0.5:
            return 4 * t * t * t
        else:
            p = 2 * t - 2
            return 1 + p * p * p / 2

    def paintEvent(self, event):
        """Main paint event - optimized"""
        painter = QPainter(self)
        painter.setRenderHint(QPainter.SmoothPixmapTransform, True)
        painter.setRenderHint(QPainter.Antialiasing, True)

        # Draw dark background with smooth fade
        bg_color = QColor(0, 0, 0, int(self.background_opacity))
        painter.fillRect(self.rect(), bg_color)

        # Navigation slide animation
        if self.navigation_animation and self.old_pixmap and self.new_pixmap:
            self._draw_slide_animation(painter)
        elif self.pixmap:
            self._draw_single_image(painter, self.pixmap)
        elif self.movie and self.movie.state() == QMovie.MovieState.Running:
            current_pixmap = self.movie.currentPixmap()
            if not current_pixmap.isNull():
                self._draw_single_image(painter, current_pixmap)

    def _draw_slide_animation(self, painter):
        """Draw sliding animation between two images - improved smoothness"""
        # Use smooth easing curve
        t = self._ease_in_out_cubic(self.navigation_progress)
        
        screen_width = self.width()
        # Reduced slide distance for less jarring transition
        slide_distance = screen_width * 0.8
        
        # Add parallax effect for depth
        parallax_factor = 0.3
        
        if self.navigation_direction > 0:  # Next image
            old_x_offset = -slide_distance * t * parallax_factor
            new_x_offset = slide_distance * (1 - t)
        else:  # Previous image
            old_x_offset = slide_distance * t * parallax_factor
            new_x_offset = -slide_distance * (1 - t)
        
        # Draw old image with fade and scale
        painter.save()
        painter.translate(old_x_offset, 0)
        old_scale = 1.0 - t * 0.05  # Subtle scale down
        painter.scale(old_scale, old_scale)
        painter.setOpacity(1.0 - t * 0.5)  # Smoother fade
        self._draw_single_image(painter, self.old_pixmap)
        painter.restore()
        
        # Draw new image with fade and scale
        painter.save()
        painter.translate(new_x_offset, 0)
        new_scale = 0.95 + t * 0.05  # Scale up to normal
        painter.scale(new_scale, new_scale)
        painter.setOpacity(0.5 + t * 0.5)  # Fade in
        self._draw_single_image(painter, self.new_pixmap)
        painter.restore()

    def _draw_single_image(self, painter, pixmap):
        """Draw a single image with current transforms"""
        if not pixmap or pixmap.isNull():
            return

        # Calculate image dimensions
        final_scale = self.current_scale
        if self.opening_animation:
            final_scale *= self.opening_scale
        elif self.closing_animation:
            final_scale *= self.closing_scale
            
        img_w = pixmap.width() * final_scale
        img_h = pixmap.height() * final_scale

        # Draw image
        painter.save()
        painter.translate(self.current_offset)
        
        if self.rotation != 0:
            painter.rotate(self.rotation)
        
        # Set opacity for animations
        current_opacity = painter.opacity()
        if self.opening_animation:
            painter.setOpacity(current_opacity * self.opening_opacity)
        elif self.closing_animation:
            painter.setOpacity(current_opacity * self.closing_opacity)
        
        # Draw the pixmap centered
        target_rect = QRectF(-img_w / 2, -img_h / 2, img_w, img_h)
        source_rect = QRectF(pixmap.rect())
        
        painter.drawPixmap(target_rect, pixmap, source_rect)
        painter.restore()

    def closeEvent(self, event):
        """Clean up on close"""
        self._stop_loading_thread()

        # Clean up movie
        if self.movie:
            self.movie.stop()
            self.movie.deleteLater()
            self.movie = None
            
        super().closeEvent(event)


def main():
    """Main entry point for the application"""
    app = QApplication(sys.argv)

    path = None
    if len(sys.argv) >= 2:
        path = sys.argv[1]

    viewer = BlurViewer(path)
    viewer.show()

    sys.exit(app.exec())


if __name__ == '__main__':
    main()
