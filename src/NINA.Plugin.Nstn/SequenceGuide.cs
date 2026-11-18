namespace NINA.Plugin.Nstn {

    /// <summary>
    /// How an NSTN sequence is built, shown under "NSTN Night Info" and in the
    /// plugin description. Constants, so the assembly attribute can use them too.
    /// </summary>
    public static class SequenceGuide {

        public const string Tree =
            "Target container (Deep Sky Object)        triggers: Meridian Flip, Autofocus, Dither\n" +
            "├─ NSTN Night Info                        (shows the night, takes no time)\n" +
            "├─ Slew and center, Start guiding\n" +
            "└─ Instruction set \"Loop\"                 condition: Loop Until Time (morning twilight)\n" +
            "   ├─ Wait for NSTN window                (first in the loop)\n" +
            "   ├─ Block \"O III\"                       conditions: Loop while NSTN window open (O III)\n" +
            "   │  │                                               + Loop For Iterations 1\n" +
            "   │  └─ Smart Exposure  OIII  5 × 300 s\n" +
            "   ├─ Block \"H-alpha\"                     same, H-alpha\n" +
            "   └─ Block \"L/RGB\"                       same, L/RGB";

        public const string Rules =
            "1. Conditions on an instruction set are loop conditions. A set with only the NSTN condition repeats as long as its window is open; a set with no condition at all runs exactly once.\n" +
            "2. One block per filter: an instruction set holding the filter switch and the exposures (Smart Exposure does both), with 'Loop while NSTN window open' for that band. \"From this block's filter\" reads the band from the block's filter switch.\n" +
            "3. Add 'Loop For Iterations' to every block for a round robin: each open block takes its turn once per pass, the count sets the weight. Without it the first open block keeps its window to itself.\n" +
            "4. The loop around the blocks needs its own end, such as 'Loop Until Time' at morning twilight; without one the target is done after a single pass.\n" +
            "5. 'Wait for NSTN window' goes first in that loop. When every window is closed, N.I.N.A. 3.2 would otherwise race through the loop without a pause.\n" +
            "6. Triggers on the target container (or the whole sequence) fire between exposures: Meridian Flip, Autofocus after a filter change, Dither. Slewing, centring and guiding belong before the loop, not inside it.\n" +
            "7. A window is checked before every exposure. A running exposure is always finished, never aborted.";
    }
}
