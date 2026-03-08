# PilotPro

A comprehensive **Rust Oxide plugin** that transforms basic minicopter flight into a professional, skill-based racing experience with advanced scoring systems, streaks, zones, and visual feedback.

## Scoring System

**Base Formula**: `Ground Proximity × Speed Bonus × Biome Multiplier × Altitude Modifier × Streak Multiplier`

### Multipliers
- **Streak Progression**: 1.0x (0-10s) → 1.1x (10s) → 1.2x (20s) → ... → 3.0x (360s+)
- **Speed Bonuses**: Higher speeds = more points
- **Biome Effects**: Forests (1.2x), Deserts (0.8x), Water (1.5x), etc.
- **Zone Bonuses**: Difficulty-scaled fly-through rewards

### Penalties
- **Altitude**: Too high reduces points
- **Crashes**: Damage/destruction penalties (50-75% of streak earnings)
- **Cooldowns**: Post-crash recovery periods

## Commands

### Player Commands
- `/mc stats` - View personal statistics
- `/mc leaderboard` - Show top players
- `/mc help` - Command reference

### Admin Commands
- `/mc zonecreate <size> <difficulty> [name]` - Create racing zones
- `/mc zonelist` - List all zones
- `/mc zonetp <zone_id>` - Teleport to zone
- `/mc zonedel <zone_id>` - Delete zone
- `/mc reset` - Clear all data
- `/mc debug` - Toggle debug UI
