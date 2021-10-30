using System;
using System.Collections.Generic;
using System.Linq;
using NetConsole = System.Console;

namespace PowerUp.Core.Console
{
    /// <summary>
    /// XConsole is an console extension that allows it's users
    /// to write to console and format its output at the same time;
    /// The console uses a parser to create formatted output.
    /// 
    /// Additionally the Console provides simple drawing methods like plots,
    /// vectors, lists and tables.
    /// </summary>
    public static class XConsole
    {
        static XConsoleTokenizer tokenizer = new XConsoleTokenizer();

        private static Dictionary<string, ConsolePattern> patterns =
            new Dictionary<string, ConsolePattern>()
        {
            { "*", new ConsolePattern() { Color = NetConsole.ForegroundColor } },
            { "`", new ConsolePattern() { Color = ConsoleColor.Green } },
            { "'", new ConsolePattern() { Color = ConsoleColor.Red } }
        };

        public static void DrawPlot(int x, int y, double[] values)
        {
            var topCopy = NetConsole.CursorTop;
            var leftCopy = NetConsole.CursorLeft;

            var max = values.Max();
            var min = values.Min();
            var size = max / 10;

            int index = 0;
            int inc = 0;
            foreach (var value in values)
            {
                int bars = (int)(value / size);
                bool isMin = value == min;
                bool isMax = value == max;

                for (int i = 0; i < bars; i++)
                {
                    NetConsole.SetCursorPosition(x + inc, y - i);

                    if (i == bars - 1)
                    {
                        var topBar = CreateBorderTop();
                           
                        if (isMax)
                            topBar = $"`{topBar}`";

                        if (isMin)
                            topBar = $"'{topBar}'";

                        Write(topBar);
                    }
                    else if (i == 0)
                    {
                        var bottom = CreateBorderBottom();
                        Write(bottom);
                    }
                    else if (i == 1)
                    {
                        var cell = CreateCell($"`{index}`");
                        Write(cell);
                    }
                    else
                    {
                        var cell = CreateCell($".");
                        Write(cell);
                    }
                }

                inc += 4;
                index++;
            }

            NetConsole.SetCursorPosition(leftCopy, topCopy);
        }

        public static void DrawSeparator(int len)
        {
            WriteLine(new string(ConsoleBorderStyle.TopBottom, len));
        }

        public static void DrawSeparator(int len, int x, int y)
        {
            var xc = NetConsole.CursorLeft;
            var yc = NetConsole.CursorTop;

            NetConsole.SetCursorPosition(x, y);
            WriteLine(new string(ConsoleBorderStyle.TopBottom, len));
            NetConsole.SetCursorPosition(xc, yc);
        }

        public static int WriteTokens(ref int i, string pattern, List<Token> tokens)
        {
            var count = 0;
            var lastStyle = NetConsole.ForegroundColor;
            NetConsole.ForegroundColor = patterns[pattern].Color;

            for (; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token is QuoteToken quote)
                {
                    //
                    // Check if we are in the same pattern.
                    // If so then we need to reset back to default
                    // pattern.
                    //
                    if (token.GetValue() == pattern)
                    {
                        break;
                    }

                    i++;
                    count += WriteTokens(ref i, token.GetValue(), tokens);
                }
                else
                {
                    var value = token.GetValue();
                    count += value.Length;
                    NetConsole.Write(value);
                }
            }

            NetConsole.ForegroundColor = lastStyle;
            return count;
        }

        public static int Write(string text)
        {
            var tokens = tokenizer.Tokenize(text);
            int i = 0;
            return WriteTokens(ref i, "*", tokens);
        }

        public static int WriteLine(string text)
        {
            return Write(text + Environment.NewLine);
        }

        public static void SetCursorPosition(int x, int y)
        {
            NetConsole.SetCursorPosition(x, y);
        }

        public static int CursorTop => NetConsole.CursorTop;
        public static int CursorLeft => NetConsole.CursorLeft;

        private static string CreateBorderTop(int size = 1)
        {
            return
                ConsoleBorderStyle.TopLeft.ToString() +
                new string(ConsoleBorderStyle.TopBottom, size) +
                ConsoleBorderStyle.TopRight.ToString();
        }

        private static string CreateBorderBottom(int size = 1)
        {
            return
                ConsoleBorderStyle.SeparatorBottom.ToString() +
                new string(ConsoleBorderStyle.TopBottom, size) +
                ConsoleBorderStyle.SeparatorBottom.ToString();
        }

        private static string CreateCell(string value)
        {
            return
                ConsoleBorderStyle.Left.ToString() +
                value +
                ConsoleBorderStyle.Right.ToString();
        }

        struct ConsolePattern
        {
            public ConsoleColor Color;
        }

        public struct ConsoleBorderStyle
        {
            public const char TopBottom = '─';
            public const char Left = '│';
            public const char Right = '│';
            public const char TopRight = '┐';
            public const char TopLeft = '┌';
            public const char BottomLeft = '└';
            public const char BottomRight = '┘';
            public const char SeparatorLeft = '├';
            public const char SeparatorRight = '┤';
            public const char SeparatorTop = '┬';
            public const char SeparatorBottom = '┴';
            public const char SeparatorBoth = '┼';
            public const char Bullet = '◦';
            public const char Arrow = '➜';
        }
    }
}
