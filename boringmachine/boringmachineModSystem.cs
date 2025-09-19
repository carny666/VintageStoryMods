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

namespace boringmachine
{
    public class boringmachineModSystem : ModSystem
    {
        ICoreServerAPI sapi;
        private Dictionary<string, DiggerMachine> activeMachines = new Dictionary<string, DiggerMachine>();

        public override void Start(ICoreAPI api)
        {
            if (api is ICoreServerAPI serverApi)
            {
                sapi = serverApi;
                sapi.RegisterCommand("startdig", "Starts a boring machine that digs a tunnel", "[blockcode] [optional: dropItems]", StartDigCommand, Privilege.chat);
                sapi.RegisterCommand("stopdig", "Stops your active boring machine", "", StopDigCommand, Privilege.chat);
                sapi.RegisterCommand("digspeed", "Set the speed of your active boring machine in blocks per second (1-250)", "<bps>", DigSpeedCommand, Privilege.chat);
                
                // Update diggers every 200ms (~5 ticks per second). Actual movement uses dt for precision
                sapi.Event.RegisterGameTickListener(UpdateDiggers, 200);
            }
        }
        
        private void UpdateDiggers(float dt)
        {
            // Make a copy of the keys to avoid collection modified errors
            string[] playerIds = new string[activeMachines.Count];
            activeMachines.Keys.CopyTo(playerIds, 0);
            
            foreach (string playerId in playerIds)
            {
                if (activeMachines.TryGetValue(playerId, out DiggerMachine machine))
                {
                    if (machine != null)
                    {
                        bool stillActive = machine.Update(dt);
                        if (!stillActive)
                        {
                            // Machine finished or stopped internally
                            activeMachines.Remove(playerId);
                        }
                    }
                }
            }
        }

        private void StartDigCommand(IServerPlayer player, int groupId, CmdArgs args)
        {
            if (player == null) return;
            
            string playerId = player.PlayerUID;
            
            // Check if player already has an active digger
            if (activeMachines.ContainsKey(playerId))
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, "You already have an active boring machine. Use /stopdig to stop it first.", EnumChatType.Notification);
                return;
            }

            // Get player position and facing
            EntityPos pos = player.Entity.Pos;
            BlockFacing facing = CardinalFromYaw(pos.Yaw);
            
            // Log facing direction for debugging
            player.SendMessage(GlobalConstants.GeneralChatGroup, $"Your facing direction: {facing}", EnumChatType.Notification);
            
            // Calculate position 2 blocks in front of player
            Vec3d startPos = pos.XYZ.Clone();
            
            // Fix directional placement to ensure it's always in front of the player
            if (facing == BlockFacing.NORTH)
            {
                startPos.Z -= 2; // North is negative Z
            }
            else if (facing == BlockFacing.SOUTH)
            {
                startPos.Z += 2; // South is positive Z
            }
            else if (facing == BlockFacing.EAST)
            {
                startPos.X += 2; // East is positive X
            }
            else if (facing == BlockFacing.WEST)
            {
                startPos.X -= 2; // West is negative X
            }
            
            // Set Y to the ground level
            startPos.Y = Math.Floor(startPos.Y);
            
            // Try to get block from command argument first
            Block rockBlock = null;
            string customBlockCode = args.PopWord();
            
            // Check if we should drop items
            bool dropItems = false;
            string dropItemsArg = args.PopWord()?.ToLowerInvariant();
            if (!string.IsNullOrEmpty(dropItemsArg) && (dropItemsArg == "true" || dropItemsArg == "yes" || dropItemsArg == "1" || dropItemsArg == "dropitems"))
            {
                dropItems = true;
            }
            
            if (!string.IsNullOrEmpty(customBlockCode))
            {
                // Try to parse the provided block code
                AssetLocation blockLocation;
                
                // If the domain isn't specified, assume "game" domain
                if (!customBlockCode.Contains(":"))
                {
                    blockLocation = new AssetLocation("game", customBlockCode);
                }
                else
                {
                    blockLocation = new AssetLocation(customBlockCode);
                }
                
                rockBlock = sapi.World.GetBlock(blockLocation);
                
                if (rockBlock == null)
                {
                    player.SendMessage(GlobalConstants.GeneralChatGroup, $"Block '{customBlockCode}' not found. Using default stone block instead.", EnumChatType.Notification);
                }
            }
            
