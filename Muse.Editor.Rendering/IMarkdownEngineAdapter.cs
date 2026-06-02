using System.Threading.Tasks;
namespace Muse.Editor.Rendering
{
    public interface IMarkdownEngineAdapter
    {
        Task<string> RenderToHtmlAsync(string markdown);
    }
}
