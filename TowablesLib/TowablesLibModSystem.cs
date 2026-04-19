using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

using TowablesLib.EntityBehaviors;
using TowablesLib.Rendering;

namespace TowablesLib
{
    public class TowablesLibModSystem : ModSystem
    {
        private TowableDebugRenderer debugRenderer;

        public override void Start(ICoreAPI api)
        {
            api.RegisterEntityBehaviorClass("hitchable", typeof(EntityBehaviorHitchable));
            api.RegisterEntityBehaviorClass("towable", typeof(EntityBehaviorTowable));
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            debugRenderer = new TowableDebugRenderer(api);
            api.Event.RegisterRenderer(debugRenderer, EnumRenderStage.Opaque, "towableslib-debug");
        }

        public override void Dispose()
        {
            debugRenderer?.Dispose();
        }
    }
}
