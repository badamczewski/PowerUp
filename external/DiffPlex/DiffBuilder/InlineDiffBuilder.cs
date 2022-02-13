using System;
using System.Collections.Generic;
using DiffPlex.Chunkers;
using DiffPlex.DiffBuilder.Model;
using DiffPlex.Model;

namespace DiffPlex.DiffBuilder
{
    public class InlineDiffBuilder : IInlineDiffBuilder
    {
        private readonly IDiffer differ;

        /// <summary>
        /// Gets the default singleton instance of the inline diff builder.
        /// </summary>
        public static InlineDiffBuilder Instance { get; } = new InlineDiffBuilder();

        public InlineDiffBuilder(IDiffer differ = null)
        {
            this.differ = differ ?? Differ.Instance;
        }

        public DiffPaneModel BuildDiffModel(string oldText, string newText)
            => BuildDiffModel(oldText, newText, ignoreWhitespace: true);

        public DiffPaneModel BuildDiffModel(string oldText, string newText, bool ignoreWhitespace)
        {
            var chunker = new LineChunker();
            return BuildDiffModel(oldText, newText, ignoreWhitespace, false, chunker);
        }

        public DiffPaneModel BuildDiffModel(string oldText, string newText, bool ignoreWhitespace, bool ignoreCase, IChunker chunker)
        {
            if (oldText == null) throw new ArgumentNullException(nameof(oldText));
            if (newText == null) throw new ArgumentNullException(nameof(newText));

            var model = new DiffPaneModel();
            var diffResult = differ.CreateDiffs(oldText, newText, ignoreWhitespace, ignoreCase: ignoreCase, chunker);
            BuildDiffPieces(diffResult, model.Lines);
            
            return model;
        }

        /// <summary>
        /// Gets the inline textual diffs.
        /// </summary>
        /// <param name="oldText">The old text to diff.</param>
        /// <param name="newText">The new text.</param>
        /// <param name="ignoreWhiteSpace">true if ignore the white space; othewise, false.</param>
        /// <param name="ignoreCase">true if case-insensitive; otherwise, false.</param>
        /// <param name="chunker">The chunker.</param>
        /// <returns>The diffs result.</returns>
        public static DiffPaneModel Diff(string oldText, string newText, bool ignoreWhiteSpace = true, bool ignoreCase = false, IChunker chunker = null)
        {
            return Diff(Differ.Instance, oldText, newText, ignoreWhiteSpace, ignoreCase, chunker);
        }

        /// <summary>
        /// Gets the inline textual diffs.
        /// </summary>
        /// <param name="differ">The differ instance.</param>
        /// <param name="oldText">The old text to diff.</param>
        /// <param name="newText">The new text.</param>
        /// <param name="ignoreWhiteSpace">true if ignore the white space; othewise, false.</param>
        /// <param name="ignoreCase">true if case-insensitive; otherwise, false.</param>
        /// <param name="chunker">The chunker.</param>
        /// <returns>The diffs result.</returns>
        public static DiffPaneModel Diff(IDiffer differ, string oldText, string newText, bool ignoreWhiteSpace = true, bool ignoreCase = false, IChunker chunker = null)
        {
            if (oldText == null) throw new ArgumentNullException(nameof(oldText));
            if (newText == null) throw new ArgumentNullException(nameof(newText));

            var model = new DiffPaneModel();
            var diffResult = (differ ?? Differ.Instance).CreateDiffs(oldText, newText, ignoreWhiteSpace, ignoreCase, chunker ?? LineChunker.Instance);
            BuildDiffPieces(diffResult, model.Lines);
            
            return model;
        }

        private static void BuildDiffPieces(DiffResult diffResult, List<DiffPiece> pieces)
        {
            int bPos = 0;

            foreach (var diffBlock in diffResult.DiffBlocks)
            {
                for (; bPos < diffBlock.InsertStartB; bPos++)
                    pieces.Add(new DiffPiece(diffResult.PiecesNew[bPos], ChangeType.Unchanged, bPos + 1));

                int i = 0;
                for (; i < Math.Min(diffBlock.DeleteCountA, diffBlock.InsertCountB); i++)
                    pieces.Add(new DiffPiece(diffResult.PiecesOld[i + diffBlock.DeleteStartA], ChangeType.Deleted));

                i = 0;
                for (; i < Math.Min(diffBlock.DeleteCountA, diffBlock.InsertCountB); i++)
                {
                    pieces.Add(new DiffPiece(diffResult.PiecesNew[i + diffBlock.InsertStartB], ChangeType.Inserted, bPos + 1));
                    bPos++;
                }

                if (diffBlock.DeleteCountA > diffBlock.InsertCountB)
                {
                    for (; i < diffBlock.DeleteCountA; i++)
                        pieces.Add(new DiffPiece(diffResult.PiecesOld[i + diffBlock.DeleteStartA], ChangeType.Deleted));
                }
                else
                {
                    for (; i < diffBlock.InsertCountB; i++)
                    {
                        pieces.Add(new DiffPiece(diffResult.PiecesNew[i + diffBlock.InsertStartB], ChangeType.Inserted, bPos + 1));
                        bPos++;
                    }
                }
            }

            for (; bPos < diffResult.PiecesNew.Length; bPos++)
                pieces.Add(new DiffPiece(diffResult.PiecesNew[bPos], ChangeType.Unchanged, bPos + 1));
        }
    }
}
