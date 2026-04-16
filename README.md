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

Belongs on the entity that can be pulled.

```json
behaviorConfigs: {
  towable: {
    interactionPoint: "TowAP",
    towPoint: "TowAP",
    searchRange: 4,
    latchSpeed: 30,
    maxHitchDistance: 20
  },
  selectionboxes: {
    selectionBoxes: ["TowAP"]
  }
}
```

`latchSpeed` is optional. It controls how quickly the towable snaps its anchor point toward the hitchable's effective pull point. Higher values behave more like a hard latch.

`maxHitchDistance` is optional. If the towable gets this far from the hitchable, the hitch is cleared.

## Shape Attachment Points

`hitchPoint`, `interactionPoint`, and `towPoint` are attachment point codes from the entity shape file.

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

The player interacts with the towable's `interactionPoint`. The towable scans nearby entities for a valid `hitchable`, stores that entity as its hitch target, then keeps its own `towPoint` aligned with the hitchable entity's `hitchPoint`.

The hitchable owns the hitch offset because the pulling entity knows its own body size and working clearance. A goat, elk, polar bear, wagon, or tractor can each define a different effective pull point for the same towable.

## Compatibility Targets

TowablesLib contains compatibility patches for the following mods:

| Mod | Mod ID | Version | Status |
| --- | --- | --- | --- |
| Ancient Tools | `ancienttools` | `1.6.1` | Planned |
| Cartwrights Caravan | `cartwrightscaravan` | `1.8.0` | Planned |
