# Vintage Story Entity Movement Notes

This note exists to answer one practical question for TowablesLib:

How do we move an entity in Vintage Story while keeping it physically valid in the world?

## Short Answer

For normal `EntityAgent` movement, Vintage Story does not primarily want us to move entities by repeatedly writing raw position.

The normal authority chain is:

1. choose a target or movement intent
2. write steering and control state
3. let controlled physics convert that to motion
4. let collision, stepping, liquid, and climb logic resolve the final legal position

The engine's preferred model is intent-driven movement, not per-tick teleporting.

## Core Rule

If we want the cart to remain a real world object, we should usually control:

- `Yaw`
- `Forward`
- `WalkVector`
- `FlyVector` when relevant
- path target / waypoint target

and let Vintage Story control:

- `Motion`
- collision resolution
- stepping
- `OnGround`
- `Swimming`
- `FeetInLiquid`
- the final accepted position

## Movement Authority

For `EntityAgent`, the useful authority chain is:

1. `EntityBehaviorTaskAI` owns a `WaypointsTraverser`
2. AI tasks use that traverser
3. the traverser writes steering state like `Forward`, `WalkVector`, and `Yaw`
4. `EntityBehaviorControlledPhysics` reads controls during physics tick
5. physics modules add to `pos.Motion`
6. terrain collision and step logic clamp the move
7. the final resolved position becomes the entity state

Important detail:

On the server, `EntityAgent.Initialize()` sets `servercontrols = controls`, so `ServerControls` and `Controls` are effectively the same control object on the server. That means server-side tow code can drive movement by writing `ServerControls`.

## What `AiTaskGotoEntity` Actually Does

`AiTaskGotoEntity` is useful because it shows how VS follows a moving target without directly writing `ServerPos.X/Y/Z`.

It does this:

1. computes a minimum follow distance from the two entities' selection boxes
2. calls `pathTraverser.NavigateTo_Async(targetEntity.ServerPos.XYZ, moveSpeed, minDistance, ...)`
3. keeps updating the traverser's current target to the target entity's current position
4. stops when close enough, too far away, too much time has passed, or the traverser gets stuck

The key lesson is that the task does not physically move the entity itself.

It delegates movement to `WaypointsTraverser`.

## What `WaypointsTraverser` Actually Does

`WaypointsTraverser` is the bridge between "I want to go there" and "the entity moves normally."

It does three important jobs:

1. pathfinding
   - it finds waypoints from the entity's current block position to the target block position
   - it uses the entity's `CollisionBox` and `StepHeight`

2. steering
   - it turns the entity toward the current target
   - it writes `Forward = true`
   - it writes `WalkVector`
   - it adjusts speed as the entity nears the target

3. stuck detection
   - it watches collision state
   - it watches actual progress over time
   - it stops and fires `OnStuck` if movement is not succeeding

This means `WaypointsTraverser` is not a teleport system.
It is a physics-respecting movement controller.

## Where Motion Actually Comes From

The traverser does not write position. It writes controls.

Those controls become motion in the physics modules:

- `PModuleOnGround` adds `WalkVector` into `pos.Motion` while grounded
- `PModuleInAir` adds `WalkVector` into `pos.Motion` while airborne
- `PModuleInLiquid` uses `FlyVector` while swimming

This is the important middle layer:

`WalkVector` -> `pos.Motion` -> collision resolution -> accepted position

## Where Legal Position Is Produced

`EntityBehaviorControlledPhysics` is where world-valid position is actually produced.

During physics tick it:

1. runs the physics modules
2. applies terrain collision
3. handles climbing
4. handles stepping
5. handles sneak edge prevention
6. clamps blocked axes
7. updates `OnGround`, `Swimming`, `FeetInLiquid`, and related state
8. finalizes the resolved position

This is why a valid VS position is not just XYZ.
It is XYZ plus the motion and collision state the engine accepts after resolution.

## Why Raw `ServerPos` Has Been Unstable

Repeated raw position assignment skips too much of the normal acceptance path.

What it can bypass or desynchronize:

