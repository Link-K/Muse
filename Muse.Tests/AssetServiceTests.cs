using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Muse.Assets;
using Xunit;

namespace Muse.Tests;

public sealed class AssetServiceTests : IDisposable
{
    private readonly string _tempDir;

    public AssetServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "muse-tests-assets", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task SaveImageAsync_WritesFile_AndReturnsRelativePath()
    {
        var bytes = Encoding.UTF8.GetBytes("fake-image-data");
        var svc = new AssetService(_tempDir);

        var rel = await svc.SaveImageAsync(bytes, "img.png");

        Assert.Equal("assets/img.png", rel.Replace('\\','/'));
        var absolute = Path.Combine(_tempDir, "img.png");
        Assert.True(File.Exists(absolute));
        var read = await File.ReadAllBytesAsync(absolute);
        Assert.Equal(bytes, read);
    }

    [Fact]
    public async Task SaveImageAsync_Throws_OnEmptyData()
    {
        var svc = new AssetService(_tempDir);
        await Assert.ThrowsAsync<ArgumentException>(() => svc.SaveImageAsync(Array.Empty<byte>(), "img.png"));
    }

    [Fact]
    public async Task SaveImageAsync_Throws_OnEmptyName()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var svc = new AssetService(_tempDir);
        await Assert.ThrowsAsync<ArgumentException>(() => svc.SaveImageAsync(bytes, ""));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
        catch { }
    }
}
