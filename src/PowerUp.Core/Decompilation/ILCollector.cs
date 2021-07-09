using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Core.Decompilation
{
    public class ILCollector : ITextOutput
    {
        public List<ILToken> ILCodes = new List<ILToken>();

        public string IndentationString { get; set; }

        public void Indent()
        {
            ILCodes.Add(new ILToken() { Type = ILTokenType.Indent });
        }

        public void MarkFoldEnd()
        {
            ILCodes.Add(new ILToken() { Type = ILTokenType.FoldEnd });
        }

        public void MarkFoldStart(string collapsedText = "...", bool defaultCollapsed = false)
        {
            ILCodes.Add(new ILToken() { Type = ILTokenType.FoldStart });
        }

        public void Unindent()
        {
            ILCodes.Add(new ILToken() { Type = ILTokenType.Unindent });
        }

        public void Write(char ch)
        {
            ILCodes.Add(new ILToken() { Type = ILTokenType.Char, Value = ch.ToString() });
        }

        public void Write(string text)
        {
            ILCodes.Add(new ILToken() { Type = ILTokenType.Text, Value = text });
        }

        public void WriteLine()
        {
            ILCodes.Add(new ILToken() { Type = ILTokenType.NewLine, Value = "\r\n" });
        }

        public void WriteLocalReference(string text, object reference, bool isDefinition = false)
        {
            ILCodes.Add(new ILToken() { Type = ILTokenType.LocalRef, Value = text });
        }

        public void WriteReference(OpCodeInfo opCode, bool omitSuffix = false)
        {
            ILCodes.Add(new ILToken() { Type = ILTokenType.OpCode, Value = opCode.Name });
        }

        public void WriteReference(PEFile module, Handle handle, string text, string protocol = "decompile", bool isDefinition = false)
        {
            ILCodes.Add(new ILToken() { Type = ILTokenType.Ref, Value = text });
        }

        public void WriteReference(IType type, string text, bool isDefinition = false)
        {
            ILCodes.Add(new ILToken() { Type = ILTokenType.Ref, Value = text });
        }

        public void WriteReference(IMember member, string text, bool isDefinition = false)
        {
            ILCodes.Add(new ILToken() { Type = ILTokenType.Ref, Value = text });
        }
    }
}
