using ICSharpCode.Decompiler.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
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
                    foreach (var attr in list.Attributes)
                    {
                        var attrName = attr.Name.ToString();
                        if (attrName == "Bench")
                        {
                            //
                            // Generate benchmark function
                            //
                            var functionName = node.Identifier.ValueText;

                            var warmUpCount = TryExtractValueFromAttribute<int>(attr, "WarmUpCount");
                            var runCount = TryExtractValueFromAttribute<int>(attr, "RunCount");
                            var arguments = TryExtractValueFromAttribute<object[]>(attr, "Arguments", Array.Empty<object[]>());

                            if (_options.UseCustomAttributes)
                                node = node.RemoveNode(list, SyntaxRemoveOptions.KeepNoTrivia);

                            //
                            // Do Basic argument validation
                            //
                            var paramsToInsert = node.ParameterList;
                            if (paramsToInsert.Parameters.Count != arguments.Length)
                            {
                                _options.HelpText += "[Bench] Attribute is missing arguments" + Environment.NewLine;
                                _options.HelpText += "Please use:" + Environment.NewLine;
                                _options.HelpText += "    [Bench (Arguments = new object[] {";

                                int count = 0;
                                foreach (var param in paramsToInsert.Parameters)
                                {
                                    _options.HelpText += "(" + param.Type.ToString() + ")<value>";
                                    count++;
                                    if (count < paramsToInsert.Parameters.Count)
                                        _options.HelpText += ",";
                                }
                                _options.HelpText += "})]";

                                return base.VisitLocalFunctionStatement(node);
                            }

                            int itterationCount = 64;
                            int tryCount = 32;

                            if (warmUpCount == 0) warmUpCount = 1000;
                            if (runCount == 0) runCount = 1000;

                            //
                            // Add Benchmark attributes that were created by rewriting
                            // the attribute code.
                            //
                            var argString = CreateFunctionArguments(arguments);

                            //
                            // Figure out how many times the method needs to be run.
                            // We need at least ~100 ms of run time to be able to determine
                            // what's the call time.
                            //
                            // @TODO: Make more roboust and later change to per process run benchmark
                            // this will allow for correct perf mesurements, otherwise all benchmark results
                            // are meh ...
                            //
                            // Here's a simplified procedure:
                            // 1. Call method to JIT Compile.
                            // 2. Warm Up for at least 100ms.
                            // 3. Call method again. (at this point we *hope* to have the optimized version of code)
                            // 4. Start bench calls and measure time.
                            //
                            var functionCall = $"{functionName}({argString})";
                            _benchCodeBuilder.Append($@"
                                    public (decimal,int,int) Bench_{functionName}() 
                                    {{ 
                                        {functionCall};
                                        var itterations = {itterationCount};
                                        var tries = {tryCount};
                                        Stopwatch w = new Stopwatch();
                                        while(tries > 0) 
                                        {{
                                            w.Start();
                                            for(int n = 0; n < itterations; n++)
                                            {{
                                                for(int i = 0; i < {warmUpCount}; i++) {functionCall};
                                            }}
                                            w.Stop();
                                            if(w.ElapsedMilliseconds <= 300)
                                            {{
                                                itterations *= 2;
                                                w.Reset();
                                                tries--;
                                            }}
                                            else {{ break; }}
                                        }}
                                        w.Reset();
                                        {functionCall};
                                        w.Start();
                                        for(int n = 0; n < itterations; n++)
                                        {{
                                            for(int i = 0; i < {runCount}; i++) {functionCall};
                                        }}
                                        var cost = w.ElapsedMilliseconds / (decimal)itterations;

                                        return (cost, {warmUpCount}, {runCount});
                                    }}
                                    ");


                        }
                        else if (attrName == "Run")
                        {
                            var functionName = node.Identifier.ValueText;

                            var arguments = TryExtractValueFromAttribute<object[]>(attr, "Arguments", Array.Empty<object[]>());

                            if (_options.UseCustomAttributes)
                                node = node.RemoveNode(list, SyntaxRemoveOptions.KeepNoTrivia);

                            var argString = CreateFunctionArguments(arguments);
                            var functionCall = $"{functionName}({argString})";
                            //
                            // Create a simple function to be able to run code with arguments.
                            //
                            _benchCodeBuilder.Append($@"
                                    public void Run_{functionName}() 
                                    {{ 
                                        {functionCall};
                                    }}
                                    ");
                        }
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
        private T TryExtractValueFromAttribute<T>(AttributeSyntax attr, string name, T def = default(T))
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

            return def;
        }

        private object ToArray(ArrayCreationExpressionSyntax array)
        {
            List<object> list = new List<object>();
            foreach (var exp in array.Initializer.Expressions)
            {
                var item = ParseArrayItem(exp);
                if (item != null)
                {
                    list.Add(item);
                }
            }
            object obj = list.ToArray();
            return obj;
        }

        private string CreateFunctionArguments(object[] arguments)
        {
            StringBuilder argBuilder = new StringBuilder();
            foreach (var arg in arguments)
            {
                if (arg is ArraryCreation arraryCreation)
                {
                    var array = arraryCreation.Items;
                    argBuilder.Append($"new {arraryCreation.ArrayCreationExpr} {{ ");
                    if (array.Length > 0)
                    {
                        foreach (var inner in array)
                            argBuilder.Append(inner.ToString() + ",");

                        argBuilder.Remove(argBuilder.Length - 1, 1);
                    }
                    argBuilder.Append("},");
                }
                else
                {
                    argBuilder.Append(arg.ToString() + ",");
                }
            }
            if (argBuilder.Length > 0) argBuilder.Remove(argBuilder.Length - 1, 1);

            return argBuilder.ToString();
        }


        public class ArraryCreation
        {
            public string ArrayCreationExpr { get; set; }

            public override string ToString()
            {
                return ArrayCreationExpr;
            }
            public object[] Items { get; set; }
        }

        public class ObjectCreation
        {
            public string CreationExpr { get; set; }

            public override string ToString()
            {
                return CreationExpr;
            }
        }

        private object ToArray(ImplicitArrayCreationExpressionSyntax array)
        {
            List<object> list = new List<object>();
            foreach (var exp in array.Initializer.Expressions)
            {
                var item = ParseArrayItem(exp);
                if (item != null)
                {
                    list.Add(item);
                }
            }
            object obj = list.ToArray();
            return obj;
        }

        private object ParseArrayItem(ExpressionSyntax exp)
        {
            object result = null;

            if (exp is LiteralExpressionSyntax arrayLiteral)
            {
                var element = arrayLiteral.Token.Value;
                result = element;
            }
            else if (_options.UseCustomAttributes && exp is ObjectCreationExpressionSyntax objectCreation)
            {
                return new ObjectCreation() { CreationExpr = objectCreation.ToString() };
            }
            else if (_options.UseCustomAttributes && exp is ArrayCreationExpressionSyntax arrayCreationExpr)
            {
                var typeName = "";
                if (arrayCreationExpr.Type.ElementType is PredefinedTypeSyntax typeSyntax)
                {
                    typeName = "[]";
                }
                else
                {
                    typeName = ((IdentifierNameSyntax)arrayCreationExpr.Type.ElementType).Identifier.Text + "[]";
                }

                ArraryCreation arraryCreation = new ArraryCreation();
                arraryCreation.ArrayCreationExpr = typeName;
                arraryCreation.Items = (object[])ToArray(arrayCreationExpr);

                result = arraryCreation;
            }
            else if (_options.UseCustomAttributes && exp is CastExpressionSyntax castExpressionSyntax)
            {
                var castType = castExpressionSyntax.Type.ToString();
                if (castExpressionSyntax.Expression is InvocationExpressionSyntax maybeRange)
                {
                    if (maybeRange.Expression is IdentifierNameSyntax identifier)
                    {
                        if (identifier.Identifier.ValueText == "Range")
                        {
                            var range = ToRange(maybeRange, castType);
                            result = range;
                        }
                    }
                }
            }
            else if (_options.UseCustomAttributes && exp is InvocationExpressionSyntax invocationExpr)
            {
                var range = ToRange(invocationExpr);
                result = range;
            }

            return result;
        }

        private ArraryCreation ToRange(InvocationExpressionSyntax invocationExpr, string type = "int[]")
        {
            if (invocationExpr.Expression is IdentifierNameSyntax identifierName)
            {
                if (identifierName.Identifier.ValueText == "Range")
                {
                    ArraryCreation arrayFromRange = new ArraryCreation();
                    arrayFromRange.ArrayCreationExpr = type;

                    var fromValue  = invocationExpr.ArgumentList.Arguments[0].ToString();
                    var countValue = invocationExpr.ArgumentList.Arguments[1].ToString();

                    bool isNumeric = int.TryParse(fromValue, out var from);
                    int count = int.Parse(countValue);

                    arrayFromRange.Items = new object[count];

                    for (int i = 0; i < count; i++)
                    {
                        if (isNumeric)
                        {
                            arrayFromRange.Items[i] = from + i;
                        }
                        else
                        {
                            // The Range is not a numeric type, use the fromValue as an object initializer
                            arrayFromRange.Items[i] = fromValue;
                        }
                    }

                    return arrayFromRange;
                }
            }

            return null;
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
