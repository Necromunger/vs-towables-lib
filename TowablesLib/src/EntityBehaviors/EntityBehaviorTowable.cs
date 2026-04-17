using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TowablesLib.EntityBehaviors;

public class EntityBehaviorTowable : EntityBehavior
{
    /// <summary>
    /// Selection box attachment point the player must interact with to hitch or unhitch this towable.
    /// </summary>
    public string InteractionPoint { get; private set; } = "TowAP";

    /// <summary>
    /// Attachment point on this towable used as the rope/tension anchor.
    /// </summary>
    public string TowPoint { get; private set; } = "TowAP";

    /// <summary>
    /// Optional local-space offset used as the tow anchor instead of resolving a shape attachment point.
    /// </summary>
    public Vec3d TowOffset { get; private set; } = null;

    /// <summary>
    /// If true, use the entity origin as the physics tow anchor. Useful for unstable third-party attachment points.
    /// </summary>
    public bool UseEntityCenterAsTowPoint { get; private set; }

    /// <summary>
    /// Radius searched for nearby hitchable entities when the player interacts with the towable.
    /// </summary>
    public float HitchSearchRange { get; private set; } = 4f;

    /// <summary>
    /// Maximum allowed horizontal distance between tow point and hitch point before the hitch is cleared.
    /// </summary>
    public float MaxTowDistance { get; private set; } = 20f;

    /// <summary>
    /// Entity id of the current hitch target, or 0 when not hitched.
    /// </summary>
    public long HitchEntityId => entity.WatchedAttributes.GetLong(HitchEntityIdAttribute, 0);

    /// <summary>
    /// True when this towable currently has a stored hitch target.
    /// </summary>
    public bool IsHitched => HitchEntityId > 0;

    /// <summary>
    /// Movement strength applied by rope tension. Higher values pull harder.
    /// </summary>
    public float PullStrength { get; private set; } = 7f;

    /// <summary>
    /// Movement strength applied when the towable is too close and should back away from the hitch.
    /// </summary>
    public float CompressionStrength { get; private set; } = 7f;

    /// <summary>
    /// Exponent applied to normalized compression. Values above 1 soften the start of reverse pressure.
    /// </summary>
    public float CompressionCurve { get; private set; } = 1.5f;

    /// <summary>
    /// Desired horizontal distance between the tow point and hitch point.
    /// </summary>
    public float TargetTowDistance { get; private set; } = 1.0f;

    /// <summary>
    /// Distance around the target tow distance where the towable stops to avoid jitter.
    /// </summary>
    public float TowDistanceDeadZone { get; private set; } = 0.1f;

    /// <summary>
    /// Exponent applied to normalized tension. Values below 1 ramp faster; values above 1 ramp slower.
    /// </summary>
    public float TensionCurve { get; private set; } = 0.3f;

    /// <summary>
    /// Forward distance error where pull reaches full strength.
    /// </summary>
    public float PullFullStrengthDistance { get; private set; } = 3f;

    /// <summary>
    /// Maximum horizontal walk vector length sent to Vintage Story movement.
    /// </summary>
    public float MaxJointWalkVector { get; private set; } = 0.08f;

    /// <summary>
    /// How much of the hitch point's actual per-tick movement the towable inherits before pull correction is added.
    /// </summary>
    public float HitchVelocityInheritance { get; private set; } = 1f;

    /// <summary>
    /// Fraction of the new walk vector applied each tick. Lower values smooth pulsing.
    /// </summary>
    public float WalkVectorSmoothing { get; private set; } = 0.35f;

    public EntityBehaviorTowable(Entity entity) : base(entity) { }

    private const string HitchEntityIdAttribute = "towableslib:hitchEntityId";
    private EntityAgent towableAgent = null!;
    private bool disabled;
    private int interactionPointSelectionBoxIndex = -1;
    private int towPointSelectionBoxIndex = -1;
    private bool selectionBoxIndexesReady;
    private bool loggedSelectionBoxPending;
    private Vec3d lastHitchPoint;
    private readonly Vec3d smoothedWalkVector = new Vec3d();

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        towableAgent = entity as EntityAgent;
        if (towableAgent == null)
        {
            disabled = true;
            entity.World.Logger.Error(
                "[TowablesLib] Towable behavior on {0} requires EntityAgent. Behavior disabled.",
                entity.Code
            );
            return;
        }

