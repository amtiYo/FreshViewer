using FreshViewer.ViewModels;
using FreshViewer.Models;
using Xunit;

namespace FreshViewer.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void ApplyMetadata_SetsVisibilityFlags()
    {
        var viewModel = new MainWindowViewModel();
        var metadata = new ImageMetadata(new[]
        {
            new MetadataSection("General", new[] { new MetadataField("Label", "Value") })
        });

        viewModel.ApplyMetadata("file.png", "100x100", "Loaded", metadata);

        Assert.True(viewModel.IsMetadataVisible);
        Assert.True(viewModel.HasMetadataDetails);
        Assert.True(viewModel.HasSummaryItems);
        Assert.Equal("file.png", viewModel.FileName);
        Assert.Equal("Loaded", viewModel.StatusText);
    }

    [Fact]
    public void ResetMetadata_HidesPanels()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.ApplyMetadata("file.png", "100x100", "Loaded", null);

        viewModel.ResetMetadata();

        Assert.False(viewModel.IsMetadataVisible);
        Assert.False(viewModel.HasMetadataDetails);
        Assert.Null(viewModel.FileName);
        Assert.True(viewModel.ShowMetadataPlaceholder);
    }
}
