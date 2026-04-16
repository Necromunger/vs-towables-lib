using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace TowablesLib.EntityBehaviors;

public class EntityBehaviorHitchable : EntityBehavior
{
    public string HitchPoint { get; private set; } = "HitchAP";
    public float Distance { get; private set; } = 1.5f;
    public float MinDistance { get; private set; } = 1f;
    public float MaxDistance { get; private set; } = 2f;

    public EntityBehaviorHitchable(Entity entity) : base(entity) {}

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        HitchPoint = attributes?["hitchPoint"].AsString(HitchPoint) ?? HitchPoint;
        Distance = attributes?["distance"].AsFloat(Distance) ?? Distance;
        MinDistance = attributes?["minDistance"].AsFloat(MinDistance) ?? MinDistance;
        MaxDistance = attributes?["maxDistance"].AsFloat(MaxDistance) ?? MaxDistance;
    }

    public override string PropertyName() => "hitchable";
}
