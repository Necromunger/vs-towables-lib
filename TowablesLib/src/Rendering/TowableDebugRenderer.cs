using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

using TowablesLib.EntityBehaviors;

namespace TowablesLib.Rendering;

public class TowableDebugRenderer : IRenderer
{
    public double RenderOrder => 0.55;
    public int RenderRange => 999999;

    private static readonly int HitchColor = ColorUtil.ToRgba(255, 255, 48, 160);
    private static readonly int TowColor = ColorUtil.ToRgba(255, 255, 48, 48);
    private static readonly int GoalColor = ColorUtil.ToRgba(255, 48, 220, 255);
    private static readonly int LineColor = ColorUtil.ToRgba(255, 0, 20, 255);

    private readonly ICoreClientAPI capi;
    private readonly double hitchMarkerRadius = 0.15;
    private readonly double towMarkerRadius = 0.22;
    private readonly double goalMarkerRadius = 0.18;

    public TowableDebugRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Opaque)
            return;

        var loadedEntities = capi.World?.LoadedEntities;
        if (loadedEntities == null)
            return;

        foreach (Entity entity in loadedEntities.Values)
        {
            if (entity == null)
                continue;

            EntityBehaviorTowable towable = entity.GetBehavior<EntityBehaviorTowable>();
            if (towable == null || !towable.TryGetDebugTowState(out Vec3d hitchPoint, out Vec3d towPoint, out Vec3d towPointGoal))
                continue;

            RenderMarker(hitchPoint, HitchColor, hitchMarkerRadius);
            RenderMarker(towPoint, TowColor, towMarkerRadius);
            RenderMarker(towPointGoal, GoalColor, goalMarkerRadius);
            RenderLine(towPoint, towPointGoal, LineColor);
        }
    }

    public void Dispose() { }

    private void RenderLine(Vec3d from, Vec3d to, int color)
    {
        BlockPos origin = new((int)Math.Floor(from.X), (int)Math.Floor(from.Y), (int)Math.Floor(from.Z));
        
        capi.Render.LineWidth = 4f;
        capi.Render.RenderLine(
            origin,
            (float)(from.X - origin.X),
            (float)(from.Y - origin.Y),
            (float)(from.Z - origin.Z),
            (float)(to.X - origin.X),
            (float)(to.Y - origin.Y),
            (float)(to.Z - origin.Z),
            color
        );
    }

    private void RenderMarker(Vec3d position, int color, double radius)
    {
        RenderLine(position.AddCopy(-radius, 0, 0), position.AddCopy(radius, 0, 0), color);
        RenderLine(position.AddCopy(0, -radius, 0), position.AddCopy(0, radius, 0), color);
        RenderLine(position.AddCopy(0, 0, -radius), position.AddCopy(0, 0, radius), color);
    }
}
