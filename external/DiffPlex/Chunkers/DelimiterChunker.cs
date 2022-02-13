using System;
using System.Collections.Generic;

namespace DiffPlex.Chunkers
{
    public class DelimiterChunker:IChunker
    {
        private readonly char[] delimiters;

        public DelimiterChunker(char[] delimiters)
        {
            if (delimiters is null || delimiters.Length == 0)
            {
                throw new ArgumentException($"{nameof(delimiters)} cannot be null or empty.", nameof(delimiters));
            }

            this.delimiters = delimiters;
        }

        public string[] Chunk(string str)
        {
            var list = new List<string>();
            int begin = 0;
            bool processingDelim = false;
            int delimBegin = 0;
            for (int i = 0; i < str.Length; i++)
            {
                if (Array.IndexOf(delimiters, str[i]) != -1)
                {
                    if (i >= str.Length - 1)
                    {
                        if (processingDelim)
                        {
                            list.Add(str.Substring(delimBegin, (i + 1 - delimBegin)));
                        }
                        else
                        {
                            list.Add(str.Substring(begin, (i - begin)));
                            list.Add(str.Substring(i, 1));
                        }
                    }
                    else
                    {
                        if (!processingDelim)
                        {
                            list.Add(str.Substring(begin, (i - begin)));
                            processingDelim = true;
                            delimBegin = i;
                        }
                    }

                    begin = i + 1;
                }
                else
                {
                    if (processingDelim)
                    {
                        if (i - delimBegin > 0)
                        {
                            list.Add(str.Substring(delimBegin, (i - delimBegin)));
                        }

                        processingDelim = false;
                    }

                    if (i >= str.Length - 1)
                    {
                        list.Add(str.Substring(begin, (i + 1 - begin)));
                    }
                }
            }

            return list.ToArray();
        }
    }
}