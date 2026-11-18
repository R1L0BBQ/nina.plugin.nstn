namespace NINA.Plugin.Nstn {

    /// <summary>
    /// The guide "How to build an NSTN sequence" in English, for the plugin
    /// description in N.I.N.A.'s plugin list - an assembly attribute needs constants.
    /// In the sequencer the guide comes from the string tables (Guide_Tree,
    /// Guide_Rules) in the user's language. Generated from Strings/en.json;
    /// TextTests fails when the two drift apart.
    /// </summary>
    public static class SequenceGuide {

        public const string Tree = @"Deep Sky Object Instruction Set   triggers: Meridian Flip, AF After Filter Change, Dither after Exposures
├─ NSTN Night Info   shows the night, takes no time
├─ Slew and center, Start Guiding
└─ Sequential Instruction Set ""Loop""   condition: Loop Until Time (morning twilight)
   ├─ Wait for NSTN window   first in the loop
   ├─ Block ""O III""   conditions: Loop while NSTN window open (O III) + Loop For Iterations 1
   │  └─ Smart Exposure   OIII 5 × 300 s
   ├─ Block ""H-alpha""   the same with H-alpha
   └─ Block ""L/RGB""   the same with L/RGB";

        public const string Rules = @"1. Conditions on an instruction set are loop conditions. A set with only the NSTN condition repeats as long as its window is open; a set with no condition at all runs exactly once.
2. One block per filter: an instruction set holding the filter switch and the exposures (Smart Exposure does both), with 'Loop while NSTN window open' for that band. 'From this block's filter' reads the band from the block's filter switch.
3. Add 'Loop For Iterations' to every block for a round robin: each open block takes its turn once per pass, the count sets the weight. Without it the first open block keeps its window to itself.
4. The loop around the blocks needs its own end, such as 'Loop Until Time' at morning twilight; without one the target is done after a single pass.
5. 'Wait for NSTN window' goes first in that loop. When every window is closed, N.I.N.A. 3.2 would otherwise race through the loop without a pause.
6. Triggers on the target container (or the whole sequence) fire between exposures: Meridian Flip, AF After Filter Change, Dither after Exposures. Slewing, centring and guiding belong before the loop, not inside it.
7. A window is checked before every exposure. A running exposure is always finished, never aborted.";
    }
}
