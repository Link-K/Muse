using System.Threading.Tasks;

namespace Muse.Services
{
	public interface IClipboardService
	{
		/// <summary>
		/// 尝试将文本写入系统剪贴板。返回是否成功写入剪贴板。
		/// </summary>
		Task<bool> SetTextAsync(string text);
	}
}
