using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace TowablesLib.EntityBehaviors;

public class EntityBehaviorTowable : EntityBehavior
{
    public string InteractionPoint { get; private set; } = "TowAP";
    public string TowPoint { get; private set; } = "TowAP";
    public float SearchRange { get; private set; } = 4f;
    public float FollowSpeed { get; private set; } = 0.04f;
    public float FollowSmoothing { get; private set; } = 6f;
    public float FollowBrakeSmoothing { get; private set; } = 16f;
    public float MaxHitchDistance { get; private set; } = 20f;
    public long HitchEntityId => entity.WatchedAttributes.GetLong(HitchEntityIdAttribute, 0);
    public bool IsHitched => HitchEntityId > 0;

    public EntityBehaviorTowable(Entity entity) : base(entity) { }

    private const string HitchEntityIdAttribute = "towableslib:hitchEntityId";
    private int interactionPointSelectionBoxIndex = -1;
    private int towPointSelectionBoxIndex = -1;
    private bool loggedFirstTickSelectionBoxState;
    private readonly Vec3d smoothedMovement = new Vec3d();

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        InteractionPoint = attributes?["interactionPoint"].AsString(InteractionPoint) ?? InteractionPoint;
        TowPoint = attributes?["towPoint"].AsString(TowPoint) ?? TowPoint;
        SearchRange = attributes?["searchRange"].AsFloat(SearchRange) ?? SearchRange;
        FollowSpeed = attributes?["followSpeed"].AsFloat(FollowSpeed) ?? FollowSpeed;
        FollowSmoothing = attributes?["followSmoothing"].AsFloat(FollowSmoothing) ?? FollowSmoothing;
        FollowBrakeSmoothing = attributes?["followBrakeSmoothing"].AsFloat(FollowBrakeSmoothing) ?? FollowBrakeSmoothing;
        MaxHitchDistance = attributes?["maxHitchDistance"].AsFloat(MaxHitchDistance) ?? MaxHitchDistance;
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
    {
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

        FollowHitch(hitchEntity, hitchable, deltaTime);
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
            SearchRange,
            SearchRange,
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

    private void FollowHitch(Entity hitchEntity, EntityBehaviorHitchable hitchable, float deltaTime)
    {
        Vec3d towPoint = GetEntityOriginPosition(entity);
        Vec3d hitchPoint = GetHitchPointPosition(hitchEntity, hitchable);
        Vec3d offset = towPoint.SubCopy(hitchPoint);
        offset.Y = 0;

        double currentDistance = Math.Sqrt(offset.X * offset.X + offset.Z * offset.Z);
        if (currentDistance > MaxHitchDistance)
        {
            entity.World.Logger.Notification(
                "[TowablesLib] Clearing hitch on {0}; distance {1:0.##} exceeded maxHitchDistance {2:0.##}",
                entity.Code, currentDistance, MaxHitchDistance
            );
            ClearHitch();
            return;
        }

        if (currentDistance < 0.001)
        {
            offset.Set(0, 0, 1);
            currentDistance = 1;
        }

        bool insideDistanceBand = currentDistance >= hitchable.MinDistance && currentDistance <= hitchable.MaxDistance;
        entity.ServerPos.Yaw = (float)Math.Atan2(hitchPoint.X - towPoint.X, hitchPoint.Z - towPoint.Z);

        if (insideDistanceBand)
        {
            EaseTowableMovementToZero(deltaTime, FollowBrakeSmoothing);
            return;
        }

        bool moveBackward = currentDistance < hitchable.MinDistance;
        double desiredDistance = moveBackward ? hitchable.MinDistance : hitchable.MaxDistance;

        offset.Mul(desiredDistance / currentDistance);

        Vec3d targetTowPoint = new Vec3d(hitchPoint.X + offset.X, towPoint.Y, hitchPoint.Z + offset.Z);
        Vec3d movement = targetTowPoint.SubCopy(towPoint);

        if (entity is EntityAgent agent)
        {
            FollowHitchWithControls(agent, movement, deltaTime, moveBackward);
            return;
        }

        FollowHitchWithMotion(movement, deltaTime, moveBackward);
    }

    private void FollowHitchWithControls(EntityAgent agent, Vec3d movement, float deltaTime, bool isTooClose)
    {
        StopEntityControls(agent);

        double horizontalDistance = Math.Sqrt(movement.X * movement.X + movement.Z * movement.Z);
        if (horizontalDistance < 0.025)
        {
            EaseTowableMovementToZero(deltaTime, FollowBrakeSmoothing);
            return;
        }

        double speed = FollowSpeed;
        float smoothing = isTooClose ? FollowBrakeSmoothing : FollowSmoothing;
        UpdateSmoothedMovement(movement.X / horizontalDistance * speed, movement.Z / horizontalDistance * speed, deltaTime, smoothing);

        agent.ServerControls.WalkVector.Set(smoothedMovement.X, 0, smoothedMovement.Z);
    }

    private void FollowHitchWithMotion(Vec3d movement, float deltaTime, bool isTooClose)
    {
        double horizontalDistance = Math.Sqrt(movement.X * movement.X + movement.Z * movement.Z);
        if (horizontalDistance < 0.025)
        {
            EaseTowableMovementToZero(deltaTime, FollowBrakeSmoothing);
            return;
        }

        double speed = FollowSpeed;
        float smoothing = isTooClose ? FollowBrakeSmoothing : FollowSmoothing;
        UpdateSmoothedMovement(movement.X / horizontalDistance * speed, movement.Z / horizontalDistance * speed, deltaTime, smoothing);

        entity.ServerPos.Motion.X = smoothedMovement.X;
        entity.ServerPos.Motion.Z = smoothedMovement.Z;
    }

    private void StopTowableMovement()
    {
        smoothedMovement.Set(0, 0, 0);

        if (entity is EntityAgent agent)
        {
            StopEntityControls(agent);
        }

        entity.ServerPos.Motion.X = 0;
        entity.ServerPos.Motion.Z = 0;
    }

    private void EaseTowableMovementToZero(float deltaTime, float smoothing)
    {
        UpdateSmoothedMovement(0, 0, deltaTime, smoothing);

        if (Math.Abs(smoothedMovement.X) < 0.001 && Math.Abs(smoothedMovement.Z) < 0.001)
        {
            smoothedMovement.Set(0, 0, 0);
            StopTowableMovement();
            return;
        }

        if (entity is EntityAgent agent)
        {
            StopEntityControls(agent);
            agent.ServerControls.WalkVector.Set(smoothedMovement.X, 0, smoothedMovement.Z);
            return;
        }

        entity.ServerPos.Motion.X = smoothedMovement.X;
        entity.ServerPos.Motion.Z = smoothedMovement.Z;
    }

    private void UpdateSmoothedMovement(double targetX, double targetZ, float deltaTime, float smoothing)
    {
        float lerp = Math.Min(1f, deltaTime * smoothing);
        smoothedMovement.X = GameMath.Lerp(smoothedMovement.X, targetX, lerp);
        smoothedMovement.Z = GameMath.Lerp(smoothedMovement.Z, targetZ, lerp);
    }

    private static void StopEntityControls(EntityAgent agent)
    {
        agent.ServerControls.StopAllMovement();
        agent.ServerControls.WalkVector.Set(0, 0, 0);
        agent.ServerControls.FlyVector.Set(0, 0, 0);
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

    private static Vec3d GetEntityOriginPosition(Entity pointEntity) => pointEntity.ServerPos.XYZ;

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
}
