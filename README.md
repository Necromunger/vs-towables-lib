# TowablesLib

Small Vintage Story library for entities that can hitch to, and tow, other entities.

## Entity Behavior Configs

### hitchable

Belongs on the entity that can pull something.

```json
behaviorConfigs: {
  hitchable: {
    hitchPoint: "HitchAP",
    hitchOffset: { x: 0, y: 0, z: -1.2 }
  }
}
```

`hitchOffset` is optional. It is a local position offset from the hitchable entity origin and turns with the hitchable. Use it to place the effective pull point behind, beside, or in front of the pulling entity without depending on server-side selection box position helpers.

### towable

Belongs on the entity that can be pulled. The entity must be an `EntityAgent`; if not, the behavior logs an error and disables itself.

```json
behaviorConfigs: {
  towable: {
    interactionPoint: "TowAP",
    towPoint: "TowAP",
    hitchSearchRange: 4,
    pullStrength: 30,
    compressionStrength: 30,
    maxTowDistance: 20,
    targetTowDistance: 1,
    towDistanceDeadZone: 0.1,
    tensionCurve: 0.3
  },
  selectionboxes: {
    selectionBoxes: ["TowAP"]
  }
}
```

All numeric towable settings are optional:

- `hitchSearchRange`: how far the towable scans for a nearby `hitchable` when interacted with.
- `pullStrength`: how strongly tension moves the towable toward the hitchable's effective pull point.
- `compressionStrength`: how strongly compression moves the towable away when it is too close to the hitchable's effective pull point.
- `maxTowDistance`: if the towable gets this far from the hitchable, the hitch is cleared.
- `targetTowDistance`: desired distance between the towable's `towPoint` and the hitchable's effective pull point.
- `towDistanceDeadZone`: range around `targetTowDistance` where the towable stops moving instead of chasing tiny offsets.
- `tensionCurve`: shape of the tension ramp. Values below `1` ramp faster; values above `1` ramp slower.

## Shape Attachment Points

`hitchPoint`, `interactionPoint`, and `towPoint` are attachment point codes from the entity shape file. The towable behavior uses `interactionPoint` for player interaction and `towPoint` for its pull anchor; the hitchable behavior uses `hitchOffset` as the current server-side effective pull point.

Vintage Story shape elements can contain an `attachmentpoints` array. The shape element owns the 3D volume, and the attachment point gives that volume a named anchor.

```json
{
  "name": "TowCoupler",
  "from": [0.0, 0.0, 0.0],
  "to": [2.0, 2.0, 2.0],
  "faces": {},
  "attachmentpoints": [
    {
      "code": "TowAP",
      "posX": "0.0",
      "posY": "0.0",
      "posZ": "0.0",
      "rotationX": "0.0",
      "rotationY": "0.0",
      "rotationZ": "0.0"
    }
  ]
}
```

`selectionboxes.selectionBoxes` references those same attachment point codes. For example, `selectionBoxes: ["TowAP"]` makes the `TowAP` shape element selectable through Vintage Story's `selectionboxes` behavior.

## Behavior Lists

Add the behavior on both client and server when the entity needs click interaction and synchronized towing behavior.

```json
client: {
  behaviors: [
    { code: "selectionboxes" },
    { code: "towable" }
  ]
},
server: {
  behaviors: [
    { code: "selectionboxes" },
    { code: "towable" }
  ]
}
```

For a pulling entity:

```json
client: {
  behaviors: [
    { code: "hitchable" }
  ]
},
server: {
  behaviors: [
    { code: "hitchable" }
  ]
}
```

## Intended Flow

The player interacts with the towable's `interactionPoint`. The towable scans nearby entities within `hitchSearchRange` for a valid `hitchable`, stores that entity as its hitch target, then applies tension from its own `towPoint` toward the hitchable entity's effective pull point.

The hitchable owns the `hitchOffset` because the pulling entity knows its own body size and working clearance. A goat, elk, polar bear, wagon, or tractor can each define a different effective pull point for the same towable.

## Compatibility Targets

TowablesLib contains compatibility patches for the following mods:

| Mod | Mod ID | Version | Status |
| --- | --- | --- | --- |
| Cartwrights Caravan | `cartwrightscaravan` | `1.8.0` | Planned |
