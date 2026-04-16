using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace TowablesLib.EntityBehaviors;

public class EntityBehaviorHitchable : EntityBehavior
{
    public string HitchPoint { get; private set; } = "HitchAP";
    public Vec3d HitchOffset { get; private set; } = new Vec3d();

    public EntityBehaviorHitchable(Entity entity) : base(entity) {}

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        HitchPoint = attributes?["hitchPoint"].AsString(HitchPoint) ?? HitchPoint;

        JsonObject hitchOffset = attributes?["hitchOffset"];
        if (hitchOffset?.Exists == true)
        {
            HitchOffset.Set(
                hitchOffset["x"].AsDouble(HitchOffset.X),
                hitchOffset["y"].AsDouble(HitchOffset.Y),
                hitchOffset["z"].AsDouble(HitchOffset.Z)
            );
        }
    }

    public override string PropertyName() => "hitchable";
}
