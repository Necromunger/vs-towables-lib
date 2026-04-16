# TowablesLib

Small Vintage Story library for entities that can hitch to, and tow, other entities.

## Entity Behavior Configs

### hitchable

Belongs on the entity that can pull something.

```json
behaviorConfigs: {
  hitchable: {
    hitchPoint: "HitchAP",
    distance: 2.5,
    minDistance: 1.8,
    maxDistance: 3.2
  }
}
```

`distance` is the preferred maintained distance from the hitchable's `hitchPoint` to the towable's `towPoint`.

`minDistance` is the closest the towable should get before it is pushed back or prevented from collapsing into the hitchable.

`maxDistance` is the farthest the towable should get before it is pulled along by the hitchable.

### towable

Belongs on the entity that can be pulled.

```json
behaviorConfigs: {
  towable: {
    interactionPoint: "TowAP",
    towPoint: "TowAP",
    searchRange: 4
  },
  selectionboxes: {
    selectionBoxes: ["TowAP"]
  }
}
```

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

The hitchable owns the distance band because the pulling entity knows its own body size and working clearance. A goat, elk, polar bear, wagon, or tractor can each define different `minDistance`, `distance`, and `maxDistance` values for the same towable.