- collision validation
- step-up logic
- motion continuity
- grounded state
- liquid state
- climbing state
- tick-order expectations

That explains the failure mode we saw:

- the cart visually moves
- but then sinks, phases, or loses the feeling of being a proper world object

## Practical Rule For TowablesLib

If the goal is "the cart follows while still respecting the world," then the first-choice approach should be:

- drive it through VS movement controls
- avoid per-tick raw `ServerPos` assignment

If we ever do force repositioning, it should be rare and deliberate, not the main towing loop.

## Simplification Opportunity

Yes, the current mission can probably be simplified a lot by moving away from the custom rope-force loop in `EntityBehaviorTowable` and toward a traverser-driven follow model.

But there is one refinement:

Do not think of the target as the hitch entity's exact `ServerPos`.

Think of the target as:

- a desired follow point behind the hitch entity
- or at least a point offset from the hitch point by the desired tow distance

That is closer to towing than pathing directly into the hitch entity.

## Recommended MVP

The simplest physics-respecting towing MVP is:

1. towable owns a `WaypointsTraverser` or uses one from task AI if present
2. each server tick, compute a desired follow point behind the hitch entity
3. start or refresh traverser movement toward that follow point
4. let traverser write `WalkVector` and `Yaw`
5. let controlled physics do the actual movement

This will not produce trailer physics.

It should produce a much better first win:

- the cart moves through valid terrain movement
- the engine keeps collision and stepping
- most of the current tension and smoothing code can disappear

## Towable Config

The current path-following tow behavior exposes these useful config keys:

- `interactionPoint`
  - selection box attach point used for hitch/unhitch interaction
- `hitchSearchRange`
  - radius used to find a nearby hitchable entity
- `maxTowDistance`
  - maximum allowed distance before the hitch is cleared
- `followDistance`
  - trailing distance behind the hitch entity
- `followMoveSpeed`
  - movement speed passed into the traverser
- `repathDistanceThreshold`
  - how far the requested follow target must move before a repath is requested
- `repathIntervalMs`
  - minimum time between repaths
- `pathSearchDepth`
  - search depth used for the async pathfinder
- `pathDistanceTolerance`
  - pathfinder Manhattan-distance tolerance
- `arriveDistance`
  - optional explicit target distance; if omitted or `0`, the towable uses an automatic size-based distance

Compatibility aliases still accepted:

- `targetTowDistance` -> `followDistance`
- `moveSpeed` -> `followMoveSpeed`
- `targetDistance` -> `arriveDistance`

## Caveats About `NavigateTo_Async()`

Using `NavigateTo_Async(targetEntity.ServerPos.XYZ, ...)` directly is promising, but not enough on its own.

Reasons:

1. exact target position
   - towing wants a trailing point, not the hitch entity's exact location

2. moving target updates
   - a moving hitch target may require updating the final target and sometimes repathing

3. entity requirements
   - this approach needs the towable to actually behave as an `EntityAgent`
   - it also needs controlled physics and a traverser path

4. behavior availability
   - not every towable may already have `EntityBehaviorTaskAI`
   - in that case, the tow behavior can own its own `WaypointsTraverser`

5. obstacles and tight turns
   - simply mutating the current target may not be enough when the hitch entity moves around obstacles
   - repathing may still be needed when the desired follow point changes significantly

## Best Current Hypothesis

For TowablesLib, the best near-term direction is probably:

1. stop trying to invent towing from raw position writes
2. stop tuning custom joint forces as the primary movement system
3. treat towing as "follow a moving trailing target with standard VS movement"
4. only return to spring or rope shaping after stable movement exists

## Suggested Next Experiment

Implement a narrow prototype with these rules:

- only server side
- only for towables that are `EntityAgent`
- create or reuse a `WaypointsTraverser`
- compute a follow target behind the hitch entity
- navigate toward that target
- stop traverser on unhitch

Success criteria:

- cart stays above ground
- cart collides and steps normally
- cart follows without sinking or phasing
- no direct per-tick `ServerPos` writes are needed for towing

If this prototype works, then towing becomes a control problem instead of a broken placement problem.
