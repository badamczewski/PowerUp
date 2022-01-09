using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Core.Decompilation
{
    public class ILAnalyzer
    {
        private enum ILMethodParseState
        {
            Metadata,
            Return,
            From,
            Name,
            Args
        }
        public void Analyze(DecompilationUnit unit)
        {
            var na = new ILToken();
            ILToken next = na;

            bool isMethodContext = false;

            int methodIndent = 0;
            int methodILStartIndex = 0;
            int methodILEndIndex = 0;
            DecompiledMethod refMethod = null;

            var methods = unit.DecompiledMethods.ToUniqueDictionary(k => k.Name, v => v);

            bool IsMethodContext() => isMethodContext && refMethod != null;

            for (int i = 0; i < unit.ILTokens.Length; i++)
            {
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
                        break;
                    case ILTokenType.LocalRef:
                        break;
                    case ILTokenType.Ref:
                        //
                        // We are enterinng method context.
                        //
                        if (il.Value == ".method")
                        {
                            isMethodContext = true;
                            methodILStartIndex = i;
                        }
                        break;
                    case ILTokenType.Text:

                        string value = il.Value;
                        if (methods.TryGetValue(value, out var method) && isMethodContext)
                        {
                            refMethod = method;
                            methodIndent = 0;
                        }

                        if (value.StartsWith("{"))
                        {
                            //
                            // This marks the start of the method.
                            //
                            if (isMethodContext)
                            {
                                methodIndent++;
                            }
                        }
                        else if (value.StartsWith("}"))
                        {
                            //
                            // Methods in IL can have nested lexical scopes so
                            // we need to count them and act only when we have no lexical scopes
                            // this will mark method end.
                            //
                            if (isMethodContext) methodIndent--;
                            //
                            // Is this end of the method IL ?
                            //
                            if (methodIndent == 0 && IsMethodContext())
                            {
                                isMethodContext = false;
                                methodILEndIndex = i;

                                refMethod.ILOffsetStart = methodILStartIndex;
                                refMethod.ILOffsetEnd = methodILEndIndex;

                                methodILStartIndex = -1;
                                methodILEndIndex = -1;
                                refMethod = null;
                            }
                        }

                        break;
                    case ILTokenType.NewLine:
                        break;
                    case ILTokenType.OpCode:
                        if (next.Type == ILTokenType.LocalRef)
                        {
                            i++;
                        }
                        //
                        // Found a call or call virt:
                        // Get the call signature and try to find the method that is beeing called.
                        //
                        // Once we have the method we have all of the information to be able
                        // to detect if the call was inlinined when we get down to ASM.
                        //
                        value = il.Value;
                        if (IsMethodContext() && value == "call" || value == "callvirt")
                        {
                            var methodSignature = ParseMethodSignature(i + 1, unit.ILTokens, out var newIndex);
                            i = newIndex;
                            //
                            // Match signature and add call metadata to the decompiled method.
                            //
                            if (MatchesAnyMethodSignature(unit, methodSignature, out var matchedCall))
                            {
                                refMethod.Calls.Add(new MethodSignature()
                                {
                                    Name = matchedCall.Name,
                                    Return = matchedCall.Return,
                                    TypeName = matchedCall.TypeName,
                                    Arguments = matchedCall.Arguments,
                                });
                            }
                        }

                        break;
                    case ILTokenType.Indent:
                        break;
                    case ILTokenType.Unindent:
                        break;
                    default:
                        break;
                }
            }
        }

        private bool MatchesAnyMethodSignature(DecompilationUnit unit, MethodSignature methodSignature, out MethodSignature matched)
        {
            matched = null;
            //
            // Match method signature to decompiled methods:
            // Check Names, Then Arguments.
            // @NOTE: This method is missing a few checks like From checks, return type checks
            // and perhaps a few more if we include generic methods, this will be expanded as needed.
            //
            foreach (var call in unit.DecompiledMethods)
            {
                if (call.Name == methodSignature.Name)
                {
                    if (call.Arguments.Length == methodSignature.Arguments.Length)
                    {
                        int matches = methodSignature.Arguments.Length;
                        int idx = 0;
                        foreach (var arg in call.Arguments)
                        {
                            if (arg.ToLower() == methodSignature.Arguments[idx])
                            {
                                matches--;
                                if (matches == 0)
                                {
                                    break;
                                }
                            }
                            idx++;
                        }

                        if (matches == 0)
                        {
                            matched = call;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private MethodSignature ParseMethodSignature(int index, ILToken[] tokens, out int newIndex)
        {
            ILMethodParseState state = ILMethodParseState.Metadata;
            string methodReturn = null;
            string methodType = null;
            string methodName = null;
            List<string> args = new List<string>();
            //
            // Parse signature:
            // (instance | static) ({TYPE}/{RETURN} | {RETURN})::{NAME}({ARGS})
            //
            // To parse the signature we shall be using a simple state machine and go through
            // states like: {Metadata} {Type} {Return} {Name} {Args}; we know what to expect in each
            // state so this should be simple.
            //
            StringBuilder sigBuilder = new StringBuilder();
            for (newIndex = index; newIndex < tokens.Length; newIndex++)
            {
                var ilSig = tokens[newIndex];
                var sigVal = ilSig.Value.Trim();
                if (ilSig.Type == ILTokenType.LocalRef && sigVal.StartsWith("IL"))
                {
                    break;
                }
                else if (sigVal == "" ||
                    sigVal == "(" ||
                    sigVal == ")" ||
                    sigVal == "::" ||
                    sigVal == ",")
                {
                    continue;
                }

                //
                // This state is optional.
                //
                if (sigVal.StartsWith("instance"))
                {
                    state = ILMethodParseState.Return;
                }
                else if (state == ILMethodParseState.Return)
                {
                    //
                    // Nested type is tokenized with return type.
                    //
                    if (sigVal.Contains("/"))
                    {
                        var typeRet = sigVal.Split('/');
                        methodType = typeRet[0];
                        methodReturn = typeRet[1];
                        state = ILMethodParseState.Name;
                        continue;
                    }

                    state = ILMethodParseState.From;
                    methodReturn = sigVal;
                }
                else if (state == ILMethodParseState.From)
                {
                    state = ILMethodParseState.Name;
                    methodType = sigVal;
                }
                else if (state == ILMethodParseState.Name)
                {
                    state = ILMethodParseState.Args;
                    methodName = sigVal;
                }
                else if (state == ILMethodParseState.Args)
                {
                    args.Add(sigVal);
                }
                //
                // Metadata not found, move to the next state.
                //
                else if (state == ILMethodParseState.Metadata)
                    state = ILMethodParseState.Return;
            }

            return new MethodSignature()
            {
                Name = methodName,
                Arguments = args.ToArray(),
                Return = methodReturn,
                TypeName = methodType
            };
        }
    }

}
