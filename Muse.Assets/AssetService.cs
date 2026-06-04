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
			if (!string.IsNullOrWhiteSpace(assetsRoot))
			{
				_assetsRoot = assetsRoot!;
				return;
			}

			// Try to locate repository root by walking up from base directory (look for .sln or .git)
			string baseDir = AppContext.BaseDirectory ?? Environment.CurrentDirectory;
			var dir = new DirectoryInfo(baseDir);
			DirectoryInfo? repoRoot = null;
			while (dir != null)
			{
				// stop at drive root
				if (dir.GetFiles("*.sln").Any() || Directory.Exists(Path.Combine(dir.FullName, ".git")))
				{
					repoRoot = dir;
					break;
				}
				dir = dir.Parent;
			}
			if (repoRoot is not null)
			{
				// place assets under repository `files/assets` so they are visible in project tree
				_assetsRoot = Path.Combine(repoRoot.FullName, "files", "assets");
			}
			else
			{
				// fallback to current working directory
				_assetsRoot = Path.Combine(Environment.CurrentDirectory, "assets");
			}
		}

		public async Task<string> SaveImageAsync(byte[] imageData, string suggestedName)
		{
			if (imageData == null || imageData.Length == 0) throw new ArgumentException("imageData is empty", nameof(imageData));
			if (string.IsNullOrWhiteSpace(suggestedName)) throw new ArgumentException("suggestedName is required", nameof(suggestedName));

			try { Console.WriteLine($"[DEBUG] AssetService.SaveImageAsync assetsRoot={_assetsRoot}, suggestedName={suggestedName}, imageLen={imageData.Length}"); } catch { }
			Directory.CreateDirectory(_assetsRoot);

			var safeName = Path.GetFileName(suggestedName);
			if (string.IsNullOrWhiteSpace(safeName)) safeName = suggestedName;

			// Compute content hash to detect duplicates
			string newHash;
			using (var sha = System.Security.Cryptography.SHA256.Create())
			{
				newHash = BitConverter.ToString(sha.ComputeHash(imageData)).Replace("-", string.Empty);
			}

			// 1) If a file with the same name exists and content matches, reuse it
			var candidatePath = Path.Combine(_assetsRoot, safeName);
			if (File.Exists(candidatePath))
			{
				try
				{
					var existing = await File.ReadAllBytesAsync(candidatePath).ConfigureAwait(false);
					using var sha = System.Security.Cryptography.SHA256.Create();
					var existingHash = BitConverter.ToString(sha.ComputeHash(existing)).Replace("-", string.Empty);
					if (string.Equals(existingHash, newHash, StringComparison.OrdinalIgnoreCase))
					{
						return Path.Combine("assets", safeName).Replace(Path.DirectorySeparatorChar, '/');
					}
				}
				catch { }
			}

			// 2) Search entire assets folder for any file with identical content and reuse it
			try
			{
				var files = Directory.GetFiles(_assetsRoot);
				foreach (var f in files)
				{
					try
					{
						var existing = await File.ReadAllBytesAsync(f).ConfigureAwait(false);
						using var sha = System.Security.Cryptography.SHA256.Create();
						var existingHash = BitConverter.ToString(sha.ComputeHash(existing)).Replace("-", string.Empty);
						if (string.Equals(existingHash, newHash, StringComparison.OrdinalIgnoreCase))
						{
							var fileName = Path.GetFileName(f);
							return Path.Combine("assets", fileName).Replace(Path.DirectorySeparatorChar, '/');
						}
					}
					catch { }
				}
			}
			catch { }

			// 3) Save new file; avoid name collisions by appending numeric suffix
			var path = Path.Combine(_assetsRoot, safeName);
			var baseName = Path.GetFileNameWithoutExtension(safeName);
			var ext = Path.GetExtension(safeName);
			int idx = 1;
			while (File.Exists(path))
			{
				// if file exists but content didn't match earlier, find a new name
				var candidate = $"{baseName}-{idx}{ext}";
				path = Path.Combine(_assetsRoot, candidate);
				idx++;
			}

			await File.WriteAllBytesAsync(path, imageData).ConfigureAwait(false);
			var finalName = Path.GetFileName(path);
			try { Console.WriteLine($"[DEBUG] AssetService wrote file: {path}, exists={File.Exists(path)}, size={(File.Exists(path) ? new FileInfo(path).Length : -1)}"); } catch { }
			return Path.Combine("assets", finalName).Replace(Path.DirectorySeparatorChar, '/');
		}
	}
}
