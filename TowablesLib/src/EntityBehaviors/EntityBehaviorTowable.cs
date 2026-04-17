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
    /// Radius searched for nearby hitchable entities when the player interacts with the towable.
    /// </summary>
    public float HitchSearchRange { get; private set; } = 4f;

    /// <summary>
    /// Maximum movement strength applied by rope tension. Higher values pull harder.
    /// </summary>
    public float PullStrength { get; private set; } = 30f;

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
    /// Distance below which the towable stops moving to avoid jitter from tiny corrections.
    /// </summary>
    public float MinPullDistance { get; private set; } = 1.0f;

    /// <summary>
    /// Distance where rope tension begins ramping in.
    /// </summary>
    public float TensionStartDistance { get; private set; } = 1.0f;

    /// <summary>
    /// Exponent applied to normalized tension. Values below 1 ramp faster; values above 1 ramp slower.
    /// </summary>
    public float TensionCurve { get; private set; } = 0.3f;

    public EntityBehaviorTowable(Entity entity) : base(entity) { }

    private const string HitchEntityIdAttribute = "towableslib:hitchEntityId";
    private EntityAgent towableAgent = null!;
    private bool disabled;
    private int interactionPointSelectionBoxIndex = -1;
    private int towPointSelectionBoxIndex = -1;
    private bool loggedFirstTickSelectionBoxState;

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
        HitchSearchRange = attributes?["hitchSearchRange"].AsFloat(HitchSearchRange) ?? HitchSearchRange;
        PullStrength = attributes?["pullStrength"].AsFloat(PullStrength) ?? PullStrength;
        MaxTowDistance = attributes?["maxTowDistance"].AsFloat(MaxTowDistance) ?? MaxTowDistance;
        MinPullDistance = attributes?["minPullDistance"].AsFloat(MinPullDistance) ?? MinPullDistance;
        TensionStartDistance = attributes?["tensionStartDistance"].AsFloat(TensionStartDistance) ?? TensionStartDistance;
        TensionCurve = attributes?["tensionCurve"].AsFloat(TensionCurve) ?? TensionCurve;
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
    {
        if (disabled)
        {
            base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
            return;
        }

        // fail - not interact mode or not server side
        if (mode != EnumInteractMode.Interact || entity.World.Side != EnumAppSide.Server) {
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

        // Client and server both setup here
        // OnInit OnSpawned and all the rest had no selection box info, need to find real onload point with selection box info available to cache the indexes
        if (!loggedFirstTickSelectionBoxState)
        {
            loggedFirstTickSelectionBoxState = true;
            RefreshSelectionBoxesIfNeeded();
            interactionPointSelectionBoxIndex = FindSelectionBoxIndex(entity, InteractionPoint);
            towPointSelectionBoxIndex = FindSelectionBoxIndex(entity, TowPoint);
            LogSelectionBoxState("first tick");
        }

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

        ApplyTowTension(hitchEntity, hitchable, deltaTime);
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
    }

    private void ApplyTowTension(Entity hitchEntity, EntityBehaviorHitchable hitchable, float deltaTime)
    {
        Vec3d towPoint = GetTowPointPosition();
        Vec3d hitchPoint = GetHitchPointPosition(hitchEntity, hitchable);
        Vec3d correction = hitchPoint.SubCopy(towPoint);

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

        if (towDistance < MinPullDistance) { StopTowableMovement(); return; }

        double slack = Math.Max(towDistance - TensionStartDistance, 0);
        double tensionRange = Math.Max(MaxTowDistance - TensionStartDistance, 0.001);
        double normalizedTension = Math.Min(slack / tensionRange, 1.0);
        double pullSpeed = Math.Pow(normalizedTension, TensionCurve) * PullStrength * deltaTime;
        
        Vec3d dir = new Vec3d(correction.X, 0, correction.Z);
        dir.Normalize();

        towableAgent.ServerControls.WalkVector.Set(dir.X * pullSpeed, 0, dir.Z * pullSpeed);

        entity.ServerPos.Yaw = (float)Math.Atan2(hitchPoint.X - towPoint.X, hitchPoint.Z - towPoint.Z);
    }

    private void StopTowableMovement()
    {
        towableAgent.ServerControls.StopAllMovement();
        towableAgent.ServerControls.WalkVector.Set(0, 0, 0);
    }

    private Vec3d GetHitchPointPosition(Entity hitchEntity, EntityBehaviorHitchable hitchable)
    {
        return GetOffsetPosition(hitchEntity, hitchable.HitchOffset);
    }

    private static Vec3d GetOffsetPosition(Entity pointEntity, Vec3d localOffset)
    {
        return pointEntity.ServerPos.XYZ.AddCopy(localOffset.RotatedCopy(pointEntity.ServerPos.Yaw));
    }

    private static int FindSelectionBoxIndex(Entity pointEntity, string pointCode)
    {
        var selectionBoxes = pointEntity.GetBehavior<EntityBehaviorSelectionBoxes>()?.selectionBoxes;
        if (selectionBoxes != null)
        {
            for (int i = 0; i < selectionBoxes.Length; i++)
            {
                if (selectionBoxes[i].AttachPoint.Code == pointCode)
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
        var selectionBoxes = entity.GetBehavior<EntityBehaviorSelectionBoxes>()?.selectionBoxes;
        if (selectionBoxes != null && towPointSelectionBoxIndex >= 0 && towPointSelectionBoxIndex < selectionBoxes.Length)
        {
            var ap = selectionBoxes[towPointSelectionBoxIndex].AttachPoint;
            Vec3d offset = new Vec3d(ap.PosX / 16.0, ap.PosY / 16.0, ap.PosZ / 16.0);
            return GetOffsetPosition(entity, offset);
        }

        return entity.ServerPos.XYZ;
    }
}
