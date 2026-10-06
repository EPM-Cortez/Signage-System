using Signage.Application;

namespace Signage.UnitTests;

public sealed class ConversionProgressTests
{
    [Fact]
    public void Rendering_progress_climbs_through_each_slide_and_never_goes_backwards()
    {
        var previous = ConversionProgress.Starting;
        for (var rendered = 0; rendered <= 7; rendered++)
        {
            var current = ConversionProgress.Rendering(rendered, 7);
            Assert.True(current.Percent >= previous.Percent);
            Assert.True(current.NextPercent > current.Percent);
            previous = current;
        }
        var packaging = ConversionProgress.Packaging(7);
        Assert.Equal(previous.NextPercent, packaging.Percent);
        Assert.True(packaging.NextPercent < 100);
    }

    [Fact]
    public void Rendering_progress_tolerates_counts_the_converter_should_never_send()
    {
        Assert.Equal(ConversionProgress.Rendering(7, 7).Percent, ConversionProgress.Rendering(9, 7).Percent);
        Assert.Equal(ConversionProgress.Rendering(0, 7).Percent, ConversionProgress.Rendering(-1, 7).Percent);
        Assert.Equal(ConversionProgress.Starting.NextPercent, ConversionProgress.Rendering(0, 0).Percent);
    }

    [Fact]
    public void Tracker_forgets_a_version_once_cleared()
    {
        var tracker = new ConversionProgressTracker();
        var versionId = Guid.NewGuid();
        tracker.Report(versionId, ConversionProgress.Rendering(2, 4));
        Assert.Equal(2, tracker.Get(versionId)?.SlidesRendered);
        tracker.Clear(versionId);
        Assert.Null(tracker.Get(versionId));
    }
}
