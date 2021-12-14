using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerUp.Core.Console
{
    public class XConsoleTokenizer
    {
        private ReadOnlyMemory<char> input;
        private char newLineChar = '\n';
        private char[] hexTable = new char[] {
            'A', 'B', 'C', 'D', 'E', 'F',
            'a', 'b', 'c', 'd', 'e', 'f'
        };

        public List<Token> Tokenize(string program, char newLineChar = '\n')
        {
            List<Token> tokens = new List<Token>();

            input = program.AsMemory();
            this.newLineChar = newLineChar;

            int line = 1;
            int offset = 0;
            int i = 0;
            //
            // Itteration count = len * 2 since there are (rare) cases when processing
            // the entire input will trigger the infinite loop exception, this is esp true
            // with some hidden unicore characters.
            //
            int itterationCount = input.Length * 2;

            while (i < input.Length)
            {
                var c = input.Span[i];

                if (c == newLineChar)
                {
                    i++;
                    line++;
                    offset = 0;

                    continue;
                }
                else if (char.IsDigit(c))
                {
                    var number = ParseNumber(ref i, ref line, ref offset);
                    tokens.Add(number);
                }
                else if (IsBinaryOperator(i))
                {
                    var @operator = ParseBinaryOperator(ref i, ref line, ref offset);
                    tokens.Add(@operator);
                }
                else if (IsChar(i, '=') && (IsBinaryOperator(i + 1) || IsChar(i + 1, '=')))
                {
                    var @operator = ParseBinaryOperator(ref i, ref line, ref offset);
                    tokens.Add(@operator);
                }
                else if (IsOperator(i))
                {
                    var @operator = ParseOperator(ref i, ref line, ref offset);
                    tokens.Add(@operator);
                }
                else if (IsBlockOpen(i))
                {
                    var block = ParseBlockOpen(ref i, ref line, ref offset);
                    tokens.Add(block);
                }
                else if (IsBlockClose(i))
                {
                    var block = ParseBlockClose(ref i, ref line, ref offset);
                    tokens.Add(block);
                }
                else if (IsBracketOpen(c))
                {
                    var bracket = ParseBracketOpen(ref i, ref line, ref offset);
                    tokens.Add(bracket);
                }
                else if (IsBracketClose(c))
                {
                    var bracket = ParseBracketClose(ref i, ref line, ref offset);
                    tokens.Add(bracket);
                }
                else if (IsQuote(c))
                {
                    var quote = ParseQuote(ref i, ref line, ref offset);
                    tokens.Add(quote);
                }
                else if (IsSeparator(c))
                {
                    var separator = ParseSeparator(ref i, ref line, ref offset);
                    tokens.Add(separator);
                }
                else if (char.IsWhiteSpace(c))
                {
                    var whitespace = ParseWhitespace(ref i, ref line, ref offset);
                    tokens.Add(whitespace);
                }
                else if (char.IsLetter(c) || char.IsSymbol(c) || char.IsPunctuation(c))
                {
                    var word = ParseWord(ref i, ref line, ref offset);
                    tokens.Add(word);
                }
                else
                {
                    i++;
                    offset++;
                }

                //
                // Get out of the loop and throw.
                //
                if (itterationCount == 0)
                    throw new Exception("Tokenizer is in an infinite loop");

                itterationCount--;

            }

            return tokens;
        }

        public bool IsQuote(int i)
        {
            if (i < input.Length)
            {
                var c = input.Span[i];
                if (c == '`' || c == '\'' || c == '"')
                    return true;
            }
            return false;
        }

        public bool IsBlockOpen(int i)
        {
            if (i < input.Length)
            {
                var c = input.Span[i];
                if (c == '{')
                    return true;
            }
            return false;
        }

        public bool IsBlockClose(int i)
        {
            if (i < input.Length)
            {
                var c = input.Span[i];
                if (c == '}')
                    return true;
            }
            return false;
        }

        public bool IsChar(int i, char check)
        {
            if (i < input.Length)
            {
                var c = input.Span[i];
                if (c == check)
                    return true;
            }
            return false;
        }

        public bool IsOperator(int i)
        {
            if (i < input.Length)
            {
                var c = input.Span[i];
                if (c == '+' || c == '-' || c == '*' || c == '/' || c == '=' || c == '%')
                    return true;
            }
            return false;
        }

        public bool IsBinaryOperator(int i)
        {
            if (i < input.Length)
            {
                var c = input.Span[i];
                if (c == '>' || c == '<' || c == '!' || c == '&' || c == '|' || c == '^' || c == '~')
                    return true;
            }
            return false;
        }

        public bool IsSeparator(char c)
        {
            return c == ',';
        }

        private bool IsQuote(char c)
        {
            return c == '\'' || c == '\"' || c == '`';
        }

        private bool IsBracketClose(char c)
        {
            return c == ')' || c == ']';
        }

        private bool IsBracketOpen(char c)
        {
            return c == '(' || c == '[';
        }

        private BlockOpenToken ParseBlockOpen(ref int i, ref int line, ref int offset)
        {
            var block = new BlockOpenToken() { Input = input.Slice(i, 1), Line = line, Position = offset };

            i++;
            offset++;

            return block;
        }

        private BlockCloseToken ParseBlockClose(ref int i, ref int line, ref int offset)
        {
            var block = new BlockCloseToken() { Input = input.Slice(i, 1), Line = line, Position = offset };

            i++;
            offset++;

            return block;
        }

        private SeparatorToken ParseSeparator(ref int i, ref int line, ref int offset)
        {
            var separator = new SeparatorToken() { Input = input.Slice(i, 1), Line = line, Position = offset };

            i++;
            offset++;

            return separator;
        }

        private BracketOpenToken ParseBracketOpen(ref int i, ref int line, ref int offset)
        {
            var bracket = new BracketOpenToken() { Input = input.Slice(i, 1), Line = line, Position = offset };

            i++;
            offset++;

            return bracket;
        }

        private BracketCloseToken ParseBracketClose(ref int i, ref int line, ref int offset)
        {
            var bracket = new BracketCloseToken() { Input = input.Slice(i, 1), Line = line, Position = offset };

            i++;
            offset++;

            return bracket;
        }

        private QuoteToken ParseQuote(ref int i, ref int line, ref int offset)
        {
            var quote = new QuoteToken() { Input = input.Slice(i, 1), Line = line, Position = offset };

            i++;
            offset++;

            return quote;
        }

        private TextToken ParseText(ref int i, char enterQuote, ref int line, ref int position)
        {
            int start = i;
            int startPosition = position;

            for (; i < input.Length; i++)
            {
                var c = input.Span[i];
                //
                // Enter and Exit Quotes have to match.
                //
                if (c == enterQuote)
                {
                    break;
                }

                position++;
            }

            var result = new TextToken() { Input = input.Slice(start, i - start), Line = line, Position = startPosition };
            return result;
        }

        private WhitespaceToken ParseWhitespace(ref int i, ref int line, ref int position)
        {
            int start = i;
            int startPosition = position;

            //
            // Word has to start with letter, but after that we allow '_' and digits.
            //
            for (; i < input.Length; i++)
            {
                var c = input.Span[i];

                if (char.IsWhiteSpace(c) == false)
                {
                    break;
                }

                position++;
            }

            var result = new WhitespaceToken() { Input = input.Slice(start, i - start), Line = line, Position = startPosition };
            return result;
        }

        private WordToken ParseWord(ref int i, ref int line, ref int position)
        {
            int start = i;
            int startPosition = position;

            //
            // Word has to start with letter, but after that we allow '_' and digits.
            //
            for (; i < input.Length; i++)
            {
                var c = input.Span[i];

                if (IsQuote(c) == true || 
                    IsBracketClose(c)  ||
                    IsBracketOpen(c)   ||
                    IsSeparator(c)     ||
                    IsOperator(i)      ||
                    (char.IsLetter(c) == false &&
                     char.IsDigit(c) == false  &&
                     char.IsPunctuation(c) == false &&
                     char.IsSymbol(c) == false) && c != '_')
                {
                    break;
                }

                position++;
            }

            var result = new WordToken() { Input = input.Slice(start, i - start), Line = line, Position = startPosition };
            return result;
        }

        private BinaryOperatorToken ParseBinaryOperator(ref int i, ref int line, ref int position)
        {
            int level = 0;
            int start = i;
            int startPosition = position;

            for (; i < input.Length; i++)
            {
                var c = input.Span[i];

                if (IsBinaryOperator(i) == false && IsChar(i, '=') == false)
                {
                    break;
                }
                else
                {
                    switch (c)
                    {
                        case '~': level = 40; break;
                        case '^': level = 40; break;
                        case '&': level = 40; break;
                        case '|': level = 40; break;
                        case '>': level = 50; break;
                        case '<': level = 50; break;
                        case '!': level = 50; break;
                        case '=': level = 50; break;
                    }
                }

                position++;
            }

            var result = new BinaryOperatorToken() { Input = input.Slice(start, i - start), Level = level, Line = line, Position = startPosition };

            return result;
        }

        private OperatorToken ParseOperator(ref int i, ref int line, ref int position)
        {
            int level = 0;
            int start = i;
            int startPosition = position;

            for (; i < input.Length; i++)
            {
                var c = input.Span[i];

                if (IsOperator(i) == false)
                {
                    break;
                }
                else
                {
                    switch (c)
                    {
                        case '-': level = 100; break;
                        case '+': level = 100; break;
                        case '*': level = 200; break;
                        case '/': level = 200; break;
                        case '%': level = 200; break;
                    }
                }

                position++;

            }

            var result = new OperatorToken() { Input = input.Slice(start, i - start), Level = level, Line = line, Position = startPosition };
            return result;
        }

        public NumberToken ParseNumber(ref int i, ref int line, ref int position)
        {
            int start = i;
            int startPosition = position;
            char format = 'n';
            bool signed = true;

            if (i + 1 < input.Length && input.Span[i] == '0' &&
                (input.Span[i + 1] == 'x' || input.Span[i + 1] == 'b'))
            {
                format = input.Span[i + 1];
                position += 2;
                i += 2;
                start = i;
            }

            for (; i < input.Length; i++)
            {
                var c = input.Span[i];

                if (char.IsWhiteSpace(c))
                    break;

                if (char.IsNumber(c) == false && c != '_' && (format == 'x' && hexTable.Contains(c)) == false)
                {
                    break;
                }

                position++;
            }
            //
            // Check sign
            //
            if (i < input.Length && (input.Span[i] == 'u' || input.Span[i] == 'U'))
            {
                signed = false;
                position++;
                i++;
            }

            var result = new NumberToken()
            {
                IsSigned = signed,
                Format = format,
                Input = input.Slice(start, signed == false ? (i - 1) - start : i - start),
                Line = line,
                Position = startPosition
            };
            return result;
        }
    }

    public class Token
    {
        public Token(string name)
        {
            TokenName = name;
        }

        public int Line { get; set; }
        public int Position { get; set; }

        public string TokenName { get; set; }
        public ReadOnlyMemory<char> Input { get; set; }

        private string value;

        public string GetValue()
        {
            if (value == null)
                value = Input.ToString();

            return value;
        }
    }

    public class TextToken : Token
    {
        public TextToken() : base("Text") { }
    }

    public class WordToken : Token
    {
        public WordToken() : base("Word") { }
    }

    public class WhitespaceToken : Token
    {
        public WhitespaceToken() : base("Whitespace") { }
    }

    public class NumberToken : Token
    {
        public char Format { get; set; }
        public bool IsSigned { get; set; }
        public NumberToken() : base("Number") { }
    }

    public class SeparatorToken : Token
    {
        public SeparatorToken() : base("Separator") { }
    }

    public class OperatorToken : Token
    {
        public int Level { get; set; }

        public OperatorToken() : base("Operator") { }

        public OperatorToken(string specificOperator) : base(specificOperator) { }
    }

    public class BinaryOperatorToken : OperatorToken
    {
        public BinaryOperatorToken() : base("BinaryOperator") { }
    }

    public class BlockOpenToken : Token
    {
        public BlockOpenToken() : base("BlockOpen") { }
    }

    public class BlockCloseToken : Token
    {
        public BlockCloseToken() : base("BlockClose") { }
    }

    public class BracketOpenToken : Token
    {
        public BracketOpenToken() : base("BracketOpen") { }
    }

    public class BracketCloseToken : Token
    {
        public BracketCloseToken() : base("BracketClose") { }
    }

    public class QuoteToken : Token
    {
        public QuoteToken() : base("Quote") { }
    }
}
