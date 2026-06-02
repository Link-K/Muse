using System;
using System.IO;
using System.Threading.Tasks;

namespace Muse.Assets
{
    public class AssetService
    {
        private readonly string _assetsRoot;

        public AssetService(string? assetsRoot = null)
        {
            // default to an `assets` directory under current working directory
            _assetsRoot = string.IsNullOrWhiteSpace(assetsRoot) ? Path.Combine(Environment.CurrentDirectory, "assets") : assetsRoot;
        }

        public async Task<string> SaveImageAsync(byte[] imageData, string suggestedName)
        {
            if (imageData == null || imageData.Length == 0) throw new ArgumentException("imageData is empty", nameof(imageData));
            if (string.IsNullOrWhiteSpace(suggestedName)) throw new ArgumentException("suggestedName is required", nameof(suggestedName));

            Directory.CreateDirectory(_assetsRoot);

            var safeName = Path.GetFileName(suggestedName);
            var path = Path.Combine(_assetsRoot, safeName);

            await File.WriteAllBytesAsync(path, imageData).ConfigureAwait(false);

            // return a relative path using forward slashes as repository expects
            var rel = Path.Combine("assets", safeName).Replace(Path.DirectorySeparatorChar, '/');
            return rel;
        }
    }
}
