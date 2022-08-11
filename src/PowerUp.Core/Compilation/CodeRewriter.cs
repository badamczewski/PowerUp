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
        private readonly StringBuilder _benchCodeBuilder = new StringBuilder();
        private readonly StringBuilder _usingBuilder     = new StringBuilder();
        private readonly StringBuilder _structSizeOfbuilder = new StringBuilder();

        public CodeRewriter(CompilationOptions options)
        {
            _options = options;
        }

        public string GetBenchCodeOrEmpty()
        {
            return _benchCodeBuilder.ToString();
        }

        public string GetStructSizeOrEmpty()
        {
            return _structSizeOfbuilder.ToString();
        }

        public string GetUsingsOrEmpty()
        {
            return _usingBuilder.ToString();
        }

        public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node)
        {
            //
            // This using directive is in the wrong place, and we need to move it to usings.
            // The reason for this is the design of the compiler file.
            // We want to have a minimal expierence where you can just type in the file
            // without ever providing any usings, namespaces, classes or anything else,
            // it has to just work.
            //
            // That's why we generate code arround what you type to make the compiler happy.
            // But that also means that if you need something added we have to take it and put it,
            // in the correct place.
            //
            _usingBuilder.AppendLine(node.ToFullString());

            return null;
        }

        public override SyntaxNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            if (node.AttributeLists.Count > 0)
            {
                foreach (var list in node.AttributeLists)
                {
                    var found = list.Attributes.FirstOrDefault(x => x.Name.ToString() == "Bench");
                    if (found != null)
                    {
                        //
                        // Generate benchmark function
                        //
                        var functionName = node.Identifier.ValueText;

                        var warmUpCount = TryExtractValueFromAttribute<int>(found, "WarmUpCount");
                        var runCount    = TryExtractValueFromAttribute<int>(found, "RunCount");
                        var arguments   = TryExtractValueFromAttribute<object[]>(found, "Arguments");


                        if (warmUpCount == 0) warmUpCount = 1000;
                        if (runCount == 0)    runCount = 1000;

                        //
                        // Add Bench Code
                        //
                        string argString = arguments != null ? string.Join(",", arguments) : "";
                        _benchCodeBuilder.Append($@"
                                    public (long,int,int) Bench_{functionName}() 
                                    {{ 
                                        Stopwatch w = new Stopwatch();
                                        for(int i = 0; i < {warmUpCount}; i++) {functionName}({argString});
                                        w.Start();
                                        for(int i = 0; i < {runCount}; i++) {functionName}({argString});
                                        w.Stop();
                                        return (w.ElapsedMilliseconds, {warmUpCount}, {runCount});
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
                    var nameofName  = SyntaxFactory.IdentifierName("nameof");
                    var nameofArgs  = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList<ArgumentSyntax>(new[] { SyntaxFactory.Argument(nameOfParam) }));
                    var nameofCall  = SyntaxFactory.InvocationExpression(nameofName, nameofArgs);
                    var nameOfPrint = SyntaxFactory.IdentifierName("Print");
                    var printArgs   = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList<ArgumentSyntax>(new[] { SyntaxFactory.Argument(nameOfParam), SyntaxFactory.Argument(nameofCall) }));

                    var rewritenPrintCall = SyntaxFactory.InvocationExpression(nameOfPrint, printArgs);

                    return rewritenPrintCall.WithTriviaFrom(node);
                }
            }

            return base.VisitInvocationExpression(node);
        }

        public override SyntaxNode VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            _structSizeOfbuilder.AppendLine($@"
                public static int SizeOf_{node.Identifier.Text}() => Unsafe.SizeOf<{node.Identifier.Text}>();");

            return base.VisitEnumDeclaration(node);
        }

        public override SyntaxNode VisitStructDeclaration(StructDeclarationSyntax node)
        {
            _structSizeOfbuilder.AppendLine($@"
                public static int SizeOf_{node.Identifier.Text}() => Unsafe.SizeOf<{node.Identifier.Text}>();");

            return base.VisitStructDeclaration(node);
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
                    case "QuickJIT":
                        {
                            var local = (LocalFunctionStatementSyntax)node.Parent;
                            var functionName = local.Identifier.ValueText;
                            _options.CompilationMap.TryAdd(functionName, "QuickJIT");
                            remove = true;
                            break;
                        }
                    case "PGO":
                        {
                            var local = (LocalFunctionStatementSyntax)node.Parent;
                            var functionName = local.Identifier.ValueText;
                            _options.CompilationMap.TryAdd(functionName, "PGO");
                            remove = true;
                            break;
                        }
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
                            if(argument.Expression is ArrayCreationExpressionSyntax array)
                            {
                                return (T)ToArray(array);
                            }
                            else if (argument.Expression is ImplicitArrayCreationExpressionSyntax implicitArray)
                            {
                                return (T)ToArray(implicitArray);
                            }
                            var literalOffset = (LiteralExpressionSyntax)argument.Expression;
                            var value = (T)literalOffset.Token.Value;
                            return value;
                        }
                    }
                }
            }

            return default(T);
        }

        private object ToArray(ArrayCreationExpressionSyntax array)
        {
            List<object> list = new List<object>();
            foreach (var exp in array.Initializer.Expressions)
            {
                if (exp is LiteralExpressionSyntax arrayLiteral)
                {
                    var element = arrayLiteral.Token.Value;
                    list.Add(element);
                }
            }
            object obj = list.ToArray();
            return obj;
        }

        private object ToArray(ImplicitArrayCreationExpressionSyntax array)
        {
            List<object> list = new List<object>();
            foreach (var exp in array.Initializer.Expressions)
            {
                if (exp is LiteralExpressionSyntax arrayLiteral)
                {
                    var element = arrayLiteral.Token.Value;
                    list.Add(element);
                }
            }
            object obj = list.ToArray();
            return obj;
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
