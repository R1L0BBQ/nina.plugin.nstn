using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// The plugin's identity in NINA. The GUID keys the per-profile plugin settings:
// never change it, or every profile loses its NSTN options.
[assembly: Guid("da1b84a3-4397-4f30-8eb9-5ed431d4ff06")]

[assembly: AssemblyTitle("NSTN – Narrowband Saves The Night")]
[assembly: AssemblyDescription("Sequencer conditions that run an instruction set only while the NSTN sky model rates a filter band as worth exposing, plus a night overview with a check of the sequence structure.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Marc Fischer")]
[assembly: AssemblyProduct("NSTN for N.I.N.A.")]
[assembly: AssemblyCopyright("Copyright © 2026 Marc Fischer")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]

// The check program sets NstnClock and reads the plan's failure count.
[assembly: InternalsVisibleTo("NINA.Plugin.Nstn.Checks")]

[assembly: AssemblyVersion("0.6.0.0")]
[assembly: AssemblyFileVersion("0.6.0.0")]

// Built against NINA.Plugin 3.2.0.9001 (.NET 8).
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.9001")]

[assembly: AssemblyMetadata("Tags", "Sequencer,Conditions,Narrowband,Moon,Twilight,Sky brightness")]

[assembly: AssemblyMetadata("LongDescription", @"NSTN models the sky background towards your target over the whole night: light pollution of the site (from its SQM value), twilight, and moonlight after Krisciunas & Schaefer, weighted per emission line and bandwidth. A band is worth exposing while its in-band background stays darker than a calibrated threshold.

**Loop while NSTN window open** is a sequencer condition. Put it on an instruction set inside a target container, choose the band (L/RGB, H-alpha, O III, S II, H-beta) and its bandwidth, and the set runs only while that band's window is open. The condition shows tonight's window for the target directly in the sequencer.

The condition is checked between instructions: a running exposure is always finished, never aborted. Triggers such as autofocus, dithering and meridian flip keep working because the exposures are N.I.N.A.'s own instructions.

Set the site's sky quality (SQM, mag/arcsec²) in the plugin options first.

The plugin speaks N.I.N.A.'s language (Options > General > Language): English and German so far, other languages fall back to English. N.I.N.A.'s own instruction names in the texts come from N.I.N.A. itself.

**NSTN Night Info** shows the night for its target container: twilight, moon phase, rise and set, the altitude of target and moon, their distance, the sky towards the target and tonight's windows, with values per time under the mouse. It also checks how the target's sequence is built and explains what to change. It takes no time when the sequence runs.

### How to build an NSTN sequence

```
" + NINA.Plugin.Nstn.SequenceGuide.Tree + @"
```

" + NINA.Plugin.Nstn.SequenceGuide.Rules)]
