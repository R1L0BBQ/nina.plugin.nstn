using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// The plugin's identity in NINA. The GUID keys the per-profile plugin settings:
// never change it, or every profile loses its NSTN options.
[assembly: Guid("da1b84a3-4397-4f30-8eb9-5ed431d4ff06")]

[assembly: AssemblyTitle("NSTN – Narrowband Saves The Night")]
[assembly: AssemblyDescription("A filter plan that exposes each filter - or an L/RGB set in a ratio - while the NSTN sky model rates its band as worth exposing, round after round until the night is over; the sequencer condition behind it, and a night overview with a check of the sequence structure.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Marc Fischer")]
[assembly: AssemblyProduct("NSTN for N.I.N.A.")]
[assembly: AssemblyCopyright("Copyright © 2026 Marc Fischer")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]

// The check program sets NstnClock and reads the plan's failure count.
[assembly: InternalsVisibleTo("NINA.Plugin.Nstn.Checks")]

[assembly: AssemblyVersion("0.11.1.0")]
[assembly: AssemblyFileVersion("0.11.1.0")]

// Built against NINA.Plugin 3.2.0.9001 (.NET 8).
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.9001")]

[assembly: AssemblyMetadata("Tags", "Sequencer,Conditions,Filter plan,Narrowband,LRGB,Moon,Twilight,Sky brightness")]

[assembly: AssemblyMetadata("LongDescription", @"NSTN models the sky background towards your target over the whole night: light pollution of the site (from its SQM value), twilight, and moonlight after Krisciunas & Schaefer, weighted per emission line and bandwidth. A band is worth exposing while its in-band background stays darker than a calibrated threshold.

**NSTN filter plan** is the easy way: one instruction with a table.

- One row per filter: its name as in the profile, exposures per round, exposure time.
- The row **L/RGB** combines L, R, G and B in a ratio such as 3:1:1:1 (0 leaves a filter out), with one exposure time for L and one for R, G and B. NSTN finds the four filters by their names (L, Lum, R, Red ...).
- For the whole plan: gain, offset, binning, and sets in a row - how often a row runs before the next filter comes.
- **Own settings** per row. Window: priority, a stricter or looser threshold, a minimum altitude, a time span. Camera: gain, offset and binning of its own (an L/RGB row has one set for L and one for RGB). Empty fields take the plan's values.
- **Priority:** as soon as the row's window opens, it takes over after the running exposure, also in the middle of a round, and nothing else runs while it is open. Of two open rows with priority the upper one runs. Give it to a row with a short window, such as L/RGB in the moonless hours.

It waits for the first window, exposes each row while its window is open, round after round, and ends by itself when no window opens again this night - no clock time. A plan still running at sunrise ends there instead of waiting for the evening. It shows the night by both dates (after sunrise the coming one), each row's window and a night plan with the expected integration per filter, and it stops after a number of failed exposures in a row instead of changing filters all night without an image.

**Loop while NSTN window open** is the sequencer condition behind it, for building the pattern by hand. Put it on an instruction set inside a target container, choose the band (L/RGB, H-alpha, O III, S II, H-beta) and its bandwidth, and the set runs only while that band's window is open. The condition shows tonight's window for the target directly in the sequencer.

The condition is checked between instructions: a running exposure is always finished, never aborted. Triggers such as autofocus, dithering and meridian flip keep working because the exposures are N.I.N.A.'s own instructions.

Set the site's sky quality (SQM, mag/arcsec²) in the plugin options first. The thresholds are calibrated defaults (broadband 20.6, narrowband 23.1); the options warn about values far below them.

The plugin speaks N.I.N.A.'s language (Options > General > Language): English and German so far, other languages fall back to English. N.I.N.A.'s own instruction names in the texts come from N.I.N.A. itself.

**NSTN Night Info** shows the night for its target container: twilight, moon phase, rise and set, the altitude of target and moon, their distance, the sky towards the target and tonight's windows, with values per time under the mouse. It also checks how the target's sequence is built and explains what to change. It takes no time when the sequence runs.

### How to build an NSTN sequence

" + NINA.Plugin.Nstn.SequenceGuide.Easy + @"

```
" + NINA.Plugin.Nstn.SequenceGuide.Tree + @"
```

" + NINA.Plugin.Nstn.SequenceGuide.Rules)]
