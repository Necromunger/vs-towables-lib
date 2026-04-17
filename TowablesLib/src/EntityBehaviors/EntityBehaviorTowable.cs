using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Essentials;
using Vintagestory.GameContent;

namespace TowablesLib.EntityBehaviors;

public class EntityBehaviorTowable : EntityBehavior
{
    public string InteractionPoint { get; private set; } = "TowAP";

    public float HitchSearchRange { get; private set; } = 4f;

    public float MaxTowDistance { get; private set; } = 20f;

    public float FollowDistance { get; private set; } = 2f;

    public float FollowMoveSpeed { get; private set; } = 0.03f;

    public float RepathDistanceThreshold { get; private set; } = 1.5f;

    public int RepathIntervalMs { get; private set; } = 750;

    public int PathSearchDepth { get; private set; } = 1000;

    public int PathDistanceTolerance { get; private set; } = 1;

    public float ArriveDistance { get; private set; }

    public long HitchEntityId => entity.WatchedAttributes.GetLong(HitchEntityIdAttribute, 0);

    public bool IsHitched => HitchEntityId > 0;

    private const string HitchEntityIdAttribute = "towableslib:hitchEntityId";

    private EntityAgent towableAgent = null!;
    private WaypointsTraverser towTraverser = null!;
    private bool disabled;
    private int interactionPointSelectionBoxIndex = -1;
    private bool selectionBoxIndexResolved;
    private Vec3d lastRequestedTarget;
    private long nextRepathMs;

    public EntityBehaviorTowable(Entity entity) : base(entity) { }

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
        HitchSearchRange = Math.Max(0.1f, attributes?["hitchSearchRange"].AsFloat(HitchSearchRange) ?? HitchSearchRange);
        MaxTowDistance = Math.Max(0.1f, attributes?["maxTowDistance"].AsFloat(MaxTowDistance) ?? MaxTowDistance);
        FollowDistance = ReadFollowDistance(attributes);
        FollowMoveSpeed = Math.Max(0.001f, ReadMoveSpeed(attributes));
        RepathDistanceThreshold = Math.Max(0.05f, attributes?["repathDistanceThreshold"].AsFloat(RepathDistanceThreshold) ?? RepathDistanceThreshold);
        RepathIntervalMs = Math.Max(0, attributes?["repathIntervalMs"].AsInt(RepathIntervalMs) ?? RepathIntervalMs);
        PathSearchDepth = Math.Max(1, attributes?["pathSearchDepth"].AsInt(PathSearchDepth) ?? PathSearchDepth);
        PathDistanceTolerance = Math.Max(0, attributes?["pathDistanceTolerance"].AsInt(PathDistanceTolerance) ?? PathDistanceTolerance);
        ArriveDistance = Math.Max(0f, ReadArriveDistance(attributes));
        towTraverser = new WaypointsTraverser(towableAgent);
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
    {
        if (disabled || entity.World.Side != EnumAppSide.Server || mode != EnumInteractMode.Interact)
        {
            base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
            return;
        }

        if (!IsInteractionPoint(byEntity))
        {
            base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
            return;
        }

        handled = EnumHandling.Handled;

        if (IsHitched)
        {
            ClearHitch();
            return;
        }

        Entity hitchEntity = FindNearestHitchable();
        if (hitchEntity == null)
        {
            return;
        }

        SetHitch(hitchEntity.EntityId);
    }

    public override void OnGameTick(float deltaTime)
    {
        base.OnGameTick(deltaTime);

        if (disabled)
        {
            return;
        }

        ResolveSelectionBoxIndexIfNeeded();

        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        if (!IsHitched)
        {
            if (towTraverser?.Active == true || lastRequestedTarget != null)
            {
                StopPathFollowing();
            }

            return;
        }

        Entity hitchEntity = entity.World.GetEntityById(HitchEntityId);
        EntityBehaviorHitchable hitchable = hitchEntity?.GetBehavior<EntityBehaviorHitchable>();
        if (hitchEntity == null || hitchable == null)
        {
            ClearHitch();
            return;
        }

        Vec3d hitchPoint = GetHitchPointPosition(hitchEntity, hitchable);
        if (hitchPoint.HorizontalSquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Z) > MaxTowDistance * MaxTowDistance)
        {
            ClearHitch();
            return;
        }

