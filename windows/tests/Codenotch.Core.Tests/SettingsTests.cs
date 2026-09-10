using Codenotch.Core;
using Xunit;

namespace Codenotch.Core.Tests;

public class SettingsTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), "codenotch-settings-" + Guid.NewGuid() + ".json");

    [Fact]
    public void SettingsDefaultHasTheDocumentedValues()
    {
        var s = Settings.Default;
        Assert.Equal(60, s.PollSeconds);
        Assert.Null(s.Endpoint);
        Assert.True(s.ShowNotch);
        Assert.False(s.TrayPercent);
        Assert.True(s.Notify);
        Assert.Null(s.Providers);
    }

    [Fact]
    public void LoadingAMissingPathReturnsSettingsDefault()
    {
        var loaded = SettingsStore.Load(TempPath());
        Assert.Equal(Settings.Default, loaded);
    }

    [Fact]
    public void LoadingACorruptFileReturnsSettingsDefaultAndDoesNotThrow()
    {
        var path = TempPath();
        File.WriteAllText(path, "{ this is not valid json");
        try
        {
            var loaded = SettingsStore.Load(path);
            Assert.Equal(Settings.Default, loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveThenLoadRoundTripsEveryFieldIncludingANonNullProvidersList()
    {
        var path = TempPath();
        var settings = new Settings
        {
            PollSeconds = 45,
            Endpoint = "http://127.0.0.1:8787/usage",
            ShowNotch = false,
            TrayPercent = true,
            Notify = false,
            Providers = new[] { "claude" },
        };
        try
        {
            SettingsStore.Save(path, settings);
            var loaded = SettingsStore.Load(path);

            Assert.Equal(settings.PollSeconds, loaded.PollSeconds);
            Assert.Equal(settings.Endpoint, loaded.Endpoint);
            Assert.Equal(settings.ShowNotch, loaded.ShowNotch);
            Assert.Equal(settings.TrayPercent, loaded.TrayPercent);
            Assert.Equal(settings.Notify, loaded.Notify);
            Assert.Equal(settings.Providers, loaded.Providers);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SavedJsonIsCamelCaseAndOmitsProvidersWhenNull()
    {
        var path = TempPath();
        try
        {
            SettingsStore.Save(path, Settings.Default);
            var text = File.ReadAllText(path);

            Assert.Contains("\"pollSeconds\"", text);
            Assert.Contains("\"showNotch\"", text);
            Assert.DoesNotContain("\"providers\"", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnknownKeysInAnExistingFileAreIgnoredOnLoad()
    {
        var path = TempPath();
        File.WriteAllText(path, """{"pollSeconds":45,"somethingWeDontKnowAbout":true}""");
        try
        {
            var loaded = SettingsStore.Load(path);
            Assert.Equal(45, loaded.PollSeconds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EffectivePollSecondsIsThirtyInEndpointModeAndSixtyBuiltInByDefaultAndClampsOutOfRangeValues()
    {
        var endpointMode = Settings.Default with { Endpoint = "http://127.0.0.1:8787/usage" };
        Assert.Equal(30, endpointMode.EffectivePollSeconds);

        Assert.Equal(60, Settings.Default.EffectivePollSeconds);

        var tooLow = Settings.Default with { PollSeconds = 1 };
        Assert.Equal(10, tooLow.EffectivePollSeconds);

        var tooHigh = Settings.Default with { PollSeconds = 100_000 };
        Assert.Equal(3600, tooHigh.EffectivePollSeconds);
    }
}
