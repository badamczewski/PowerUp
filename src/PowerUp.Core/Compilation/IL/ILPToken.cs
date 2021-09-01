using System;

namespace PowerUp.Core.Compilation
{
    public enum ILTokenKind { NA, Word, Comment, LBracket, RBracket, LParen, RParen, Comma, LIndex, RIndex, String }

    public class ILPToken
    {
        private ReadOnlyMemory<char> _input;
        public ILPToken(ILTokenKind kind, ReadOnlyMemory<char> input, int pos)
        {
            _input = input;
            Position = pos;
            Kind = kind;
        }

        private string value;
        public string GetValue()
        {
            if (value == null)
                value = _input.ToString();

            return value;
        }
        public ILTokenKind Kind { get; set; }
        public int Position { get; set; }

        public override string ToString()
        {
            return $"{Kind.ToString()} => {GetValue()}";
        }

        public bool Is(ILTokenKind kind) => Kind == kind;
    }
}
