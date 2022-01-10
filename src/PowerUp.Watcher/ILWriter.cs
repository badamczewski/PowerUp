using PowerUp.Core.Decompilation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Watcher
{
    public class ILWriter
    {
        private int _opCodeIndentLen;
        public ILWriter(int opCodeIndentLen = 12)
        {
            _opCodeIndentLen = opCodeIndentLen;
        }

        public string ToILString(DecompilationUnit unit)
        {
            var lines = unit.SouceCode.Split(Environment.NewLine);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine();

            int indentLevel = 0;
            string indent = new string(' ', indentLevel);
            var na = new ILToken();
            ILToken next = na;

            for (int i = 0; i < unit.ILTokens.Length; i++)
            {
                i = SkipThroughHiddenMethods(unit, i);

                var il = unit.ILTokens[i];
                if (i + 1 < unit.ILTokens.Length)
                {
                    next = unit.ILTokens[i + 1];
                }
                else
                {
                    next = na;
                }

                switch (il.Type)
                {
                    case ILTokenType.Char:
                        builder.Append($"{il.Value}");
                        break;
                    case ILTokenType.LocalRef:
                        builder.Append($"{il.Value}");
                        break;
                    case ILTokenType.Ref:
                        builder.Append($"{il.Value}");
                        break;
                    case ILTokenType.Text:

                        bool newLineAdded = false;
                        string value = il.Value;
                        if (value.StartsWith("{"))
                        {
                            indentLevel += 4;
                            indent = new string(' ', indentLevel);
                        }
                        else if (value.StartsWith("}"))
                        {
                            indentLevel -= 4;
                            if (indentLevel < 0) indentLevel = 0;
                            indent = new string(' ', indentLevel);

                            builder.Remove(builder.Length - 4, 4);
                            newLineAdded = true;
                        }

                        //
                        // Remove comments.
                        //
                        var commentsIdx = value.IndexOf("//");
                        if (commentsIdx != -1)
                        {
                            value = value.Substring(0, commentsIdx);
                            newLineAdded = true;
                        }

                        //
                        // Sequence Point.
                        //
                        else if (value.StartsWith("(line"))
                        {
                            //
                            // Let's parse this sequence point and get the relevant code line
                            //
                            var point = ParseSequencePoint(value);
                            var startLine = lines[point.StartLine - 1];
                            var endLine = lines[point.EndLine - 1];

                            value = "";
                            for (int lineId = point.StartLine - 1; lineId < point.EndLine; lineId++)
                            {
                                value += "// " + lines[lineId].Trim();
                            }
                        }

                        builder.Append($"{value}");

                        if (newLineAdded && next.Type == ILTokenType.NewLine)
                        {
                            i++;
                        }

                        break;
                    case ILTokenType.NewLine:
                        builder.Append($"{il.Value}{indent}");

                        break;
                    case ILTokenType.OpCode:
                        var offsetLen = _opCodeIndentLen - il.Value.Length;
                        if (offsetLen <= 0) offsetLen = 1;

                        builder.Append($"{il.Value}{new string(' ', offsetLen)}");

                        if (next.Type == ILTokenType.LocalRef)
                        {
                            i++;
                        }

                        break;
                    case ILTokenType.Indent:
                        break;
                    case ILTokenType.Unindent:
                        break;
                    default:
                        builder.Append($"{il.Value}{indent}");
                        break;
                }
            }
            return builder.ToString();
        }

        private int SkipThroughHiddenMethods(DecompilationUnit unit, int index)
        {
            foreach (var method in unit.DecompiledMethods)
            {
                if (method.IsVisible == false && index == method.ILOffsetStart)
                {
                    return method.ILOffsetEnd + 1;
                }
            }
            return index;
        }

        private (int StartLine, int StartCol, int EndLine, int EndCol)
            ParseSequencePoint(string value)
        {
            //
            // A Sequence point is a structure that contains the line and column
            // source map, meaning it will link IL code with language code for C#, F# and
            // other languages in .NET.
            //
            // When we output it to IL stream it will have to be parsed and it will have the
            // following form:
            //
            //    (line 100, col 200) to (line 120, col 220) in SomeFile 
            //  
            // We can parse it as a simple state transition table since the order of the values
            // should be consistent.
            //
            // We care only about digits everything else we throw away.
            //
            bool isDigit = false;
            string tmp = "";
            //
            // state indexes the data table that will collect all of the values.
            //
            int state = 0;
            int[] data = new int[4];
            for (int i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (char.IsDigit(c))
                {
                    isDigit = true;
                    tmp += c;
                }
                else if (isDigit)
                {
                    // No longer is Digit.
                    data[state++] = int.Parse(tmp);
                    tmp = "";
                    isDigit = false;
                }
            }
            return (data[0], data[1], data[2], data[3]);
        }
    }
}
