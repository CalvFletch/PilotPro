using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Network;

namespace Oxide.Plugins
{
    [Info("PilotPro", "Calvin Fletcher", "1.0.0")]
    [Description("Professional minicopter racing with advanced scoring, streaks, and zones")]
    public class MinicopterSkillRace : RustPlugin
    {
        #region Development Mode Constants
        
        // Development mode switches - set to true for development
        private const bool DEV_MODE_RESET_CONFIG = true;  // Rewrite default config on every boot
        private const bool DEV_MODE_RESET_PLAYER_DATA = false;  // Clear all player data on boot
        private const bool DEV_MODE_RESET_ZONE_DATA = false;  // Clear all zone data on boot
        
        #endregion

        #region Fields

        private Configuration config;
        private StoredData storedData;
        private ZoneData zoneData;
        private Dictionary<ulong, PlayerFlightData> activePlayers = new Dictionary<ulong, PlayerFlightData>();
        private Dictionary<NetworkableId, ulong> minicopterOwners = new Dictionary<NetworkableId, ulong>(); // minicopter netID -> player steamID
        private Dictionary<NetworkableId, float> minicopterLastOccupied = new Dictionary<NetworkableId, float>(); // minicopter netID -> last occupied time
        private Timer scoringTimer;
        private Timer cleanupTimer;
        private Timer zoneVisualsTimer;
        private Dictionary<ulong, List<string>> activePointDisplays = new Dictionary<ulong, List<string>>(); // player -> UI element IDs
        private Dictionary<ulong, float> lastZoneVisualsUpdate = new Dictionary<ulong, float>(); // player -> last update time
        private Dictionary<string, BaseEntity> zoneEntities = new Dictionary<string, BaseEntity>(); // zone ID -> entity
        private Dictionary<ulong, bool> activeStreakEndUIs = new Dictionary<ulong, bool>(); // player -> has active streak end UI
        private Dictionary<ulong, bool> activeDebugUIs = new Dictionary<ulong, bool>(); // player -> has active debug location UI

        private const string MinicopterPrefab = "assets/content/vehicles/minicopter/minicopter.entity.prefab";

        #endregion

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Max Point Distance From Ground")]
            public float MaxPointDistanceFromGround = 10f;

            [JsonProperty("Proximity Points Modifier")]
            public float ProximityPointsModifier = 0.1f;

            [JsonProperty("Speed Multiplier Base")]
            public float SpeedMultiplierBase = 20f;

            [JsonProperty("Streak Start Speed Threshold")]
            public float StreakStartSpeedThreshold = 10f; // 10 m/s minimum speed

            [JsonProperty("Streak Start Duration Threshold")]
            public float StreakStartDurationThreshold = 10f; // Must maintain conditions for 10 seconds to start streak

            [JsonProperty("Streak Initial Multiplier")]
            public float StreakInitialMultiplier = 1.1f; // Start at 1.1x when streak begins

            [JsonProperty("Streak Major Milestone Duration")]
            public float StreakMajorMilestoneDuration = 50f; // At 50 seconds of streak time (60s total), hit 2x multiplier

            [JsonProperty("Streak Major Milestone Multiplier")]
            public float StreakMajorMilestoneMultiplier = 2.0f; // 2x multiplier at major milestone

            [JsonProperty("Super Streak Interval")]
            public float SuperStreakInterval = 30f; // Every 30 seconds after 2.5x milestone

            [JsonProperty("Super Streak Increment")]
            public float SuperStreakIncrement = 0.1f; // Add 0.1x every interval

            [JsonProperty("Super Streak Max Multiplier")]
            public float SuperStreakMaxMultiplier = 3.0f; // Cap at 3x (super streak)

            [JsonProperty("Streak End UI Duration")]
            public float StreakEndUIDuration = 3f; // How long streak end card is displayed (also sets crash cooldown)

            [JsonProperty("Leaderboard Size")]
            public int LeaderboardSize = 10;

            [JsonProperty("Scoring Interval")]
            public float ScoringInterval = 0.5f;

            [JsonProperty("Damage Multiplier")]
            public float DamageMultiplier = 1f;

            [JsonProperty("Location Multipliers")]
            public Dictionary<string, float> BiomeMultipliers = new Dictionary<string, float>
            {
                // Hierarchy Level 1: Special Locations (highest priority)
                ["train_tunnels"] = 2.5f,
                
                // Hierarchy Level 2: Water Areas (high priority, definitive)
                ["ocean"] = 1.0f,
                
                // Hierarchy Level 3: Infrastructure & Geography (medium priority)
                ["monuments"] = 2.0f,
                ["roads"] = 1.2f,
                ["river"] = 1.1f,
                ["forest"] = 2.0f,
                
                // Hierarchy Level 3: Biomes (lower priority)
                ["arid"] = 1.0f,
                ["temperate"] = 1.1f,
                ["tundra"] = 1.2f,
                ["arctic"] = 1.3f,
                ["swamp"] = 1.4f,
                ["alpine"] = 1.5f,
                ["jungle"] = 1.6f,
                ["grassland"] = 1.0f,
                ["deciduous"] = 1.1f,
                ["coniferous"] = 1.2f,
                ["river"] = 1.2f,
                ["lake"] = 1.2f,
                ["water"] = 1.2f,
                ["default"] = 1.0f
            };

            [JsonProperty("Water Height Bonus")]
            public float WaterHeightBonus = 0.5f;

            [JsonProperty("Train Tunnel Y Threshold")]
            public float TrainTunnelYThreshold = -25f;

            [JsonProperty("Flip Detection")]
            public FlipDetectionConfig FlipDetection = new FlipDetectionConfig();

            [JsonProperty("Zone System")]
            public ZoneConfig ZoneSystem = new ZoneConfig();

            [JsonProperty("Feature Toggles")]
            public FeatureToggles Features = new FeatureToggles();

            [JsonProperty("Point Display")]
            public PointDisplayConfig PointDisplay = new PointDisplayConfig();

            [JsonProperty("Log Level")]
            public int LogLevel = 2;
        }

        private class FlipDetectionConfig
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true; // Enable/disable flip detection entirely

            [JsonProperty("Base Flip Points")]
            public float BaseFlipPoints = 250f; // Base points for flip at 100m+

            [JsonProperty("Max Risk Multiplier")]
            public float MaxRiskMultiplier = 10f; // Max multiplier for ground-level flips

            [JsonProperty("Recovery Confirmation Time")]
            public float RecoveryConfirmationTime = 3f; // Must stay upright for X seconds

            [JsonProperty("Upside Down Threshold")]
            public float UpsideDownThreshold = -0.5f; // transform.up.y threshold for upside down

            [JsonProperty("Max Altitude For Risk Bonus")]
            public float MaxAltitudeForRiskBonus = 100f; // Above this altitude = base points only

            [JsonProperty("Min Altitude For Flip")]
            public float MinAltitudeForFlip = 2f; // Must be at least this high to attempt flip

            [JsonProperty("Risk Multiplier Curve")]
            public string RiskMultiplierCurve = "linear"; // "linear" or "exponential"

            [JsonProperty("Exponential Risk Factor")]
            public float ExponentialRiskFactor = 2f; // Used if curve is exponential

            [JsonProperty("Show Flip Debug Messages")]
            public bool ShowFlipDebugMessages = true; // Show flip status messages in chat

            [JsonProperty("Award Points During Streak Only")]
            public bool AwardPointsDuringStreakOnly = false; // Only award flip points if streak is active

            [JsonProperty("Reset Flip On Damage")]
            public bool ResetFlipOnDamage = true; // Reset flip progress if minicopter takes damage

