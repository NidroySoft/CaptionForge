using System.Text.Json;
using CaptionForge.Modularity;
namespace CaptionForge.Tests.Application;

public sealed class ModuleDiscoveryTests
{
    [Fact]
    public void MissingModuleFolderLeavesHostRegistryUnchanged()
    {
        var registry = new ModuleRegistry();
        Assert.Empty(new ModuleDiscovery().RegisterFrom(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), registry));
        Assert.Empty(registry.Definitions);
    }
    [Fact]
    public void MalformedAndEscapingManifestsAreReportedWithoutLoadingCode()
    {
        string directory = Path.Combine(Path.GetTempPath(), "CaptionForge-discovery-" + Guid.NewGuid());
        string invalid = Path.Combine(directory, "invalid"), escaping = Path.Combine(directory, "escaping");
        Directory.CreateDirectory(invalid); Directory.CreateDirectory(escaping);
        File.WriteAllText(Path.Combine(invalid, "module.json"), "{ broken");
        File.WriteAllText(Path.Combine(escaping, "module.json"), JsonSerializer.Serialize(new ModuleManifest("x", "X", "../outside.dll", "X")));
        try
        {
            var registry = new ModuleRegistry();
            Assert.Equal(2, new ModuleDiscovery().RegisterFrom(directory, registry).Count);
            Assert.Empty(registry.Definitions);
        }
        finally
        {
            File.Delete(Path.Combine(invalid, "module.json")); File.Delete(Path.Combine(escaping, "module.json"));
            Directory.Delete(invalid); Directory.Delete(escaping); Directory.Delete(directory);
        }
    }
}
