"""Write samples/M31-NSTN-Test.json: a target container in N.I.N.A. 3.2 sequence format.

The round-robin pattern from the README, small enough for a simulator test:
an outer loop (3 passes), first "Wait for NSTN window", then one block per band,
each with "Loop while NSTN window open" plus a two-pass limit and one 2 s light.

N.I.N.A. loads a lone target container via "Load sequence" and wraps it in a new
sequence itself (Sequence2VM.LoadSequenceFromFile). Only the $type of instructions,
containers, conditions and triggers is evaluated on load; the collection $type
strings are kept as N.I.N.A. writes them, for readability.

Every instruction, condition and container needs "Parent": {"$ref": <parent $id>}.
N.I.N.A. fills the collections directly on load and takes the parent from that
reference, not from Add(); without it a condition has no parent and cannot find
its target container.
"""

import json
from itertools import count
from pathlib import Path

SEQ = "NINA.Sequencer"
_ids = count(1)


def _id():
    return str(next(_ids))


def collection(kind, values):
    return {
        "$id": _id(),
        "$type": f"System.Collections.ObjectModel.ObservableCollection`1[[{kind}, {SEQ}]], System.ObjectModel",
        "$values": values,
    }


def container(type_name, name, items, conditions=(), extra=None):
    node = {"$id": _id(), "$type": type_name}
    if extra:
        node.update(extra)
    node.update({
        "Strategy": {"$type": f"{SEQ}.Container.ExecutionStrategy.SequentialStrategy, {SEQ}"},
        "Name": name,
        "Conditions": collection(f"{SEQ}.Conditions.ISequenceCondition", list(conditions)),
        "IsExpanded": True,
        "Items": collection(f"{SEQ}.SequenceItem.ISequenceItem", list(items)),
        "Triggers": collection(f"{SEQ}.Trigger.ISequenceTrigger", []),
        "Parent": None,
        "ErrorBehavior": 0,
        "Attempts": 1,
    })
    return node


def loop(iterations):
    return {"$id": _id(), "$type": f"{SEQ}.Conditions.LoopCondition, {SEQ}",
            "CompletedIterations": 0, "Iterations": iterations}


def nstn_window(line, bandwidth_nm):
    return {"$id": _id(), "$type": "NINA.Plugin.Nstn.Conditions.NstnWindowCondition, NINA.Plugin.Nstn",
            "Line": line, "BandwidthNm": bandwidth_nm}


def nstn_preset(preset, line, bandwidth_nm, preset_first=False):
    """A condition taken from the filter catalog. preset_first writes FilterPreset
    before Line, the reverse of what N.I.N.A. writes, to show the order does not matter."""
    head = {"$id": _id(), "$type": "NINA.Plugin.Nstn.Conditions.NstnWindowCondition, NINA.Plugin.Nstn"}
    if preset_first:
        return {**head, "FilterPreset": preset, "Line": line, "BandwidthNm": bandwidth_nm}
    return {**head, "Line": line, "BandwidthNm": bandwidth_nm, "FilterPreset": preset}


def nstn_wait():
    return {"$id": _id(), "$type": "NINA.Plugin.Nstn.Instructions.NstnWaitForWindow, NINA.Plugin.Nstn",
            "ErrorBehavior": 0, "Attempts": 1}


def light(seconds):
    return {"$id": _id(), "$type": f"{SEQ}.SequenceItem.Imaging.TakeExposure, {SEQ}",
            "ExposureTime": seconds, "Gain": -1, "Offset": -1,
            "Binning": {"$id": _id(), "$type": "NINA.Core.Model.Equipment.BinningMode, NINA.Core", "X": 1, "Y": 1},
            "ImageType": "LIGHT", "ExposureCount": 0, "ErrorBehavior": 0, "Attempts": 1}


def band_block(label, line, bandwidth_nm):
    return container(f"{SEQ}.Container.SequentialContainer, {SEQ}", label,
                     items=[light(2.0)], conditions=[nstn_window(line, bandwidth_nm), loop(2)])


