using System;
using Vintagestory.API.Client;
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

    public string TowPoint { get; private set; } = "TowAP";

    public Vec3d TowOffset { get; private set; } = null;

    public float HitchSearchRange { get; private set; } = 4f;

    public float MaxTowDistance { get; private set; } = 20f;

    public float FollowDistance { get; private set; } = 2f;

    public float FollowMoveSpeed { get; private set; } = 0.03f;

    public float FollowMoveSpeedNearFactor { get; private set; } = 0.4f;

    public float FollowMoveSpeedFarFactor { get; private set; } = 1.2f;

    public float FollowMoveSpeedRampDistance { get; private set; }

    public float FollowMoveSpeedRampCurve { get; private set; } = 1.5f;

    public float FollowTargetLeadSeconds { get; private set; } = 0.35f;

    public float FollowTargetMaxLeadDistance { get; private set; } = 0.75f;

    public float RepathDistanceThreshold { get; private set; } = 1.5f;

    public int RepathIntervalMs { get; private set; } = 750;

    public int PathSearchDepth { get; private set; } = 1000;

    public int PathDistanceTolerance { get; private set; } = 1;

    public float ArriveDistance { get; private set; }

    public float PushDistance { get; private set; } = 1f;

    public float PushDeadZone { get; private set; } = 0.1f;

    public float PushStrength { get; private set; } = 7f;

    public float PushCurve { get; private set; } = 1.5f;

    public float PushTurnStrength { get; private set; } = 1.5f;

    public float PushMaxWalkVector { get; private set; } = 0.08f;

    public long HitchEntityId => entity.WatchedAttributes.GetLong(HitchEntityIdAttribute, 0);

    public bool IsHitched => HitchEntityId > 0;

    private const string HitchEntityIdAttribute = "towableslib:hitchEntityId";

    private EntityAgent towableAgent = null!;
    private WaypointsTraverser towTraverser = null!;
    private bool disabled;
    private int interactionPointSelectionBoxIndex = -1;
    private int towPointSelectionBoxIndex = -1;
    private bool selectionBoxIndexesResolved;
    private Vec3d lastRequestedTarget;
    private float lastRequestedMoveSpeed;
    private long nextRepathMs;
    private bool pushActive;

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
        TowPoint = attributes?["towPoint"].AsString(TowPoint) ?? TowPoint;
        TowOffset = ReadVec3d(attributes?["towOffset"]);
        HitchSearchRange = Math.Max(0.1f, attributes?["hitchSearchRange"].AsFloat(HitchSearchRange) ?? HitchSearchRange);
        MaxTowDistance = Math.Max(0.1f, attributes?["maxTowDistance"].AsFloat(MaxTowDistance) ?? MaxTowDistance);
        FollowDistance = ReadFollowDistance(attributes);
        FollowMoveSpeed = Math.Max(0.001f, ReadMoveSpeed(attributes));
        FollowMoveSpeedNearFactor = Math.Max(0.05f, attributes?["followMoveSpeedNearFactor"].AsFloat(FollowMoveSpeedNearFactor) ?? FollowMoveSpeedNearFactor);
        FollowMoveSpeedFarFactor = Math.Max(FollowMoveSpeedNearFactor, attributes?["followMoveSpeedFarFactor"].AsFloat(FollowMoveSpeedFarFactor) ?? FollowMoveSpeedFarFactor);
        FollowMoveSpeedRampDistance = Math.Max(0f, attributes?["followMoveSpeedRampDistance"].AsFloat(FollowMoveSpeedRampDistance) ?? FollowMoveSpeedRampDistance);
        FollowMoveSpeedRampCurve = Math.Max(0.001f, attributes?["followMoveSpeedRampCurve"].AsFloat(FollowMoveSpeedRampCurve) ?? FollowMoveSpeedRampCurve);
        FollowTargetLeadSeconds = Math.Max(0f, attributes?["followTargetLeadSeconds"].AsFloat(FollowTargetLeadSeconds) ?? FollowTargetLeadSeconds);
        FollowTargetMaxLeadDistance = Math.Max(0f, attributes?["followTargetMaxLeadDistance"].AsFloat(FollowTargetMaxLeadDistance) ?? FollowTargetMaxLeadDistance);
        RepathDistanceThreshold = Math.Max(0.05f, attributes?["repathDistanceThreshold"].AsFloat(RepathDistanceThreshold) ?? RepathDistanceThreshold);
        RepathIntervalMs = Math.Max(0, attributes?["repathIntervalMs"].AsInt(RepathIntervalMs) ?? RepathIntervalMs);
        PathSearchDepth = Math.Max(1, attributes?["pathSearchDepth"].AsInt(PathSearchDepth) ?? PathSearchDepth);
        PathDistanceTolerance = Math.Max(0, attributes?["pathDistanceTolerance"].AsInt(PathDistanceTolerance) ?? PathDistanceTolerance);
        ArriveDistance = Math.Max(0f, ReadArriveDistance(attributes));
        PushDistance = Math.Max(0f, ReadPushDistance(attributes));
        PushDeadZone = Math.Max(0f, ReadPushDeadZone(attributes));
        PushStrength = Math.Max(0f, ReadPushStrength(attributes));
        PushCurve = Math.Max(0.001f, ReadPushCurve(attributes));
        PushTurnStrength = Math.Max(0f, ReadPushTurnStrength(attributes));
        PushMaxWalkVector = Math.Max(0f, ReadPushMaxWalkVector(attributes));
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
            if (towTraverser?.Active == true || lastRequestedTarget != null || pushActive)
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

        Vec3d towPoint = GetTowPointPosition();
        Vec3d hitchPoint = GetHitchPointPosition(hitchEntity, hitchable);
        double towDistance = GetHorizontalDistance(towPoint, hitchPoint);
        if (towDistance > MaxTowDistance)
        {
            ClearHitch();
            return;
        }

        bool wasPushActive = pushActive;
        pushActive = ShouldUsePush(towDistance);
        if (pushActive)
        {
            SuspendPathFollowing();
            ApplyPushMovement(hitchPoint, towPoint, towDistance, deltaTime);
            return;
        }

        if (wasPushActive)
        {
            StopTowableMovement();
        }

        Vec3d followTarget = GetFollowTarget(hitchEntity, hitchPoint);
        float followMoveSpeed = GetFollowMoveSpeed(followTarget, towDistance);
        UpdatePathFollowing(followTarget, followMoveSpeed);
        towTraverser?.OnGameTick(deltaTime);
    }

    public override string PropertyName() => "towable";

    public bool TryGetDebugTowLine(out Vec3d hitchPoint, out Vec3d towPoint)
    {
        hitchPoint = null;
        towPoint = null;

        if (disabled || !IsHitched)
        {
            return false;
        }

        ResolveSelectionBoxIndexIfNeeded();

        Entity hitchEntity = entity.World.GetEntityById(HitchEntityId);
        EntityBehaviorHitchable hitchable = hitchEntity?.GetBehavior<EntityBehaviorHitchable>();
        if (hitchEntity == null || hitchable == null)
        {
            return false;
        }

        towPoint = GetTowPointPosition()?.Clone();
        hitchPoint = GetHitchPointPosition(hitchEntity, hitchable)?.Clone();
        return towPoint != null && hitchPoint != null;
    }

    private bool IsInteractionPoint(EntityAgent byEntity)
    {
        if (!selectionBoxIndexesResolved)
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
        pushActive = false;
        ResetPathFollowingState();
    }

    private void ClearHitch()
    {
        pushActive = false;
        StopPathFollowing();
        entity.WatchedAttributes.RemoveAttribute(HitchEntityIdAttribute);
        entity.WatchedAttributes.MarkPathDirty(HitchEntityIdAttribute);
        MarkChunkModified();
    }

    private bool ShouldUsePush(double towDistance)
    {
        if (PushDistance <= 0 || PushStrength <= 0 || PushMaxWalkVector <= 0)
        {
            return false;
        }

        double enterDistance = Math.Max(PushDistance - PushDeadZone, 0);
        double exitDistance = Math.Max(PushDistance + PushDeadZone, enterDistance);

        return pushActive ? towDistance <= exitDistance : towDistance < enterDistance;
    }

    private void ApplyPushMovement(Vec3d hitchPoint, Vec3d towPoint, double towDistance, float deltaTime)
    {
        double activeError = PushDistance - towDistance - PushDeadZone;
        if (activeError <= 0)
        {
            StopTowableMovement();
            return;
        }

        Vec3d directionToHitch = towDistance <= 0.001
            ? GetTowableForward()
            : new Vec3d((hitchPoint.X - towPoint.X) / towDistance, 0, (hitchPoint.Z - towPoint.Z) / towDistance);

        double responseRange = Math.Max(PushDistance - PushDeadZone, 0.001);
        double normalizedPush = Math.Min(activeError / responseRange, 1.0);
        double response = Math.Pow(normalizedPush, PushCurve);
        double moveSpeed = Math.Min(response * PushStrength * deltaTime, PushMaxWalkVector);
        Vec3d moveDirection = GetPushDirection(directionToHitch);

        towableAgent.ServerControls.StopAllMovement();
        towableAgent.ServerControls.WalkVector.Set(moveDirection.X * moveSpeed, 0, moveDirection.Z * moveSpeed);
        towableAgent.ServerControls.FlyVector.Set(0, 0, 0);

        ApplyPushSteering(hitchPoint, towPoint, normalizedPush, deltaTime);
    }

    private void ApplyPushSteering(Vec3d hitchPoint, Vec3d towPoint, double normalizedPush, float deltaTime)
    {
        float hingeYaw = (float)Math.Atan2(hitchPoint.X - towPoint.X, hitchPoint.Z - towPoint.Z);
        float yawDelta = AngleRadDistance(entity.ServerPos.Yaw, hingeYaw);
        entity.ServerPos.Yaw += yawDelta * PushTurnStrength * (float)normalizedPush * deltaTime;
    }

    private void UpdatePathFollowing(Vec3d followTarget, float moveSpeed)
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

        bool speedChanged = lastRequestedMoveSpeed > 0f && Math.Abs(lastRequestedMoveSpeed - moveSpeed) > 0.0005f;

        bool shouldRepath = towTraverser.Active
            && towTraverser.Ready
            && (
                speedChanged
                || (lastRequestedTarget != null
                    && lastRequestedTarget.HorizontalSquareDistanceTo(followTarget.X, followTarget.Z) > RepathDistanceThreshold * RepathDistanceThreshold)
            )
            && nowMs >= nextRepathMs;

        if (!shouldStartPath && !shouldRepath)
        {
            return;
        }

        if (!towTraverser.NavigateTo_Async(
                followTarget.Clone(),
                moveSpeed,
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
        lastRequestedMoveSpeed = moveSpeed;
        nextRepathMs = nowMs + RepathIntervalMs;
    }

    private Vec3d GetFollowTarget(Entity hitchEntity, Vec3d hitchPoint)
    {
        double yaw = hitchEntity.ServerPos.Yaw;
        double offsetX = Math.Sin(yaw) * FollowDistance;
        double offsetZ = Math.Cos(yaw) * FollowDistance;
        Vec3d followTarget = hitchPoint.AddCopy(-offsetX, 0, -offsetZ);

        if (FollowTargetLeadSeconds <= 0 || FollowTargetMaxLeadDistance <= 0)
        {
            return followTarget;
        }

        double leadX = hitchEntity.ServerPos.Motion.X * FollowTargetLeadSeconds;
        double leadZ = hitchEntity.ServerPos.Motion.Z * FollowTargetLeadSeconds;
        double leadLength = Math.Sqrt(leadX * leadX + leadZ * leadZ);
        if (leadLength <= 0.0001)
        {
            return followTarget;
        }

        double maxLeadDistance = FollowTargetMaxLeadDistance;
        if (leadLength > maxLeadDistance)
        {
            double leadScale = maxLeadDistance / leadLength;
            leadX *= leadScale;
            leadZ *= leadScale;
        }

        followTarget.X += leadX;
        followTarget.Z += leadZ;
        return followTarget;
    }

    private float GetArriveDistance()
    {
        if (ArriveDistance > 0)
        {
            return ArriveDistance;
        }

        return Math.Max(0.6f, entity.SelectionBox.XSize * 0.5f);
    }

    private float GetFollowMoveSpeed(Vec3d followTarget, double towDistance)
    {
        float arriveDistance = GetArriveDistance();
        double targetDistance = GetHorizontalDistance(entity.ServerPos.XYZ, followTarget);
        double targetError = Math.Max(0, targetDistance - arriveDistance);
        double distanceError = targetError;

        if (PushDistance > 0)
        {
            double hingeSlowDistance = Math.Max(PushDistance + PushDeadZone, arriveDistance);
            double hingeError = Math.Max(0, towDistance - hingeSlowDistance);
            distanceError = Math.Min(distanceError, hingeError);
        }

        float rampDistance = GetFollowMoveSpeedRampDistance(arriveDistance);
        float normalizedDistance = rampDistance <= 0.0001f
            ? 1f
            : (float)Math.Min(distanceError / rampDistance, 1.0);
        float easedDistance = (float)Math.Pow(normalizedDistance, FollowMoveSpeedRampCurve);
        float minSpeed = Math.Max(0.001f, FollowMoveSpeed * FollowMoveSpeedNearFactor);
        float maxSpeed = Math.Max(minSpeed, FollowMoveSpeed * FollowMoveSpeedFarFactor);
        return minSpeed + (maxSpeed - minSpeed) * easedDistance;
    }

    private float GetFollowMoveSpeedRampDistance(float arriveDistance)
    {
        if (FollowMoveSpeedRampDistance > 0)
        {
            return FollowMoveSpeedRampDistance;
        }

        float followRampDistance = FollowDistance > 0 ? FollowDistance : PushDistance + 0.75f;
        return Math.Max(arriveDistance, Math.Max(0.5f, followRampDistance));
    }

    private Vec3d GetPushDirection(Vec3d directionToHitch)
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

    private static float AngleRadDistance(float from, float to)
    {
        while (to - from > Math.PI) to -= GameMath.TWOPI;
        while (to - from < -Math.PI) to += GameMath.TWOPI;
        return to - from;
    }

    private void OnPathGoalReached()
    {
        lastRequestedTarget = null;
        lastRequestedMoveSpeed = 0f;
    }

    private void OnPathStuck()
    {
        lastRequestedTarget = null;
        lastRequestedMoveSpeed = 0f;
        nextRepathMs = 0;
    }

    private void StopPathFollowing()
    {
        towTraverser?.Stop();
        pushActive = false;
        ResetPathFollowingState();
        StopTowableMovement();
    }

    private void SuspendPathFollowing()
    {
        towTraverser?.Stop();
        ResetPathFollowingState();
    }

    private void ResetPathFollowingState()
    {
        lastRequestedTarget = null;
        lastRequestedMoveSpeed = 0f;
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

    private Vec3d GetTowPointPosition()
    {
        if (TowOffset != null)
        {
            return GetTowPointWorldPosition(TowOffset);
        }

        Vec3d selectionBoxCenter = GetTowPointSelectionBoxCenter();
        if (selectionBoxCenter != null)
        {
            return selectionBoxCenter;
        }

        Vec3d towPointLocalOffset = GetTowPointLocalOffset();
        if (towPointLocalOffset == null)
        {
            return entity.ServerPos.XYZ;
        }

        return GetTowPointWorldPosition(towPointLocalOffset);
    }

    private Vec3d GetTowPointLocalOffset()
    {
        if (TowOffset != null)
        {
            return TowOffset;
        }

        Vec3d resolvedAttachPointOffset = GetTowPointResolvedAttachPointLocalOffset();
        if (resolvedAttachPointOffset != null)
        {
            return resolvedAttachPointOffset;
        }

        Vec3d animatedOffset = GetTowPointAnimatedLocalOffset();
        if (animatedOffset != null && IsReasonableTowPointLocalOffset(animatedOffset))
        {
            return animatedOffset;
        }

        return GetTowPointRawAttachPointLocalOffset();
    }

    private static bool IsReasonableTowPointLocalOffset(Vec3d localOffset)
    {
        const double maxLocalOffset = 8.0;
        return localOffset.SquareDistanceTo(0, 0, 0) <= maxLocalOffset * maxLocalOffset;
    }

    private Vec3d GetTowPointAnimatedLocalOffset()
    {
        var selectionBox = GetTowPointSelectionBox();
        var animModelMatrix = selectionBox?.AnimModelMatrix;
        var attachPoint = selectionBox?.AttachPoint;
        if (animModelMatrix == null || animModelMatrix.Length < 16 || attachPoint == null)
        {
            return null;
        }

        return TransformModelPointToLocalOffset(animModelMatrix, attachPoint.PosX, attachPoint.PosY, attachPoint.PosZ);
    }

    private Vec3d GetTowPointResolvedAttachPointLocalOffset()
    {
        var attachPoint = GetTowPointSelectionBox()?.AttachPoint;
        if (attachPoint == null)
        {
            return null;
        }

        ShapeElement parentElement = attachPoint.ParentElement;
        if (parentElement?.GetInverseModelMatrix() is float[] inverseModelMatrix && inverseModelMatrix.Length >= 16)
        {
            float[] modelMatrix = Mat4f.Create();
            Mat4f.Invert(modelMatrix, inverseModelMatrix);
            return TransformModelPointToLocalOffset(modelMatrix, attachPoint.PosX, attachPoint.PosY, attachPoint.PosZ);
        }

        Vec3d rawOffset = GetTowPointRawAttachPointLocalOffset();
        if (rawOffset == null)
        {
            return null;
        }

        Vec3d resolvedOffset = rawOffset.Clone();
        while (parentElement != null)
        {
            if (parentElement.From != null && parentElement.From.Length >= 3)
            {
                resolvedOffset.X += parentElement.From[0] / 16.0;
                resolvedOffset.Y += parentElement.From[1] / 16.0;
                resolvedOffset.Z += parentElement.From[2] / 16.0;
            }

            parentElement = parentElement.ParentElement;
        }

        return resolvedOffset;
    }

    private Vec3d GetTowPointRawAttachPointLocalOffset()
    {
        var attachPoint = GetTowPointSelectionBox()?.AttachPoint;
        if (attachPoint == null)
        {
            return null;
        }

        return new Vec3d(attachPoint.PosX / 16.0, attachPoint.PosY / 16.0, attachPoint.PosZ / 16.0);
    }

    private Vec3d GetTowPointSelectionBoxCenter()
    {
        var selectionBox = GetTowPointSelectionBox();
        if (selectionBox?.AnimModelMatrix == null || selectionBox.AnimModelMatrix.Length < 16)
        {
            return null;
        }

        ShapeElement parentElement = selectionBox.AttachPoint?.ParentElement;
        if (parentElement?.From == null || parentElement.To == null || parentElement.From.Length < 3 || parentElement.To.Length < 3)
        {
            return null;
        }

        Matrixf boxTransform = new Matrixf();
        boxTransform.Identity();
        ApplyTowPointSelectionBoxTransform(boxTransform, selectionBox, parentElement);

        Vec4d boxCenter = new(
            (parentElement.To[0] - parentElement.From[0]) / 32.0,
            (parentElement.To[1] - parentElement.From[1]) / 32.0,
            (parentElement.To[2] - parentElement.From[2]) / 32.0,
            1.0
        );

        return boxTransform.TransformVector(boxCenter).XYZ.Add(entity.Pos.XYZ);
    }

    private AttachmentPointAndPose GetTowPointSelectionBox()
    {
        var selectionBoxes = entity.GetBehavior<EntityBehaviorSelectionBoxes>()?.selectionBoxes;
        if (selectionBoxes == null || towPointSelectionBoxIndex < 0 || towPointSelectionBoxIndex >= selectionBoxes.Length)
        {
            return null;
        }

        return selectionBoxes[towPointSelectionBoxIndex];
    }

    private Vec3d GetTowPointWorldPosition(Vec3d localOffset)
    {
        return localOffset == null ? null : GetOffsetPosition(entity, localOffset);
    }

    private void ApplyTowPointSelectionBoxTransform(Matrixf boxTransform, AttachmentPointAndPose selectionBox, ShapeElement parentElement)
    {
        EntityShapeRenderer entityShapeRenderer = entity.Properties.Client?.Renderer as EntityShapeRenderer;

        boxTransform.RotateY((float)Math.PI / 2f + entity.SidedPos.Yaw);

        if (entityShapeRenderer != null)
        {
            boxTransform.Translate(0f, entity.SelectionBox.Y2 / 2f, 0f);
            boxTransform.RotateX(entityShapeRenderer.xangle);
            boxTransform.RotateY(entityShapeRenderer.yangle);
            boxTransform.RotateZ(entityShapeRenderer.zangle);
            boxTransform.Translate(0f, -entity.SelectionBox.Y2 / 2f, 0f);
        }

        boxTransform.Translate(0f, 0.7f, 0f);
        boxTransform.RotateX(entityShapeRenderer?.nowSwivelRad ?? 0f);
        boxTransform.Translate(0f, -0.7f, 0f);

        float size = entity.Properties.Client?.Size ?? 1f;
        boxTransform.Scale(size, size, size);
        boxTransform.Translate(-0.5f, 0f, -0.5f);
        boxTransform.Mul(selectionBox.AnimModelMatrix);

        float scaleX = (float)(parentElement.To[0] - parentElement.From[0]) / 16f;
        float scaleY = (float)(parentElement.To[1] - parentElement.From[1]) / 16f;
        float scaleZ = (float)(parentElement.To[2] - parentElement.From[2]) / 16f;
        boxTransform.Scale(scaleX, scaleY, scaleZ);
    }

    private static Vec3d TransformModelPointToLocalOffset(float[] modelMatrix, double x, double y, double z)
    {
        Vec3f transformed = new();
        Mat4f.MulWithVec3_Position(modelMatrix, (float)x, (float)y, (float)z, transformed);
        return new Vec3d(transformed.X / 16.0, transformed.Y / 16.0, transformed.Z / 16.0);
    }

    private static Vec3d GetOffsetPosition(Entity pointEntity, Vec3d localOffset)
    {
        return pointEntity.ServerPos.XYZ.AddCopy(localOffset.RotatedCopy(pointEntity.ServerPos.Yaw));
    }

    private static double GetHorizontalDistance(Vec3d from, Vec3d to)
    {
        double dx = to.X - from.X;
        double dz = to.Z - from.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    private static float ReadFollowDistance(JsonObject attributes)
    {
        if (attributes?["followDistance"]?.Exists == true)
            return Math.Max(0f, attributes["followDistance"].AsFloat(2f));

        return 2f;
    }

    private static float ReadMoveSpeed(JsonObject attributes)
    {
        if (attributes?["followMoveSpeed"]?.Exists == true)
            return attributes["followMoveSpeed"].AsFloat(0.03f);

        return 0.03f;
    }

    private static float ReadArriveDistance(JsonObject attributes)
    {
        if (attributes?["arriveDistance"]?.Exists == true)
            return attributes["arriveDistance"].AsFloat(0f);

        return 0f;
    }

    private float ReadPushDistance(JsonObject attributes)
    {
        if (attributes?["pushDistance"]?.Exists == true)
            return attributes["pushDistance"].AsFloat(PushDistance);

        return PushDistance;
    }

    private float ReadPushDeadZone(JsonObject attributes)
    {
        if (attributes?["pushDeadZone"]?.Exists == true)
            return attributes["pushDeadZone"].AsFloat(PushDeadZone);

        return PushDeadZone;
    }

    private float ReadPushStrength(JsonObject attributes)
    {
        if (attributes?["pushStrength"]?.Exists == true)
            return attributes["pushStrength"].AsFloat(PushStrength);

        return PushStrength;
    }

    private float ReadPushCurve(JsonObject attributes)
    {
        if (attributes?["pushCurve"]?.Exists == true)
            return attributes["pushCurve"].AsFloat(PushCurve);

        return PushCurve;
    }

    private float ReadPushTurnStrength(JsonObject attributes)
    {
        if (attributes?["pushTurnStrength"]?.Exists == true)
            return attributes["pushTurnStrength"].AsFloat(PushTurnStrength);

        return PushTurnStrength;
    }

    private float ReadPushMaxWalkVector(JsonObject attributes)
    {
        if (attributes?["pushMaxWalkVector"]?.Exists == true)
            return attributes["pushMaxWalkVector"].AsFloat(PushMaxWalkVector);

        return PushMaxWalkVector;
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
        if (selectionBoxIndexesResolved)
        {
            return;
        }

        RefreshSelectionBoxesIfNeeded();
        interactionPointSelectionBoxIndex = FindSelectionBoxIndex(entity, InteractionPoint);
        towPointSelectionBoxIndex = TowOffset == null ? FindSelectionBoxIndex(entity, TowPoint) : -1;
        selectionBoxIndexesResolved = interactionPointSelectionBoxIndex >= 0;
    }
}
