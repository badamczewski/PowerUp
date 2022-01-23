using PowerUp.Core.Decompilation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static PowerUp.Core.Console.XConsole;

namespace PowerUp.Watcher
{
    /// <summary>
    /// Generic class that will be used to detect and set jump guides.
    /// To be able to set Guides for any decompilation type you need the following:
    /// 
    /// 1. The method needs a known address and code size.
    /// 2. Instruction needs to have a  'RefAddress' set.
    /// 3. Instruction Argument needs a 'HasReferenceAddress' flag set.
    /// 
    /// </summary>
    public class JumpGuideDetector
    {
        //
        // This section describes guides drawing.
        //
        // When presented with the following code:
        //
        // 001: A
        // 002: JMP 005
        // 003: B
        // 004: JMP 005
        // 005: C
        // 006: D
        // 007: JMP 001
        //
        // We need to produce the following guides:
        //
        // ┌001: A
        // │┌002: JMP 005
        // ││003: B
        // ││┌004: JMP 005
        // │└└005: C
        // │006: D
        // └007: JMP 001
        //
        // To make this plesant to look at the longest jumps should be the most outer ones.
        // 1. We need to figure out the direction of each jump, and see if the jump is inside our method.
        // 2. We need to compute the lenght of each jump.
        // 3. We then sort the jump table by the longest length
        // 4. We draw the guides.
        //
        // Since we're writing out one instruction at a time from top to bottom we will need an additional piece of information
        // on the instruction or as a lookup table that will tell us if we should draw a line segment(s), for example:
        // When we are writing out 005: C we need to lookup the guides table that will contain:
        //   005 =>
        //      [0] = |
        //      [1] = └
        //      [2] = └
        // 
        public static (int jumpSize, int nestingLevel) PopulateGuides(DecompiledMethod method)
        {
            if (method == null || method.Instructions.Any() == false) return (0, 0);

            var methodAddress = method.CodeAddress;
            var codeSize = method.CodeSize;
            int jumps = 0;

            foreach (var instruction in method.Instructions)
            {
                foreach (var arg in instruction.Arguments)
                {
                    if (arg.HasReferenceAddress && instruction.RefAddress > 0)
                    {
                        instruction.jumpDirection = JumpDirection.Out;
                        if (instruction.RefAddress >= methodAddress && instruction.RefAddress <= methodAddress + codeSize)
                        {
                            jumps++;
                            //
                            //  Jump Up
                            //
                            instruction.jumpDirection = JumpDirection.Up;
                            if (instruction.Address < instruction.RefAddress)
                            {
                                //
                                // Jump Down
                                //
                                instruction.jumpDirection = JumpDirection.Down;
                            }
                            //
                            // Find the instruction and relative distance
                            //
                            foreach (var jmpTo in method.Instructions)
                            {
                                if (instruction.RefAddress == jmpTo.Address)
                                {
                                    //Found.
                                    instruction.JumpIndex = jmpTo.OrdinalIndex;
                                    instruction.JumpSize = Math.Abs(instruction.OrdinalIndex - jmpTo.OrdinalIndex);

                                    //
                                    // Can only jump to one target
                                    //
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            //
            // Most outer instruction -> most inner instruction
            //
            int index = 0;
            int maxJmpSize = -1;

            //
            // If a function has too many jumps then give up on generation.
            //
            if (jumps * 2 >= AssemblyInstruction.MaxGuides)
                return (0, 0);

            var orderedInstructions = method.Instructions.OrderByDescending(x => x.JumpSize).ToArray();
            maxJmpSize = orderedInstructions[0].JumpSize;

            foreach (var orderedInstruction in orderedInstructions)
            {
                if (orderedInstruction.jumpDirection == JumpDirection.Out || orderedInstruction.jumpDirection == JumpDirection.None)
                    continue;

                var inst = method.Instructions[orderedInstruction.OrdinalIndex];

                if (inst.jumpDirection == JumpDirection.Down)
                {
                    PopulateGuidesForDownJump(inst, method, jumps, index);
                }
                else if (inst.jumpDirection == JumpDirection.Up)
                {
                    PopulateGuidesForUpJump(inst, method, jumps, index);
                }

                index += 2;
            }

            return (maxJmpSize, index);
        }

        private static void PopulateGuidesForDownJump(AssemblyInstruction inst, DecompiledMethod method, int methodJumpCount, int nestingIndex)
        {
            //
            // What is our maximum nesting level for this jump.
            //
            var level = 2 * methodJumpCount - nestingIndex;
            //
            // Generate starting guides 
            //
            inst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.TopLeft;
            for (int i = 1; i < level - 1; i++)
                inst.GuideBlocks[nestingIndex + i] = ConsoleBorderStyle.TopBottom;

            inst.GuideBlocks[nestingIndex + level - 1] = ConsoleBorderStyle.Bullet;


            for (int i = 1; i < inst.JumpSize; i++)
            {
                var nestedInst = method.Instructions[inst.OrdinalIndex + i];

                //
                // Check prev guide and if the guide is TopBotom char then 
                // we change our guide to a plus.
                //
                if (nestingIndex > 0 && nestedInst.GuideBlocks[nestingIndex - 1] == ConsoleBorderStyle.TopBottom)
                    nestedInst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.SeparatorBoth;
                else
                    nestedInst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.Left;

                //
                // Populate everything down with whitespace.
                //
                for (int l = 1; l < level; l++)
                {
                    if (nestedInst.GuideBlocks[nestingIndex + l] == '\0')
                        nestedInst.GuideBlocks[nestingIndex + l] = ' ';
                }
            }

            //
            // Get last instruction to set the arrow.
            //
            var lastInst = method.Instructions[inst.OrdinalIndex + inst.JumpSize];
            //
            // If guide above me is TopBotom then change it to arrow since, we are ending a jump here;
            // So someone jumps here as well.
            //
            if (nestingIndex > 0 && lastInst.GuideBlocks[nestingIndex - 1] == ConsoleBorderStyle.TopBottom)
                lastInst.GuideBlocks[nestingIndex - 1] = '>';

            //
            // Generate ending guides 
            //
            lastInst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.BottomLeft;
            for (int i = 1; i < level - 1; i++)
                lastInst.GuideBlocks[nestingIndex + i] = ConsoleBorderStyle.TopBottom;

            lastInst.GuideBlocks[nestingIndex + level - 1] = '>';
        }
        private static void PopulateGuidesForUpJump(AssemblyInstruction inst, DecompiledMethod method, int methodJumpCount, int nestingIndex)
        {
            //
            // What is our maximum nesting level for this jump.
            //
            var level = 2 * methodJumpCount - nestingIndex;

            //
            // Generate starting guides 
            //
            inst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.BottomLeft;
            for (int i = 1; i < level - 1; i++)
                inst.GuideBlocks[nestingIndex + i] = ConsoleBorderStyle.TopBottom;

            inst.GuideBlocks[nestingIndex + level - 1] = ConsoleBorderStyle.Bullet;


            for (int i = 1; i < inst.JumpSize; i++)
            {
                var nestedInst = method.Instructions[inst.OrdinalIndex - i];

                //
                // Check prev guide and if the guide is TopBotom char then 
                // we change our guide to a plus.
                //
                if (nestingIndex > 0 && nestedInst.GuideBlocks[nestingIndex - 1] == ConsoleBorderStyle.TopBottom)
                    nestedInst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.SeparatorBoth;
                else
                    nestedInst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.Left;

                //
                // Populate everything down with whitespace.
                //
                for (int l = 1; l < level; l++)
                {
                    if (nestedInst.GuideBlocks[nestingIndex + l] == '\0')
                        nestedInst.GuideBlocks[nestingIndex + l] = ' ';
                }
            }

            //
            // Generate ending guides 
            //
            var lastInst = method.Instructions[inst.OrdinalIndex - inst.JumpSize];
            lastInst.GuideBlocks[nestingIndex] = ConsoleBorderStyle.TopLeft;
            for (int i = 1; i < level - 1; i++)
                lastInst.GuideBlocks[nestingIndex + i] = ConsoleBorderStyle.TopBottom;

            lastInst.GuideBlocks[nestingIndex + level - 1] = '>';
        }
    }
}