        Vec3d followTarget = GetFollowTarget(hitchEntity, hitchPoint);
        UpdatePathFollowing(followTarget);
        towTraverser?.OnGameTick(deltaTime);
    }

    public override string PropertyName() => "towable";

    private bool IsInteractionPoint(EntityAgent byEntity)
    {
        if (!selectionBoxIndexResolved)
        {
            return false;
        }

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
        ResetPathFollowingState();
    }

    private void ClearHitch()
    {
        StopPathFollowing();
        entity.WatchedAttributes.RemoveAttribute(HitchEntityIdAttribute);
        entity.WatchedAttributes.MarkPathDirty(HitchEntityIdAttribute);
        MarkChunkModified();
    }

    private void UpdatePathFollowing(Vec3d followTarget)
    {
        if (towTraverser == null)
        {
            return;
        }

        float arriveDistance = GetArriveDistance();
        long nowMs = entity.World.ElapsedMilliseconds;

        if (towTraverser.Ready)
        {
            Vec3d currentTarget = towTraverser.CurrentTarget;
            currentTarget.X = followTarget.X;
            currentTarget.Y = followTarget.Y;
            currentTarget.Z = followTarget.Z;
        }

        bool shouldStartPath = !towTraverser.Active
            && followTarget.HorizontalSquareDistanceTo(entity.ServerPos.X, entity.ServerPos.Z) > arriveDistance * arriveDistance;

        bool shouldRepath = towTraverser.Active
            && towTraverser.Ready
            && lastRequestedTarget != null
            && lastRequestedTarget.HorizontalSquareDistanceTo(followTarget.X, followTarget.Z) > RepathDistanceThreshold * RepathDistanceThreshold
            && nowMs >= nextRepathMs;

        if (!shouldStartPath && !shouldRepath)
        {
            return;
        }

        if (!towTraverser.NavigateTo_Async(
                followTarget.Clone(),
                FollowMoveSpeed,
                arriveDistance,
                OnPathGoalReached,
                OnPathStuck,
                null,
                PathSearchDepth,
                PathDistanceTolerance
            ))
        {
            return;
        }

        lastRequestedTarget = followTarget.Clone();
        nextRepathMs = nowMs + RepathIntervalMs;
    }

    private Vec3d GetFollowTarget(Entity hitchEntity, Vec3d hitchPoint)
    {
        double yaw = hitchEntity.ServerPos.Yaw;
        double offsetX = Math.Sin(yaw) * FollowDistance;
        double offsetZ = Math.Cos(yaw) * FollowDistance;

        return hitchPoint.AddCopy(-offsetX, 0, -offsetZ);
    }

    private float GetArriveDistance()
    {
        if (ArriveDistance > 0)
        {
            return ArriveDistance;
        }

        return Math.Max(0.6f, entity.SelectionBox.XSize * 0.5f);
    }

    private void OnPathGoalReached()
    {
        lastRequestedTarget = null;
    }

    private void OnPathStuck()
    {
        lastRequestedTarget = null;
        nextRepathMs = 0;
    }

    private void StopPathFollowing()
    {
        towTraverser?.Stop();
        ResetPathFollowingState();
        StopTowableMovement();
    }

    private void ResetPathFollowingState()
    {
        lastRequestedTarget = null;
        nextRepathMs = 0;
    }

    private void StopTowableMovement()
    {
        towableAgent.ServerControls.StopAllMovement();
        towableAgent.ServerControls.WalkVector.Set(0, 0, 0);
        towableAgent.ServerControls.FlyVector.Set(0, 0, 0);
    }

    private static Vec3d GetHitchPointPosition(Entity hitchEntity, EntityBehaviorHitchable hitchable)
    {
        Vec3d localOffset = hitchable.HitchOffset.RotatedCopy(hitchEntity.ServerPos.Yaw);
        return hitchEntity.ServerPos.XYZ.AddCopy(localOffset);
    }

    private static float ReadFollowDistance(JsonObject attributes)
    {
        if (attributes?["followDistance"]?.Exists == true)
        {
            return Math.Max(0.1f, attributes["followDistance"].AsFloat(2f));
        }

        if (attributes?["targetTowDistance"]?.Exists == true)
        {
            return Math.Max(0.1f, attributes["targetTowDistance"].AsFloat(2f));
        }

        return 2f;
    }

    private static float ReadMoveSpeed(JsonObject attributes)
    {
        if (attributes?["followMoveSpeed"]?.Exists == true)
        {
            return attributes["followMoveSpeed"].AsFloat(0.03f);
        }

        if (attributes?["moveSpeed"]?.Exists == true)
        {
            return attributes["moveSpeed"].AsFloat(0.03f);
        }

        return 0.03f;
    }

    private static float ReadArriveDistance(JsonObject attributes)
    {
        if (attributes?["arriveDistance"]?.Exists == true)
        {
            return attributes["arriveDistance"].AsFloat(0f);
        }

        if (attributes?["targetDistance"]?.Exists == true)
        {
            return attributes["targetDistance"].AsFloat(0f);
        }

        return 0f;
    }

    private static int FindSelectionBoxIndex(Entity pointEntity, string pointCode)
    {
        var selectionBoxes = pointEntity.GetBehavior<EntityBehaviorSelectionBoxes>()?.selectionBoxes;
        if (selectionBoxes == null)
        {
            return -1;
        }

        for (int i = 0; i < selectionBoxes.Length; i++)
        {
            if (selectionBoxes[i].AttachPoint?.Code == pointCode)
            {
                return i;
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

    private void ResolveSelectionBoxIndexIfNeeded()
    {
        if (selectionBoxIndexResolved)
        {
            return;
        }

        RefreshSelectionBoxesIfNeeded();
        interactionPointSelectionBoxIndex = FindSelectionBoxIndex(entity, InteractionPoint);
        selectionBoxIndexResolved = interactionPointSelectionBoxIndex >= 0;
    }
}
