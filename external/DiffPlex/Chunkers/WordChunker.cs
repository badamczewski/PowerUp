namespace DiffPlex.Chunkers
{
    public class WordChunker:DelimiterChunker
    {
        private static char[] WordSeparaters { get; } = { ' ', '\t', '.', '(', ')', '{', '}', ',', '!', '?', ';' };

        /// <summary>
        /// Gets the default singleton instance of the chunker.
        /// </summary>
        public static WordChunker Instance { get; } = new WordChunker();

        public WordChunker() : base(WordSeparaters)
        {
        }
    }
}