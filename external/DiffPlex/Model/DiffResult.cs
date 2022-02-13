using System.Collections.Generic;

namespace DiffPlex.Model
{
    /// <summary>
    /// The result of diffing two peices of text
    /// </summary>
    public class DiffResult
    {
        /// <summary>
        /// The chunked peices of the old text
        /// </summary>
        public string[] PiecesOld { get; }

        /// <summary>
        /// The chunked peices of the new text
        /// </summary>
        public string[] PiecesNew { get; }


        /// <summary>
        /// A collection of DiffBlocks which details deletions and insertions
        /// </summary>
        public IList<DiffBlock> DiffBlocks { get; }

        public DiffResult(string[] peicesOld, string[] piecesNew, IList<DiffBlock> blocks)
        {
            PiecesOld = peicesOld;
            PiecesNew = piecesNew;
            DiffBlocks = blocks;
        }
    }
}