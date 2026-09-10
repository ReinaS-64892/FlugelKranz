using Xunit;

namespace FlugelKranz.Tests;

public sealed class MonadoRuntimeLocatorTests
{
    [Fact]
    public void ReadsLibMonadoPathFromRuntimeManifest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fl kranz-runtime-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{\"runtime\":{\"MND_libmonado_path\":\"/tmp/libmonado-test.so\"}}");
            Assert.Equal("/tmp/libmonado-test.so", FlugelKranz.MonadoRuntimeLocator.Resolve(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FallsBackWhenManifestIsMissingOrInvalid()
    {
        Assert.Equal(
            FlugelKranz.MonadoRuntimeLocator.FallbackLibraryPath,
            FlugelKranz.MonadoRuntimeLocator.Resolve("/path/that/does/not/exist.json"));
    }
}
