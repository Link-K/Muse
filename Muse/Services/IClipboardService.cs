using System.Threading.Tasks;

namespace Muse.Services
{
	public interface IClipboardService
	{
		/// <summary>
		/// 尝试将文本写入系统剪贴板。返回是否成功写入剪贴板。
		/// </summary>
		Task<bool> SetTextAsync(string text);

		/// <summary>
		/// 尝试从系统剪贴板读取图片并以字节数组返回。若无图片则返回 null。
		/// 默认实现返回 null，具体平台实现可覆盖以提供图片读取能力。
		/// </summary>
		async Task<byte[]?> GetImageAsync()
		{
			return await Task.FromResult<byte[]?>(null).ConfigureAwait(false);
		}

		/// <summary>
		/// 尝试从系统剪贴板读取文本（用于 file:// URIs 或 data:URI）。
		/// 默认实现返回 null，平台实现可覆盖。
		/// </summary>
		async Task<string?> GetTextAsync()
		{
			return await Task.FromResult<string?>(null).ConfigureAwait(false);
		}
	}
}