            // If no valid block specified or found, use fallback options
            if (rockBlock == null)
            {
                // Try multiple possible stone blocks to ensure one is found
                string[] possibleBlockCodes = new string[] 
                {
                    "rock-granite", 
                    "rock-basalt", 
                    "rock-andesite", 
                    "stonebricks-granite",
                    "stone-granite", 
                    "cobblestone-granite"
                };
                
                foreach (string blockCode in possibleBlockCodes)
                {
                    rockBlock = sapi.World.GetBlock(new AssetLocation("game:" + blockCode));
                    if (rockBlock != null) break;
                }
                
                // Fallback to first available solid block if none of our specific blocks are found
                if (rockBlock == null)
                {
                    foreach (Block block in sapi.World.Blocks)
                    {
                        if (block.SideSolid[BlockFacing.UP.Index] && block.Code.Domain == "game")
                        {
                            rockBlock = block;
                            break;
                        }
                    }
                }
            }
            
            if (rockBlock == null)
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, "Could not find a suitable block to spawn", EnumChatType.Notification);
                return;
            }
            
            // Debug message to help diagnose positioning issues
            player.SendMessage(GlobalConstants.GeneralChatGroup, $"Starting boring machine at position: {(int)startPos.X}, {(int)startPos.Y}, {(int)startPos.Z}, facing: {facing}", EnumChatType.Notification);
            
            // Create a new digger machine (default 5 bps)
            DiggerMachine digger = new DiggerMachine(sapi, player, rockBlock, startPos, facing, dropItems, 5.0);
            activeMachines[playerId] = digger;
            
            // Initialize the digger (place initial blocks)
            digger.Initialize();

            // Inform ETA to world edge
            int distanceBlocks = digger.GetDistanceToMapEdgeBlocks();
            if (distanceBlocks >= 0)
            {
                double seconds = distanceBlocks / digger.BlocksPerSecond;
                string etaText = FormatEta(seconds);
                player.SendMessage(GlobalConstants.GeneralChatGroup, $"Estimated time to map edge: {etaText} at {digger.BlocksPerSecond:0.##} blocks/sec.", EnumChatType.Notification);
            }
            
            string dropItemsMessage = dropItems ? " Items from broken blocks will be dropped." : "";
            player.SendMessage(GlobalConstants.GeneralChatGroup, $"Started boring machine with {rockBlock.Code} blocks. The machine will dig forward automatically at {digger.BlocksPerSecond:0.##} blocks per second.{dropItemsMessage}", EnumChatType.Notification);
        }
        
        private void StopDigCommand(IServerPlayer player, int groupId, CmdArgs args)
        {
            if (player == null) return;
            
            string playerId = player.PlayerUID;
            
            if (activeMachines.TryGetValue(playerId, out DiggerMachine machine))
            {
                machine.Cleanup();
                activeMachines.Remove(playerId);
                player.SendMessage(GlobalConstants.GeneralChatGroup, "Boring machine stopped.", EnumChatType.Notification);
            }
            else
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, "You don't have an active boring machine.", EnumChatType.Notification);
            }
        }

        private void DigSpeedCommand(IServerPlayer player, int groupId, CmdArgs args)
        {
            if (player == null) return;
            string playerId = player.PlayerUID;
            double? val = args.PopDouble();
            if (val == null)
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, "Usage: /digspeed <blocksPerSecond> (allowed range: 1-250)", EnumChatType.Notification);
                return;
            }

            if (!activeMachines.TryGetValue(playerId, out DiggerMachine machine))
            {
                player.SendMessage(GlobalConstants.GeneralChatGroup, "You don't have an active boring machine.", EnumChatType.Notification);
                return;
            }

            double requested = val.Value;
            machine.SetSpeed(requested);
            double finalSpeed = machine.BlocksPerSecond;

            int distanceBlocks = machine.GetDistanceToMapEdgeBlocks();
            if (distanceBlocks >= 0)
            {
                double seconds = distanceBlocks / finalSpeed;
                string etaText = FormatEta(seconds);
                string clampNote = requested != finalSpeed ? " (clamped to 1-250)" : string.Empty;
                player.SendMessage(GlobalConstants.GeneralChatGroup, $"Speed set to {finalSpeed:0.##} blocks/sec{clampNote}. New ETA to map edge: {etaText}", EnumChatType.Notification);
            }
            else
            {
                string clampNote = requested != finalSpeed ? " (clamped to 1-250)" : string.Empty;
                player.SendMessage(GlobalConstants.GeneralChatGroup, $"Speed set to {finalSpeed:0.##} blocks/sec{clampNote}.", EnumChatType.Notification);
            }
        }

        private string FormatEta(double seconds)
        {
            if (seconds < 60)
            {
                return $"{seconds:0}s";
            }
            int mins = (int)(seconds / 60);
            int secs = (int)(seconds % 60);
            return $"{mins}m {secs}s";
        }

        BlockFacing CardinalFromYaw(double yaw)
        {
            // Normalize yaw to 0-360 degrees
            double degYaw = yaw * (180.0 / Math.PI) % 360;
            if (degYaw < 0) degYaw += 360;
            
            // Vintage Story yaw: 0° looks South (+Z)
            if (degYaw >= 315 || degYaw < 45) return BlockFacing.SOUTH;
            if (degYaw >= 45 && degYaw < 135) return BlockFacing.EAST;
            if (degYaw >= 135 && degYaw < 225) return BlockFacing.NORTH;
            if (degYaw >= 225 && degYaw < 315) return BlockFacing.WEST;
            
            // Fallback
            return BlockFacing.SOUTH;
        }
        
        public override void Dispose()
        {
            foreach (var machine in activeMachines.Values)
            {
                machine.Cleanup();
            }
            activeMachines.Clear();
            base.Dispose();
        }
    }
    
    public class DiggerMachine
    {
        private ICoreServerAPI api;
        private IServerPlayer owner;
        private Block blockType;
        private Vec3d currentPosition;
        private BlockFacing direction;
        private List<BlockPos> machineBlocks = new List<BlockPos>();
        private bool dropItems = false;
        private Random random = new Random();
        private Block pathBlock; // Cached reference to stone path block
        private double stepAccumulator = 0;
        private double statusTimer = 0; // Accumulates time to send status updates every 10s
        
        public double BlocksPerSecond { get; private set; }
        
        public DiggerMachine(ICoreServerAPI api, IServerPlayer owner, Block blockType, Vec3d startPosition, BlockFacing direction, bool dropItems = false, double blocksPerSecond = 5.0)
        {
            this.api = api;
            this.owner = owner;
            this.blockType = blockType;
            this.currentPosition = startPosition;
            this.direction = direction;
            this.dropItems = dropItems;
            this.BlocksPerSecond = Math.Clamp(blocksPerSecond, 1.0, 250.0);
            
            // Resolve the stone path block once
            pathBlock = TryResolvePathBlock();
        }
        
        public void SetSpeed(double bps)
        {
            BlocksPerSecond = Math.Clamp(bps, 1.0, 250.0);
        }
        
        private Block TryResolvePathBlock()
        {
            // Prefer known flat path codes first
            string[] tryCodes = new string[]
            {
                "game:stonepath",
                "game:path-stone",
                "game:path_cobblestone",
                "game:path-stonebricks"
            };
            foreach (var code in tryCodes)
            {
                var b = api.World.GetBlock(new AssetLocation(code));
                if (b != null) return b;
            }

            // Helper to reject stair/step/slab variants
            bool IsStairOrStep(string codePath)
            {
                if (string.IsNullOrEmpty(codePath)) return false;
                codePath = codePath.ToLowerInvariant();
                return codePath.Contains("stair") || codePath.Contains("stairs") || codePath.Contains("step") || codePath.Contains("slab");
            }
            
            // Search registry for a flat path-like block (prefer stone path)
            Block preferred = null;
            Block anyFlatPath = null;
            foreach (var b in api.World.Blocks)
            {
                if (b?.Code == null) continue;
                string path = b.Code.Path ?? string.Empty;
                if (path.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (IsStairOrStep(path)) continue; // skip stair/step/slab variants

                    if (path.IndexOf("stone", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        preferred = b; // stone path (flat)
                        break;
                    }

                    if (anyFlatPath == null)
                    {
                        anyFlatPath = b; // remember first flat path (non-stairs)
                    }
                }
            }
            if (preferred != null)
            {
                owner.SendMessage(GlobalConstants.GeneralChatGroup, $"Using path block: {preferred.Code}", EnumChatType.Notification);
                return preferred;
            }
            if (anyFlatPath != null)
            {
                owner.SendMessage(GlobalConstants.GeneralChatGroup, $"Using path block: {anyFlatPath.Code}", EnumChatType.Notification);
                return anyFlatPath;
            }

            // Final fallback to a visible floor block
            var fallback = api.World.GetBlock(new AssetLocation("game:stonebricks-granite"))
                           ?? api.World.GetBlock(new AssetLocation("game:cobblestone-granite"));
            if (fallback != null)
            {
                owner.SendMessage(GlobalConstants.GeneralChatGroup, $"Path block not found. Using {fallback.Code} as floor.", EnumChatType.Notification);
            }
            else
            {
                owner.SendMessage(GlobalConstants.GeneralChatGroup, "No suitable path block found.", EnumChatType.Notification);
            }
            return fallback;
        }
        
        public void Initialize()
        {
            // Place the initial 3x3x3 cube
            PlaceMachineBlocks();
        }
        
        public int GetDistanceToMapEdgeBlocks()
        {
            int mapSizeX = api.World.BlockAccessor.MapSizeX;
            int mapSizeZ = api.World.BlockAccessor.MapSizeZ;
            int cx = (int)Math.Floor(currentPosition.X);
            int cz = (int)Math.Floor(currentPosition.Z);

            if (direction == BlockFacing.NORTH)
            {
                return cz;
            }
            if (direction == BlockFacing.SOUTH)
            {
                return (mapSizeZ - 1) - cz;
            }
            if (direction == BlockFacing.EAST)
            {
                return (mapSizeX - 1) - cx;
            }
            if (direction == BlockFacing.WEST)
            {
                return cx;
            }
            return -1;
        }
        
        public bool Update(float dt)
        {
            // Accumulate progress in blocks and time
            stepAccumulator += BlocksPerSecond * dt; // dt is seconds
            statusTimer += dt;

            if (stepAccumulator < 1)
            {
                // Still place effects occasionally even if not stepping
                MaybeDoEffects();

                // Periodic status update based on time
                MaybeSendStatus();
                return true;
            }

            int steps = (int)Math.Floor(stepAccumulator);
            stepAccumulator -= steps;

            for (int i = 0; i < steps; i++)
            {
                // Before moving, check if next step exceeds world bounds
                if (AtWorldEdgeNextStep())
                {
                    // Finish and inform
                    owner.SendMessage(GlobalConstants.GeneralChatGroup, "Boring complete: reached the end of the map.", EnumChatType.Notification);
                    Cleanup();
                    return false; // signal removal
                }

                // Remove previous machine blocks
                RemovePreviousBlocks();

                // Move forward one block based on direction
                if (direction == BlockFacing.NORTH)
                {
                    currentPosition.Z -= 1;
                }
                else if (direction == BlockFacing.SOUTH)
                {
                    currentPosition.Z += 1;
                }
                else if (direction == BlockFacing.EAST)
                {
                    currentPosition.X += 1;
                }
                else if (direction == BlockFacing.WEST)
                {
                    currentPosition.X -= 1;
                }
                
                // Clear any obstacles in the path
                ClearObstacles();

                // Place stone path blocks on the floor under the machine
                PlacePathFloor();
                
                // Place new blocks at the new position
                PlaceMachineBlocks();

                MaybeDoEffects();
            }

            // Periodic status update based on time
            MaybeSendStatus();

            return true;
        }

        private void MaybeSendStatus()
        {
            if (statusTimer >= 10.0)
            {
                statusTimer -= 10.0;
                int x = (int)currentPosition.X;
                int y = (int)currentPosition.Y;
                int z = (int)currentPosition.Z;
                int dist = GetDistanceToMapEdgeBlocks();
                string etaText = dist >= 0 ? FormatEtaText(dist / BlocksPerSecond) : "n/a";
                owner.SendMessage(GlobalConstants.GeneralChatGroup, $"Boring machine at {x}, {y}, {z} | {BlocksPerSecond:0.##} bps | ETA: {etaText}", EnumChatType.Notification);
            }
        }

        private string FormatEtaText(double seconds)
        {
            if (seconds < 60)
            {
                return $"{seconds:0}s";
            }
            int mins = (int)(seconds / 60);
            int secs = (int)(seconds % 60);
            return $"{mins}m {secs}s";
        }

        private bool AtWorldEdgeNextStep()
        {
            int mapSizeX = api.World.BlockAccessor.MapSizeX;
            int mapSizeZ = api.World.BlockAccessor.MapSizeZ;
            int cx = (int)Math.Floor(currentPosition.X);
            int cz = (int)Math.Floor(currentPosition.Z);

            if (direction == BlockFacing.NORTH)
            {
                return (cz - 1) < 0;
            }
            if (direction == BlockFacing.SOUTH)
            {
                return (cz + 1) >= mapSizeZ;
            }
            if (direction == BlockFacing.EAST)
            {
                return (cx + 1) >= mapSizeX;
            }
            if (direction == BlockFacing.WEST)
            {
                return (cx - 1) < 0;
            }
            return false;
        }

        private int effectsCounter = 0;
        private void MaybeDoEffects()
        {
            effectsCounter++;
            if (effectsCounter % 3 != 0) return;

            float centerX = (float)currentPosition.X;
            float centerY = (float)(currentPosition.Y + 1);
            float centerZ = (float)currentPosition.Z;
            
            if (direction == BlockFacing.NORTH)
            {
                centerZ -= 1;
            }
            else if (direction == BlockFacing.SOUTH)
            {
                centerZ += 1;
            }
            else if (direction == BlockFacing.EAST)
            {
                centerX += 1;
            }
            else if (direction == BlockFacing.WEST)
            {
                centerX -= 1;
            }
            
            SimpleParticleProperties props = new SimpleParticleProperties(
                20,
                5,
                1,
                new Vec3d(0.5, 0.5, 0.5),
                new Vec3d(0.2, 0.2, 0.2),
                new Vec3f(0.8f, 0.8f, 0.8f),
                new Vec3f(0.2f, 0.2f, 0.2f),
                1f,
                0.25f
            );
            
            props.AddPos.Set(centerX, centerY, centerZ);
            props.AddVelocity.Set(0.5f, 0.5f, 0.5f);
            props.ParticleModel = EnumParticleModel.Cube;
            
            api.World.SpawnParticles(props);
            
            api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/stonebreak"), 
                centerX, centerY, centerZ, null, 
                true, 12f, 1f);
        }
        
        private void PlacePathFloor()
        {
            if (pathBlock == null) return;
            int y = (int)currentPosition.Y - 1;
            for (int xoff = -1; xoff <= 1; xoff++)
            {
                int tx = (int)currentPosition.X;
                int tz = (int)currentPosition.Z;

                if (direction == BlockFacing.NORTH || direction == BlockFacing.SOUTH)
                {
                    tx += xoff;
                }
                else if (direction == BlockFacing.EAST || direction == BlockFacing.WEST)
                {
                    tz += xoff;
                }

                BlockPos pos = new BlockPos(tx, y, tz);
                api.World.BlockAccessor.SetBlock(pathBlock.Id, pos);
            }
        }
        
        private void RemovePreviousBlocks()
        {
            foreach (BlockPos pos in machineBlocks)
            {
                Block currentBlock = api.World.BlockAccessor.GetBlock(pos);
                if (currentBlock.Id == blockType.Id)
                {
                    api.World.BlockAccessor.SetBlock(0, pos);
                }
            }
            machineBlocks.Clear();
        }
        
        private void ClearObstacles()
        {
            for (int x = -1; x <= 1; x++)
            {
                for (int y = 0; y <= 2; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        int transformedX = (int)currentPosition.X;
                        int transformedZ = (int)currentPosition.Z;
                        
                        if (direction == BlockFacing.NORTH)
                        {
                            transformedX += x;
                        }
                        else if (direction == BlockFacing.SOUTH)
                        {
                            transformedX += x;
                        }
                        else if (direction == BlockFacing.EAST)
                        {
                            transformedZ += x;
                        }
                        else if (direction == BlockFacing.WEST)
                        {
                            transformedZ += x;
                        }
                        
                        BlockPos pos = new BlockPos(transformedX, (int)currentPosition.Y + y, transformedZ);
                        
                        // Destroy any entity at this position
                        Entity[] entities = api.World.GetEntitiesAround(new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5), 0.5f, 0.5f);
                        foreach (var entity in entities)
                        {
                            // Don't kill the owner
                            if (entity != owner.Entity)
                            {
                                EntityDespawnData despawnData = new EntityDespawnData() { Reason = EnumDespawnReason.Removed };
                                api.World.DespawnEntity(entity, despawnData);
                            }
                        }
                        
                        Block existingBlock = api.World.BlockAccessor.GetBlock(pos);
                        if (existingBlock.Id != 0)
                        {
                            if (dropItems)
                            {
                                TryDropBlockItems(existingBlock, pos);
                            }
                            
                            api.World.BlockAccessor.SetBlock(0, pos);
                        }
                    }
                }
            }
        }
        
        private void TryDropBlockItems(Block block, BlockPos pos)
        {
            try
            {
                if (block.Id == 0 || block.LiquidLevel > 0) return;
                
                Vec3d dropPos = new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5);
                
                BlockEntity blockEntity = api.World.BlockAccessor.GetBlockEntity(pos);
                
                if (blockEntity == null)
                {
                    ItemStack[] drops = block.GetDrops(api.World, pos, owner);
                    if (drops != null && drops.Length > 0)
                    {
                        foreach (ItemStack drop in drops)
                        {
                            if (drop != null)
                            {
                                api.World.SpawnItemEntity(drop, dropPos, new Vec3d(
                                    (random.NextDouble() - 0.5) * 0.5,
                                    random.NextDouble() * 0.5,
                                    (random.NextDouble() - 0.5) * 0.5
                                ));
                            }
                        }
                    }
                }
                else
                {
                    if (blockEntity is BlockEntityContainer container)
                    {
                        if (container.Inventory != null)
                        {
                            for (int i = 0; i < container.Inventory.Count; i++)
                            {
                                ItemSlot slot = container.Inventory[i];
                                if (slot.Itemstack != null)
                                {
                                    api.World.SpawnItemEntity(slot.Itemstack, dropPos, new Vec3d(
                                        (random.NextDouble() - 0.5) * 0.5,
                                        random.NextDouble() * 0.5,
                                        (random.NextDouble() - 0.5) * 0.5
                                    ));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Silently ignore any errors to avoid crashing the game
            }
        }
        
        private void PlaceMachineBlocks()
        {
            for (int x = -1; x <= 1; x++)
            {
                for (int y = 0; y <= 2; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        // Skip all internal blocks to create a 3x3 hollow tunnel
                        // We only place the outer frame of the 3x3x3 cube
                        if (x == 0 && y != 2 && z == 0)
                            continue;
                            
                        // Transform coordinates based on direction
                        int transformedX = (int)currentPosition.X;
                        int transformedZ = (int)currentPosition.Z;
                        
                        if (direction == BlockFacing.NORTH)
                        {
                            transformedX += x;
                        }
                        else if (direction == BlockFacing.SOUTH)
                        {
                            transformedX += x;
                        }
                        else if (direction == BlockFacing.EAST)
                        {
                            transformedZ += x;
                        }
                        else if (direction == BlockFacing.WEST)
                        {
                            transformedZ += x;
                        }
                        
                        BlockPos pos = new BlockPos(transformedX, (int)currentPosition.Y + y, transformedZ);
                        
                        // Place the machine block
                        api.World.BlockAccessor.SetBlock(blockType.Id, pos);
                        
                        // Remember this block for cleanup
                        machineBlocks.Add(pos);
                    }
                }
            }
        }
        
        public void Cleanup()
        {
            foreach (BlockPos pos in machineBlocks)
            {
                api.World.BlockAccessor.SetBlock(0, pos); // Set to air
            }
            machineBlocks.Clear();
        }
    }
}