        InteractionPoint = attributes?["interactionPoint"].AsString(InteractionPoint) ?? InteractionPoint;
        TowPoint = attributes?["towPoint"].AsString(TowPoint) ?? TowPoint;
        TowOffset = ReadVec3d(attributes?["towOffset"]);
        UseEntityCenterAsTowPoint = attributes?["useEntityCenterAsTowPoint"].AsBool(UseEntityCenterAsTowPoint) ?? UseEntityCenterAsTowPoint;
        HitchSearchRange = attributes?["hitchSearchRange"].AsFloat(HitchSearchRange) ?? HitchSearchRange;
        MaxTowDistance = attributes?["maxTowDistance"].AsFloat(MaxTowDistance) ?? MaxTowDistance;
        PullStrength = Math.Max(0f, attributes?["pullStrength"].AsFloat(PullStrength) ?? PullStrength);
        CompressionStrength = Math.Max(0f, attributes?["compressionStrength"].AsFloat(CompressionStrength) ?? CompressionStrength);
        CompressionCurve = Math.Max(0.001f, attributes?["compressionCurve"].AsFloat(CompressionCurve) ?? CompressionCurve);
        TargetTowDistance = Math.Max(0f, attributes?["targetTowDistance"].AsFloat(TargetTowDistance) ?? TargetTowDistance);
        TowDistanceDeadZone = Math.Max(0f, attributes?["towDistanceDeadZone"].AsFloat(TowDistanceDeadZone) ?? TowDistanceDeadZone);
        TensionCurve = Math.Max(0.001f, attributes?["tensionCurve"].AsFloat(TensionCurve) ?? TensionCurve);
        PullFullStrengthDistance = Math.Max(0.001f, attributes?["pullFullStrengthDistance"].AsFloat(PullFullStrengthDistance) ?? PullFullStrengthDistance);
        MaxJointWalkVector = Math.Max(0f, attributes?["maxJointWalkVector"].AsFloat(MaxJointWalkVector) ?? MaxJointWalkVector);
        HitchVelocityInheritance = Math.Max(0f, attributes?["hitchVelocityInheritance"].AsFloat(HitchVelocityInheritance) ?? HitchVelocityInheritance);
        WalkVectorSmoothing = GameMath.Clamp(attributes?["walkVectorSmoothing"].AsFloat(WalkVectorSmoothing) ?? WalkVectorSmoothing, 0f, 1f);
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
    {
        if (disabled)
        {
            base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
            return;
        }

        // fail - not interact mode or not server side
        if (mode != EnumInteractMode.Interact || entity.World.Side != EnumAppSide.Server)
        {
            base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
            return;
        }

        // fail - no entity selected
        int selectionBoxIndex = (byEntity as EntityPlayer)?.EntitySelection?.SelectionBoxIndex ?? -1;
        if (selectionBoxIndex < 0)
        {
            base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
            return;
        }

        // fail - no selection box found for interaction point
        if (!IsInteractionPoint(byEntity))
        {
            entity.World.Logger.Notification(
                "[TowablesLib] OnInteract ignored on {0} selectedBox={1}, expectedSelectedBox={2}",
                entity.Code, selectionBoxIndex, interactionPointSelectionBoxIndex
            );
            base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
            return;
        }

        entity.World.Logger.Notification(
            "[TowablesLib] OnInteract on {0} selectedBox={1}, interactionIndex={2}",
            entity.Code, selectionBoxIndex, interactionPointSelectionBoxIndex
        );

        handled = EnumHandling.Handled;

        if (IsHitched)
        {
            entity.World.Logger.Notification("[TowablesLib] Clearing hitch on {0}", entity.Code);
            ClearHitch();
            return;
        }

        // fail - no hitchable found nearby
        Entity hitchEntity = FindNearestHitchable();
        if (hitchEntity == null)
        {
            entity.World.Logger.Notification("[TowablesLib] No hitchable found near {0}", entity.Code);
            base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
            return;
        }

        entity.World.Logger.Notification("[TowablesLib] Hitching {0} to {1}", entity.Code, hitchEntity.Code);
        SetHitch(hitchEntity.EntityId);
    }

