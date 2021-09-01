using System.Collections.Generic;

namespace PowerUp.Core.Compilation
{
    public class ILParser
    {
        private List<ILPToken> tokens = null;
        private int IP = 0;

        public ILClass Parse(List<ILPToken> tokens)
        {
            ILTokenizer tokenizer = new ILTokenizer();
            this.tokens = tokens;
            IP = 0;

            //
            // For now let's just parse methods;
            // @TODO: 30.08 BA Improve this.
            // 
            ILClass root = new ILClass();

            for(IP = 0; IP < tokens.Count; IP++)
            {
                var token = tokens[IP];
                if(token.Is(ILTokenKind.Word) && token.GetValue() == ".method")
                {
                    root.Methods.Add(ParseMethod());
                }
            }

            return root;
        }

        private ILMethod ParseMethod()
        {
            ILMethod method = new ILMethod();

            //
            // Parse Attributes:
            // Parse Accessor + HideBySig + Instance
            //
            var accessorToken = Consume();
            var sigToken      = Consume();
            var instanceToken = Consume();

            method.Accessor = accessorToken.GetValue();

            //
            // Parse Method Signatue
            //
            var returnType = Consume();
            var name = Consume(); 
            method.Returns = returnType.GetValue();
            method.Name = name.GetValue();
            method.Args = ParseMethodArguments();

            //
            // Parse Method Code
            //
            if (Find(ILTokenKind.LBracket))
            {
                Find(ILTokenKind.Word);
                //
                // Possible maxstack opcode here.
                // This is probably required but I would like to avoid it.
                //
                var instructionToken = tokens[IP];
                if (instructionToken.GetValue() == ".maxstack")
                {
                    //Size
                    var size = Consume();
                    method.StackSize = int.Parse(size.GetValue());
                    //
                    // Move to code
                    //
                    instructionToken = Consume();
                }

                //
                // Opcodes can be labeled, so we need to parse two options:
                // 1) L0001: ret
                // 2) ret
                //
                for (;IP < tokens.Count;)
                {
                    var ilInstruction = new ILInst();
                    if (instructionToken.Is(ILTokenKind.RBracket)) break;
                    //
                    // Label
                    //
                    if (IsLabel(instructionToken))
                    {
                        ilInstruction.Label = instructionToken.GetValue();
                        ilInstruction.Label = ilInstruction.Label.Substring(0, ilInstruction.Label.Length - 1);

                        instructionToken = Consume();
                        ilInstruction.OpCode = instructionToken.GetValue();
                        method.Code.Add(ilInstruction);

                        //
                        // We might have arg list here.
                        //
                        instructionToken = Consume();

                        //
                        // Continue or Exit.
                        //
                        if (IsLabel(instructionToken)) { continue; }
                        else if (instructionToken.Is(ILTokenKind.RBracket))
                            break;
                        //
                        // Parse Argument.
                        // Here are some examples: 
                        // - 123
                        // - "abc"
                        // - [System.Private.CoreLib]System.Int32
                        // - (LABEL1, LABEL2)

                        //
                        // @TODO 31.08.21 BA Expand this.
                        //

                        // Array, Call, or Type
                        if (instructionToken.Is(ILTokenKind.LIndex))
                        {
                            //
                            // Parse the entire index and whatever is left.
                            // The index brackets should denote the library/namespace in this context.
                            //
                            if (Find(ILTokenKind.RIndex))
                            {
                                //
                                // Fetch the type.
                                //
                                instructionToken = Consume();
                                ilInstruction.Arguments = new[] { instructionToken.GetValue() };
                            }
                            //
                            // @TODO: Report Error.
                            //
                        }
                        //
                        // Arg List
                        //
                        else if (instructionToken.Is(ILTokenKind.LParen))
                        {
                            List<string> argumentList = new List<string>();
                            for (; IP < tokens.Count;)
                            {
                                var arg = Consume();
                                if (arg.Is(ILTokenKind.RParen)) break;

                                argumentList.Add(arg.GetValue());

                                var possibleCommaOrEnd = Peek();
                                if (possibleCommaOrEnd.Is(ILTokenKind.Comma))
                                {
                                    Consume();
                                    continue;
                                }
                                else if (possibleCommaOrEnd.Is(ILTokenKind.RParen))
                                {
                                    Consume();
                                    break;
                                }
                            }

                            ilInstruction.Arguments = argumentList.ToArray();
                        }
                        //
                        // Meh, we should detect that in tokenization; for not let's leave it like that.
                        // 
                        else if (ilInstruction.OpCode == "call")
                        {
                            for (; IP < tokens.Count;)
                            {
                                instructionToken = Consume();

                                if (instructionToken.Is(ILTokenKind.LIndex))
                                {
                                    if (Find(ILTokenKind.RIndex))
                                    {
                                        //
                                        // This should be our method call.
                                        //
                                        instructionToken = Consume();
                                    }
                                }
                                //
                                // @TODO: Report Error.
                                //
                            }

                            ilInstruction.Arguments = new[] { instructionToken.GetValue() };
                        }
                        else
                        {
                            ilInstruction.Arguments = new[] { instructionToken.GetValue() };
                        }
                    }
                    // Value
                    else if(instructionToken.Is(ILTokenKind.Word))
                    {
                        ilInstruction.OpCode = instructionToken.GetValue();
                        method.Code.Add(ilInstruction);
                    }

                    instructionToken = Consume();
                }
            }

            return method;    
        }

        private bool IsLabel(ILPToken token)
        {
            return token.Kind == ILTokenKind.Word && token.GetValue().EndsWith(":");
        }

        private bool Find(ILTokenKind kind)
        {
            for (; IP < tokens.Count; IP++)
                if (tokens[IP].Kind == kind)
                    return true;

            return false;
        }

        private List<ILMethodArg> ParseMethodArguments()
        {
            List<ILMethodArg> args = new List<ILMethodArg>();
            //
            // Skip over LParen
            //
            Consume();
            //
            // Arg
            //
            for(;IP < tokens.Count;)
            {
                var argType = Consume();
                var argName = Consume();

                args.Add(new ILMethodArg() { Name = argName.GetValue(), Type = argType.GetValue() });

                var possibleCommaOrEnd = Peek();
                if (possibleCommaOrEnd.Is(ILTokenKind.Comma))
                {
                    Consume();
                    continue;
                }
                else if (possibleCommaOrEnd.Is(ILTokenKind.RParen))
                {
                    Consume();
                    break;
                }

            }

            return args;
        }

        private ILPToken Peek()
        {
            return tokens[IP + 1];
        }

        private ILPToken Consume()
        {
            return tokens[++IP];
        }

        //
        // This is a function that will create the error and return it up.
        //
        private void Error(string message) { }
    }
}
