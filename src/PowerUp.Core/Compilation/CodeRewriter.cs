using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Core.Compilation
{
    public class CodeRewriter : CSharpSyntaxRewriter
    {
        private readonly CompilationOptions _options;
        private readonly StringBuilder _benchCodeBuilder = new();

        public CodeRewriter(CompilationOptions options)
        {
            _options = options;
        }

        public string GetBenchCodeOrEmpty()
        {
            return _benchCodeBuilder.ToString();
        }

        public override SyntaxNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            if (node.AttributeLists.Count > 0)
            {
                foreach (var list in node.AttributeLists)
                {
                    if (list.Attributes.FirstOrDefault(x => x.Name.ToString() == "Bench") != null)
                    {
                        //
                        // Generate benchmark function
                        //
                        var functionName = node.Identifier.ValueText;
                        //
                        // Add Bench Code
                        //
                        _benchCodeBuilder.Append($@"
                                    public long Bench_{functionName}() 
                                    {{ 
                                        Stopwatch w = new Stopwatch();
                                        for(int i = 0; i < 1000; i++) {functionName}();
                                        w.Start();
                                        for(int i = 0; i < 1000; i++) {functionName}();
                                        w.Stop();
                                        return w.ElapsedMilliseconds;
                                    }}

                                    ");
                    }
                }
            }

            return base.VisitLocalFunctionStatement(node);
        }

        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (node?.Expression?.ToString() == "Print")
            {
                var printCall = node.Expression.ToString();
                var arg = node.ArgumentList.Arguments[0].ToString();
                if (node.ArgumentList.Arguments[0].Expression.Kind() == SyntaxKind.IdentifierName)
                {
                    //
                    // Convert single arg print to multi arg print.
                    //
                    var nameOfParam = SyntaxFactory.IdentifierName(arg);
                    var nameofName = SyntaxFactory.IdentifierName("nameof");
                    var nameofArgs = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList<ArgumentSyntax>(new[] { SyntaxFactory.Argument(nameOfParam) }));
                    var nameofCall = SyntaxFactory.InvocationExpression(nameofName, nameofArgs);
                    var nameOfPrint = SyntaxFactory.IdentifierName("Print");
                    var printArgs = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList<ArgumentSyntax>(new[] { SyntaxFactory.Argument(nameOfParam), SyntaxFactory.Argument(nameofCall) }));

                    var rewritenPrintCall = SyntaxFactory.InvocationExpression(nameOfPrint, printArgs);

                    return rewritenPrintCall.WithTriviaFrom(node);
                }
            }

            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode VisitAttributeList(AttributeListSyntax node)
        {
            var attributes = node.Attributes;
            bool remove = false;
            foreach (var attr in attributes)
            {
                var name = attr.Name.ToString();
                switch (name)
                {
                    case "ShowGuides":
                        _options.ShowGuides = true;
                        remove = true;
                        break;
                    case "NoGuides":
                        _options.ShowGuides = false;
                        remove = true;
                        break;
                    case "ShowASMDocs":
                        _options.ShowASMDocumentation = true;
                        remove = true;
                        _options.ASMDocumentationOffset = TryExtractValueFromAttribute<int>(attr, "offset");
                        break;
                    case "ShortAddr":
                        _options.ShortAddresses = true;
                        remove = true;
                        _options.AddressesCutByLength = TryExtractValueFromAttribute<int>(attr, "by");
                        break;
                    case "Inline":
                        return CreateAttribute("MethodImpl", "MethodImplOptions", "AggressiveInlining");
                    case "NoInline":
                        return CreateAttribute("MethodImpl", "MethodImplOptions", "NoInlining");
                }
            }
            if (remove)
            {
                return null;
            }

            return base.VisitAttributeList(node);
        }

        private T TryExtractValueFromAttribute<T>(AttributeSyntax attr, string name)
        {
            if (attr.ArgumentList != null)
            {
                foreach (var arg in attr.ArgumentList.ChildNodes())
                {
                    if (arg is AttributeArgumentSyntax argument)
                    {
                        if (argument.NameEquals.Name.ToString() == name)
                        {
                            var literalOffset = (LiteralExpressionSyntax)argument.Expression;
                            var value = (T)literalOffset.Token.Value;
                            return value;
                        }
                    }
                }
            }

            return default(T);
        }

        private CSharpSyntaxNode CreateAttribute(string name, string property, string value)
        {
            AttributeArgumentListSyntax attributeArgumentListSyntax = null;

            if (property != null && value != null)
            {
                var memberName = SyntaxFactory.IdentifierName(property);
                var simpleName = (SimpleNameSyntax)SyntaxFactory.IdentifierName(value);
                var memberAccess = SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, memberName, simpleName);

                var expandedAttrArgs = SyntaxFactory.SeparatedList<AttributeArgumentSyntax>();
                expandedAttrArgs = expandedAttrArgs.Add(SyntaxFactory.AttributeArgument(memberAccess));

                attributeArgumentListSyntax = SyntaxFactory.AttributeArgumentList(expandedAttrArgs);
            }
            var expandedlist = SyntaxFactory.SeparatedList<AttributeSyntax>();
            var expandedName = SyntaxFactory.ParseName(name);

            expandedlist = expandedlist.Add(SyntaxFactory.Attribute(expandedName, attributeArgumentListSyntax));
            var expandedAttr = SyntaxFactory.AttributeList(expandedlist);
            return expandedAttr.WithTrailingTrivia(SyntaxFactory.CarriageReturn);
        }
    }

}
