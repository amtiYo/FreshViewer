using System.Collections.Generic;
using System.Linq;
using FreshViewer.Models;

namespace FreshViewer.ViewModels;

/// <summary>
/// Represents a metadata item adapted for binding.
/// </summary>
public sealed record MetadataItemViewModel(string Label, string? Value)
{
    /// <summary>
    /// Gets the value formatted for display, returning an em dash when empty.
    /// </summary>
    public string DisplayValue => string.IsNullOrWhiteSpace(Value) ? "—" : Value;
}

/// <summary>
/// Represents a metadata section adapted for binding.
/// </summary>
public sealed record MetadataSectionViewModel(string Title, IReadOnlyList<MetadataItemViewModel> Items)
{
    /// <summary>
    /// Creates a view model from the domain model.
    /// </summary>
    public static MetadataSectionViewModel FromModel(MetadataSection section)
        => new(section.Title, section.Fields.Select(f => new MetadataItemViewModel(f.Label, f.Value)).ToList());
}

/// <summary>
/// Builds view model representations from the metadata domain model.
/// </summary>
public static class MetadataViewModelFactory
{
    /// <summary>
    /// Converts <see cref="ImageMetadata"/> to a list of section view models.
    /// </summary>
    public static IReadOnlyList<MetadataSectionViewModel>? Create(ImageMetadata? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        return metadata.Sections
            .Where(section => section.Fields.Any())
            .Select(MetadataSectionViewModel.FromModel)
            .ToList();
    }
}
