using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace TowablesLib.EntityBehaviors;

public class EntityBehaviorHitchable : EntityBehavior
{
    public float Distance { get; private set; } = 1.5f;

    public EntityBehaviorHitchable(Entity entity) : base(entity) {}

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        Distance = attributes?["distance"].AsFloat(Distance) ?? Distance;
    }

    public override string PropertyName() => "hitchable";
}
