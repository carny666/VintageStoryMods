using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace spawnboi
{
    public class spawnboiModSystem : ModSystem
    {
        ICoreServerAPI sapi;
        public override void Start(ICoreAPI api)
        {
            if (api is ICoreServerAPI serverApi)
            {
                sapi = serverApi;
                //sapi.RegisterCommand("startdig", "Starts a boring machine that digs a tunnel", "[blockcode] [optional: dropItems]", StartDigCommand, Privilege.chat);
                //sapi.RegisterCommand("stopdig", "Stops your active boring machine", "", StopDigCommand, Privilege.chat);
                //sapi.RegisterCommand("digspeed", "Set the speed of your active boring machine in blocks per second (1-250)", "<bps>", DigSpeedCommand, Privilege.chat);

                // Update ?? every 200ms (~5 ticks per second). Actual movement uses dt for precision                
                //sapi.Event.RegisterGameTickListener(UpdateDiggers, 200);



            }
        }

    }
}
