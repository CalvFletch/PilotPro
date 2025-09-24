# PilotPro

A comprehensive **Rust Oxide plugin** that transforms basic minicopter flight into a professional, skill-based racing experience with advanced scoring systems, streaks, zones, and visual feedback.

## 🚁 Features

### Core Systems
- **Skill-Based Scoring**: Points based on proximity to ground, speed, and environmental factors
- **Streak Multipliers**: Progressive multipliers (1.0x → 3.0x) for consecutive flight time
- **Forgiveness System**: 3-second grace periods for altitude and speed violations
- **Crash Penalties**: Realistic consequences for ending streaks via damage/destruction
- **High Score Tracking**: Personal best streak records with celebrations

### Advanced Features
- **Fly-Through Zones**: Strategic racing elements with difficulty-scaled bonuses
- **Biome Multipliers**: Different environments (forests, deserts, water) affect scoring
- **Flip Bonuses**: Advanced maneuver recognition and rewards
- **Real-Time UI**: Persistent streak display with color-coded multipliers
- **Milestone Celebrations**: Visual feedback for major achievements (2.0x, 2.5x, 3.0x)

### Player Experience
- **Leaderboard System**: Competitive rankings and statistics
- **Visual Feedback**: Animated point displays and countdown warnings
- **Chat Commands**: Player statistics and admin zone management
- **Debug Tools**: Developer information overlay for testing

## 🎮 Installation

1. Place `MinicopterSkillRace.cs` in your `oxide/plugins/` directory
2. Reload plugins or restart server
3. Configure settings via `/mc help` or config file

## ⚙️ Configuration

The plugin includes extensive configuration options:
- Scoring multipliers and thresholds
- UI timing and display settings
- Zone system parameters
- Debug and development options

## 🏆 Scoring System

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

## 🎯 Commands

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

## 🏗️ Development

### Prerequisites
- Rust server with Oxide/uMod
- C# development environment
- Basic understanding of Oxide plugin development

### Project Structure
```
MinicopterSkillRace.cs          # Main plugin file
.gitignore                      # Git ignore rules
README.md                       # This file
```

### Key Systems
1. **Configuration System** - Centralized settings
2. **Data Management** - Player/zone data persistence
3. **Scoring Engine** - Point calculation algorithms
4. **Streak System** - Consecutive flight tracking
5. **UI Framework** - Canvas-based displays
6. **Zone System** - Fly-through bonus zones
7. **Flight Tracking** - Real-time player monitoring

## 📊 Data Storage

The plugin uses JSON-based storage for:
- Player statistics and rankings
- Zone definitions and positions
- Configuration settings

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

## 📝 License

This project is open source. Please credit the original author when using or modifying.

## 🆘 Support

For issues or questions:
1. Check the configuration settings
2. Review server logs for errors
3. Test in a development environment first
4. Provide detailed bug reports with reproduction steps

---

**Transform your Rust server's minicopter gameplay into a competitive, skill-based racing experience!** 🏁