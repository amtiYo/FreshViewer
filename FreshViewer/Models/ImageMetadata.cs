using System.Collections.Generic;

namespace FreshViewer.Models;

/// <summary>
/// Represents the metadata extracted from an image, grouped into sections.
/// </summary>
public sealed class ImageMetadata
{
    public ImageMetadata(IReadOnlyList<MetadataSection> sections, IReadOnlyDictionary<string, string?>? raw = null)
    {
        Sections = sections;
        Raw = raw;
    }

    /// <summary>
    /// Gets the top-level metadata sections.
    /// </summary>
    public IReadOnlyList<MetadataSection> Sections { get; }

    /// <summary>
    /// Gets the raw key/value pairs when available.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? Raw { get; }
}

/// <summary>
/// Represents a logical grouping of metadata items.
/// </summary>
public sealed class MetadataSection
{
    public MetadataSection(string title, IReadOnlyList<MetadataField> fields)
    {
        Title = title;
        Fields = fields;
    }

    /// <summary>
    /// Gets the section title displayed to the user.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the metadata items inside the section.
    /// </summary>
    public IReadOnlyList<MetadataField> Fields { get; }
}

/// <summary>
/// Represents an individual metadata entry.
/// </summary>
public sealed class MetadataField
{
    public MetadataField(string label, string? value)
    {
        Label = label;
        Value = value;
    }

    /// <summary>
    /// Gets the metadata label.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets the metadata value.
    /// </summary>
    public string? Value { get; }
}
