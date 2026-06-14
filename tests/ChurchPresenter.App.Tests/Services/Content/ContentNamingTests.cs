using ChurchPresenter.Services.Content;

using FluentAssertions;

namespace ChurchPresenter.App.Tests.Services.Content;

public sealed class ContentNamingTests
{
    [Theory]
    [InlineData("Sunday Morning", "Sunday Morning")]
    [InlineData("  Welcome: Everyone!  ", "Welcome Everyone!")]
    [InlineData("", "Untitled")]
    public void SanitizeFileName_uses_plain_text(string input, string expected)
    {
        ContentNaming.SanitizeFileName(input).Should().Be(expected);
    }

    [Fact]
    public void EnsureUniqueFilePath_appends_incrementing_suffix()
    {
        var directory = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var first = Path.Combine(directory, "Welcome.cpres");
            File.WriteAllText(first, "first");

            var second = ContentNaming.EnsureUniqueFilePath(Path.Combine(directory, "Welcome.cpres"));
            second.Should().Be(Path.Combine(directory, "Welcome_2.cpres"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AllocateDirectoryName_appends_incrementing_suffix_for_folders()
    {
        var parent = Path.Combine(Path.GetTempPath(), "church-presenter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(Path.Combine(parent, "Seasonal"));

        try
        {
            var allocated = ContentNaming.AllocateDirectoryName(parent, "Seasonal");
            Path.GetFileName(allocated).Should().Be("Seasonal_2");
        }
        finally
        {
            if (Directory.Exists(parent))
                Directory.Delete(parent, recursive: true);
        }
    }
}
