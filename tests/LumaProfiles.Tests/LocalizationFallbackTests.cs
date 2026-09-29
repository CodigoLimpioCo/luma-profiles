using LumaProfiles.Services;

namespace LumaProfiles.Tests;

public class LocalizationFallbackTests
{
    [Fact]
    public void BundledLanguagesWorkWhenTheFolderCannotBeCreated()
    {
        using var dir = new TempDirectory();
        var blocker = Path.Combine(dir.Path, "not-a-folder");
        File.WriteAllText(blocker, "a file where the Locales folder should be");
        var impossible = Path.Combine(blocker, "Locales");

        var languages = LocalizationService.DiscoverLanguages(impossible);

        Assert.Contains(languages, language => language.Code == "es");
        Assert.Contains(languages, language => language.Code == "en");
        Assert.Contains(languages, language => language.Code == "pt");
        var english = LocalizationService.LoadValues(impossible, "en");
        Assert.StartsWith("Ready", english["ui.Ready"]);
        Assert.True(english.Count > 200);
    }

    [Fact]
    public void AnOldReadOnlyFileGetsItsMissingKeysFromTheBundledCopy()
    {
        using var dir = new TempDirectory();
        var file = Path.Combine(dir.Path, "en.lang");
        File.WriteAllText(file, "meta.code=en\nmeta.name=English (custom)\nui.Ready=Custom ready text\n");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            var values = LocalizationService.LoadValues(dir.Path, "en");

            Assert.Equal("Custom ready text", values["ui.Ready"]);
            Assert.True(values.ContainsKey("ui.RestorePointTitle"));
            Assert.Equal("English (custom)", LocalizationService.DiscoverLanguages(dir.Path).Single(language => language.Code == "en").DisplayName);
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    [Fact]
    public void UserEditsInAWritableFolderAreKeptAndNewKeysAreAppended()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "pt.lang"), "meta.code=pt\nmeta.name=Português\nui.Ready=Meu texto\n");

        var values = LocalizationService.LoadValues(dir.Path, "pt");

        Assert.Equal("Meu texto", values["ui.Ready"]);
        Assert.True(values.ContainsKey("ui.RestorePointTitle"));
    }

    [Fact]
    public void UnknownLanguagesReturnNothing()
    {
        using var dir = new TempDirectory();

        Assert.Empty(LocalizationService.LoadValues(dir.Path, "xx"));
    }
}