            [JsonProperty("Min Speed For Flip")]
            public float MinSpeedForFlip = 5f; // Minimum speed required to start/continue flip
        }

        private class ZoneConfig
        {
            [JsonProperty("Zone Detection Enabled")]
            public bool ZoneDetectionEnabled = true;

            [JsonProperty("Zone Bonus Multiplier")]
            public float ZoneBonusMultiplier = 1.5f; // Base multiplier for flying through zones

            [JsonProperty("Zone Difficulty Multipliers")]
            public Dictionary<int, float> ZoneDifficultyMultipliers = new Dictionary<int, float>
            {
                [1] = 1.0f,  // Easy
                [2] = 1.5f,  // Medium
                [3] = 2.0f,  // Hard
                [4] = 3.0f,  // Expert
                [5] = 5.0f   // Insane
            };

            [JsonProperty("Zone Cooldown Time")]
            public float ZoneCooldownTime = 30f; // Seconds before same zone can be triggered again

            [JsonProperty("Min Speed For Zone")]
            public float MinSpeedForZone = 10f; // Minimum speed to trigger zones

            [JsonProperty("Show Zone Messages")]
            public bool ShowZoneMessages = true;

            [JsonProperty("Show Zone Visuals")]
            public bool ShowZoneVisuals = true;

            [JsonProperty("Zone Visual Update Interval")]
            public float ZoneVisualUpdateInterval = 1f; // How often to update zone visuals for players
        }

        private class FeatureToggles
        {
            [JsonProperty("Enable Damage Penalties")]
            public bool EnableDamagePenalties = true;

            [JsonProperty("Enable Streak System")]
            public bool EnableStreakSystem = true;

            [JsonProperty("Enable Biome Multipliers")]
            public bool EnableBiomeMultipliers = true;

            [JsonProperty("Enable Real-time Scoring")]
            public bool EnableRealtimeScoring = true;

            [JsonProperty("Enable Leaderboard UI")]
            public bool EnableLeaderboardUI = true;

            [JsonProperty("Enable Flight Data UI")]
            public bool EnableFlightDataUI = true;

            [JsonProperty("Enable Auto Cleanup")]
            public bool EnableAutoCleanup = true;

            [JsonProperty("Enable Infinite Fuel")]
            public bool EnableInfiniteFuel = true;

            [JsonProperty("Enable Health Regeneration")]
            public bool EnableHealthRegeneration = true;

            [JsonProperty("Enable Bullet Damage Immunity")]
            public bool EnableBulletDamageImmunity = true;

            [JsonProperty("Enable Train Tunnel Detection")]
            public bool EnableTrainTunnelDetection = true;

            [JsonProperty("Enable Minicopter Ownership")]
            public bool EnableMinicopterOwnership = true;
        }

        private class PointDisplayConfig
        {
            [JsonProperty("Show Point Changes")]
            public bool ShowPointChanges = true;

            [JsonProperty("Display Duration")]
            public float DisplayDuration = 3f;

            [JsonProperty("Font Size")]
            public int FontSize = 16;

            [JsonProperty("Show Damage Points")]
            public bool ShowDamagePoints = true;

            [JsonProperty("Show Flip Points")]
            public bool ShowFlipPoints = true;

            [JsonProperty("Show Zone Points")]
            public bool ShowZonePoints = true;

            [JsonProperty("Show Streak Points")]
            public bool ShowStreakPoints = true;

            [JsonProperty("Max Concurrent Displays")]
            public int MaxConcurrentDisplays = 5;
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }
            }
            catch
            {
                LogWarning("Configuration file is corrupt, using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        #endregion

        #region Data Classes

        private class StoredData
        {
            public Dictionary<string, PlayerData> Players = new Dictionary<string, PlayerData>();
        }

        private class PlayerData
        {
            public string SteamID;
            public string DisplayName;
            public float TotalScore;
            public float HighestScore;
            public float BestStreakPoints; // Changed from LongestStreak to track points instead
            public DateTime LastPlayed;

            // Backward compatibility for old data files
            [JsonProperty("LongestStreak")]
            private float _longestStreak
            {
                get { return 0f; } // Always return 0 for old field
                set 
                { 
                    // If we're loading old data and BestStreakPoints is 0, ignore the old value
                    // We'll start fresh with the new points-based system
                }
            }

            public PlayerData() { }

            public PlayerData(string steamId, string displayName)
            {
                SteamID = steamId;
                DisplayName = displayName;
                TotalScore = 0f;
                HighestScore = 0f;
                BestStreakPoints = 0f; // Changed from LongestStreak
                LastPlayed = DateTime.Now;
            }
        }

        private class ZoneData
        {
            public Dictionary<string, FlyThroughZone> Zones = new Dictionary<string, FlyThroughZone>();
        }

        private class FlyThroughZone
        {
            public string Id;
            public string Name;
            public Vector3 Position;
            public float Size;
            public int Difficulty; // 1-5
            public string CreatedBy;
            public DateTime CreatedAt;

            public FlyThroughZone() { }

            public FlyThroughZone(string id, string name, Vector3 position, float size, int difficulty, string createdBy)
            {
                Id = id;
                Name = name;
                Position = position;
                Size = size;
                Difficulty = difficulty;
                CreatedBy = createdBy;
                CreatedAt = DateTime.Now;
            }
        }

        private class PlayerFlightData
        {
            public BasePlayer Player;
            public Minicopter Minicopter;
            public float CurrentFlightScore;
            public float StreakStartTime;
            public bool IsStreakActive;
            public Vector3 LastPosition;

            // Flip detection fields
            public bool IsUpsideDown;
            public float UpsideDownStartTime;
            public bool FlipInProgress;
            public float FlipStartTime;
            public float ClosestGroundDistanceDuringFlip;
            public bool RecoveryInProgress;
            public float RecoveryStartTime;

            // Streak score tracking
            public float StreakScoreTotal;
            public float LastStreakScore; // Store the score of the last completed streak
            
            // Streak qualification tracking
            public bool IsQualifyingForStreak; // Currently meeting streak conditions but not yet started
            public float StreakQualificationStartTime; // When we started meeting conditions

            // Zone tracking
            public Dictionary<string, float> ZoneCooldowns;
            public string LastZoneTriggered;
            public float LastZoneTime;

            // Streak UI tracking
            public string StreakUIElementId;
            public float LastUIUpdate;

            // High altitude tracking for streak leniency
            public bool IsHighAltitude; // Currently above 10m
            public float HighAltitudeStartTime; // When high altitude period started

            // GET LOW countdown UI tracking
            public string GetLowUIElementId;
            
            // Speed forgiveness tracking
            public bool IsInSpeedForgiveness; // Currently too slow but within forgiveness period
            public float SpeedForgivenessStartTime; // When speed forgiveness started
            public string SpeedUpUIElementId; // UI element for speed forgiveness countdown

            // Penalty tracking to prevent double application
            public bool PenaltyApplied;

            // Debug tracking for streak lifecycle
            public bool StreakEnded; // Flag to track if streak has already ended

            // Crash cooldown tracking
            public float LastCrashTime; // When the last crash occurred
            public bool RecentlyCrashed; // Flag to prevent immediate streak restart
            
            // Milestone tracking for streak multiplier notifications
            public float LastMultiplierMilestone; // Track the last milestone reached to prevent spam

            public PlayerFlightData(BasePlayer player, Minicopter minicopter)
            {
                Player = player;
                Minicopter = minicopter;
                CurrentFlightScore = 0f;
                StreakStartTime = 0f;
                IsStreakActive = false;
                LastPosition = player.transform.position;

                // Initialize flip detection
                IsUpsideDown = false;
                UpsideDownStartTime = 0f;
                FlipInProgress = false;
                FlipStartTime = 0f;
                ClosestGroundDistanceDuringFlip = float.MaxValue;
                RecoveryInProgress = false;
                RecoveryStartTime = 0f;

                // Initialize streak score tracking
                StreakScoreTotal = 0f;
                
                // Initialize streak qualification tracking
                IsQualifyingForStreak = false;
                StreakQualificationStartTime = 0f;

                // Initialize zone tracking
                ZoneCooldowns = new Dictionary<string, float>();
                LastZoneTriggered = null;
                LastZoneTime = 0f;

                // Initialize streak UI tracking
                StreakUIElementId = null;
                LastUIUpdate = 0f;
                IsHighAltitude = false;
                HighAltitudeStartTime = 0f;
                GetLowUIElementId = null;
                
                // Initialize speed forgiveness tracking
                IsInSpeedForgiveness = false;
                SpeedForgivenessStartTime = 0f;
                SpeedUpUIElementId = null;
                
                PenaltyApplied = false;

                // Initialize debug tracking
                StreakEnded = false;

                // Initialize crash cooldown
                LastCrashTime = 0f;
                RecentlyCrashed = false;
                
                // Initialize milestone tracking
                LastMultiplierMilestone = 1.0f;
            }
        }

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            // Development Mode: Reset data if flags are enabled
            if (DEV_MODE_RESET_CONFIG)
            {
                LogWarning("DEV MODE: Resetting config to defaults");
                LoadDefaultConfig();
                SaveConfig();
            }

            if (DEV_MODE_RESET_PLAYER_DATA)
            {
                LogWarning("DEV MODE: Clearing all player data");
                storedData = new StoredData();
                SaveData();
            }
            else
            {
                LoadData();
            }

            if (DEV_MODE_RESET_ZONE_DATA)
            {
                LogWarning("DEV MODE: Clearing all zone data");
                zoneData = new ZoneData();
                SaveZoneData();
            }
            else
            {
                LoadZoneData();
            }

            // Clear old UI elements for all connected players on plugin load
            timer.Once(1f, () => ClearAllPlayersUI());

            // Start the scoring timer
            scoringTimer = timer.Every(config.ScoringInterval, UpdateScoring);

            // Start cleanup timer for abandoned minicopters (check every 30 seconds)
            if (config.Features.EnableAutoCleanup)
            {
                cleanupTimer = timer.Every(30f, () => CleanupAbandonedMinicopters());
            }

            // Start zone visuals timer
            if (config.ZoneSystem.ShowZoneVisuals)
            {
                zoneVisualsTimer = timer.Every(config.ZoneSystem.ZoneVisualUpdateInterval, () => UpdateZoneVisualsForAllPlayers());
            }

            // Register permissions
            permission.RegisterPermission("minicopterskillrace.admin", this);

            Log("Minicopter plugin initialized");
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            // Show leaderboard UI after a short delay to ensure player is fully loaded
            if (config.Features.EnableLeaderboardUI)
            {
                timer.Once(2f, () =>
                {
                    if (player != null && player.IsConnected)
                    {
                        CreateLeaderboardUI(player);
                    }
                });
            }

            // Create flight data UI if enabled
            if (config.Features.EnableFlightDataUI && activePlayers.ContainsKey(player.userID))
            {
                CreateFlightInfoUI(player, activePlayers[player.userID]);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            // Clean up if player was being tracked
            if (activePlayers.ContainsKey(player.userID))
            {
                StopTracking(player);
            }

            // Destroy flight data UI if enabled
            if (config.Features.EnableFlightDataUI)
            {
                DestroyFlightInfoUI(player);
            }

            // Destroy debug location UI if enabled
            if (activeDebugUIs.ContainsKey(player.userID) && activeDebugUIs[player.userID])
            {
                CuiHelper.DestroyUi(player, "MinicopterDebugLocation");
                activeDebugUIs[player.userID] = false;
            }
        }

        private void Unload()
        {
            SaveData();
            scoringTimer?.Destroy();

            // Clean up active players
            activePlayers.Clear();
        }

        private void OnEntityMounted(BaseMountable mountable, BasePlayer player)
        {
            if (mountable?.GetParentEntity() is Minicopter minicopter)
            {
                // Transfer ownership to the new pilot
                minicopterOwners[minicopter.net.ID] = player.userID;
                // Update last occupied time
                minicopterLastOccupied[minicopter.net.ID] = Time.time;
                StartTracking(player, minicopter);
            }
        }

        private object OnFuelConsume(BaseOven oven, Item fuel, ItemModBurnable burnable)
        {
            // Check if this fuel consumption is from a minicopter
            var minicopter = oven.GetComponentInParent<Minicopter>();
            if (minicopter != null)
            {
                return true; // Block fuel consumption for minicopters
            }
            return null;
        }

        // Alternative approach - set fuel consumption to 0
        private void OnEntitySpawned(BaseNetworkable entity)
        {
            if (entity is Minicopter minicopter)
            {
                NextTick(() =>
                {
                    if (minicopter != null && !minicopter.IsDestroyed)
                    {
                        minicopter.fuelPerSec = 0f; // Set fuel consumption to 0
                    }
                });
            }
        }

        private void OnEntityDismounted(BaseMountable mountable, BasePlayer player)
        {
            if (mountable?.GetParentEntity() is Minicopter minicopter)
            {
                // Ownership system:
                // - Ownership persists for 30 seconds after dismount to allow quick re-mounting
                // - After 30 seconds, ownership can be claimed by anyone who mounts
                // - Minicopter is destroyed after 2 minutes of complete abandonment (no driver/passenger)
                
                // Update last occupied time when dismounting (used for abandonment cleanup)
                minicopterLastOccupied[minicopter.net.ID] = Time.time;
                
                // Schedule ownership expiration after 30 seconds if not re-mounted
                timer.Once(30f, () =>
                {
                    // Check if the minicopter still exists and if the original owner is still the owner
                    if (minicopter != null && !minicopter.IsDestroyed && 
                        minicopterOwners.TryGetValue(minicopter.net.ID, out ulong currentOwner) && 
                        currentOwner == player.userID)
                    {
                        // Check if someone else has mounted it during the 30 seconds
                        bool hasDriver = minicopter.GetDriver() != null;
                        bool hasPassenger = minicopter.GetPassenger() != null;
                        
                        if (!hasDriver && !hasPassenger)
                        {
                            // No one is in the minicopter after 30 seconds, clear ownership
                            minicopterOwners.Remove(minicopter.net.ID);
                            Log($"Cleared ownership of minicopter {minicopter.net.ID} after 30 seconds of abandonment");
                        }
                        // If someone is in it, they would have triggered OnEntityMounted and claimed ownership
                    }
                });
                
                // Stop tracking the player's flight data
                // Note: Exiting minicopter no longer ends streaks automatically
                // Players must maintain their streak through skill, not by strategic exits
                StopTracking(player);
            }
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            if (entity is Minicopter minicopter && hitInfo != null)
            {
                // Block bullet/projectile damage to minicopters
                if (IsBulletDamage(hitInfo))
                {
                    hitInfo.damageTypes.ScaleAll(0f); // Nullify all damage
                    return;
                }

                // Check if this minicopter has an owner
                if (minicopterOwners.ContainsKey(minicopter.net.ID))
                {
                    var ownerSteamId = minicopterOwners[minicopter.net.ID];
                    var ownerSteamIdString = ownerSteamId.ToString();

                    // Apply damage penalty based on config
                    if (config.Features.EnableDamagePenalties && config.DamageMultiplier > 0)
                    {
                        float damageAmount = hitInfo.damageTypes.Total();
                        float penaltyPoints = damageAmount * config.DamageMultiplier;

                        // Check if in train tunnel for extra penalty
                        bool isInTrainTunnel = config.Features.EnableTrainTunnelDetection && minicopter.transform.position.y < config.TrainTunnelYThreshold;
                        if (isInTrainTunnel)
                        {
                            penaltyPoints *= 2f; // Double penalty in train tunnels
                        }

                        if (storedData.Players.ContainsKey(ownerSteamIdString))
                        {
                            storedData.Players[ownerSteamIdString].TotalScore -= penaltyPoints;

                            // Find the player if they're online
                            var owner = BasePlayer.FindByID(ownerSteamId);
                            if (owner != null)
                            {
                                // Show floating damage points
                                if (config.PointDisplay.ShowDamagePoints)
                                {
                                    ShowFloatingPoints(owner, penaltyPoints, "damage");
                                }

                                string locationText = isInTrainTunnel ? " (TRAIN TUNNEL 2x)" : "";
                                string multiplierText = "";

                                if (isInTrainTunnel)
                                {
                                    float totalMultiplier = config.DamageMultiplier * 2f;
                                    multiplierText = $" x{totalMultiplier}";
                                }
                                else if (config.DamageMultiplier != 1f)
                                {
                                    multiplierText = $" x{config.DamageMultiplier}";
                                }

                                owner.ChatMessage($"<color=red>DAMAGE PENALTY: -{penaltyPoints:F0} points ({damageAmount:F0} damage{multiplierText}){locationText} | New Total: {storedData.Players[ownerSteamIdString].TotalScore:F0}</color>");
                            }

                            SaveData();
                            if (config.Features.EnableLeaderboardUI)
                            {
                                UpdateAllLeaderboards();
                            }
                        }
                    }

                    // Reset flip detection if configured to do so
                    if (config.FlipDetection.ResetFlipOnDamage && activePlayers.ContainsKey(ownerSteamId))
                    {
                        var flightData = activePlayers[ownerSteamId];
                        if (flightData.FlipInProgress)
                        {
                            ResetFlipDetection(flightData);

                            var owner = BasePlayer.FindByID(ownerSteamId);
                            if (owner != null && config.FlipDetection.ShowFlipDebugMessages)
                            {
                                PrintToChat(owner, "[Flip] Reset due to damage");
                            }
                        }
                    }

                    // Reset streak on any damage to minicopter
                    if (config.Features.EnableStreakSystem && activePlayers.ContainsKey(ownerSteamId))
                    {
                        var flightData = activePlayers[ownerSteamId];
                        if (flightData.IsStreakActive)
                        {
                            // Reset streak with damage reason (this will show the red damage UI)
                            ResetStreak(flightData, "damage");
                        }
                    }
                }
            }
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity is Minicopter minicopter)
            {
                // Apply explosion penalty to owner if exists
                if (minicopterOwners.ContainsKey(minicopter.net.ID))
                {
                    var ownerSteamId = minicopterOwners[minicopter.net.ID];
                    var ownerSteamIdString = ownerSteamId.ToString();

                    if (storedData.Players.ContainsKey(ownerSteamIdString))
                    {
                        storedData.Players[ownerSteamIdString].TotalScore -= 500f;

                        // Notify owner if online
                        var owner = BasePlayer.FindByID(ownerSteamId);
                        if (owner != null)
                        {
                            owner.ChatMessage($"<color=red>YOUR MINICOPTER DESTROYED: -500 points! New total: {storedData.Players[ownerSteamIdString].TotalScore:F0}</color>");
                        }

                        SaveData();
                        UpdateAllLeaderboards();
                    }
                }

                // Clean up tracking dictionaries
                minicopterOwners.Remove(minicopter.net.ID);
                minicopterLastOccupied.Remove(minicopter.net.ID);

                // Stop tracking if player is currently in this minicopter
                var playerData = activePlayers.Values.FirstOrDefault(p => p.Minicopter == minicopter);
                if (playerData != null)
                {
                    // End streak with destruction reason if active
                    if (playerData.IsStreakActive)
                    {
                        ResetStreak(playerData, "DESTROYED");
                    }
                    StopTracking(playerData.Player);
                }
            }
        }

        private bool IsCollisionDamage(HitInfo hitInfo)
        {
            // Check if damage is from collision (impact)
            return hitInfo.damageTypes.Has(Rust.DamageType.Collision) ||
                   hitInfo.damageTypes.Has(Rust.DamageType.Fall) ||
                   (hitInfo.Initiator == null && hitInfo.WeaponPrefab == null);
        }

        private bool IsBulletDamage(HitInfo hitInfo)
        {
            // Check if damage is from bullets/projectiles
            return hitInfo.damageTypes.Has(Rust.DamageType.Bullet) ||
                   hitInfo.damageTypes.Has(Rust.DamageType.Arrow) ||
                   hitInfo.damageTypes.Has(Rust.DamageType.Slash) ||
                   hitInfo.damageTypes.Has(Rust.DamageType.Stab) ||
                   hitInfo.damageTypes.Has(Rust.DamageType.Blunt) ||
                   (hitInfo.Initiator is BasePlayer) || // Player-initiated damage
                   (hitInfo.WeaponPrefab != null && hitInfo.WeaponPrefab.ShortPrefabName.Contains("projectile"));
        }

        private void RegenerateMinicopterHealth(Minicopter minicopter, float biomeMultiplier, float speed)
        {
            if (minicopter == null || minicopter.IsDestroyed) return;

            // Regenerate health scaled by biome and speed multipliers (base 10 HP per second)
            // Formula: (speed * 0.1) * baseHealthPerSecond * biomeMultiplier
            float baseHealthPerSecond = 10f;
            float speedMultiplier = speed * 0.1f;
            float healthToAdd = speedMultiplier * baseHealthPerSecond * biomeMultiplier * config.ScoringInterval;

            float currentHealth = minicopter.Health();
            float maxHealth = minicopter.MaxHealth();

            if (currentHealth < maxHealth)
            {
                float newHealth = Mathf.Min(currentHealth + healthToAdd, maxHealth);
                minicopter.SetHealth(newHealth);
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if (entity is Minicopter minicopter)
            {
                // Find and stop tracking any player in this minicopter
                var playerData = activePlayers.Values.FirstOrDefault(p => p.Minicopter == minicopter);
                if (playerData != null)
                {
                    StopTracking(playerData.Player);
                }
            }
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            // Prevent players from accessing minicopter fuel storage
            var minicopter = container.GetComponentInParent<Minicopter>();
            if (minicopter != null)
            {
                player.ChatMessage("<color=red>Fuel access is disabled for minicopters in this race!</color>");
                return false; // Block the loot attempt
            }
            
            return null; // Allow other containers
        }

        #endregion

        #region Helper Methods

        private void Log(string message)
        {
            if (config.LogLevel > 0)
            {
                Puts(message);
            }
        }

        private void LogWarning(string message)
        {
            PrintWarning(message);
        }

        private void LoadData()
        {
            try
            {
                storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to load data file: {ex.Message}");
                storedData = new StoredData();
            }

            if (storedData?.Players == null)
            {
                storedData = new StoredData();
            }
        }

        private void SaveData()
        {
            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to save data file: {ex.Message}");
            }
        }

        private void LoadZoneData()
        {
            try
            {
                zoneData = Interface.Oxide.DataFileSystem.ReadObject<ZoneData>(Name + "_zones");
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to load zones file: {ex.Message}");
                zoneData = new ZoneData();
            }

            if (zoneData?.Zones == null)
            {
                zoneData = new ZoneData();
            }
        }

        private void SaveZoneData()
        {
            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(Name + "_zones", zoneData);
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to save zones file: {ex.Message}");
            }
        }

        private void StartTracking(BasePlayer player, Minicopter minicopter)
        {
            if (player == null || minicopter == null) return;

            var flightData = new PlayerFlightData(player, minicopter);
            activePlayers[player.userID] = flightData;

            // Initialize player data if not exists
            var steamId = player.UserIDString;
            if (!storedData.Players.ContainsKey(steamId))
            {
                storedData.Players[steamId] = new PlayerData(steamId, player.displayName);
            }
        }

        private void StopTracking(BasePlayer player)
        {
            if (player == null) return;

            if (activePlayers.TryGetValue(player.userID, out PlayerFlightData flightData))
            {
                // Clean up UIs
                if (config.Features.EnableFlightDataUI)
                {
                    DestroyFlightInfoUI(player);
                }
                CleanupPlayerPointDisplays(player);
                DestroyZoneVisualsForPlayer(player);
                DestroyPersistentStreakUI(player, flightData);
                DestroyGetLowCountdownUI(player, flightData);
                DestroySpeedUpCountdownUI(player, flightData);
                FinalizeFlightScore(flightData);
                activePlayers.Remove(player.userID);
            }
        }

        private void FinalizeFlightScore(PlayerFlightData flightData)
        {
            var steamId = flightData.Player.UserIDString;
            if (storedData.Players.TryGetValue(steamId, out PlayerData playerData))
            {
                // Update highest score if this flight was better
                if (flightData.CurrentFlightScore > playerData.HighestScore)
                {
                    playerData.HighestScore = flightData.CurrentFlightScore;
                }

                playerData.LastPlayed = DateTime.Now;
                playerData.DisplayName = flightData.Player.displayName;

                SaveData();

            }
        }

        private void ResetStreak(PlayerFlightData flightData, string customReason = null)
        {
            if (!flightData.IsStreakActive)
            {
                // Streak is not active, don't process penalty
                return;
            }

            if (flightData.StreakEnded)
            {
                // Streak has already been ended, don't process again
                return;
            }

            // Mark streak as ended immediately to prevent double processing
            flightData.StreakEnded = true;

            if (flightData.IsStreakActive)
            {
                // Calculate streak duration
                float streakDuration = Time.time - flightData.StreakStartTime;
                
                // FINALIZE STREAK SCORE - this is the final, immutable score for this streak
                float finalStreakScore = flightData.StreakScoreTotal;
                float originalPoints = finalStreakScore; // Original points before any penalty
                float crashPenalty = 0f;
                float finalNetPoints = finalStreakScore; // Net points after penalty (starts as full points)
                
                // Store the finalized streak score for records
                flightData.LastStreakScore = finalStreakScore;
                
                // Check if streak ended due to damage or destruction and calculate penalty
                bool isDamageEnding = customReason != null && customReason.ToLower() == "damage";
                bool isDestroyedEnding = customReason != null && customReason.ToLower() == "destroyed";
                
                if ((isDamageEnding || isDestroyedEnding) && finalStreakScore > 0f && !flightData.PenaltyApplied)
                {
                    // Mark penalty as applied to prevent double application
                    flightData.PenaltyApplied = true;
                    
                    // Set crash cooldown to prevent immediate streak restart
                    flightData.LastCrashTime = Time.time;
                    flightData.RecentlyCrashed = true;
                    
                    // Calculate penalty based on ending type
                    float penaltyRate = isDamageEnding ? 0.5f : 0.75f; // 50% for damage, 75% for destruction
                    crashPenalty = finalStreakScore * penaltyRate;
                    finalNetPoints = finalStreakScore - crashPenalty; // Final net points after penalty
                    
                    // Debug output
                    
                    // Apply penalty to player's total score
                    var steamId = flightData.Player.UserIDString;
                    if (storedData.Players.TryGetValue(steamId, out PlayerData playerData))
                    {
                        playerData.TotalScore -= crashPenalty;
                        
                        // Show crash penalty notification with specific rates
                        string penaltyType = isDamageEnding ? "DAMAGE" : "DESTRUCTION";
                        int penaltyPercent = isDamageEnding ? 50 : 75;
                        flightData.Player.ChatMessage($"<color=red>{penaltyType} PENALTY: -{crashPenalty:F0} points ({penaltyPercent}% of streak earnings lost!)</color>");
                        
                        // Show animated point display for the penalty
                        if (config.PointDisplay.ShowDamagePoints)
                        {
                            ShowFloatingPoints(flightData.Player, -crashPenalty, "damage");
                        }
                        
                        SaveData();
                        UpdateAllLeaderboards();
                    }
                }
                else if ((isDamageEnding || isDestroyedEnding) && flightData.PenaltyApplied)
                {
                    // Penalty was already applied, calculate what it would have been for display
                    float penaltyRate = isDamageEnding ? 0.5f : 0.75f;
                    crashPenalty = finalStreakScore * penaltyRate;
                    finalNetPoints = finalStreakScore - crashPenalty;
                }
                
                // Update player's best streak points record using FINAL NET points (after penalty)
                var steamId2 = flightData.Player.UserIDString;
                if (storedData.Players.TryGetValue(steamId2, out PlayerData playerData2))
                {
                    if (finalNetPoints > playerData2.BestStreakPoints)
                    {
                        playerData2.BestStreakPoints = finalNetPoints;
                        SaveData(); // Save the new record
                        
                        // Notify player of new record (show final net points after penalty)
                        flightData.Player.ChatMessage($"<color=#00ff00>NEW STREAK RECORD!</color> {finalNetPoints:F0} points");
                        
                        // Show floating point notification for high score
                        if (config.PointDisplay.ShowStreakPoints)
                        {
                            ShowFloatingPoints(flightData.Player, 0f, "major_milestone", "HIGH SCORE!");
                        }
                    }
                }

                // Show comprehensive streak end summary with finalized numbers
                ShowStreakEndInPersistentUI(flightData, streakDuration, customReason, finalNetPoints, originalPoints, crashPenalty);
                
                // Reset streak data - IMPORTANT: Do this AFTER showing UI to prevent double processing
                flightData.IsStreakActive = false;
                flightData.StreakStartTime = 0f;
                flightData.StreakScoreTotal = 0f;
                flightData.PenaltyApplied = false; // Reset penalty flag for next streak
                flightData.LastMultiplierMilestone = 1.0f; // Reset milestone tracking
                
                // Reset qualification tracking
                flightData.IsQualifyingForStreak = false;
                flightData.StreakQualificationStartTime = 0f;
                
                // Reset high altitude tracking
                flightData.IsHighAltitude = false;
                flightData.HighAltitudeStartTime = 0f;
                
                // Reset speed forgiveness tracking
                flightData.IsInSpeedForgiveness = false;
                flightData.SpeedForgivenessStartTime = 0f;
                
                // Destroy persistent streak UI (will be replaced by end summary)
                DestroyPersistentStreakUI(flightData.Player, flightData);
                
                // Destroy GET LOW countdown UI
                DestroyGetLowCountdownUI(flightData.Player, flightData);
                
                // Destroy SPEED UP countdown UI
                DestroySpeedUpCountdownUI(flightData.Player, flightData);
            }
            else
            {
                // Streak is not active, don't process penalty
            }

            // Also reset flip detection when streak resets
            ResetFlipDetection(flightData);
        }

        private void CheckMultiplierMilestones(PlayerFlightData flightData, float currentMultiplier)
        {
            // Use raw streak duration for milestone checks (when multiplier is actually reached)
            float rawStreakDuration = Time.time - flightData.StreakStartTime;
            float displayDuration = rawStreakDuration + config.StreakStartDurationThreshold; // For display/logging only
            
            // Check for 2.0x milestone at exactly 60s raw streak time (when multiplier is actually reached)
            if (rawStreakDuration >= 60f && flightData.LastMultiplierMilestone < 2.0f)
            {
                flightData.LastMultiplierMilestone = 2.0f;
                
                // Show major milestone (no chat message, only floating points)
                if (config.PointDisplay.ShowStreakPoints)
                {
                    ShowFloatingPoints(flightData.Player, 0f, "major_milestone", "2.0x STREAK!");
                }
            }
            
            // Check for 2.5x milestone at exactly 210s raw streak time (when multiplier is actually reached)
            if (rawStreakDuration >= 210f && flightData.LastMultiplierMilestone < 2.5f)
            {
                flightData.LastMultiplierMilestone = 2.5f;
                
                // Show 2.5x milestone
                if (config.PointDisplay.ShowStreakPoints)
                {
                    ShowFloatingPoints(flightData.Player, 0f, "major_milestone", "2.5x STREAK!");
                }
            }
            
            // Check for 3.0x super streak milestone at exactly 360s raw streak time (when multiplier is actually reached)
            if (rawStreakDuration >= 360f && flightData.LastMultiplierMilestone < 3.0f)
            {
                flightData.LastMultiplierMilestone = 3.0f;
                
                // Show super streak milestone (no chat message, only floating points)
                if (config.PointDisplay.ShowStreakPoints)
                {
                    ShowFloatingPoints(flightData.Player, 0f, "super_streak", "3.0x SUPER STREAK!");
                }
            }
        }

        private void ShowStreakEndInPersistentUI(PlayerFlightData flightData, float streakDuration, string customReason = null, float netPointsGained = 0f, float originalPoints = 0f, float crashPenalty = 0f)
        {
            var player = flightData.Player;
            
            // Calculate final multiplier that was achieved using new system
            float finalMultiplier = GetStreakMultiplier(streakDuration);

            // Generate unique element ID
            string elementId = $"StreakEndUI_{player.userID}";

            // Mark that streak end UI is active for this player
            activeStreakEndUIs[player.userID] = true;

            // Destroy existing UI
            CuiHelper.DestroyUi(player, elementId);

            var elements = new CuiElementContainer();

            // Create streak end UI panel (same position and style as persistent streak UI) - smaller background only
            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.7" }, // Semi-transparent black background
                RectTransform = { AnchorMin = "0.35 0.12", AnchorMax = "0.65 0.20" } // Smaller panel to match active streak UI
            }, "Hud", elementId);

            // Format multiplier text
            string multiplierText;
            if (finalMultiplier % 1 == 0)
            {
                multiplierText = $"{finalMultiplier:F0}x";
            }
            else
            {
                multiplierText = $"{finalMultiplier:F1}x";
            }

            // All streak endings must have a reason - if missing, default to "UNKNOWN"
            string reasonText = "";
            if (!string.IsNullOrEmpty(customReason))
            {
                switch (customReason.ToLower())
                {
                    case "damage":
                        reasonText = "DAMAGE";
                        break;
                    case "speed":
                        reasonText = "TOO SLOW";
                        break;
                    case "altitude":
                        reasonText = "TOO HIGH";
                        break;
                    case "destroyed":
                        reasonText = "DESTROYED";
                        break;
                    default:
                        reasonText = customReason.ToUpper();
                        break;
                }
            }
            else
            {
                reasonText = "UNKNOWN";
            }
            
            // Streak end with reason - mixed colors: red reason, green details
            string endText = $"<color=#ff3333>STREAK ENDED - {reasonText}</color>\n";
            
            // Show points with penalty visualization if there was a penalty or if it's a damage/destruction ending
            Log($"UI Display - crashPenalty: {crashPenalty:F0}, originalPoints: {originalPoints:F0}, netPointsGained: {netPointsGained:F0}, reason: {customReason}");
            
            bool isDamageOrDestructionEnding = !string.IsNullOrEmpty(customReason) && 
                (customReason.ToLower() == "damage" || customReason.ToLower() == "destroyed");
            
            if (crashPenalty > 0f || isDamageOrDestructionEnding)
            {
                // If crashPenalty is 0 but it's a damage ending, calculate what the penalty should be for display
                if (crashPenalty == 0f && isDamageOrDestructionEnding && originalPoints > 0f)
                {
                    bool isDamage = customReason.ToLower() == "damage";
                    float penaltyRate = isDamage ? 0.5f : 0.75f;
                    crashPenalty = originalPoints * penaltyRate;
                    netPointsGained = originalPoints - crashPenalty;
                    Log($"Recalculated penalty for display: -{crashPenalty:F0}, net: {netPointsGained:F0}");
                }
                
                // Show original points with strikethrough and net points after penalty
                endText += $"<color=#33ff33>Duration: {streakDuration:F1}s | Points: </color>";
                endText += $"<color=#ff3333><s>{originalPoints:F0}</s></color> <color=#33ff33>{netPointsGained:F0}</color>";
            }
            else
            {
                // Normal ending, show the net points gained (should be same as original for non-penalty endings)
                endText += $"<color=#33ff33>Duration: {streakDuration:F1}s | Points Gained: {netPointsGained:F0}</color>";
            }

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = endText,
                    Color = "1 1 1 1", // White base color (colors handled by rich text)
                    FontSize = 16,
                    Align = TextAnchor.MiddleCenter
                },
                RectTransform = { AnchorMin = "0.02 0.1", AnchorMax = "0.98 0.9" }
            }, elementId);

            CuiHelper.AddUi(player, elements);

            // Auto-remove the end summary after configured duration
            timer.Once(config.StreakEndUIDuration, () =>
            {
                CuiHelper.DestroyUi(player, elementId);
                // Clear the streak end UI flag so new streak UI can appear
                activeStreakEndUIs[player.userID] = false;
                
                // Clear crash cooldown when UI disappears (allowing new streaks to start)
                if (activePlayers.ContainsKey(player.userID))
                {
                    var playerFlightData = activePlayers[player.userID];
                    playerFlightData.RecentlyCrashed = false;
                }
            });
        }

        private void ResetFlipDetection(PlayerFlightData flightData)
        {
            flightData.IsUpsideDown = false;
            flightData.UpsideDownStartTime = 0f;
            flightData.FlipInProgress = false;
            flightData.FlipStartTime = 0f;
            flightData.ClosestGroundDistanceDuringFlip = float.MaxValue;
            flightData.RecoveryInProgress = false;
            flightData.RecoveryStartTime = 0f;
        }

        private void UpdateScoring()
        {
            bool leaderboardNeedsUpdate = false;

            foreach (var kvp in activePlayers.ToList())
            {
                var flightData = kvp.Value;
                if (flightData?.Player == null || flightData.Minicopter == null || flightData.Minicopter.IsDestroyed)
                {
                    // Clean up UI when player stops being tracked
                    DestroyFlightInfoUI(flightData?.Player);
                    activePlayers.Remove(kvp.Key);
                    continue;
                }

                var prevScore = flightData.CurrentFlightScore;
                CalculateAndAddScore(flightData);

                // Regenerate minicopter health over time (scaled by biome and speed multipliers)
                var biomeMultiplier = GetBiomeMultiplier(flightData.Player.transform.position);
                var speed = flightData.Minicopter.GetComponent<Rigidbody>().velocity.magnitude;
                RegenerateMinicopterHealth(flightData.Minicopter, biomeMultiplier, speed);

                // Update flight info UI every tick for real-time data
                CreateFlightInfoUI(flightData.Player, flightData);

                // Update debug location UI if enabled for this player
                if (activeDebugUIs.ContainsKey(flightData.Player.userID) && activeDebugUIs[flightData.Player.userID])
                {
                    CreateDebugLocationUI(flightData.Player);
                }

                // Update leaderboard if score changed significantly
                if (flightData.CurrentFlightScore - prevScore > 10f)
                {
                    leaderboardNeedsUpdate = true;
                }
            }

            // Update leaderboards less frequently - only every 5 seconds or when significant changes occur
            if (leaderboardNeedsUpdate || (Time.time % 5f < config.ScoringInterval && Time.time % 5f >= 0f))
            {
                UpdateAllLeaderboards();
            }

            // Update debug UIs for all players who have it enabled (including those not in minicopters)
            foreach (var kvp in activeDebugUIs.ToList())
            {
                if (kvp.Value) // Debug UI is enabled for this player
                {
                    var player = BasePlayer.FindByID(kvp.Key);
                    if (player != null && player.IsConnected && !activePlayers.ContainsKey(kvp.Key))
                    {
                        // Player has debug UI enabled but isn't in a minicopter, update anyway
                        CreateDebugLocationUI(player);
                    }
                    else if (player == null || !player.IsConnected)
                    {
                        // Player disconnected, clean up
                        activeDebugUIs[kvp.Key] = false;
                    }
                }
            }
        }

        private float GetBiomeMultiplier(Vector3 position)
        {
            try
            {
                // HIERARCHY LEVEL 1: Special Locations (highest priority)
                // Train tunnels - using configurable threshold
                bool isTrainTunnel = position.y < config.TrainTunnelYThreshold;

                if (isTrainTunnel)
                {
                    return config.BiomeMultipliers.GetValueOrDefault("train_tunnels", 2.5f);
                }

                // HIERARCHY LEVEL 2: Water Areas (high priority, definitive)
                // Check topology for ocean - this overrides biomes when present
                if (TerrainMeta.TopologyMap != null)
                {
                    int topology = TerrainMeta.TopologyMap.GetTopology(position);
                    
                    // Add ocean topology check if we find the correct flag
                    // (this might need adjustment based on actual ocean topology values)
                }

                // Check if over water using our water detection
                if (IsPositionOverWater(position))
                {
                    return config.BiomeMultipliers.GetValueOrDefault("ocean", 1.0f);
                }

                // HIERARCHY LEVEL 2.5: Roads, Railways, and Monuments (medium priority)
                // Check topology for roads, railways, and monuments - these override biomes but not water areas
                if (TerrainMeta.TopologyMap != null)
                {
                    // Monument detection using proper topology check
                    bool isMonument = TerrainMeta.TopologyMap.GetTopology(position, TerrainTopology.MONUMENT);
                    if (isMonument)
                    {
                        return 2.0f; // 2x multiplier for monuments
                    }
                    
                    // Road detection using proper topology check
                    bool isRoad = TerrainMeta.TopologyMap.GetTopology(position, TerrainTopology.ROAD);
                    
                    if (isRoad)
                    {
                        return 1.2f; // Fixed 1.2x multiplier for roads
                    }
                    
                    // River detection - geographical feature that overrides biomes
                    bool isRiver = TerrainMeta.TopologyMap.GetTopology(position, TerrainTopology.RIVER);
                    if (isRiver)
                    {
                        return 1.1f; // 1.1x multiplier for rivers
                    }
                    
                    // Forest detection - terrain feature that overrides biomes
                    bool isForest = TerrainMeta.TopologyMap.GetTopology(position, TerrainTopology.FOREST);
                    if (isForest)
                    {
                        return 2.0f; // 2x multiplier for forests
                    }
                }

                // HIERARCHY LEVEL 3: Biomes (lowest priority)
                // Only check biomes if not in train tunnels or water areas
                float arid = TerrainMeta.BiomeMap.GetBiome(position, 1);
                float temperate = TerrainMeta.BiomeMap.GetBiome(position, 2);
                float tundra = TerrainMeta.BiomeMap.GetBiome(position, 4);
                float arctic = TerrainMeta.BiomeMap.GetBiome(position, 8);

                // Determine dominant biome (highest value)
                float maxBiome = Mathf.Max(arid, temperate, tundra, arctic);

                if (maxBiome <= 0.1f)
                {
                    return config.BiomeMultipliers.GetValueOrDefault("temperate", 1.1f);
                }

                if (arid == maxBiome && arid > 0.5f)
                {
                    return config.BiomeMultipliers.GetValueOrDefault("arid", 1.0f);
                }
                else if (temperate == maxBiome)
                {
                    // Check specific topology features
                    bool isSwamp = TerrainMeta.TopologyMap.GetTopology(position, TerrainTopology.SWAMP);

                    if (isSwamp)
                    {
                        return config.BiomeMultipliers.GetValueOrDefault("swamp", 1.4f);
                    }
                    
                    return config.BiomeMultipliers.GetValueOrDefault("temperate", 1.1f);
                }
                else if (tundra == maxBiome || arctic == maxBiome)
                {
                    // Check if it's alpine (mountainous)
                    bool isAlpine = TerrainMeta.TopologyMap.GetTopology(position, TerrainTopology.SUMMIT);
                    if (isAlpine)
                    {
                        return config.BiomeMultipliers.GetValueOrDefault("alpine", 1.5f);
                    }

                    return arctic == maxBiome ?
                        config.BiomeMultipliers.GetValueOrDefault("arctic", 1.3f) :
                        config.BiomeMultipliers.GetValueOrDefault("tundra", 1.2f);
                }

                return config.BiomeMultipliers.GetValueOrDefault("default", 1.0f);
            }
            catch (Exception ex)
            {
                LogWarning($"Error getting biome multiplier: {ex.Message}");
                return 1.0f;
            }
        }

        private float GetDistanceToGround(Vector3 position)
        {
            // Find the player who is flying this minicopter
            var playerData = activePlayers.Values.FirstOrDefault(p => p.Minicopter != null && Vector3.Distance(p.Minicopter.transform.position, position) < 1f);
            
            Vector3 castFromPosition;
            if (playerData?.Player != null)
            {
                // Cast from player's position (torso area)
                castFromPosition = playerData.Player.transform.position;
            }
            else
            {
                // Fallback to minicopter position if no player found
                castFromPosition = position + Vector3.up * 0.5f;
            }
            
            // Get terrain distance via raycast
            RaycastHit hit;
            float distanceToTerrain = float.MaxValue;
            
            if (Physics.Raycast(castFromPosition, Vector3.down, out hit, 200f))
            {
                distanceToTerrain = hit.distance - 0.2f;
            }
            
            // Check if we're over water and get water distance
            bool isOverWater = IsPositionOverWater(position);
            float distanceToWater = float.MaxValue;
            
            if (isOverWater)
            {
                // Use actual sea level (0) for water distance calculation
                float actualSeaLevel = 0f;
                distanceToWater = Math.Max(0f, position.y - actualSeaLevel);
            }
            
            // Use whichever is closer - water surface or terrain
            if (isOverWater && distanceToWater < distanceToTerrain)
            {
                return distanceToWater;
            }
            else if (distanceToTerrain != float.MaxValue)
            {
                return distanceToTerrain;
            }
            
            // No ground found within 200m
            return 200f;
        }

        private bool IsPositionOverWater(Vector3 position)
        {
            try
            {
                // Method 1: Check topology data for water/beach areas
                if (TerrainMeta.TopologyMap != null)
                {
                    int topology = TerrainMeta.TopologyMap.GetTopology(position);
                    
                    // Check for multiple water-related topology flags (ocean only)
                    bool hasWaterTopology = (topology & 128) != 0 ||   // Water/Ocean (common value)
                                           (topology & 256) != 0 ||   // Another water variant
                                           (topology & 512) != 0 ||   // River/water course
                                           (topology & 1024) != 0;    // Lake/large water body
                    
                    if (hasWaterTopology)
                    {
                        return true;
                    }
                }
                
                // Method 2: Fallback - Use water height detection for areas without topology
                float waterHeight = TerrainMeta.WaterMap.GetHeight(position);
                
                // If we're above water level and water height is at or near sea level (0-1m)
                // and we're not too high above it, consider it water
                bool isOverSeaLevel = waterHeight >= -1f && waterHeight <= 1f && 
                                     position.y > waterHeight && 
                                     position.y < waterHeight + 100f; // Within 100m of water surface
                
                return isOverSeaLevel;
            }
            catch (System.Exception ex)
            {
                Log($"Error in IsPositionOverWater: {ex.Message}");
                return false;
            }
        }

        private void CalculateAndAddScore(PlayerFlightData flightData)
        {
            var player = flightData.Player;
            var minicopter = flightData.Minicopter;
            var position = minicopter.transform.position;

            // Calculate speed and distance to ground
            float speed = minicopter.GetComponent<Rigidbody>()?.velocity.magnitude ?? 0f;
            float distanceToGround = GetDistanceToGround(position);

            // Check crash cooldown (matches streak end UI duration)
            bool crashCooldownActive = flightData.RecentlyCrashed && (Time.time - flightData.LastCrashTime) < config.StreakEndUIDuration;

            // Check base conditions
            bool altitudeOK = distanceToGround <= 10f;
            bool speedOK = speed >= config.StreakStartSpeedThreshold;

            // Handle altitude forgiveness (get low system)
            if (!altitudeOK && flightData.IsStreakActive && !flightData.IsHighAltitude)
            {
                // Too high - start altitude forgiveness
                flightData.IsHighAltitude = true;
                flightData.HighAltitudeStartTime = Time.time;
                CreateGetLowCountdownUI(flightData.Player, flightData, 3f);
                Log($"[FORGIVENESS DEBUG] Altitude forgiveness started - Player: {flightData.Player.displayName}");
            }
            else if (altitudeOK && flightData.IsHighAltitude)
            {
                // Back to correct altitude - end forgiveness
                flightData.IsHighAltitude = false;
                flightData.HighAltitudeStartTime = 0f;
                DestroyGetLowCountdownUI(flightData.Player, flightData);
                Log($"[FORGIVENESS DEBUG] Altitude forgiveness ended (back to low altitude) - Player: {flightData.Player.displayName}");
            }
            
            // Handle speed forgiveness (speed up system)
            if (!speedOK && flightData.IsStreakActive && !flightData.IsInSpeedForgiveness)
            {
                // Too slow - start speed forgiveness
                flightData.IsInSpeedForgiveness = true;
                flightData.SpeedForgivenessStartTime = Time.time;
                CreateSpeedUpCountdownUI(flightData.Player, flightData, 3f);
                Log($"[FORGIVENESS DEBUG] Speed forgiveness started - Player: {flightData.Player.displayName}");
            }
            else if (speedOK && flightData.IsInSpeedForgiveness)
            {
                // Back to correct speed - end forgiveness
                flightData.IsInSpeedForgiveness = false;
                flightData.SpeedForgivenessStartTime = 0f;
                DestroySpeedUpCountdownUI(flightData.Player, flightData);
                Log($"[FORGIVENESS DEBUG] Speed forgiveness ended (back to speed) - Player: {flightData.Player.displayName}");
            }

            // Check for forgiveness timeouts (3 seconds each)
            bool altitudeForgiven = false;
            bool speedForgiven = false;
            
            if (flightData.IsHighAltitude)
            {
                float altitudeForgivenessTime = Time.time - flightData.HighAltitudeStartTime;
                float remainingTime = 3f - altitudeForgivenessTime;
                
                if (altitudeForgivenessTime >= 3f)
                {
                    // Altitude forgiveness expired
                    flightData.IsHighAltitude = false;
                    flightData.HighAltitudeStartTime = 0f;
                    DestroyGetLowCountdownUI(flightData.Player, flightData);
                    Log($"[FORGIVENESS DEBUG] Altitude forgiveness expired - Player: {flightData.Player.displayName}");
                }
                else
                {
                    altitudeForgiven = true; // Still within forgiveness period
                    // Update countdown UI with remaining time
                    CreateGetLowCountdownUI(flightData.Player, flightData, remainingTime);
                }
            }
            
            if (flightData.IsInSpeedForgiveness)
            {
                float speedForgivenessTime = Time.time - flightData.SpeedForgivenessStartTime;
                float remainingTime = 3f - speedForgivenessTime;
                
                if (speedForgivenessTime >= 3f)
                {
                    // Speed forgiveness expired
                    flightData.IsInSpeedForgiveness = false;
                    flightData.SpeedForgivenessStartTime = 0f;
                    DestroySpeedUpCountdownUI(flightData.Player, flightData);
                    Log($"[FORGIVENESS DEBUG] Speed forgiveness expired - Player: {flightData.Player.displayName}");
                }
                else
                {
                    speedForgiven = true; // Still within forgiveness period
                    // Update countdown UI with remaining time
                    CreateSpeedUpCountdownUI(flightData.Player, flightData, remainingTime);
                }
            }

            // Determine final streak conditions (with forgiveness applied)
            // Prioritize speed forgiveness over altitude forgiveness when both are happening
            bool meetingStreakConditions;
            if (speedForgiven && altitudeForgiven)
            {
                // Both forgiving - prioritize speed forgiveness over altitude
                meetingStreakConditions = true;
                // Destroy altitude forgiveness UI to show speed forgiveness is prioritized
                if (!string.IsNullOrEmpty(flightData.GetLowUIElementId))
                {
                    DestroyGetLowCountdownUI(flightData.Player, flightData);
                }
            }
            else
            {
                // Apply normal conditions or single forgiveness
                meetingStreakConditions = (altitudeOK || altitudeForgiven) && (speedOK || speedForgiven);
            }

            if (config.Features.EnableStreakSystem && !crashCooldownActive)
            {
                if (meetingStreakConditions)
                {
                    if (!flightData.IsQualifyingForStreak && !flightData.IsStreakActive)
                    {
                        // Start qualification period
                        flightData.IsQualifyingForStreak = true;
                        flightData.StreakQualificationStartTime = Time.time;
                    }
                    else if (flightData.IsQualifyingForStreak && !flightData.IsStreakActive)
                    {
                        // Check if qualification period is complete
                        float qualificationDuration = Time.time - flightData.StreakQualificationStartTime;
                        if (qualificationDuration >= config.StreakStartDurationThreshold)
                        {
                            // Start the streak!
                            flightData.IsQualifyingForStreak = false;
                            flightData.IsStreakActive = true;
                            flightData.StreakStartTime = Time.time;
                            flightData.StreakScoreTotal = 0f;
                            flightData.RecentlyCrashed = false;
                            
                            // Reset streak flags
                            flightData.StreakEnded = false;
                            flightData.PenaltyApplied = false;
                            flightData.LastMultiplierMilestone = 1.0f;

                            // Create persistent streak UI (no floating point notification for streak start)
                            CreatePersistentStreakUI(player, flightData);
                        }
                    }
                }
                else
                {
                    // Not meeting conditions - reset qualification or end streak
                    if (flightData.IsQualifyingForStreak)
                    {
                        // Lost qualification
                        flightData.IsQualifyingForStreak = false;
                        flightData.StreakQualificationStartTime = 0f;
                        // Clear any active forgiveness states during qualification loss
                        if (flightData.IsHighAltitude)
                        {
                            flightData.IsHighAltitude = false;
                            flightData.HighAltitudeStartTime = 0f;
                            DestroyGetLowCountdownUI(flightData.Player, flightData);
                        }
                        if (flightData.IsInSpeedForgiveness)
                        {
                            flightData.IsInSpeedForgiveness = false;
                            flightData.SpeedForgivenessStartTime = 0f;
                            DestroySpeedUpCountdownUI(flightData.Player, flightData);
                        }
                        Log($"[STREAK DEBUG] QUALIFICATION LOST - Player: {flightData.Player.displayName}");
                    }
                    else if (flightData.IsStreakActive)
                    {
                        // End active streak - determine reason based on what failed
                        string reason;
                        if (!speedOK && !speedForgiven)
                        {
                            reason = "speed";
                        }
                        else if (!altitudeOK && !altitudeForgiven)
                        {
                            reason = "altitude";
                        }
                        else
                        {
                            // Fallback - determine based on current conditions
                            reason = speed < config.StreakStartSpeedThreshold ? "speed" : "altitude";
                        }
                        ResetStreak(flightData, reason);
                    }
                }
            }

            // Calculate proximity score
            float proximityScore = 0f;

            if (distanceToGround <= config.MaxPointDistanceFromGround)
            {
                proximityScore = Mathf.Max(0f, 100f - (distanceToGround * distanceToGround)) * config.ProximityPointsModifier;
            }

            // Calculate speed multiplier (ensure minimum 1.0x, no penalties for slow speed)
            float speedMultiplier = Mathf.Max(1.0f, speed / config.SpeedMultiplierBase);

            // Get biome multiplier
            float biomeMultiplier = config.Features.EnableBiomeMultipliers ? GetBiomeMultiplier(position) : 1f;

            // Check for zone bonus
            float zoneBonus = CheckZoneBonus(flightData, position, speed);

            // Calculate base points for this tick
            float basePoints = proximityScore * speedMultiplier * biomeMultiplier * config.ScoringInterval;

            // Add total points to flight score
            float totalPoints = basePoints + zoneBonus;

            // Apply streak multiplier and check for milestones
            if (config.Features.EnableStreakSystem && flightData.IsStreakActive)
            {
                float streakDuration = Time.time - flightData.StreakStartTime;
                float currentStreakMultiplier = GetStreakMultiplier(streakDuration);

                // Check for milestone achievements and show floating points
                CheckMultiplierMilestones(flightData, currentStreakMultiplier);

                totalPoints *= currentStreakMultiplier;
            }

            flightData.CurrentFlightScore += totalPoints;

            // Add to streak score total if streak is active
            if (flightData.IsStreakActive && totalPoints > 0)
            {
                flightData.StreakScoreTotal += totalPoints;
                
                // Update persistent streak UI
                CreatePersistentStreakUI(player, flightData);
            }

            // CONTINUOUSLY update the player's total score in real-time
            if (config.Features.EnableRealtimeScoring)
            {
                var steamId = player.UserIDString;
                if (storedData.Players.TryGetValue(steamId, out PlayerData playerData))
                {
                    playerData.TotalScore += totalPoints;
                    playerData.DisplayName = player.displayName;

                    // Save periodically (every 10 points to avoid too much I/O)
                    if (totalPoints > 0 && (int)playerData.TotalScore % 10 == 0)
                    {
                        SaveData();
                    }
                }
            }

            // Debug logging (if enabled)
            if (config.LogLevel > 1 && totalPoints > 0)
            {
                Log($"{player.displayName}: Speed={speed:F1}, Distance={distanceToGround:F1}, " +
                    $"Proximity={proximityScore:F1}, SpeedMult={speedMultiplier:F1}, BiomeMult={biomeMultiplier:F1}, " +
                    $"Points={totalPoints:F1}, Total={flightData.CurrentFlightScore:F1}");
            }

            // Handle flip detection
            HandleFlipDetection(flightData, distanceToGround);

            // Update last position for potential future use
            flightData.LastPosition = position;
        }

        private void HandleFlipDetection(PlayerFlightData flightData, float distanceToGround)
        {
            var minicopter = flightData.Minicopter;
            var player = flightData.Player;

            // Check if flip detection is enabled
            if (!config.FlipDetection.Enabled)
                return;

            // Check minimum altitude requirement
            if (distanceToGround < config.FlipDetection.MinAltitudeForFlip)
                return;

            // Check minimum speed requirement
            float speed = minicopter.GetComponent<Rigidbody>()?.velocity.magnitude ?? 0f;
            if (speed < config.FlipDetection.MinSpeedForFlip)
                return;

            // Check if minicopter is upside down using transform.up.y
            bool isCurrentlyUpsideDown = minicopter.transform.up.y < config.FlipDetection.UpsideDownThreshold;

            if (isCurrentlyUpsideDown && !flightData.IsUpsideDown)
            {
                // Just became upside down - start flip detection
                flightData.IsUpsideDown = true;
                flightData.UpsideDownStartTime = Time.time;
                flightData.FlipInProgress = true;
                flightData.ClosestGroundDistanceDuringFlip = distanceToGround;
                flightData.RecoveryInProgress = false;

                if (config.FlipDetection.ShowFlipDebugMessages)
                {
                    PrintToChat(player, $"[Flip] Started upside down at {distanceToGround:F1}m altitude");
                }
            }
            else if (isCurrentlyUpsideDown && flightData.IsUpsideDown)
            {
                // Still upside down - track closest distance to ground
                if (distanceToGround < flightData.ClosestGroundDistanceDuringFlip)
                {
                    flightData.ClosestGroundDistanceDuringFlip = distanceToGround;
                }
            }
            else if (!isCurrentlyUpsideDown && flightData.IsUpsideDown)
            {
                // Just became right-side up - start recovery confirmation
                flightData.IsUpsideDown = false;
                flightData.RecoveryInProgress = true;
                flightData.RecoveryStartTime = Time.time;

                if (config.FlipDetection.ShowFlipDebugMessages)
                {
                    PrintToChat(player, $"[Flip] Recovery started - closest was {flightData.ClosestGroundDistanceDuringFlip:F1}m");
                }
            }
            else if (!isCurrentlyUpsideDown && flightData.RecoveryInProgress)
            {
                // Check if recovery confirmation time has passed
                float recoveryTime = Time.time - flightData.RecoveryStartTime;
                if (recoveryTime >= config.FlipDetection.RecoveryConfirmationTime)
                {
                    // Successful flip completed!
                    AwardFlipPoints(flightData);

                    // Reset flip detection
                    flightData.FlipInProgress = false;
                    flightData.RecoveryInProgress = false;
                    flightData.ClosestGroundDistanceDuringFlip = float.MaxValue;
                }
            }
            else if (isCurrentlyUpsideDown && flightData.RecoveryInProgress)
            {
                // Went upside down again during recovery - reset recovery
                flightData.IsUpsideDown = true;
                flightData.RecoveryInProgress = false;
                if (distanceToGround < flightData.ClosestGroundDistanceDuringFlip)
                {
                    flightData.ClosestGroundDistanceDuringFlip = distanceToGround;
                }

                if (config.FlipDetection.ShowFlipDebugMessages)
                {
                    PrintToChat(player, "[Flip] Recovery failed - went upside down again");
                }
            }
        }

        private void AwardFlipPoints(PlayerFlightData flightData)
        {
            var player = flightData.Player;
            float closestDistance = flightData.ClosestGroundDistanceDuringFlip;

            // Check if we should only award points during streak
            if (config.FlipDetection.AwardPointsDuringStreakOnly && !flightData.IsStreakActive)
            {
                if (config.FlipDetection.ShowFlipDebugMessages)
                {
                    PrintToChat(player, "[Flip] No points awarded - streak required");
                }
                return;
            }

            // Calculate risk multiplier based on closest distance to ground
            float riskMultiplier = 1f;
            if (closestDistance <= config.FlipDetection.MaxAltitudeForRiskBonus)
            {
                float riskFactor = 1f - (closestDistance / config.FlipDetection.MaxAltitudeForRiskBonus);

                if (config.FlipDetection.RiskMultiplierCurve.ToLower() == "exponential")
                {
                    // Exponential curve: risk increases dramatically closer to ground
                    riskFactor = Mathf.Pow(riskFactor, config.FlipDetection.ExponentialRiskFactor);
                }

                riskMultiplier = 1f + (riskFactor * (config.FlipDetection.MaxRiskMultiplier - 1f));
            }

            float flipPoints = config.FlipDetection.BaseFlipPoints * riskMultiplier;

            // Add flip bonus to current flight score
            flightData.CurrentFlightScore += flipPoints;

            // Add to streak score total if streak is active
            if (flightData.IsStreakActive)
            {
                flightData.StreakScoreTotal += flipPoints;
            }

            // Add to player's total score
            var steamId = player.UserIDString;
            if (storedData.Players.TryGetValue(steamId, out PlayerData playerData))
            {
                playerData.TotalScore += flipPoints;
                playerData.DisplayName = player.displayName;
                SaveData();
            }

            // Show floating flip points
            if (config.PointDisplay.ShowFlipPoints)
            {
                ShowFloatingPoints(player, flipPoints, "flip");
            }

            // Notify player
            PrintToChat(player, $"[Flip Bonus] +{flipPoints:F0} points! Risk distance: {closestDistance:F1}m (x{riskMultiplier:F1})");

            if (config.LogLevel > 0)
            {
                Log($"{player.displayName} completed flip: {flipPoints:F0} points (distance: {closestDistance:F1}m, multiplier: {riskMultiplier:F1}x)");
            }
        }

        private void DeletePlayerMinicopter(ulong playerUserId)
        {
            // Find and delete any minicopter owned by this player
            var ownedMinicopters = minicopterOwners.Where(kvp => kvp.Value == playerUserId).ToList();

            foreach (var kvp in ownedMinicopters)
            {
                var networkId = kvp.Key;
                var minicopter = BaseNetworkable.serverEntities.Find(networkId) as Minicopter;

                if (minicopter != null && !minicopter.IsDestroyed)
                {
                    minicopter.Kill();
                }

                // Clean up tracking dictionaries
                minicopterOwners.Remove(networkId);
                minicopterLastOccupied.Remove(networkId);
            }
        }

        private void CleanupAbandonedMinicopters()
        {
            var currentTime = Time.time;
            var abandonedMinicopters = new List<NetworkableId>();

            // Find minicopters abandoned for more than 2 minutes (120 seconds)
            foreach (var kvp in minicopterLastOccupied.ToList())
            {
                var networkId = kvp.Key;
                var lastOccupiedTime = kvp.Value;
                var timeSinceOccupied = currentTime - lastOccupiedTime;

                if (timeSinceOccupied > 120f) // 2 minutes
                {
                    var minicopter = BaseNetworkable.serverEntities.Find(networkId) as Minicopter;

                    if (minicopter != null && !minicopter.IsDestroyed)
                    {
                        // Check if anyone is currently in the minicopter (driver or passenger)
                        bool hasDriver = minicopter.GetDriver() != null;
                        bool hasPassenger = minicopter.GetPassenger() != null;
                        bool isOccupied = hasDriver || hasPassenger;

                        Log($"Minicopter {networkId} status: Driver={hasDriver}, Passenger={hasPassenger}, Occupied={isOccupied}");

                        if (!isOccupied)
                        {
                            abandonedMinicopters.Add(networkId);

                            // Get owner name for chat message
                            var ownerName = "Unknown";
                            if (minicopterOwners.ContainsKey(networkId))
                            {
                                var ownerSteamId = minicopterOwners[networkId];
                                var ownerPlayer = BasePlayer.FindByID(ownerSteamId);
                                if (ownerPlayer != null)
                                {
                                    ownerName = ownerPlayer.displayName;
                                }
                                else
                                {
                                    // Try to get name from stored data
                                    var ownerData = storedData.Players.Values.FirstOrDefault(p => p.SteamID == ownerSteamId.ToString());
                                    if (ownerData != null)
                                    {
                                        ownerName = ownerData.DisplayName;
                                    }
                                }
                            }

                            // Announce to all players
                            Server.Broadcast($"<color=yellow>[AUTO-CLEANUP]</color> {ownerName}'s abandoned minicopter was destroyed after 2 minutes.");
                            Log($"AUTO-CLEANUP: Destroyed {ownerName}'s minicopter {networkId} after 2 minutes of abandonment");

                            minicopter.Kill();
                        }
                        else
                        {
                            // Someone got back in, update the occupied time
                            minicopterLastOccupied[networkId] = currentTime;
                        }
                    }
                    else
                    {
                        // Minicopter is already destroyed, clean up tracking
                        Log($"Minicopter {networkId} already destroyed by other means, cleaning up tracking");
                        abandonedMinicopters.Add(networkId);
                    }
                }
            }

            // Clean up tracking for destroyed minicopters
            foreach (var networkId in abandonedMinicopters)
            {
                minicopterOwners.Remove(networkId);
                minicopterLastOccupied.Remove(networkId);
            }
        }

        private void AnnounceMinicopterCount()
        {
            // Count active minicopters on the server
            var allMinicopters = BaseNetworkable.serverEntities.OfType<Minicopter>().Where(m => !m.IsDestroyed).ToList();
            var totalCount = allMinicopters.Count;
            var trackedCount = minicopterOwners.Count;
            var occupiedCount = allMinicopters.Count(m => m.AnyMounted());

            Server.Broadcast($"<color=cyan>[MINICOPTER STATUS]</color> Total: {totalCount} | Occupied: {occupiedCount} | Tracked: {trackedCount}");
        }

        #endregion

        #region Chat Commands

        [ChatCommand("mini")]
        private void SpawnMinicopterCommand(BasePlayer player, string command, string[] args)
        {
            // Delete player's previous minicopter if it exists
            DeletePlayerMinicopter(player.userID);

            // Calculate spawn position 4m in the direction player is looking
            Vector3 lookDirection = player.eyes.HeadRay().direction;
            lookDirection.y = 0f; // Keep horizontal direction only, ignore vertical look
            lookDirection = lookDirection.normalized;
            
            // Use player's eye position (camera level) instead of feet position
            Vector3 eyePosition = player.eyes.position;
            Vector3 targetPosition = eyePosition + (lookDirection * 4f);
            
            // Find ground level at target position (check within 10m above/below eye height)
            RaycastHit groundHit;
            Vector3 spawnPosition;
            
            if (Physics.Raycast(targetPosition + Vector3.up * 10f, Vector3.down, out groundHit, 20f, ~LayerMask.GetMask("Player (Server)", "Player", "AI", "Vehicle", "Trigger", "Ignore Raycast")))
            {
                // Found ground, spawn 1m above it
                spawnPosition = groundHit.point + Vector3.up * 1f;
            }
            else
            {
                // No ground found, spawn at target position + 1m up
                spawnPosition = targetPosition + Vector3.up * 1f;
            }

            var minicopter = GameManager.server.CreateEntity(MinicopterPrefab, spawnPosition) as Minicopter;

            if (minicopter != null)
            {
                minicopter.Spawn();

                // Assign ownership immediately
                minicopterOwners[minicopter.net.ID] = player.userID;
                minicopterLastOccupied[minicopter.net.ID] = Time.time;

                // Add fuel using the proper API
                NextTick(() =>
                {
                    if (minicopter != null && !minicopter.IsDestroyed)
                    {
                        var fuelSystem = minicopter.GetFuelSystem();
                        if (fuelSystem != null)
                        {
                            fuelSystem.AddFuel(500); // Add 500 fuel directly
                        }
                    }
                });
            }
            else
            {
                player.ChatMessage("<color=red>Failed to spawn minicopter.</color>");
            }
        }

        [ChatCommand("mc")]
        private void MinicopterCommand(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                ShowPlayerStats(player);
                return;
            }

            switch (args[0].ToLower())
            {
                case "score":
                case "stats":
                    ShowPlayerStats(player);
                    break;

                case "top":
                case "leaderboard":
                    ShowLeaderboard(player);
                    break;

                case "reset":
                    if (permission.UserHasPermission(player.UserIDString, "minicopterskillrace.admin"))
                    {
                        ResetAllData(player);
                    }
                    else
                    {
                        player.ChatMessage("You don't have permission to use this command.");
                    }
                    break;

                case "removeplayer":
                    if (permission.UserHasPermission(player.UserIDString, "minicopterskillrace.admin"))
                    {
                        HandleRemovePlayer(player, args);
                    }
                    else
                    {
                        player.ChatMessage("You don't have permission to use this command.");
                    }
                    break;

                case "help":
                    ShowHelp(player);
                    break;

                case "refresh":
                case "update":
                    CreateLeaderboardUI(player);
                    player.ChatMessage("Leaderboard refreshed!");
                    break;

                case "zonecreate":
                    if (permission.UserHasPermission(player.UserIDString, "minicopterskillrace.admin"))
                    {
                        HandleZoneCreate(player, args);
                    }
                    else
                    {
                        player.ChatMessage("You don't have permission to use this command.");
                    }
                    break;

                case "zonedelete":
                    if (permission.UserHasPermission(player.UserIDString, "minicopterskillrace.admin"))
                    {
                        HandleZoneDelete(player, args);
                    }
                    else
                    {
                        player.ChatMessage("You don't have permission to use this command.");
                    }
                    break;

                case "zonelist":
                    if (permission.UserHasPermission(player.UserIDString, "minicopterskillrace.admin"))
                    {
                        HandleZoneList(player);
                    }
                    else
                    {
                        player.ChatMessage("You don't have permission to use this command.");
                    }
                    break;

                case "zonetp":
                    if (permission.UserHasPermission(player.UserIDString, "minicopterskillrace.admin"))
                    {
                        HandleZoneTeleport(player, args);
                    }
                    else
                    {
                        player.ChatMessage("You don't have permission to use this command.");
                    }
                    break;

                case "clearallminis":
                    if (permission.UserHasPermission(player.UserIDString, "minicopterskillrace.admin"))
                    {
                        ClearAllMinicopters(player);
                    }
                    else
                    {
                        player.ChatMessage("You don't have permission to use this command.");
                    }
                    break;

                case "toggleflightui":
                    if (permission.UserHasPermission(player.UserIDString, "minicopterskillrace.admin"))
                    {
                        config.Features.EnableFlightDataUI = !config.Features.EnableFlightDataUI;
                        SaveConfig();
                        if (config.Features.EnableFlightDataUI)
                        {
                            player.ChatMessage("Flight data UI enabled.");
                            // Re-create UI if player is currently in a minicopter
                            if (activePlayers.ContainsKey(player.userID))
                            {
                                CreateFlightInfoUI(player, activePlayers[player.userID]);
                            }
                        }
                        else
                        {
                            player.ChatMessage("Flight data UI disabled.");
                            DestroyFlightInfoUI(player);
                        }
                    }
                    else
                    {
                        player.ChatMessage("You don't have permission to use this command.");
                    }
                    break;

                case "debuglocation":
                    if (permission.UserHasPermission(player.UserIDString, "minicopterskillrace.admin"))
                    {
                        ShowDebugLocationInfo(player);
                    }
                    else
                    {
                        player.ChatMessage("You don't have permission to use this command.");
                    }
                    break;

                default:
                    ShowHelp(player);
                    break;
            }
        }

        private void ShowPlayerStats(BasePlayer player)
        {
            var steamId = player.UserIDString;
            if (storedData.Players.TryGetValue(steamId, out PlayerData playerData))
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== Your PilotPro Stats ===");
                sb.AppendLine($"Total Score: {playerData.TotalScore:F0} points");
                sb.AppendLine($"Highest Flight Score: {playerData.HighestScore:F0} points");
                sb.AppendLine($"Best Streak Points: {playerData.BestStreakPoints:F0} points");

                // Current flight info
                if (activePlayers.ContainsKey(player.userID))
                {
                    var flightData = activePlayers[player.userID];
                    sb.AppendLine($"Current Flight: {flightData.CurrentFlightScore:F0} points");
                    if (flightData.IsStreakActive)
                    {
                        var currentStreak = Time.time - flightData.StreakStartTime;
                        sb.AppendLine($"Current Streak: {currentStreak:F1} seconds");
                    }
                }

                // Rank
                var rank = GetPlayerRank(steamId);
                if (rank > 0)
                {
                    sb.AppendLine($"Global Rank: #{rank}");
                }

                player.ChatMessage(sb.ToString());
            }
            else
            {
                player.ChatMessage("No flight data found. Get in a minicopter and start flying to begin tracking!");
            }
        }

        private void ShowLeaderboard(BasePlayer player)
        {
            var topPlayers = GetTopPlayers();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== PilotPro Leaderboard ===");

            int rank = 1;
            foreach (var playerData in topPlayers)
            {
                sb.AppendLine($"{rank}. {playerData.DisplayName}: {playerData.TotalScore:F0} points");
                rank++;
            }

            if (topPlayers.Count == 0)
            {
                sb.AppendLine("No players have recorded flights yet!");
            }

            player.ChatMessage(sb.ToString());
        }

        private void ShowHelp(BasePlayer player)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== PilotPro Commands ===");
            sb.AppendLine("/mc score - View your flight statistics");
            sb.AppendLine("/mc top - View the leaderboard");
            sb.AppendLine("/mc help - Show this help message");

            if (permission.UserHasPermission(player.UserIDString, "minicopterskillrace.admin"))
            {
                sb.AppendLine("/mc reset - Reset all player data (Admin only)");
                sb.AppendLine("/mc removeplayer <name> - Remove specific player from leaderboard");
                sb.AppendLine();
                sb.AppendLine("=== Zone Commands (Admin) ===");
                sb.AppendLine("/mc zonecreate <size> <difficulty> [name] - Create fly-through zone");
                sb.AppendLine("/mc zonedelete [id] - Delete zone (closest if no ID)");
                sb.AppendLine("/mc zonelist - List all zones");
                sb.AppendLine("/mc zonetp <id> - Teleport to zone");
                sb.AppendLine("/mc clearallminis - Remove all minicopters from server");
                sb.AppendLine("/mc debuglocation - Show detailed position and environment data");
            }

            sb.AppendLine();
            sb.AppendLine("=== How to Play ===");
            sb.AppendLine("Get in a minicopter and fly close to the ground at high speeds!");
            sb.AppendLine("• Proximity to ground + Speed = Points");
            sb.AppendLine("• Maintain streaks without collisions for bonus points");
            sb.AppendLine("• Leaderboard updates in real-time");

            player.ChatMessage(sb.ToString());
        }

        private void ResetAllData(BasePlayer player)
        {
            storedData.Players.Clear();
            activePlayers.Clear();
            SaveData();
            UpdateAllLeaderboards();
            player.ChatMessage("All PilotPro data has been reset.");
            Log($"All data reset by {player.displayName} ({player.UserIDString})");
        }

        private void ShowDebugLocationInfo(BasePlayer player)
        {
            // Toggle debug UI
            if (activeDebugUIs.ContainsKey(player.userID) && activeDebugUIs[player.userID])
            {
                // Turn off debug UI
                CuiHelper.DestroyUi(player, "MinicopterDebugLocation");
                activeDebugUIs[player.userID] = false;
                player.ChatMessage("Debug location UI disabled.");
            }
            else
            {
                // Turn on debug UI
                activeDebugUIs[player.userID] = true;
                CreateDebugLocationUI(player);
                player.ChatMessage("Debug location UI enabled.");
            }
        }

        private void HandleRemovePlayer(BasePlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                player.ChatMessage("Usage: /mc removeplayer <name>");
                player.ChatMessage("Example: /mc removeplayer acepilot");
                return;
            }

            string targetName = string.Join(" ", args.Skip(1)).ToLower();
            var playersToRemove = storedData.Players.Values
                .Where(p => p.DisplayName.ToLower().Contains(targetName))
                .ToList();

            if (playersToRemove.Count == 0)
            {
                player.ChatMessage($"No players found matching '{targetName}'");
                return;
            }

            if (playersToRemove.Count > 1)
            {
                player.ChatMessage($"Multiple players found matching '{targetName}':");
                foreach (var p in playersToRemove.Take(5))
                {
                    player.ChatMessage($"• {p.DisplayName} (Score: {p.TotalScore:F0})");
                }
                player.ChatMessage("Be more specific with the name.");
                return;
            }

            var targetPlayer = playersToRemove.First();
            storedData.Players.Remove(targetPlayer.SteamID);
            SaveData();

            if (config.Features.EnableLeaderboardUI)
            {
                UpdateAllLeaderboards();
            }

            player.ChatMessage($"Removed player '{targetPlayer.DisplayName}' from leaderboard");
            Log($"{player.displayName} removed player {targetPlayer.DisplayName} from leaderboard");
        }

        private int GetPlayerRank(string steamId)
        {
            var sortedPlayers = storedData.Players.Values
                .OrderByDescending(p => p.TotalScore)
                .ToList();

            for (int i = 0; i < sortedPlayers.Count; i++)
            {
                if (sortedPlayers[i].SteamID == steamId)
                {
                    return i + 1;
                }
            }

            return -1;
        }

        #endregion

        #region UI System

        private void CreateLeaderboardUI(BasePlayer player)
        {
            // Destroy existing UI first
            CuiHelper.DestroyUi(player, "MinicopterLeaderboard");

            // Debug: Ensure we have some data to display
            EnsurePlayerDataExists();

            // Get top players first
            var topPlayers = GetTopPlayers();

            // Create a simple single-element UI for testing
            var elements = new CuiElementContainer();

            // Create main container - made much smaller (half size)
            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.8" },
                RectTransform = { AnchorMin = "0.02 0.8", AnchorMax = "0.12 0.98" }
            }, "Hud", "MinicopterLeaderboard");

            // Build the content as a single text block
            var content = "Leaderboard\n\n";

            if (topPlayers.Count == 0)
            {
                content += "No players yet!\nFly around to earn points!";
            }
            else
            {
                int rank = 1;
                foreach (var playerData in topPlayers.Take(5))
                {
                    content += $"{rank}. {playerData.DisplayName}\n";
                    content += $"   Score: {playerData.TotalScore:F0} ";
                    if (playerData.BestStreakPoints > 0)
                    {
                        content += $"Best Streak: {playerData.BestStreakPoints:F0}pts\n";
                    }
                    rank++;
                }
            }

            content += "\n/mc commands";

            // Add the text as a single label
            elements.Add(new CuiLabel
            {
                Text = {
                    Text = content,
                    Color = "1 1 1 1",
                    FontSize = 8,
                    Align = TextAnchor.UpperLeft
                },
                RectTransform = { AnchorMin = "0.02 0.02", AnchorMax = "0.98 0.98" }
            }, "MinicopterLeaderboard");

            CuiHelper.AddUi(player, elements);
        }

        private void CreateFlightInfoUI(BasePlayer player, PlayerFlightData flightData)
        {
            // Destroy existing flight info UI
            CuiHelper.DestroyUi(player, "MinicopterFlightInfo");

            var elements = new CuiElementContainer();
            var position = flightData.Minicopter.transform.position;

            // Get current flight data
            float speed = flightData.Minicopter.GetComponent<Rigidbody>()?.velocity.magnitude ?? 0f;
            float distanceToGround = GetDistanceToGround(position);
            float biomeMultiplier = GetBiomeMultiplier(position);
            string biomeName = GetBiomeName(position);
            float speedMultiplier = Mathf.Max(1.0f, speed / config.SpeedMultiplierBase); // Ensure minimum 1.0x

            // Create flight info panel (center-left)
            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" }, // Transparent background
                RectTransform = { AnchorMin = "0.35 0.30", AnchorMax = "0.51 0.50" } // Moved down
            }, "Hud", "MinicopterFlightInfo");

            // Get speed color (higher speed = more green, slower = yellow)
            string speedColor = GetSpeedColor(speed);
            
            // Get ground distance color (closer = green, 10+ = red)
            string groundColor = GetGroundDistanceColor(distanceToGround);
            
            // Get biome color
            string biomeColor = GetBiomeColor(biomeName);

            // Calculate streak multiplier if active
            float streakMultiplier = 1.0f;
            if (flightData.IsStreakActive)
            {
                float streakDuration = Time.time - flightData.StreakStartTime;
                streakMultiplier = GetStreakMultiplier(streakDuration);
            }

            // Calculate total multiplier
            float totalMultiplier = speedMultiplier * biomeMultiplier * streakMultiplier;

            // Build clean flight info with location at bottom
            var flightInfo = $"<color={speedColor}>Speed: {speed:F1} m/s</color>\n";
            flightInfo += $"<color={groundColor}>Height: {distanceToGround:F1}m</color>\n";
            
            // Total multiplier
            string totalColor = totalMultiplier >= 5.0f ? "#ff0099" : "#ffff00"; // Pink for very high, yellow for normal
            flightInfo += $"<color={totalColor}>Multi: {totalMultiplier:F1}x</color>\n";
            flightInfo += $"<color={biomeColor}>{biomeName}</color>";

            // Add the flight info text
            elements.Add(new CuiLabel
            {
                Text = {
                    Text = flightInfo,
                    Color = "1 1 1 1",
                    FontSize = 12,
                    Align = TextAnchor.UpperLeft
                },
                RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.95" }
            }, "MinicopterFlightInfo");

            CuiHelper.AddUi(player, elements);
        }

        private void CreateDebugLocationUI(BasePlayer player)
        {
            Vector3 position = player.transform.position;
            
            // Get sea level using WaterMap
            float seaLevel = TerrainMeta.WaterMap.GetHeight(position);
            
            // In Rust, sea level should be around 0. If WaterMap returns something weird, use 0
            float actualSeaLevel = 0f;
            
            // Get distance to ground (same method used in scoring)
            float distanceToGround = GetDistanceToGround(position);
            
            // Also get individual distances for debugging
            bool isOverWater = IsPositionOverWater(position);
            float distanceToTerrain = float.MaxValue;
            float distanceToWater = float.MaxValue;
            
            // Get terrain distance
            RaycastHit hit;
            int excludeEntityLayers = ~LayerMask.GetMask("Player (Server)", "Player", "AI", "Vehicle", "Trigger", "Ignore Raycast");
            if (Physics.Raycast(position + Vector3.up * 1f, Vector3.down, out hit, 61f, excludeEntityLayers))
            {
                distanceToTerrain = position.y - hit.point.y;
            }
            
            // Get water distance  
            if (isOverWater)
            {
                distanceToWater = Math.Max(0f, position.y - actualSeaLevel);
            }
            
            // Get biome info
            float biomeMultiplier = GetBiomeMultiplier(position);
            string biomeName = GetBiomeName(position);
            
            // Get terrain height via raycast (separate from distance calculation)
            float terrainHeight = float.NaN;
            if (Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out hit, 200f, excludeEntityLayers))
            {
                terrainHeight = hit.point.y;
            }

            // Destroy existing debug UI
            CuiHelper.DestroyUi(player, "MinicopterDebugLocation");

            var elements = new CuiElementContainer();

            // Create debug info panel (right side)
            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.8" },
                RectTransform = { AnchorMin = "0.70 0.30", AnchorMax = "0.98 0.70" }
            }, "Hud", "MinicopterDebugLocation");

            // Build debug info text
            var debugInfo = new System.Text.StringBuilder();
            debugInfo.AppendLine("<color=#ffff00>DEBUG LOCATION</color>");
            debugInfo.AppendLine($"<color=#ffffff>Pos: ({position.x:F1}, {position.y:F1}, {position.z:F1})</color>");
            debugInfo.AppendLine($"<color=#00ffff>Sea Level: {actualSeaLevel:F2}m</color>");
            debugInfo.AppendLine($"<color=#888888>WaterMap: {seaLevel:F2}m</color>");
            debugInfo.AppendLine($"<color=#ffaa00>Terrain Height: {(float.IsNaN(terrainHeight) ? "N/A" : terrainHeight.ToString("F2") + "m")}</color>");
            debugInfo.AppendLine($"<color=#00ff00>Above Sea: {(position.y - actualSeaLevel):F2}m</color>");
            debugInfo.AppendLine("--- DISTANCES ---");
            if (distanceToTerrain != float.MaxValue)
                debugInfo.AppendLine($"<color=#ffaa88>To Terrain: {distanceToTerrain:F2}m</color>");
            else
                debugInfo.AppendLine($"<color=#888888>To Terrain: No Hit</color>");
            
            if (isOverWater && distanceToWater != float.MaxValue)
                debugInfo.AppendLine($"<color=#88aaff>To Water: {distanceToWater:F2}m</color>");
            else if (isOverWater)
                debugInfo.AppendLine($"<color=#888888>To Water: Error</color>");
            else
                debugInfo.AppendLine($"<color=#666666>To Water: Not Ocean</color>");
                
            debugInfo.AppendLine($"<color=#ff00ff>Final Ground: {distanceToGround:F2}m</color>");
            debugInfo.AppendLine($"<color=#ffffff>Over Water: {isOverWater}</color>");
            debugInfo.AppendLine($"<color=#aaffaa>Biome: {biomeName}</color>");
            debugInfo.AppendLine($"<color=#aaffaa>Multiplier: ×{biomeMultiplier:F1}</color>");
            
            // If in minicopter, show additional flight data
            if (activePlayers.ContainsKey(player.userID))
            {
                var flightData = activePlayers[player.userID];
                if (flightData?.Minicopter != null)
                {
                    float speed = flightData.Minicopter.GetComponent<Rigidbody>()?.velocity.magnitude ?? 0f;
                    debugInfo.AppendLine($"<color=#ff8800>Speed: {speed:F2} m/s</color>");
                    debugInfo.AppendLine($"<color=#88ff88>Streak: {flightData.IsStreakActive}</color>");
                    if (flightData.IsStreakActive)
                    {
                        float streakDuration = Time.time - flightData.StreakStartTime;
                        debugInfo.AppendLine($"<color=#88ff88>Duration: {streakDuration:F1}s</color>");
                    }
                }
            }

            // Add the debug info text
            elements.Add(new CuiLabel
            {
                Text = {
                    Text = debugInfo.ToString(),
                    Color = "1 1 1 1",
                    FontSize = 10,
                    Align = TextAnchor.UpperLeft
                },
                RectTransform = { AnchorMin = "0.05 0.05", AnchorMax = "0.95 0.95" }
            }, "MinicopterDebugLocation");

            CuiHelper.AddUi(player, elements);
        }

        private string GetBiomeName(Vector3 position)
        {
            try
            {
                // HIERARCHY LEVEL 1: Special Locations (highest priority)
                // Train tunnels - using configurable threshold
                bool isTrainTunnel = position.y < config.TrainTunnelYThreshold;

                if (isTrainTunnel)
                {
                    return "Train Tunnel";
                }

                // HIERARCHY LEVEL 2: Water Areas (high priority, definitive)
                // Check if over water using our topology-based detection
                if (IsPositionOverWater(position))
                {
                    return "Ocean";
                }

                // HIERARCHY LEVEL 2.5: Infrastructure & Terrain Features (medium priority)
                if (TerrainMeta.TopologyMap != null)
                {
                    // Monument detection
                    bool isMonument = TerrainMeta.TopologyMap.GetTopology(position, TerrainTopology.MONUMENT);
                    if (isMonument)
                    {
                        return "Monument";
                    }
                    
                    // Road detection
                    bool isRoad = TerrainMeta.TopologyMap.GetTopology(position, TerrainTopology.ROAD);
                    if (isRoad)
                    {
                        return "Road";
                    }
                    
                    // River detection
                    bool isRiver = TerrainMeta.TopologyMap.GetTopology(position, TerrainTopology.RIVER);
                    if (isRiver)
                    {
                        return "River";
                    }
                    
                    // Forest detection
                    bool isForest = TerrainMeta.TopologyMap.GetTopology(position, TerrainTopology.FOREST);
                    if (isForest)
                    {
                        return "Forest";
                    }
                }

                // HIERARCHY LEVEL 3: Biomes (lowest priority)
                float desert = TerrainMeta.BiomeMap.GetBiome(position, 1);
                float temperate = TerrainMeta.BiomeMap.GetBiome(position, 2);
                float tundra = TerrainMeta.BiomeMap.GetBiome(position, 4);
                float arctic = TerrainMeta.BiomeMap.GetBiome(position, 8);

                float maxBiome = Mathf.Max(desert, temperate, tundra, arctic);

                if (maxBiome <= 0.1f) return "Temperate";

                if (desert == maxBiome && desert > 0.5f)
                {
                    return "Arid";
                }
                else if (temperate == maxBiome)
                {
                    bool isSwamp = TerrainMeta.TopologyMap.GetTopology(position, TerrainTopology.SWAMP);
                    if (isSwamp) return "Swamp";
                    return "Temperate";
                }
                else if (tundra == maxBiome || arctic == maxBiome)
                {
                    bool isAlpine = TerrainMeta.TopologyMap.GetTopology(position, TerrainTopology.SUMMIT);
                    if (isAlpine) return "Alpine";
                    return arctic == maxBiome ? "Arctic" : "Tundra";
                }

                return "Default";
            }
            catch
            {
                return "Unknown";
            }
        }

        private void DestroyFlightInfoUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "MinicopterFlightInfo");
        }

        private void CreatePersistentStreakUI(BasePlayer player, PlayerFlightData flightData)
        {
            if (!flightData.IsStreakActive)
            {
                DestroyPersistentStreakUI(player, flightData);
                return;
            }

            // Don't show streak UI if streak end UI is currently active
            if (activeStreakEndUIs.ContainsKey(player.userID) && activeStreakEndUIs[player.userID])
            {
                return; // Wait for streak end UI to disappear
            }

            // Update UI every 0.5 seconds to avoid spam
            if (Time.time - flightData.LastUIUpdate < 0.5f)
                return;

            // Generate unique element ID
            string elementId = $"StreakUI_{player.userID}";
            flightData.StreakUIElementId = elementId;
            flightData.LastUIUpdate = Time.time;

            // Destroy existing UI
            CuiHelper.DestroyUi(player, elementId);

            var elements = new CuiElementContainer();

            // Calculate current streak stats
            float rawStreakDuration = Time.time - flightData.StreakStartTime;
            float displayStreakDuration = rawStreakDuration + config.StreakStartDurationThreshold; // Add 10s preload for display
            float currentStreakMultiplier = GetStreakMultiplier(rawStreakDuration);

            // Get color based on multiplier
            string streakColor = GetStreakMultiplierColor(currentStreakMultiplier);

            // Create streak UI panel (bottom center, above hotbar) - smaller background only
            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.7" }, // Semi-transparent black background
                RectTransform = { AnchorMin = "0.35 0.12", AnchorMax = "0.65 0.20" } // Smaller panel
            }, "Hud", elementId);

            // Format multiplier without trailing zeros
            string multiplierText;
            if (currentStreakMultiplier % 1 == 0)
            {
                multiplierText = $"{currentStreakMultiplier:F0}x"; // Show as 3x, 2x, etc.
            }
            else
            {
                multiplierText = $"{currentStreakMultiplier:F1}x"; // Show as 2.5x, 1.8x, etc.
            }

            // Streak info text - show both display duration and current multiplier
            string streakText = $"STREAK - {displayStreakDuration:F1}s | {multiplierText}\n";
            if (currentStreakMultiplier >= 3.0f)
            {
                streakText += $"SUPER STREAK | Points: {flightData.StreakScoreTotal:F0}";
            }
            else
            {
                streakText += $"Points: {flightData.StreakScoreTotal:F0}";
            }

            elements.Add(new CuiLabel
            {
                Text = {
                    Text = streakText,
                    Color = streakColor,
                    FontSize = 16,
                    Align = TextAnchor.MiddleCenter
                },
                RectTransform = { AnchorMin = "0.02 0.1", AnchorMax = "0.98 0.9" }
            }, elementId);

            CuiHelper.AddUi(player, elements);
        }

        private void DestroyPersistentStreakUI(BasePlayer player, PlayerFlightData flightData)
        {
            if (!string.IsNullOrEmpty(flightData.StreakUIElementId))
            {
                CuiHelper.DestroyUi(player, flightData.StreakUIElementId);
                flightData.StreakUIElementId = null;
            }
        }

        private void CreateGetLowCountdownUI(BasePlayer player, PlayerFlightData flightData, float timeRemaining)
        {
            // Generate unique element ID
            string elementId = $"GetLowUI_{player.userID}";
            flightData.GetLowUIElementId = elementId;

            // Destroy existing UI
            CuiHelper.DestroyUi(player, elementId);

            var elements = new CuiElementContainer();

            // Calculate color based on time remaining (yellow to red)
            string countdownColor;
            if (timeRemaining > 2f)
            {
                countdownColor = "1 1 0 1"; // Yellow
            }
            else if (timeRemaining > 1f)
            {
                // Transition from yellow to orange
                float progress = (3f - timeRemaining) / 1f; // 0 to 1 as time goes from 3 to 2
                countdownColor = $"1 {1f - (progress * 0.5f)} 0 1"; // Yellow to orange
            }
            else
            {
                countdownColor = "1 0 0 1"; // Red
            }

            // Create countdown UI panel (above streak panel)
            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.8" }, // Semi-transparent black background
                RectTransform = { AnchorMin = "0.35 0.21", AnchorMax = "0.65 0.27" } // Above streak panel
            }, "Hud", elementId);

            // GET LOW countdown text
            string countdownText = $"GET LOW! {timeRemaining:F1}s";
            
            elements.Add(new CuiLabel
            {
                Text = {
                    Text = countdownText,
                    Color = countdownColor,
                    FontSize = 18,
                    Align = TextAnchor.MiddleCenter
                },
                RectTransform = { AnchorMin = "0.02 0.1", AnchorMax = "0.98 0.9" }
            }, elementId);

            CuiHelper.AddUi(player, elements);
        }

        private void DestroyGetLowCountdownUI(BasePlayer player, PlayerFlightData flightData)
        {
            if (!string.IsNullOrEmpty(flightData.GetLowUIElementId))
            {
                CuiHelper.DestroyUi(player, flightData.GetLowUIElementId);
                flightData.GetLowUIElementId = null;
            }
        }

        private void CreateSpeedUpCountdownUI(BasePlayer player, PlayerFlightData flightData, float timeRemaining)
        {
            // Generate unique element ID
            string elementId = $"SpeedUpUI_{player.userID}";
            flightData.SpeedUpUIElementId = elementId;

            // Destroy existing UI
            CuiHelper.DestroyUi(player, elementId);

            var elements = new CuiElementContainer();

            // Calculate color based on time remaining (cyan to red)
            string countdownColor;
            if (timeRemaining > 2f)
            {
                countdownColor = "0 1 1 1"; // Cyan
            }
            else if (timeRemaining > 1f)
            {
                // Transition from cyan to orange
                float progress = (3f - timeRemaining) / 1f; // 0 to 1 as time goes from 3 to 2
                countdownColor = $"{progress} {1f - (progress * 0.5f)} {1f - progress} 1"; // Cyan to orange
            }
            else
            {
                countdownColor = "1 0 0 1"; // Red
            }

            // Create countdown UI panel (above GET LOW panel if it exists, otherwise above streak panel)
            string anchorMin = "0.35 0.28"; // Above GET LOW panel
            string anchorMax = "0.65 0.34";
            
            // Check if GET LOW UI is active, if not, position above streak panel
            if (string.IsNullOrEmpty(flightData.GetLowUIElementId))
            {
                anchorMin = "0.35 0.21"; // Above streak panel
                anchorMax = "0.65 0.27";
            }

            elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.8" }, // Semi-transparent black background
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax }
            }, "Hud", elementId);

            // SPEED UP countdown text
            string countdownText = $"SPEED UP! {timeRemaining:F1}s";
            
            elements.Add(new CuiLabel
            {
                Text = {
                    Text = countdownText,
                    Color = countdownColor,
                    FontSize = 18,
                    Align = TextAnchor.MiddleCenter
                },
                RectTransform = { AnchorMin = "0.02 0.1", AnchorMax = "0.98 0.9" }
            }, elementId);

            CuiHelper.AddUi(player, elements);
        }

        private void DestroySpeedUpCountdownUI(BasePlayer player, PlayerFlightData flightData)
        {
            if (!string.IsNullOrEmpty(flightData.SpeedUpUIElementId))
            {
                CuiHelper.DestroyUi(player, flightData.SpeedUpUIElementId);
                flightData.SpeedUpUIElementId = null;
            }
        }

        private void ClearAllPlayersUI()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected)
                {
                    // Clear all possible UI elements from previous plugin loads
                    CuiHelper.DestroyUi(player, "MinicopterLeaderboard");
                    CuiHelper.DestroyUi(player, "MinicopterFlightInfo");
                    CuiHelper.DestroyUi(player, "MinicopterDebugLocation");
                    CuiHelper.DestroyUi(player, $"StreakUI_{player.userID}");
                    CuiHelper.DestroyUi(player, $"StreakEndUI_{player.userID}");
                    CuiHelper.DestroyUi(player, $"GetLowUI_{player.userID}");
                    CuiHelper.DestroyUi(player, $"SpeedUpUI_{player.userID}");
                    
                    // Use existing cleanup function to properly clear tracked floating points
                    CleanupPlayerPointDisplays(player);
                    
                    // Comprehensive fallback: Clear all possible floating points from before restart
                    // FloatingPoints use random numbers 1000-9999, so clear the full range
                    for (int i = 1000; i <= 9999; i++)
                    {
                        CuiHelper.DestroyUi(player, $"FloatingPoints_{player.userID}_{i}");
                    }
                    
                    // Also clear any potential stuck UI elements with different patterns
                    for (int i = 0; i < 20; i++)
                    {
                        CuiHelper.DestroyUi(player, $"PointDisplay_{player.userID}_{i}");
                        CuiHelper.DestroyUi(player, $"FloatingUI_{player.userID}_{i}");
                    }
                }
            }
            
            // Clear all UI tracking state
            activeDebugUIs.Clear();
            activePointDisplays.Clear();
            activeStreakEndUIs.Clear();
        }

        private void EnsurePlayerDataExists()
        {
            // Add current connected players to ensure we have data to display
            foreach (var connectedPlayer in BasePlayer.activePlayerList)
            {
                if (connectedPlayer != null)
                {
                    var steamId = connectedPlayer.UserIDString;
                    if (!storedData.Players.ContainsKey(steamId))
                    {
                        storedData.Players[steamId] = new PlayerData(steamId, connectedPlayer.displayName);
                        SaveData();
                    }
                }
            }
        }

        private void DestroyLeaderboardUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "MinicopterLeaderboard");
        }

        private List<PlayerData> GetTopPlayers()
        {
            // Include all players, even those with 0 scores
            return storedData.Players.Values
                .OrderByDescending(p => p.TotalScore)
                .ThenBy(p => p.DisplayName) // Secondary sort by name for consistent ordering
                .Take(config.LeaderboardSize)
                .ToList();
        }

        private void UpdateAllLeaderboards()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected)
                {
                    CreateLeaderboardUI(player);
                }
            }
        }

        #endregion

        #region Zone System

        private float CheckZoneBonus(PlayerFlightData flightData, Vector3 position, float speed)
        {
            if (!config.ZoneSystem.ZoneDetectionEnabled || zoneData?.Zones == null)
                return 0f;

            // Check minimum speed requirement
            if (speed < config.ZoneSystem.MinSpeedForZone)
                return 0f;

            float currentTime = Time.time;
            float totalBonus = 0f;

            foreach (var zone in zoneData.Zones.Values)
            {
                float distance = Vector3.Distance(position, zone.Position);

                if (distance <= zone.Size)
                {
                    // Check cooldown
                    if (flightData.ZoneCooldowns.ContainsKey(zone.Id))
                    {
                        float lastTriggered = flightData.ZoneCooldowns[zone.Id];
                        if (currentTime - lastTriggered < config.ZoneSystem.ZoneCooldownTime)
                            continue; // Still on cooldown
                    }

                    // Calculate base zone bonus
                    float difficultyMultiplier = config.ZoneSystem.ZoneDifficultyMultipliers.ContainsKey(zone.Difficulty)
                        ? config.ZoneSystem.ZoneDifficultyMultipliers[zone.Difficulty]
                        : 1f;

                    float baseZoneBonus = config.ZoneSystem.ZoneBonusMultiplier * difficultyMultiplier * 100f;

                    // Calculate progressive bonus for faster zone-to-zone travel
                    float progressiveMultiplier = 1f;
                    if (!string.IsNullOrEmpty(flightData.LastZoneTriggered) && flightData.LastZoneTriggered != zone.Id)
                    {
                        float timeSinceLastZone = currentTime - flightData.LastZoneTime;
                        // Faster zone-to-zone = more points (max 3x bonus for under 5 seconds)
                        if (timeSinceLastZone < 30f) // Within 30 seconds gets bonus
                        {
                            progressiveMultiplier = Mathf.Lerp(3f, 1f, timeSinceLastZone / 30f);
                        }
                    }

                    float finalZoneBonus = baseZoneBonus * progressiveMultiplier;
                    totalBonus += finalZoneBonus;

                    // Set cooldown and update last zone info
                    flightData.ZoneCooldowns[zone.Id] = currentTime;
                    flightData.LastZoneTriggered = zone.Id;
                    flightData.LastZoneTime = currentTime;

                    // Show floating zone points (yellow text)
                    if (config.PointDisplay.ShowZonePoints)
                    {
                        ShowFloatingPoints(flightData.Player, finalZoneBonus, "zone");
                    }

                    // Notify player
                    if (config.ZoneSystem.ShowZoneMessages)
                    {
                        string difficultyName = GetDifficultyName(zone.Difficulty);
                        string bonusText = progressiveMultiplier > 1f ? $" (x{progressiveMultiplier:F1} FAST!)" : "";
                        PrintToChat(flightData.Player, $"[Zone] {zone.Name} ({difficultyName}) +{finalZoneBonus:F0} points!{bonusText}");
                    }

                    if (config.LogLevel > 0)
                    {
                        Log($"{flightData.Player.displayName} triggered zone {zone.Name}: +{finalZoneBonus:F0} points (speed: {speed:F1} m/s, multiplier: {progressiveMultiplier:F1}x)");
                    }
                }
            }

            return totalBonus;
        }

        private string GetDifficultyName(int difficulty)
        {
            switch (difficulty)
            {
                case 1: return "Easy";
                case 2: return "Medium";
                case 3: return "Hard";
                case 4: return "Expert";
                case 5: return "Insane";
                default: return "Unknown";
            }
        }

        private void HandleZoneCreate(BasePlayer player, string[] args)
        {
            if (args.Length < 3)
            {
                player.ChatMessage("Usage: /mc zonecreate <size> <difficulty> [name]");
                player.ChatMessage("Size: radius in meters (e.g., 10)");
                player.ChatMessage("Difficulty: 1-5 (1=Easy, 2=Medium, 3=Hard, 4=Expert, 5=Insane)");
                player.ChatMessage("Name: optional zone name");
                return;
            }

            if (!float.TryParse(args[1], out float size) || size <= 0 || size > 200)
            {
                player.ChatMessage("Invalid size. Must be between 1 and 200 meters.");
                return;
            }

            if (!int.TryParse(args[2], out int difficulty) || difficulty < 1 || difficulty > 5)
            {
                player.ChatMessage("Invalid difficulty. Must be between 1 and 5.");
                return;
            }

            string zoneName = args.Length > 3 ? string.Join(" ", args.Skip(3)) : $"Zone_{zoneData.Zones.Count + 1}";
            string zoneId = Guid.NewGuid().ToString("N")[..8]; // Short unique ID

            var zone = new FlyThroughZone(zoneId, zoneName, player.transform.position, size, difficulty, player.displayName);
            zoneData.Zones[zoneId] = zone;
            SaveZoneData();

            string difficultyName = GetDifficultyName(difficulty);
            player.ChatMessage($"Created zone '{zoneName}' (ID: {zoneId})");
            player.ChatMessage($"Size: {size}m, Difficulty: {difficultyName}");
            player.ChatMessage($"Position: {zone.Position}");

            Log($"{player.displayName} created zone {zoneName} (ID: {zoneId}) at {zone.Position}");
        }

        private void HandleZoneDelete(BasePlayer player, string[] args)
        {
            if (zoneData.Zones.Count == 0)
            {
                player.ChatMessage("No zones exist to delete.");
                return;
            }

            if (args.Length < 2)
            {
                // Delete closest zone
                var closestZone = GetClosestZone(player.transform.position);
                if (closestZone == null)
                {
                    player.ChatMessage("No zones found.");
                    return;
                }

                if (zoneEntities.ContainsKey(closestZone.Id))
                {
                    zoneEntities[closestZone.Id].Kill();
                    zoneEntities.Remove(closestZone.Id);
                }

                float distance = Vector3.Distance(player.transform.position, closestZone.Position);
                zoneData.Zones.Remove(closestZone.Id);
                SaveZoneData();

                player.ChatMessage($"Deleted closest zone '{closestZone.Name}' (ID: {closestZone.Id})");
                player.ChatMessage($"Distance: {distance:F1}m");
                Log($"{player.displayName} deleted zone {closestZone.Name} (ID: {closestZone.Id})");
            }
            else
            {
                // Delete by ID
                string zoneId = args[1];
                if (!zoneData.Zones.ContainsKey(zoneId))
                {
                    player.ChatMessage($"Zone with ID '{zoneId}' not found.");
                    return;
                }

                if (zoneEntities.ContainsKey(zoneId))
                {
                    zoneEntities[zoneId].Kill();
                    zoneEntities.Remove(zoneId);
                }

                var zone = zoneData.Zones[zoneId];
                zoneData.Zones.Remove(zoneId);
                SaveZoneData();

                player.ChatMessage($"Deleted zone '{zone.Name}' (ID: {zoneId})");
                Log($"{player.displayName} deleted zone {zone.Name} (ID: {zoneId})");
            }
        }

        private void HandleZoneList(BasePlayer player)
        {
            if (zoneData.Zones.Count == 0)
            {
                player.ChatMessage("No zones exist.");
                return;
            }

            player.ChatMessage($"=== Zones ({zoneData.Zones.Count}) ===");

            var sortedZones = zoneData.Zones.Values
                .OrderBy(z => Vector3.Distance(player.transform.position, z.Position))
                .Take(10); // Limit to 10 closest zones

            foreach (var zone in sortedZones)
            {
                float distance = Vector3.Distance(player.transform.position, zone.Position);
                string difficultyName = GetDifficultyName(zone.Difficulty);
                player.ChatMessage($"• {zone.Name} (ID: {zone.Id})");
                player.ChatMessage($"  Size: {zone.Size}m, Difficulty: {difficultyName}, Distance: {distance:F1}m");
            }

            if (zoneData.Zones.Count > 10)
            {
                player.ChatMessage($"... and {zoneData.Zones.Count - 10} more zones");
            }
        }

        private void HandleZoneTeleport(BasePlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                player.ChatMessage("Usage: /mc zonetp <zone_id>");
                player.ChatMessage("Use '/mc zonelist' to see available zones and their IDs.");
                return;
            }

            string zoneId = args[1];
            if (!zoneData.Zones.ContainsKey(zoneId))
            {
                player.ChatMessage($"Zone with ID '{zoneId}' not found.");
                return;
            }

            var zone = zoneData.Zones[zoneId];
            var teleportPos = zone.Position + Vector3.up * 20f; // Teleport 20m above the zone

            player.Teleport(teleportPos);
            player.ChatMessage($"Teleported to zone '{zone.Name}' (ID: {zoneId})");
            Log($"{player.displayName} teleported to zone {zone.Name} (ID: {zoneId})");
        }

        private FlyThroughZone GetClosestZone(Vector3 position)
        {
            if (zoneData.Zones.Count == 0)
                return null;

            return zoneData.Zones.Values
                .OrderBy(z => Vector3.Distance(position, z.Position))
                .FirstOrDefault();
        }

        private void UpdateZoneVisualsForAllPlayers()
        {
            if (!config.ZoneSystem.ShowZoneVisuals || zoneData?.Zones == null || zoneData.Zones.Count == 0)
                return;

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected)
                {
                    UpdateZoneVisualsForPlayer(player);
                }
            }
        }

        private void UpdateZoneVisualsForPlayer(BasePlayer player)
        {
            if (!config.ZoneSystem.ShowZoneVisuals || player == null || zoneData?.Zones == null)
                return;

            float currentTime = Time.time;
            if (lastZoneVisualsUpdate.ContainsKey(player.userID))
            {
                if (currentTime - lastZoneVisualsUpdate[player.userID] < config.ZoneSystem.ZoneVisualUpdateInterval)
                    return; // Too soon to update
            }

            lastZoneVisualsUpdate[player.userID] = currentTime;

            // Only show zones within reasonable distance (200m) to avoid lag
            var nearbyZones = zoneData.Zones.Values
                .Where(z => Vector3.Distance(player.transform.position, z.Position) <= 200f)
                .ToList();

            foreach (var zone in nearbyZones)
            {
                CreateZoneTorus(player, zone);
            }
        }

        private void CreateZoneTorus(BasePlayer player, FlyThroughZone zone)
        {
            if (zoneEntities.ContainsKey(zone.Id))
            {
                return;
            }

            var entity = GameManager.server.CreateEntity("assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab", zone.Position, Quaternion.identity) as SphereEntity;
            if (entity == null)
            {
                return;
            }

            entity.currentRadius = zone.Size;
            entity.lerpSpeed = 0f;
            entity.Spawn();
            zoneEntities[zone.Id] = entity;
        }

        private List<Vector3> GenerateTorusPoints(Vector3 center, float majorRadius, float minorRadius)
        {
            var points = new List<Vector3>();
            int majorSegments = 24; // Number of segments around the major circle
            int minorSegments = 12; // Number of segments around the minor circle

            for (int i = 0; i < majorSegments; i++)
            {
                float majorAngle = (float)i / majorSegments * 2f * Mathf.PI;

                for (int j = 0; j < minorSegments; j++)
                {
                    float minorAngle = (float)j / minorSegments * 2f * Mathf.PI;

                    float x = (majorRadius + minorRadius * Mathf.Cos(minorAngle)) * Mathf.Cos(majorAngle);
                    float y = minorRadius * Mathf.Sin(minorAngle);
                    float z = (majorRadius + minorRadius * Mathf.Cos(minorAngle)) * Mathf.Sin(majorAngle);

                    points.Add(center + new Vector3(x, y, z));
                }
            }

            return points;
        }

        private string GetZoneDifficultyColor(int difficulty)
        {
            switch (difficulty)
            {
                case 1: return "0 1 0"; // Green - Easy
                case 2: return "1 1 0"; // Yellow - Medium
                case 3: return "1 0.5 0"; // Orange - Hard
                case 4: return "1 0 0"; // Red - Expert
                case 5: return "0.5 0 1"; // Purple - Insane
                default: return "1 1 1"; // White - Unknown
            }
        }

        private void DestroyZoneVisualsForPlayer(BasePlayer player)
        {
            foreach (var entity in zoneEntities.Values)
            {
                entity.Kill();
            }
            zoneEntities.Clear();
        }

        private void PlaySoundToPlayer(BasePlayer player, string prefabPath)
        {
            // Advanced method from PlayFX plugin - more reliable
            var effectId = StringPool.Get(prefabPath);
            if (effectId == 0)
            {
                LogWarning($"Invalid effect prefab: {prefabPath}");
                return;
            }

            var effect = new Effect { broadcast = true };
            effect.Init(Effect.Type.Generic, Vector3.zero, Vector3.zero);
            effect.pooledString = prefabPath;
            effect.pooledstringid = effectId;
            effect.entity = player.net.ID;
            effect.worldPos = player.transform.position;

            var write = Net.sv.StartWrite();
            write.PacketID(Message.Type.Effect);
            effect.WriteToStream(write);
            write.Send(new SendInfo(player.net.connection));
        }

        private void ClearAllMinicopters(BasePlayer player)
        {
            int count = 0;

            // Find all minicopters specifically (not all vehicles)
            var minicopters = UnityEngine.Object.FindObjectsOfType<Minicopter>();

            foreach (var minicopter in minicopters)
            {
                if (minicopter != null && !minicopter.IsDestroyed)
                {
                    minicopter.Kill();
                    count++;
                }
            }

            // Clear tracking data
            activePlayers.Clear();
            minicopterOwners.Clear();
            minicopterLastOccupied.Clear();

            player.ChatMessage($"Killed {count} minicopters and cleared all tracking data.");
            Log($"{player.displayName} cleared all minicopters ({count} destroyed)");
        }

        #endregion

        #region Color Helper Functions

        private float GetStreakMultiplier(float streakDuration)
        {
            // Custom progression table based on streak duration (in seconds)
            
            if (streakDuration < 10f) return 1.0f;
            else if (streakDuration < 20f) return 1.2f;
            else if (streakDuration < 30f) return 1.4f;
            else if (streakDuration < 40f) return 1.6f;
            else if (streakDuration < 50f) return 1.8f;
            else if (streakDuration < 60f) return 2.0f;
            else if (streakDuration < 90f) return 2.1f;
            else if (streakDuration < 120f) return 2.2f;
            else if (streakDuration < 150f) return 2.3f;
            else if (streakDuration < 180f) return 2.4f;
            else if (streakDuration < 210f) return 2.5f;
            else if (streakDuration < 240f) return 2.6f;
            else if (streakDuration < 270f) return 2.7f;
            else if (streakDuration < 300f) return 2.8f;
            else if (streakDuration < 330f) return 2.9f;
            else return 3.0f;
        }

        private string GetStreakMultiplierColor(float multiplier)
        {
            // Smooth color progression: 1x = blue → cyan → green → purple at 3x
            // Returns RGB format for CUI compatibility
            if (multiplier <= 1.0f)
                return "0.2 0.6 1.0 1.0"; // Blue for 1.0x and below
            else if (multiplier <= 1.2f)
                return "0.1 0.7 1.0 1.0"; // Light blue for 1.1x-1.2x
            else if (multiplier <= 1.5f)
                return "0.0 0.8 1.0 1.0"; // Lighter blue for 1.3x-1.5x
            else if (multiplier <= 1.8f)
                return "0.0 1.0 1.0 1.0"; // Cyan for 1.6x-1.8x
            else if (multiplier <= 2.2f)
                return "0.0 1.0 0.7 1.0"; // Cyan-green for 1.9x-2.2x
            else if (multiplier <= 2.6f)
                return "0.0 1.0 0.4 1.0"; // Green-cyan for 2.3x-2.6x
            else if (multiplier < 3.0f)
                return "0.0 1.0 0.0 1.0"; // Green for 2.7x-2.9x
            else
                return "0.8 0.0 1.0 1.0"; // Purple for 3.0x+ (SUPER STREAK!)
        }

        private string GetSpeedColor(float speed)
        {
            // Higher speed = more green, slower = yellow
            if (speed >= 30f)
                return "#00ff00"; // Bright green for high speed
            else if (speed >= 20f)
                return "#80ff00"; // Yellow-green
            else if (speed >= 15f)
                return "#ffff00"; // Yellow
            else if (speed >= 10f)
                return "#ff8000"; // Orange
            else
                return "#ff4000"; // Red-orange for very slow
        }

        private string GetGroundDistanceColor(float distance)
        {
            // Closer to ground = green, 10+ = red
            if (distance >= 10f)
                return "#ff0000"; // Red for 10+ meters
            else if (distance >= 7f)
                return "#ff8000"; // Orange
            else if (distance >= 5f)
                return "#ffff00"; // Yellow
            else if (distance >= 3f)
                return "#80ff00"; // Yellow-green
            else
                return "#00ff00"; // Green for very close to ground
        }

        private string GetBiomeColor(string biomeName)
        {
            // Color code different biomes
            switch (biomeName.ToLower())
            {
                case "train tunnel":
                    return "#ff00ff"; // Magenta for train tunnels
                case "water":
                case "water (close)":
                    return "#00bfff"; // Deep sky blue for water
                case "arid":
                    return "#daa520"; // Golden rod for desert
                case "jungle":
                    return "#228b22"; // Forest green
                case "swamp":
                    return "#556b2f"; // Dark olive green
                case "alpine":
                    return "#b0c4de"; // Light steel blue
                case "arctic":
                    return "#f0f8ff"; // Alice blue
                case "tundra":
                    return "#708090"; // Slate gray
                case "temperate":
                default:
                    return "#90ee90"; // Light green for temperate/default
            }
        }

        #endregion

        #region Point Display System

        private void ShowFloatingPoints(BasePlayer player, float points, string type = "normal", string customMessage = null)
        {
            if (!config.PointDisplay.ShowPointChanges || player == null)
                return;

            // Initialize player display list if needed
            if (!activePointDisplays.ContainsKey(player.userID))
            {
                activePointDisplays[player.userID] = new List<string>();
            }

            var displays = activePointDisplays[player.userID];

            // Remove old displays if we're at the limit
            while (displays.Count >= config.PointDisplay.MaxConcurrentDisplays)
            {
                var oldDisplayId = displays[0];
                CuiHelper.DestroyUi(player, oldDisplayId);
                displays.RemoveAt(0);
            }

            // Generate unique ID for this display
            string displayId = $"FloatingPoints_{player.userID}_{UnityEngine.Random.Range(1000, 9999)}";
            displays.Add(displayId);

            // Determine color, prefix, text, movement direction, and font size based on type and value
            string color = "1 1 1 1"; // Default white color
            string prefix = "";
            string text = "0"; // Default text
            bool moveUp = false; // Direction of movement
            int fontSize = config.PointDisplay.FontSize; // Base font size
            
            if (type.ToLower() == "flip" || type.ToLower() == "backflip")
            {
                // Backflips are always green, but purple for 3000+
                prefix = "+";
                text = $"{prefix}{Math.Abs(points):F0}";
                moveUp = true;
                
                if (Math.Abs(points) >= 3000f)
                {
                    color = "0.5 0 1 1"; // Purple for 3000+ points
                    fontSize = (int)(config.PointDisplay.FontSize * 1.5f); // Larger for epic gains
                }
                else
                {
                    color = "0.2 1 0.2 1"; // Green for regular backflips
                    if (Math.Abs(points) >= 1000f)
                    {
                        fontSize = (int)(config.PointDisplay.FontSize * 1.5f); // Larger for 1000+
                    }
                    else if (Math.Abs(points) <= 50f)
                    {
                        fontSize = (int)(config.PointDisplay.FontSize * 0.75f); // Smaller for low values
                    }
                }
            }
            else if (points < 0 || type.ToLower() == "damage")
            {
                // Damage points - gradient from yellow to red based on magnitude
                prefix = "-";
                text = $"{prefix}{Math.Abs(points):F0}";
                moveUp = false; // Negative points move down
                
                float damageAmount = Math.Abs(points);
                
                if (damageAmount == 0f)
                {
                    color = "1 1 0 1"; // Yellow for 0 damage
                    fontSize = (int)(config.PointDisplay.FontSize * 0.75f); // Smaller for 0
                }
                else if (damageAmount >= 1000f)
                {
                    color = "1 0 0 1"; // Full red for 1000+ damage
                    fontSize = (int)(config.PointDisplay.FontSize * 1.5f); // Larger for high damage
                }
                else
                {
                    // Gradient from yellow (0) to red (1000)
                    float intensity = Math.Min(damageAmount / 1000f, 1f); // 0 to 1
                    
                    // Yellow (1,1,0) to Orange (1,0.5,0) to Red (1,0,0)
                    float red = 1f;
                    float green = 1f - intensity; // 1 at 0 damage, 0 at 1000 damage
                    float blue = 0f;
                    
                    color = $"{red} {green} {blue} 1";
                    
                    // Scale font size
                    if (damageAmount >= 500f)
                    {
                        fontSize = (int)(config.PointDisplay.FontSize * 1.25f); // Larger for significant damage
                    }
                }
            }
            else if (type.ToLower() == "streak")
            {
                color = "0.2 0.8 1.0 1.0"; // Light blue for streak bonuses
                prefix = "+";
                
                // Use custom message if provided, otherwise build default streak text
                if (!string.IsNullOrEmpty(customMessage))
                {
                    text = customMessage; // Use custom message like "STREAK STARTED!"
                }
                else
                {
                    text = $"{prefix}{Math.Abs(points):F0} STREAK"; // Default format
                }
                
                moveUp = true; // Streak bonuses move up
                
                // Scale font size based on streak bonus points
                if (Math.Abs(points) >= 1000f)
                {
                    fontSize = (int)(config.PointDisplay.FontSize * 1.5f); // Larger for 1000+
                }
                else if (Math.Abs(points) <= 50f)
                {
                    fontSize = (int)(config.PointDisplay.FontSize * 0.85f); // Slightly smaller for small bonuses
                }
            }
            else if (type.ToLower() == "zone")
            {
                color = "1 1 0 1"; // Yellow for zone points
                prefix = "+";
                text = $"{prefix}{Math.Abs(points):F0}";
                moveUp = true; // Zone points move up
                
                // Scale font size based on zone points
                if (Math.Abs(points) >= 1000f)
                {
                    fontSize = (int)(config.PointDisplay.FontSize * 1.5f); // Larger for 1000+
                }
                else if (Math.Abs(points) <= 50f)
                {
                    fontSize = (int)(config.PointDisplay.FontSize * 0.75f); // Smaller for low values
                }
            }
            else if (type.ToLower() == "milestone")
            {
                // Regular multiplier milestones (1.5x, 2.5x, etc.)
                color = "0.0 1.0 1.0 1.0"; // Cyan for milestones
                prefix = "";
                text = customMessage ?? $"{points:F1}x";
                moveUp = true;
                fontSize = (int)(config.PointDisplay.FontSize * 1.2f); // Slightly larger
            }
            else if (type.ToLower() == "major_milestone")
            {
                // Major milestones (2x)
                color = "0.0 1.0 0.0 1.0"; // Bright green for major milestones
                prefix = "";
                text = customMessage ?? $"{points:F0}x STREAK!";
                moveUp = true;
                fontSize = (int)(config.PointDisplay.FontSize * 1.4f); // Larger
            }
            else if (type.ToLower() == "super_streak")
            {
                // Super streak milestones (3x+)
                color = "1.0 0.0 1.0 1.0"; // Magenta/Purple for super streaks
                prefix = "";
                text = customMessage ?? $"{points:F1}x SUPER STREAK!";
                moveUp = true;
                fontSize = (int)(config.PointDisplay.FontSize * 1.6f); // Even larger
            }
            else if (points > 0)
            {
                // Regular positive points - always green, but purple for 3000+
                prefix = "+";
                text = $"{prefix}{Math.Abs(points):F0}";
                moveUp = true; // Positive points move up
                
                if (Math.Abs(points) == 0f)
                {
                    color = "1 1 0 1"; // Yellow for 0 points
                    fontSize = (int)(config.PointDisplay.FontSize * 0.75f); // Smaller for 0
                }
                else if (Math.Abs(points) >= 3000f)
                {
                    color = "0.5 0 1 1"; // Purple for 3000+ points (epic gains)
                    fontSize = (int)(config.PointDisplay.FontSize * 1.5f); // Larger for epic points
                }
                else
                {
                    color = "0.2 1 0.2 1"; // Green for all regular positive points
                    
                    // Scale font size
                    if (Math.Abs(points) >= 1000f)
                    {
                        fontSize = (int)(config.PointDisplay.FontSize * 1.5f); // Larger for 1000+
                    }
                    else if (Math.Abs(points) <= 50f)
                    {
                        fontSize = (int)(config.PointDisplay.FontSize * 0.75f); // Smaller for low values
                    }
                }
            }
            else if (points == 0f && type.ToLower() != "milestone" && type.ToLower() != "major_milestone" && type.ToLower() != "super_streak")
            {
                // Zero points (only for non-milestone types)
                color = "1 1 0 1"; // Yellow for zero points
                prefix = "";
                text = $"{prefix}{Math.Abs(points):F0}";
                moveUp = false; // Zero points move down
                fontSize = (int)(config.PointDisplay.FontSize * 0.75f); // Smaller for 0
            }

            // Generate random position in a circle around screen center
            float circleRadius = 0.15f; // Radius of the circle around center
            float randomAngle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float randomDistance = UnityEngine.Random.Range(0.05f, circleRadius);

            float startCenterX = 0.5f + Mathf.Cos(randomAngle) * randomDistance;
            float startCenterY = 0.5f + Mathf.Sin(randomAngle) * randomDistance;

            float width = 0.08f;
            float height = 0.03f;

            // Create animated floating point display
            CreateAnimatedPointDisplay(player, displayId, text, color, startCenterX, startCenterY, width, height, moveUp, fontSize);
        }

        private void CreateAnimatedPointDisplay(BasePlayer player, string displayId, string text, string color, float startX, float startY, float width, float height, bool moveUp, int fontSize)
        {
            // Animation parameters - increased update rate for smoother animation
            float animationDuration = config.PointDisplay.DisplayDuration; // Total animation time
            int animationFrames = 50; // Increased from 20 to 50 for smoother animation
            float frameInterval = animationDuration / animationFrames; // Time between frames

            // Animation properties
            float totalVerticalMovement = 0.04f; // Distance to move (4% of screen)
            float fadeStartAt = 0.3f; // Start fading at 30% through animation

            // Create initial display
            ShowPointFrame(player, displayId, text, color, startX, startY, width, height, 1.0f, fontSize);

            // Animate the point display
            int currentFrame = 1;
            var animationTimer = timer.Repeat(frameInterval, animationFrames - 1, () =>
            {
                float progress = (float)currentFrame / animationFrames;
                
                // Calculate new position (moving up for positive, down for negative)
                float newY;
                if (moveUp)
                {
                    newY = startY + (totalVerticalMovement * progress); // Move up for positive points
                }
                else
                {
                    newY = startY - (totalVerticalMovement * progress); // Move down for negative points
                }
                
                // Calculate fade (starts at fadeStartAt progress)
                float alpha = 1.0f;
                if (progress >= fadeStartAt)
                {
                    float fadeProgress = (progress - fadeStartAt) / (1.0f - fadeStartAt);
                    alpha = 1.0f - fadeProgress; // Fade from 1 to 0
                }

                // Parse original color and apply alpha
                var colorParts = color.Split(' ');
                string fadedColor = $"{colorParts[0]} {colorParts[1]} {colorParts[2]} {alpha:F2}";

                // Update the display
                ShowPointFrame(player, displayId, text, fadedColor, startX, newY, width, height, alpha, fontSize);
                
                currentFrame++;
            });

            // Final cleanup
            timer.Once(animationDuration, () =>
            {
                CuiHelper.DestroyUi(player, displayId);
                if (activePointDisplays.ContainsKey(player.userID))
                {
                    activePointDisplays[player.userID].Remove(displayId);
                }
            });
        }

        private void ShowPointFrame(BasePlayer player, string displayId, string text, string color, float centerX, float centerY, float width, float height, float alpha, int fontSize)
        {
            // Only show if alpha is high enough to be visible
            if (alpha < 0.05f) return;

            // Destroy previous frame
            CuiHelper.DestroyUi(player, displayId);

            var elements = new CuiElementContainer();

            // Create floating point display (text only, no background)
            elements.Add(new CuiLabel
            {
                Text = {
                    Text = text,
                    Color = color,
                    FontSize = fontSize,
                    Align = TextAnchor.MiddleCenter
                },
                RectTransform = {
                    AnchorMin = $"{centerX - width/2} {centerY - height/2}",
                    AnchorMax = $"{centerX + width/2} {centerY + height/2}"
                }
            }, "Hud", displayId);

            CuiHelper.AddUi(player, elements);
        }

        private void CleanupPlayerPointDisplays(BasePlayer player)
        {
            if (!activePointDisplays.ContainsKey(player.userID))
            {
                return;
            }

            foreach (var displayId in activePointDisplays[player.userID])
            {
                CuiHelper.DestroyUi(player, displayId);
            }

            activePointDisplays[player.userID].Clear();
            
            // Also clean up streak end UI tracking
            if (activeStreakEndUIs.ContainsKey(player.userID))
            {
                activeStreakEndUIs.Remove(player.userID);
            }
        }

        #endregion
    }
}