    public override void OnGameTick(float deltaTime)
    {
        base.OnGameTick(deltaTime);

        if (disabled)
        {
            return;
        }

        ResolveSelectionBoxIndexesIfNeeded();

        if (entity.World.Side != EnumAppSide.Server || !IsHitched)
        {
            return;
        }

        Entity hitchEntity = entity.World.GetEntityById(HitchEntityId);
        EntityBehaviorHitchable hitchable = hitchEntity?.GetBehavior<EntityBehaviorHitchable>();
        if (hitchEntity == null || hitchable == null)
        {
            ClearHitch();
            return;
        }

        ApplyJointConstraint(hitchEntity, hitchable, deltaTime);
    }

    public override string PropertyName() => "towable";

    private bool IsInteractionPoint(EntityAgent byEntity)
    {
        int selectionBoxIndex = (byEntity as EntityPlayer)?.EntitySelection?.SelectionBoxIndex ?? -1;
        if (selectionBoxIndex <= 0)
        {
            return false;
        }

        return selectionBoxIndex - 1 == interactionPointSelectionBoxIndex;
    }

    private Entity FindNearestHitchable()
    {
        return entity.World.GetNearestEntity(
            entity.ServerPos.XYZ,
            HitchSearchRange,
            HitchSearchRange,
            candidate => candidate != entity && candidate.GetBehavior<EntityBehaviorHitchable>() != null
        );
    }

    private void SetHitch(long entityId)
    {
        lastHitchPoint = null;
        entity.WatchedAttributes.SetLong(HitchEntityIdAttribute, entityId);
        entity.WatchedAttributes.MarkPathDirty(HitchEntityIdAttribute);
        MarkChunkModified();
    }

    private void ClearHitch()
    {
        StopTowableMovement();
        entity.WatchedAttributes.RemoveAttribute(HitchEntityIdAttribute);
        entity.WatchedAttributes.MarkPathDirty(HitchEntityIdAttribute);
        MarkChunkModified();
        lastHitchPoint = null;
    }

    private void ApplyJointConstraint(Entity hitchEntity, EntityBehaviorHitchable hitchable, float deltaTime)
    {
        Vec3d towPoint = GetTowPointPosition();
        Vec3d hitchPoint = GetHitchPointPosition(hitchEntity, hitchable);
        Vec3d correction = hitchPoint.SubCopy(towPoint);
        Vec3d inheritedHitchMovement = GetInheritedHitchMovement(hitchPoint);

        double towDistance = Math.Sqrt(correction.X * correction.X + correction.Z * correction.Z);
        if (towDistance > MaxTowDistance)
        {
            entity.World.Logger.Notification(
                "[TowablesLib] Clearing hitch on {0}; distance {1:0.##} exceeded maxTowDistance {2:0.##}",
                entity.Code, towDistance, MaxTowDistance
            );
            ClearHitch();
            return;
        }

        double distanceError = towDistance - TargetTowDistance;
        double activeError = Math.Abs(distanceError) - TowDistanceDeadZone;
        Vec3d directionToHitch = towDistance > 0.001
            ? new Vec3d(correction.X / towDistance, 0, correction.Z / towDistance)
            : new Vec3d();
        Vec3d inheritedPullMovement = GetInheritedPullMovement(inheritedHitchMovement, directionToHitch);

        if (activeError <= 0 || towDistance <= 0.001)
        {
            if (distanceError >= 0 && inheritedPullMovement.LengthSq() > 0.000001)
            {
                SetTowableWalkVector(inheritedPullMovement);
                entity.ServerPos.Yaw = (float)Math.Atan2(hitchPoint.X - towPoint.X, hitchPoint.Z - towPoint.Z);
            }
            else
            {
                SetTowableWalkVector(new Vec3d());
            }
            return;
        }

        bool isCompressed = distanceError < 0;
        double responseRange = isCompressed
            ? Math.Max(TargetTowDistance - TowDistanceDeadZone, 0.001)
            : PullFullStrengthDistance;
        double normalizedTension = Math.Min(activeError / responseRange, 1.0);
        double response = Math.Pow(normalizedTension, isCompressed ? CompressionCurve : TensionCurve);
        double strength = isCompressed ? CompressionStrength : PullStrength;
        double moveSpeed = response * strength * deltaTime;
        moveSpeed = Math.Min(moveSpeed, MaxJointWalkVector);

        Vec3d moveDirection = isCompressed ? GetCompressedTowableAxisDirection(directionToHitch) : directionToHitch;
        Vec3d correctionMove = moveDirection.Clone().Mul(moveSpeed);
        Vec3d walkVector = isCompressed
            ? correctionMove
            : new Vec3d(
                inheritedPullMovement.X + correctionMove.X,
                0,
                inheritedPullMovement.Z + correctionMove.Z
            );

        SetTowableWalkVector(walkVector);

        entity.ServerPos.Yaw = (float)Math.Atan2(hitchPoint.X - towPoint.X, hitchPoint.Z - towPoint.Z);
    }

