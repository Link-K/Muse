using System.Threading.Tasks;
using Markdig;

namespace Muse.Export
{
    public class ExportService
    {
        private readonly MarkdownPipeline _pipeline;

        public ExportService()
        {
            _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
        }

        public Task<string> ExportMarkdownToHtmlAsync(string markdown)
        {
            if (markdown == null) markdown = string.Empty;
            var html = Markdig.Markdown.ToHtml(markdown, _pipeline);
            return Task.FromResult(html);
        }
    }
}
