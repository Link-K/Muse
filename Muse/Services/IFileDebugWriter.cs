using System.Threading.Tasks;

namespace Muse.Services
{
	/// <summary>
	/// 将调试信息写入可检测的位置（用于测试替换和实盘回退）。
	/// 返回写入的文件路径（失败返回 null）。
	/// </summary>
	public interface IFileDebugWriter
	{
		Task<string?> WriteDebugFileAsync(string content);
	}
}
