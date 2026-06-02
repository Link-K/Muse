using System.Threading.Tasks;

namespace Muse.Services
{
	public interface IDialogService
	{
		Task ShowMessageAsync(string title, string message);
	}
}
