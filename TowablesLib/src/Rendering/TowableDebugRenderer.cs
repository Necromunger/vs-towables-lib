using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

using TowablesLib.EntityBehaviors;

namespace TowablesLib.Rendering;

public class TowableDebugRenderer : IRenderer
{
    private const int HitchColor = unchecked((int)0xFFFF30A0);
    private const int TowColor = unchecked((int)0xFFFF3030);
    private const int DebugLineColor = unchecked((int)0xFFFF30A0);

    private readonly ICoreClientAPI capi;
    private readonly double hitchMarkerRadius = 0.15;
    private readonly double towMarkerRadius = 0.22;

    public double RenderOrder => 0.55;

    public int RenderRange => 999999;

    public TowableDebugRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Opaque)
        {
            return;
        }

        foreach (Entity entity in capi.World.LoadedEntities.Values)
        {
            EntityBehaviorTowable towable = entity.GetBehavior<EntityBehaviorTowable>();
            if (towable == null || !towable.TryGetDebugTowLine(out Vec3d hitchPoint, out Vec3d towPoint))
            {
                continue;
            }

            RenderMarker(hitchPoint, HitchColor, hitchMarkerRadius);
            RenderMarker(towPoint, TowColor, towMarkerRadius);
            RenderDebugLine(hitchPoint, towPoint, DebugLineColor);
        }
    }

    public void Dispose()
    {
    }

    private void RenderDebugLine(Vec3d from, Vec3d to, int color)
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
        RenderDebugLine(position.AddCopy(-radius, 0, 0), position.AddCopy(radius, 0, 0), color);
        RenderDebugLine(position.AddCopy(0, -radius, 0), position.AddCopy(0, radius, 0), color);
        RenderDebugLine(position.AddCopy(0, 0, -radius), position.AddCopy(0, 0, radius), color);
    }
}
