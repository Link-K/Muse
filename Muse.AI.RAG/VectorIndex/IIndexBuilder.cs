using System.Threading.Tasks;
namespace Muse.AI.RAG.VectorIndex
{
	public interface IIndexBuilder
	{
		Task BuildIndexAsync(string[] documents);
	}
}
