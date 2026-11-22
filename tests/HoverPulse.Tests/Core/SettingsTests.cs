using HoverPulse.Services;

namespace HoverPulse.Tests.Core;

public class SettingsTests
{
    [Fact]
    public void SettingsLimitsAndFutureVersions()
    {
        var dir = Directory.CreateTempSubdirectory("hoverpulse-schema-");
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");
            var store = new SettingsService(path);
            File.WriteAllText(path, """{"SchemaVersion":2,"ClickTime":99,"MoveLimit":-1,"ToolbarX":-1600,"AppSize":10,"FloatMode":-1}""");
            var settings = store.Load();
            Assert.Equal((50,2,-1600,3,0), (settings.ClickTime,settings.MoveLimit,settings.ToolbarX,settings.AppSize,settings.FloatMode));
            File.WriteAllText(path, """{"SchemaVersion":3}""");
            Assert.Throws<NotSupportedException>(() => store.Load());
            Assert.Equal("""{"SchemaVersion":3}""", File.ReadAllText(path));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void PreserveVersionOneAndRecoverLastGoodSettings()
    {
        var folder = Directory.CreateTempSubdirectory("hoverpulse-settings-");
        var path = Path.Combine(folder.FullName, "settings.json");
        try
        {
            const string legacy = """{"DwellTimeMs":700,"ToleranceRadius":10,"ToolbarX":-50,"AudioEnabled":false}""";
            File.WriteAllText(path, legacy);
            var store = new SettingsService(path);
            var settings = store.Load();
            Assert.Equal(legacy, File.ReadAllText(path + ".v1"));
            Assert.Equal(2,settings.ClickTime);
            Assert.Equal(5,settings.MoveLimit);
            Assert.Equal(-50,settings.ToolbarX);
            Assert.False(settings.AudioEnabled);
            settings.ToolbarY = 0;
            store.Save(settings);
            Assert.Equal(0, store.Load().ToolbarY);
            settings.ClickTime = 12;
            store.Save(settings);
            File.WriteAllText(path, "{");
            var recovered = store.Load();
            Assert.Equal(2,recovered.ClickTime);
            Assert.Equal(700,recovered.AdditionalSettings!["DwellTimeMs"].GetInt32());
            store.Save(recovered);
            Assert.False(store.Load().AudioEnabled);
            Assert.Equal(2, new SettingsService(path + ".bak").Load().ClickTime);
        }
        finally { folder.Delete(true); }
    }
}