def series(count, seconds):
    """A nested set "count x seconds", like a Smart Exposure."""
    return container(f"{SEQ}.Container.SequentialContainer, {SEQ}", f"{count} x {seconds:g} s",
                     items=[light(seconds)], conditions=[loop(count)])


def plan_block(label, line, bandwidth_nm, count, seconds, passes):
    conditions = [nstn_window(line, bandwidth_nm)] + ([loop(passes)] if passes else [])
    return container(f"{SEQ}.Container.SequentialContainer, {SEQ}", label,
                     items=[series(count, seconds)], conditions=conditions)


def link_parents(node):
    """Point every child of a container at it, recursively, as N.I.N.A. saves it."""
    for key in ("Items", "Conditions", "Triggers"):
        for child in node.get(key, {}).get("$values", []):
            child["Parent"] = {"$ref": node["$id"]}
            link_parents(child)


def main(variant="full", out_dir=None):
    """variant: "full" (the README pattern), or for the spin test "closed-wait" /
    "closed-nowait": only bands that are closed early in the night, 1000 passes."""
    target = {
        "$id": _id(), "$type": "NINA.Astrometry.InputTarget, NINA.Astrometry",
        "Expanded": True, "TargetName": "M 31", "PositionAngle": 0.0,
        "InputCoordinates": {
            "$id": _id(), "$type": "NINA.Astrometry.InputCoordinates, NINA.Astrometry",
            "RAHours": 0, "RAMinutes": 42, "RASeconds": 44.3,
            "NegativeDec": False, "DecDegrees": 41, "DecMinutes": 16, "DecSeconds": 9.0,
        },
    }
    if variant == "full":
        blocks = [nstn_wait(),
                  band_block("O III 7 nm", "OIII", 7.0),
                  band_block("H-alpha 7 nm", "Ha", 7.0),
                  band_block("L/RGB", "RGB", 89.0)]
        passes, name = 3, "M31-NSTN-Test.json"
    elif variant == "plan":
        # A night-length round robin for the night plan: two blocks with a limit of
        # one pass of 5 x 300 s each, and L/RGB without a limit (priority, flagged).
        blocks = [nstn_wait(),
                  plan_block("O III 7 nm", "OIII", 7.0, 5, 300, 1),
                  plan_block("H-alpha 7 nm", "Ha", 7.0, 5, 300, 1),
                  plan_block("L/RGB", "RGB", 89.0, 1, 120, None)]
        passes, name = 1000, "M31-NSTN-Plan.json"
    elif variant == "duo":
        # One duo filter (Askar D1, OSC camera), judged on each of its lines: the H-alpha
        # block collects while only H-alpha is worth it, the O III block while both are.
        d1 = "Askar D1 - H-Alpha & O-III"
        blocks = [nstn_wait(),
                  container(f"{SEQ}.Container.SequentialContainer, {SEQ}", "D1 while O III is open",
                            items=[series(5, 300)], conditions=[nstn_preset(d1, "OIII", 6.0, preset_first=True), loop(1)]),
                  container(f"{SEQ}.Container.SequentialContainer, {SEQ}", "D1 while H-alpha is open",
                            items=[series(5, 300)], conditions=[nstn_preset(d1, "Ha", 6.0), loop(1)])]
        passes, name = 1000, "M31-NSTN-Duo.json"
    else:
        blocks = [band_block("O III 7 nm", "OIII", 7.0), band_block("L/RGB", "RGB", 89.0)]
        if variant == "closed-wait":
            blocks.insert(0, nstn_wait())
        passes, name = 1000, f"M31-NSTN-{variant}.json"
    rotation = container(f"{SEQ}.Container.SequentialContainer, {SEQ}", "Filter rotation",
                         items=blocks, conditions=[loop(passes)])
    dso = container(f"{SEQ}.Container.DeepSkyObjectContainer, {SEQ}", f"M 31 – NSTN {variant}",
                    items=[rotation], extra={"Target": target})
    link_parents(dso)
    out = Path(out_dir or Path(__file__).parent) / name
    out.write_text(json.dumps(dso, indent=2, ensure_ascii=False), encoding="utf-8")
    print(out)


if __name__ == "__main__":
    import sys
    main(*sys.argv[1:])
