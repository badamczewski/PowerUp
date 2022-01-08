using PowerUp.Core.Decompilation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Watcher
{
    public class InlineDetector
    {
        public static IEnumerable<string> DetectInlining(DecompiledMethod method)
        {
            List<string> inliningCalls = new List<string>();
            //
            // If we have any calls on the list, and we cannot find
            // any jmp, call, calli (Jump Out) in the assembly instruction list
            // then it must mean that the code got eliminated or inlined
            // (which is the same thing*)
            //
            if (method.Calls.Any())
            {
                //
                // Make a copy of the call list.
                //
                foreach (var call in method.Calls)
                    inliningCalls.Add(call.ToMethodCallSignature());
                //
                // This alghoritm is very tricky and buggy and we should
                // replace it with something better.
                //
                // The idea is that we go through each jump or call instruction
                // and check if we match any signature, if we match the signature
                // then we remove an inlining candidate from the list of inlining methods
                // if nothing is left then cool we finish early.
                //
                // This is working by *inverse* we assume that all calls inline then go through the
                // list and remove them, if we made a mistake downstream and incorectly detect the signtature
                // or the reference call then we will be putting tons of inlining calls but they will be ...
                //
                // False Positives 
                //
                foreach (var inst in method.Instructions)
                {
                    if(inst.jumpDirection == JumpDirection.Out)
                    {
                        if(inst.Arguments.Any())
                        {
                            var callArgument = inst.Arguments.First();
                            foreach(var methodCall in method.Calls)
                            {
                                var sig = methodCall.ToMethodCallSignature();
                                if (sig == callArgument.Value)
                                {
                                    var foundItem = inliningCalls.Where(x => x == sig).FirstOrDefault();
                                    if (foundItem != null)
                                    {
                                        inliningCalls.Remove(foundItem);
                                    }
                                    //
                                    // Nothing to check, exit
                                    //
                                    else if (inliningCalls.Count == 0) 
                                        return inliningCalls;

                                    break;
                                }
                            }
                        }
                    }
                }
            }
            return inliningCalls;
        }
    }
}