    private Vec3d GetInheritedHitchMovement(Vec3d hitchPoint)
    {
        if (lastHitchPoint == null)
        {
            lastHitchPoint = hitchPoint.Clone();
            return new Vec3d();
        }

        Vec3d movement = hitchPoint.SubCopy(lastHitchPoint);
        lastHitchPoint = hitchPoint.Clone();
        movement.Y = 0;
        return movement.Mul(HitchVelocityInheritance);
    }

    private static Vec3d GetInheritedPullMovement(Vec3d inheritedHitchMovement, Vec3d directionToHitch)
    {
        if (inheritedHitchMovement.LengthSq() <= 0.000001 || directionToHitch.LengthSq() <= 0.000001)
        {
            return new Vec3d();
        }

        double movementAwayFromTowable = inheritedHitchMovement.Dot(directionToHitch);
        if (movementAwayFromTowable <= 0)
        {
            return new Vec3d();
        }

        return directionToHitch.Clone().Mul(movementAwayFromTowable);
    }

    private Vec3d GetCompressedTowableAxisDirection(Vec3d directionToHitch)
    {
        Vec3d towableForward = GetTowableForward();
        double forwardPressure = directionToHitch.Dot(towableForward);
        double directionMultiplier = forwardPressure >= 0 ? -1 : 1;
        return towableForward.Mul(directionMultiplier);
    }

    private Vec3d GetTowableForward()
    {
        return new Vec3d(Math.Sin(entity.ServerPos.Yaw), 0, Math.Cos(entity.ServerPos.Yaw));
    }

    private void StopTowableMovement()
    {
        towableAgent.ServerControls.StopAllMovement();
        towableAgent.ServerControls.WalkVector.Set(0, 0, 0);
        smoothedWalkVector.Set(0, 0, 0);
    }

    private void SetTowableWalkVector(Vec3d targetWalkVector)
    {
        if (WalkVectorSmoothing >= 1)
        {
            smoothedWalkVector.Set(targetWalkVector.X, 0, targetWalkVector.Z);
        }
        else
        {
            smoothedWalkVector.X += (targetWalkVector.X - smoothedWalkVector.X) * WalkVectorSmoothing;
            smoothedWalkVector.Y = 0;
            smoothedWalkVector.Z += (targetWalkVector.Z - smoothedWalkVector.Z) * WalkVectorSmoothing;
        }

        if (smoothedWalkVector.LengthSq() <= 0.000001)
        {
            towableAgent.ServerControls.WalkVector.Set(0, 0, 0);
            return;
        }

        towableAgent.ServerControls.WalkVector.Set(smoothedWalkVector.X, 0, smoothedWalkVector.Z);
    }

    private Vec3d GetHitchPointPosition(Entity hitchEntity, EntityBehaviorHitchable hitchable)
    {
        return GetOffsetPosition(hitchEntity, hitchable.HitchOffset);
    }

    private static Vec3d GetOffsetPosition(Entity pointEntity, Vec3d localOffset)
    {
        return pointEntity.ServerPos.XYZ.AddCopy(localOffset.RotatedCopy(pointEntity.ServerPos.Yaw));
    }

