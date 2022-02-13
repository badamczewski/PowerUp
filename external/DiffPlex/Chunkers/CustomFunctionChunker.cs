using System;

namespace DiffPlex.Chunkers
{
    public class CustomFunctionChunker: IChunker
    {
        private readonly Func<string, string[]> customChunkerFunc;

        public CustomFunctionChunker(Func<string, string[]> customChunkerFunc)
        {
            if (customChunkerFunc == null) throw new ArgumentNullException(nameof(customChunkerFunc));
            this.customChunkerFunc = customChunkerFunc;
        }

        public string[] Chunk(string text)
        {
            return customChunkerFunc(text);
        }
    }
}