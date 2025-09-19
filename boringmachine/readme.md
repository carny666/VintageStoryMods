# Boring Machine Mod

A Vintage Story mod that creates an automatic tunnel boring machine that can dig through any terrain.

## Features

- `/startdig` command - Spawns a boring machine that digs a tunnel automatically
- `/stopdig` command - Stops your active boring machine
- `/digspeed <blocksPerSecond>` - Adjusts the speed of your active boring machine on the fly (range 1–250 bps, values are clamped)
- Customizable block type - Specify which block to use for the boring machine
- Automatic obstacle clearing - The machine breaks through any blocks or entities in its path
- Item dropping option - Choose whether to drop items from broken blocks
- Visual and sound effects - Dust particles and mining sounds while digging
- 3x3 tunnel - Creates a spacious 3x3 tunnel through the terrain
- Fast digging - Advances at 5 blocks per second by default (changeable with `/digspeed`, up to 250 bps)
- Floor laying - Lays a 3-wide floor under the tunnel using a path block when available, otherwise falls back to a common stone floor
- Auto-stop at world edge - Despawns itself and informs you when it reaches the map boundary
- ETA feedback - Estimates time to reach the map edge on start and when you change speed
- Periodic status updates - Current position, speed, and ETA every 10 seconds

## Usage

### Starting a Boring Machine
```
/startdig [blockcode] [dropItems]
```

This will spawn a boring machine 2 blocks in front of you that automatically digs forward in the direction you're facing.

Parameters:
- `blockcode` (optional) - The type of block to use for the boring machine
- `dropItems` (optional) - Set to "true" to make the machine drop items from broken blocks

Examples:
- `/startdig` - Uses default stone blocks for the boring machine
- `/startdig rock-basalt` - Uses basalt rocks for the boring machine
- `/startdig log-oak true` - Uses oak logs and drops items from broken blocks
- `/startdig game:cloth-wool-red true` - Uses red wool (using full domain notation) and drops items

### Changing Speed
```
/digspeed <blocksPerSecond>
```
Sets your active boring machine's speed. Allowed range is 1–250 blocks per second; out-of-range values are clamped to the nearest limit. Also shows an updated ETA to the world edge.

Examples:
- `/digspeed 1` sets speed to the minimum
- `/digspeed 250` sets speed to the maximum
- `/digspeed 1000` will clamp to 250

### Stopping a Boring Machine
```
/stopdig
```
Stops your active boring machine and removes all of its blocks.

## How It Works

1. The boring machine spawns a 3x3x3 structure with a 3x3 hollow center to create a spacious tunnel
2. It moves forward using time-based stepping at a default rate of 5 blocks per second (adjustable via `/digspeed`, clamped to 1–250)
3. It automatically clears any blocks or entities in its path
4. It lays a 3-wide floor on the tunnel level using a stone path block if present in your game/modpack; if no path block is found, it will use a common stone floor instead and notify you in chat
5. Old blocks from the machine are removed as it advances, ensuring it doesn't leave a trail
6. If configured to drop items, it will spawn item entities for broken blocks and containers
7. It reports an ETA to the map edge on start and after speed changes
8. While running, it sends a status update (position, speed, ETA) every 10 seconds
9. When the machine reaches the end of the map, it despawns itself and notifies you that the boring is complete
10. You can also stop it at any time with `/stopdig`

## Notes

- Each player can have only one active boring machine at a time
- The boring machine will notify you of its progress every 10 seconds
- Be careful when using the boring machine as it will destroy anything in its path (except for the player who created it)
- The tunnel is 3x3, making it spacious for player movement, storage, or transportation
- If no dedicated path blocks exist in your modpack, the floor will be laid with a common stone (e.g., stone bricks or cobblestone)
- For better performance, consider not using the item dropping feature when digging very long tunnels

## Requirements

- Vintage Story game

## Known Issues
- Sometimes the machine may appear to stop or miss blocks