    private static Vec3d ReadVec3d(JsonObject value)
    {
        if (value?.Exists != true)
        {
            return null;
        }

        return new Vec3d(
            value["x"].AsDouble(0),
            value["y"].AsDouble(0),
            value["z"].AsDouble(0)
        );
    }

    private static int FindSelectionBoxIndex(Entity pointEntity, string pointCode)
    {
        var selectionBoxes = pointEntity.GetBehavior<EntityBehaviorSelectionBoxes>()?.selectionBoxes;
        if (selectionBoxes != null)
        {
            for (int i = 0; i < selectionBoxes.Length; i++)
            {
                if (selectionBoxes[i].AttachPoint?.Code == pointCode)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private void MarkChunkModified()
    {
        if (entity.World.Side == EnumAppSide.Server)
        {
            entity.World.BlockAccessor.GetChunkAtBlockPos(entity.Pos.AsBlockPos)?.MarkModified();
        }
    }

    private void RefreshSelectionBoxesIfNeeded()
    {
        var selectionBoxesBehavior = entity.GetBehavior<EntityBehaviorSelectionBoxes>();
        if (selectionBoxesBehavior == null || (selectionBoxesBehavior.selectionBoxes?.Length ?? 0) > 0)
        {
            return;
        }

        selectionBoxesBehavior.UpdateColSelBoxes();
    }

    private void ResolveSelectionBoxIndexesIfNeeded()
    {
        if (selectionBoxIndexesReady) return;

        // Selectionboxes can be generated after entity behavior initialization,
        // so resolve their indexes lazily once the selectionboxes behavior has populated them.
        RefreshSelectionBoxesIfNeeded();
        interactionPointSelectionBoxIndex = FindSelectionBoxIndex(entity, InteractionPoint);
        towPointSelectionBoxIndex = FindSelectionBoxIndex(entity, TowPoint);
        selectionBoxIndexesReady = interactionPointSelectionBoxIndex >= 0 && towPointSelectionBoxIndex >= 0;

        if (selectionBoxIndexesReady)
        {
            LogSelectionBoxState("selection boxes ready");
            return;
        }

        if (!loggedSelectionBoxPending)
        {
            loggedSelectionBoxPending = true;
            LogSelectionBoxState("selection boxes pending");
        }
    }

    private void LogSelectionBoxState(string phase)
    {
        var selectionBoxesBehavior = entity.GetBehavior<EntityBehaviorSelectionBoxes>();
        var selectionBoxes = selectionBoxesBehavior?.selectionBoxes;

        entity.World.Logger.Notification(
            "[TowablesLib] Towable {0} on {1}. selectionboxes={2}, count={3}, interactionPoint={4}, interactionIndex={5}, towPoint={6}, towIndex={7}",
            phase,
            entity.Code,
            selectionBoxesBehavior != null,
            selectionBoxes?.Length ?? 0,
            InteractionPoint,
            interactionPointSelectionBoxIndex,
            TowPoint,
            towPointSelectionBoxIndex
        );

        if (selectionBoxes == null)
        {
            return;
        }

        for (int i = 0; i < selectionBoxes.Length; i++)
        {
            entity.World.Logger.Notification(
                "[TowablesLib] Towable selection box {0} on {1}: {2}",
                i,
                entity.Code,
                selectionBoxes[i].AttachPoint?.Code ?? "<null>"
            );
        }
    }

    private Vec3d GetTowPointPosition()
    {
        if (UseEntityCenterAsTowPoint)
        {
            return entity.ServerPos.XYZ;
        }

        if (TowOffset != null)
        {
            return GetOffsetPosition(entity, TowOffset);
        }

        var selectionBoxes = entity.GetBehavior<EntityBehaviorSelectionBoxes>()?.selectionBoxes;
        if (selectionBoxes != null && towPointSelectionBoxIndex >= 0 && towPointSelectionBoxIndex < selectionBoxes.Length)
        {
            var attachPoint = selectionBoxes[towPointSelectionBoxIndex].AttachPoint;
            if (attachPoint != null)
            {
                Vec3d offset = new Vec3d(attachPoint.PosX / 16.0, attachPoint.PosY / 16.0, attachPoint.PosZ / 16.0);
                return GetOffsetPosition(entity, offset);
            }
        }

        return entity.ServerPos.XYZ;
    }
}
