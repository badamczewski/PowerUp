using System;
using System.Collections.Generic;

namespace PowerUp.Core.Compilation
{
    public class ILTokenizer
    {
        public List<ILPToken> Tokenize(string ilCode)
        {
            List<ILPToken> tokens = new List<ILPToken>();
            var il = ilCode.AsMemory();

            for (int i = 0; i < il.Length; i++)
            {
                var c = il.Span[i];

                if(c == '\n')
                {
                    continue;
                }
                //
                // Word
                //
                if (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == ':')
                {
                    tokens.Add(TokenizeWord(il, ref i));
                }
                else if (c == '{')
                {
                    tokens.Add(TokenizeSingle(il, ref i, ILTokenKind.LBracket));
                }
                else if (c == '}')
                {
                    tokens.Add(TokenizeSingle(il, ref i, ILTokenKind.RBracket));
                }
                else if (c == '(')
                {
                    tokens.Add(TokenizeSingle(il, ref i, ILTokenKind.LParen));

                }
                else if (c == ')')
                {
                    tokens.Add(TokenizeSingle(il, ref i, ILTokenKind.RParen));
                }
                else if (c == ',')
                {
                    tokens.Add(TokenizeSingle(il, ref i, ILTokenKind.Comma));
                }
                else if (c == '[')
                {
                    tokens.Add(TokenizeSingle(il, ref i, ILTokenKind.LIndex));
                }
                else if (c == ']')
                {
                    tokens.Add(TokenizeSingle(il, ref i, ILTokenKind.RIndex));
                }
                else if (c == '"')
                {
                    tokens.Add(TokenizeString(il, ref i));
                }
                else if (c == '/') //Comment
                {
                    if (i + 1 < il.Length && il.Span[i + 1] == '/')
                    {
                        tokens.Add(TokenizeComment(il, ref i));
                    }
                }

            }
            return tokens;


        }

        private ILPToken TokenizeSingle(ReadOnlyMemory<char> il, ref int i, ILTokenKind kind)
        {
            return new ILPToken(kind, il.Slice(i, 1), i);
        }

        private ILPToken TokenizeComment(ReadOnlyMemory<char> il, ref int i)
        {
            //
            // Move past '//'
            //
            i += 2;

            int pos = i;
            for (; i < il.Length; i++)
            {
                var c = il.Span[i];
                if (c == '\n')
                {
                    break;
                }
            }

            return new ILPToken(ILTokenKind.Comment, il.Slice(pos, i - pos), pos);
        }

        private ILPToken TokenizeString(ReadOnlyMemory<char> il, ref int i)
        {
            //
            // Move past "
            //
            int pos = i;
            i += 1;
            for (; i < il.Length; i++)
            {
                var c = il.Span[i];
                if (c == '"')
                {
                    break;
                }
            }

            return new ILPToken(ILTokenKind.String, il.Slice(pos + 1, i - pos - 1), pos);
        }

        private ILPToken TokenizeWord(ReadOnlyMemory<char> il, ref int i)
        {
            int pos = i;
            int n   = i;
            for (; n < il.Length; n++)
            {
                var c = il.Span[n];
                if (char.IsLetterOrDigit(c) == false && c != ':' && c != '_' && c != '.')
                {
                    break;
                }
            }
            i = n - 1;
            return new ILPToken(ILTokenKind.Word, il.Slice(pos, n - pos), pos);
        }
    }
}
