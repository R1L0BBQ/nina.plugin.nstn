# Golden fixtures

`night_model.json` is the frozen output of the **original** TypeScript night
model (`web/src/night/astro.ts`), copied byte for byte from the FITSORA
repository (`tests/golden/night_model.json`, last changed in FITSORA commit
`dcd3fa0`, SHA-256 `0722bede26b5550cc529a251f88c94de8db67e635247d3969e15fa27ddc6c080`).

The direction is fixed: **the fixture is the reference, the port is what gets
corrected.** Do not regenerate or edit this file to make a test pass. If the
numbers disagree, the C# port is wrong until proven otherwise.

The fixture was generated with `astronomy-engine` 2.1.19; `Nstn.Core` pins
`CosineKitty.AstronomyEngine` 2.1.19 for the same reason. A different library
version moves the numbers for reasons that have nothing to do with the port.

The ten sites and dates are invented. No real observing coordinates are stored
here. What each case covers and the quirks frozen on purpose (Kasten & Young
airmass slightly below 1 at the zenith, the 12 nm anomaly, millisecond
truncation of JavaScript `Date` values) are described in FITSORA's
`tests/golden/README.md`; the tolerances in `GoldenTests.cs` are the same as in
FITSORA's `tests/test_night.py`.
