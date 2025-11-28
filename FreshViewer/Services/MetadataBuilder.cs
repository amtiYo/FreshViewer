using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media.Imaging;
using FreshViewer.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace FreshViewer.Services;

internal static class MetadataBuilder
{
    public static ImageMetadata? Build(string path, PixelSize pixelSize, bool isAnimated, Bitmap? bitmap, AnimatedImage? animation)
    {
        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(path).ToList();
        }
        catch
        {
            directories = Array.Empty<MetadataExtractor.Directory>();
        }

        var sections = new List<MetadataSection>();

        sections.Add(BuildGeneralSection(path, pixelSize, bitmap));

        var capture = BuildCaptureSection(directories);
        if (capture is not null)
        {
            sections.Add(capture);
        }

        var color = BuildColorSection(directories);
        if (color is not null)
        {
            sections.Add(color);
        }

        if (isAnimated)
        {
            var animationSection = BuildAnimationSection(animation);
            if (animationSection is not null)
            {
                sections.Add(animationSection);
            }
        }

        var advanced = BuildAdvancedSection(directories);
        if (advanced is not null)
        {
            sections.Add(advanced);
        }

        sections.RemoveAll(static section => section.Fields.Count == 0);
        if (sections.Count == 0)
        {
            return null;
        }

        var flattened = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in sections.SelectMany(static section => section.Fields))
        {
            flattened[field.Label] = field.Value;
        }

        return new ImageMetadata(sections, flattened);
    }

    private static MetadataSection BuildGeneralSection(string path, PixelSize pixelSize, Bitmap? bitmap)
    {
        var fields = new List<MetadataField>();
        var fileInfo = new FileInfo(path);

        AddField(fields, "Имя файла", fileInfo.Name);
        AddField(fields, "Тип", fileInfo.Extension.TrimStart('.').ToUpperInvariant());
        AddField(fields, "Размер", FormatFileSize(fileInfo.Length));
        AddField(fields, "Папка", fileInfo.DirectoryName);
        AddField(fields, "Создан", FormatDate(fileInfo.CreationTime));
        AddField(fields, "Изменён", FormatDate(fileInfo.LastWriteTime));

        if (pixelSize.Width > 0 && pixelSize.Height > 0)
        {
            AddField(fields, "Разрешение", $"{pixelSize.Width} × {pixelSize.Height}");
            AddField(fields, "Мегапиксели", FormatMegapixels(pixelSize.Width, pixelSize.Height));
            AddField(fields, "Соотношение", FormatAspectRatio(pixelSize.Width, pixelSize.Height));
        }

        if (bitmap is not null && bitmap.Dpi.X > 0 && bitmap.Dpi.Y > 0)
        {
            AddField(fields, "DPI", string.Format(CultureInfo.InvariantCulture, "{0:0.#} × {1:0.#}", bitmap.Dpi.X, bitmap.Dpi.Y));
        }

        return new MetadataSection("Общее", fields);
    }

    private static MetadataSection? BuildCaptureSection(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

        var fields = new List<MetadataField>();
        AddField(fields, "Производитель", exifIfd0?.GetDescription(ExifDirectoryBase.TagMake));
        AddField(fields, "Камера", exifIfd0?.GetDescription(ExifDirectoryBase.TagModel));
        AddField(fields, "Дата съёмки", exifSubIfd?.GetDescription(ExifDirectoryBase.TagDateTimeOriginal));
        AddField(fields, "Выдержка", exifSubIfd?.GetDescription(ExifDirectoryBase.TagExposureTime));
        AddField(fields, "Диафрагма", exifSubIfd?.GetDescription(ExifDirectoryBase.TagFNumber));
        AddField(fields, "ISO", exifSubIfd?.GetDescription(ExifDirectoryBase.TagIsoEquivalent));
        AddField(fields, "Фокусное расстояние", exifSubIfd?.GetDescription(ExifDirectoryBase.TagFocalLength));
        AddField(fields, "Экспозиция", exifSubIfd?.GetDescription(ExifDirectoryBase.TagExposureBias));
        AddField(fields, "Баланс белого", exifSubIfd?.GetDescription(ExifDirectoryBase.TagWhiteBalance));
        AddField(fields, "Вспышка", exifSubIfd?.GetDescription(ExifDirectoryBase.TagFlash));
        AddField(fields, "Объектив", exifSubIfd?.GetDescription(ExifDirectoryBase.TagLensModel));

        return fields.Count == 0 ? null : new MetadataSection("Параметры съёмки", fields);
    }

    private static MetadataSection? BuildColorSection(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        if (exifSubIfd is null)
        {
            return null;
        }

        var fields = new List<MetadataField>();
        AddField(fields, "Цветовое пространство", exifSubIfd.GetDescription(ExifDirectoryBase.TagColorSpace));
        AddField(fields, "Профиль", exifSubIfd.GetDescription(ExifDirectoryBase.TagPhotometricInterpretation));
        AddField(fields, "Биты на канал", exifSubIfd.GetDescription(ExifDirectoryBase.TagBitsPerSample));
        AddField(fields, "Число каналов", exifSubIfd.GetDescription(ExifDirectoryBase.TagSamplesPerPixel));


        return fields.Count == 0 ? null : new MetadataSection("Цвет", fields);
    }

    private static MetadataSection? BuildAnimationSection(AnimatedImage? animation)
    {
        if (animation is null)
        {
            return null;
        }

        var fields = new List<MetadataField>();
        AddField(fields, "Кадров", animation.Frames.Count.ToString(CultureInfo.InvariantCulture));
        AddField(fields, "Повторений", animation.LoopCount > 0
            ? animation.LoopCount.ToString(CultureInfo.InvariantCulture)
            : "Бесконечно");

        var totalDuration = TimeSpan.Zero;
        foreach (var frame in animation.Frames)
        {
            totalDuration += frame.Duration;
        }

        if (totalDuration > TimeSpan.Zero)
        {
            AddField(fields, "Длительность", totalDuration.ToString());
        }

        return fields.Count == 0 ? null : new MetadataSection("Анимация", fields);
    }

    private static MetadataSection? BuildAdvancedSection(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

        var fields = new List<MetadataField>();
        AddField(fields, "Ориентация", exifIfd0?.GetDescription(ExifDirectoryBase.TagOrientation));
        AddField(fields, "Программа", exifIfd0?.GetDescription(ExifDirectoryBase.TagSoftware));
        AddField(fields, "Авторские права", exifIfd0?.GetDescription(ExifDirectoryBase.TagCopyright));
        AddField(fields, "Метод измерения", exifSubIfd?.GetDescription(ExifDirectoryBase.TagMeteringMode));
        AddField(fields, "Тип экспозиции", exifSubIfd?.GetDescription(ExifDirectoryBase.TagExposureProgram));

        return fields.Count == 0 ? null : new MetadataSection("Дополнительно", fields);
    }

    private static void AddField(ICollection<MetadataField> fields, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        fields.Add(new MetadataField(label, value.Trim()));
    }

    private static string FormatAspectRatio(int width, int height)
    {
        var gcd = GreatestCommonDivisor(width, height);
        var ratio = $"{width / gcd}:{height / gcd}";
        var numeric = width / (double)height;
        return string.Format(CultureInfo.InvariantCulture, "{0} ({1:0.##}:1)", ratio, numeric);
    }

    private static string FormatMegapixels(int width, int height)
    {
        var mp = (width * (double)height) / 1_000_000d;
        return mp >= 0.05 ? mp.ToString("0.00 \\MP", CultureInfo.InvariantCulture) : "<0.05 MP";
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double size = bytes;
        var order = 0;
        while (size >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.##} {1}", size, suffixes[order]);
    }

    private static string FormatDate(DateTime date)
        => date == DateTime.MinValue
            ? "—"
            : date.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture);

    private static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return Math.Abs(a);
    }
}
