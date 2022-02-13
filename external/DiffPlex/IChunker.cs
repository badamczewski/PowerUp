namespace DiffPlex
{
    /// <summary>
    /// Responsible for how to turn the document into pieces
    /// </summary>
    public interface IChunker
    {
        /// <summary>
        /// Divide text into sub-parts
        /// </summary>
        string[] Chunk(string text);
    }
}