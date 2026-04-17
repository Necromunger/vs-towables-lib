# TowablesLib

Small Vintage Story library for hitchable pullers and towable carts.

Towables use normal `EntityAgent` pathing. They behave like cautious followers, not rigid trailers.

## hitchable

Put this on the pulling entity.

```json
behaviorConfigs: {
  hitchable: {
    hitchOffset: { x: 0, y: 0, z: -1.2 }
  }
}
```

- `hitchOffset`: local pull point on the hitchable

## towable

Put this on the entity being pulled. It must be an `EntityAgent`.

```json
behaviorConfigs: {
  towable: {
    interactionPoint: "TowAP",
    hitchSearchRange: 4,
    maxTowDistance: 20,
    followDistance: 2,
    followMoveSpeed: 0.03,
    repathDistanceThreshold: 1.5,
    repathIntervalMs: 750,
    pathSearchDepth: 1000,
    pathDistanceTolerance: 1,
    arriveDistance: 0
  },
  selectionboxes: {
    selectionBoxes: ["TowAP"]
  }
}
```

- `interactionPoint`: selectable attachment point used to hitch and unhitch
- `hitchSearchRange`: search radius for a nearby `hitchable`
- `maxTowDistance`: clears the hitch if exceeded
- `followDistance`: desired trailing distance behind the hitchable
- `followMoveSpeed`: base pathing speed
- `repathDistanceThreshold`: target movement needed before repathing
- `repathIntervalMs`: minimum time between repaths
- `pathSearchDepth`: pathfinding search budget
- `pathDistanceTolerance`: pathfinding tolerance
- `arriveDistance`: stop distance; `0` uses an automatic size-based value

## Quick Tuning

- Too slow: raise `followMoveSpeed`
- Too lazy: lower `repathIntervalMs` and `repathDistanceThreshold`
- Too close or too far back: change `followDistance`

Good first tweaks:

- `followMoveSpeed`: `0.03 -> 0.05`
- `repathIntervalMs`: `750 -> 250`
- `repathDistanceThreshold`: `1.5 -> 0.5`

## Shape / Behaviors

`interactionPoint` must match a selectable attachment point code on the towable shape.

Towable:

```json
client: { behaviors: [{ code: "selectionboxes" }, { code: "towable" }] },
server: { behaviors: [{ code: "selectionboxes" }, { code: "towable" }] }
```

Hitchable:

```json
client: { behaviors: [{ code: "hitchable" }] },
server: { behaviors: [{ code: "hitchable" }] }
```

Includes compatibility patches for Cartwright's Caravan.
