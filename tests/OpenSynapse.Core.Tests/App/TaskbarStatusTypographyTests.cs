using OpenSynapse.Windows.Lifecycle;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class TaskbarStatusTypographyTests
{
    [Fact]
    public void ChineseGlyphsUseTheCjkRunAndLatinTextKeepsTheDisplayFont()
    {
        Assert.Equal(
            [("性能", true), (" Performance 87%", false)],
            TaskbarStatusWindow.SplitTextRuns("性能 Performance 87%"));
    }

    [Fact]
    public void EmptyTextHasNoRuns() =>
        Assert.Empty(TaskbarStatusWindow.SplitTextRuns(string.Empty));

    [Fact]
    public void TextWithoutChineseStaysInOneDisplayFontRun() =>
        Assert.Equal([("Boost", false)], TaskbarStatusWindow.SplitTextRuns("Boost"));
}
