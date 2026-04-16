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
    public float FollowStrength { get; private set; } = 8f;
    public long HitchEntityId => entity.WatchedAttributes.GetLong(HitchEntityIdAttribute, 0);
    public bool IsHitched => HitchEntityId > 0;

    public EntityBehaviorTowable(Entity entity) : base(entity) { }

    private const string HitchEntityIdAttribute = "towableslib:hitchEntityId";
    private int interactionPointSelectionBoxIndex = -1;
    private int towPointSelectionBoxIndex = -1;
    private long cachedHitchEntityId;
    private int cachedHitchPointSelectionBoxIndex = -1;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        InteractionPoint = attributes?["interactionPoint"].AsString(InteractionPoint) ?? InteractionPoint;
        TowPoint = attributes?["towPoint"].AsString(TowPoint) ?? TowPoint;
        SearchRange = attributes?["searchRange"].AsFloat(SearchRange) ?? SearchRange;
        FollowStrength = attributes?["followStrength"].AsFloat(FollowStrength) ?? FollowStrength;
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        base.AfterInitialized(onFirstSpawn);

        interactionPointSelectionBoxIndex = FindSelectionBoxIndex(entity, InteractionPoint);
        towPointSelectionBoxIndex = FindSelectionBoxIndex(entity, TowPoint);
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
    {
        if (mode != EnumInteractMode.Interact || !IsInteractionPoint(byEntity))
        {
            base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
            return;
        }

        handled = EnumHandling.PreventSubsequent;

        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

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
        cachedHitchEntityId = 0;
        cachedHitchPointSelectionBoxIndex = -1;
        entity.WatchedAttributes.RemoveAttribute(HitchEntityIdAttribute);
        entity.WatchedAttributes.MarkPathDirty(HitchEntityIdAttribute);
        MarkChunkModified();
    }

    private void FollowHitch(Entity hitchEntity, EntityBehaviorHitchable hitchable, float deltaTime)
    {
        Vec3d towPoint = GetPointPosition(entity, towPointSelectionBoxIndex);
        Vec3d hitchPoint = GetHitchPointPosition(hitchEntity, hitchable);
        Vec3d offset = towPoint.SubCopy(hitchPoint);
        offset.Y = 0;

        double currentDistance = Math.Sqrt(offset.X * offset.X + offset.Z * offset.Z);
        if (currentDistance < 0.001)
        {
            offset.Set(0, 0, 1);
            currentDistance = 1;
        }

        double desiredDistance = Math.Clamp(currentDistance, hitchable.MinDistance, hitchable.MaxDistance);
        desiredDistance = GameMath.Lerp(desiredDistance, hitchable.Distance, Math.Min(1f, deltaTime * 2f));

        offset.Mul(desiredDistance / currentDistance);

        Vec3d targetTowPoint = hitchPoint.AddCopy(offset.X, 0, offset.Z);
        Vec3d movement = targetTowPoint.SubCopy(towPoint);
        double moveFactor = Math.Min(1, deltaTime * FollowStrength);

        entity.ServerPos.X += movement.X * moveFactor;
        entity.ServerPos.Z += movement.Z * moveFactor;
        entity.ServerPos.Yaw = (float)Math.Atan2(hitchPoint.X - towPoint.X, hitchPoint.Z - towPoint.Z);
    }

    private Vec3d GetHitchPointPosition(Entity hitchEntity, EntityBehaviorHitchable hitchable)
    {
        if (cachedHitchEntityId != hitchEntity.EntityId)
        {
            cachedHitchEntityId = hitchEntity.EntityId;
            cachedHitchPointSelectionBoxIndex = FindSelectionBoxIndex(hitchEntity, hitchable.HitchPoint);
        }

        return GetPointPosition(hitchEntity, cachedHitchPointSelectionBoxIndex);
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

    private static Vec3d GetPointPosition(Entity pointEntity, int selectionBoxIndex)
    {
        if (selectionBoxIndex < 0)
        {
            return pointEntity.ServerPos.XYZ;
        }

        return pointEntity.GetBehavior<EntityBehaviorSelectionBoxes>()?.GetCenterPosOfBox(selectionBoxIndex) ?? pointEntity.ServerPos.XYZ;
    }

    private void MarkChunkModified()
    {
        if (entity.World.Side == EnumAppSide.Server)
        {
            entity.World.BlockAccessor.GetChunkAtBlockPos(entity.Pos.AsBlockPos)?.MarkModified();
        }
    }
}
