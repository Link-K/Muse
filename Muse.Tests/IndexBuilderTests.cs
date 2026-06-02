using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Muse.AI.RAG.VectorIndex;
using Xunit;

namespace Muse.Tests;

public sealed class IndexBuilderTests : IDisposable
{
    private readonly string _tmp;

    public IndexBuilderTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "muse-index-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    [Fact]
    public async Task BuildIndexAsync_CreatesIndexFile()
    {
        var builder = new InMemoryIndexBuilder(_tmp);
        var docs = new[] { "doc1", "doc2" };

        await builder.BuildIndexAsync(docs);

        var file = Path.Combine(_tmp, "index.json");
        Assert.True(File.Exists(file));

        var txt = await File.ReadAllTextAsync(file);
        var obj = JsonSerializer.Deserialize<JsonElement>(txt);
        Assert.Equal(2, obj.GetProperty("Count").GetInt32());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); } catch { }
    }
}
