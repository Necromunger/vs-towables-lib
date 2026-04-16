using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

using TowablesLib.EntityBehaviors;

namespace TowablesLib
{
    public class TowablesLibModSystem : ModSystem
    {
        public override void Start(ICoreAPI api)
        {
            api.RegisterEntityBehaviorClass("hitchable", typeof(EntityBehaviorHitchable));
            api.RegisterEntityBehaviorClass("towable", typeof(EntityBehaviorTowable));
        }
    }
}
