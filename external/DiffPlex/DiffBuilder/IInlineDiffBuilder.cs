using DiffPlex.DiffBuilder.Model;

namespace DiffPlex.DiffBuilder
{
    public interface IInlineDiffBuilder
    {
        DiffPaneModel BuildDiffModel(string oldText, string newText);
        DiffPaneModel BuildDiffModel(string oldText, string newText, bool ignoreWhitespace, bool ignoreCase, IChunker chunker);
    }
}
