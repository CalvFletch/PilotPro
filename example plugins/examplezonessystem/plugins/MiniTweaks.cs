using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Plugins;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("MiniTweaks", "Zaddish & TimeTu", "1.2.0")]
    [Description("Manages minicopter spawn points with safe spawn detection, tweaks and more")]
    public class MiniTweaks : RustPlugin
    {
        private const string PermissionAdmin = "minitweaks.admin";
        private const float SafeSpawnRadius = 10f;
        private const string MinicopterPrefab = "assets/content/vehicles/minicopter/minicopter.entity.prefab";

        private Configuration config;
        private Dictionary<ulong, NetworkableId> playerMinicopters = new Dictionary<ulong, NetworkableId>();
        private Dictionary<ulong, System.Diagnostics.Stopwatch> mountingTimers = new Dictionary<ulong, System.Diagnostics.Stopwatch>();

        private class SpawnPoint
        {
            public Vector3 Position { get; set; }
            public float Radius { get; set; }
            public bool IsFixed { get; set; }
        }

        private class Configuration
        {
            [JsonProperty("SpawnPoints")]
            public List<SpawnPoint> SpawnPoints { get; set; } = new List<SpawnPoint>();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<Configuration>();
            if (config == null) LoadDefaultConfig();
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            config = new Configuration();
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        private void Init()
        {
            permission.RegisterPermission(PermissionAdmin, this);
        }
        
        [ChatCommand("setspawn")]
        private void CmdSetSpawn(BasePlayer player, string command, string[] args)
        {
            if (player == null || !HasPermission(player)) return;
            if (args.Length < 2 || !float.TryParse(args[0], out float radius) || radius <= 0)
            {
                player.ChatMessage("Usage: /setspawn <radius> <fixed/safe>");
                return;
            }

            var newSpawn = new SpawnPoint
            {
                Position = player.transform.position,
                Radius = radius,
                IsFixed = args[1].ToLower() == "fixed"
            };

            config.SpawnPoints.Add(newSpawn);
            SaveConfig();
            player.ChatMessage($"Spawn point set at {player.transform.position} with radius {radius} ({(newSpawn.IsFixed ? "Fixed" : "Safe")})");
        }
        [ChatCommand("listspawns")]
        private void CmdListSpawns(BasePlayer player, string command, string[] args)
        {
            if (player == null || !HasPermission(player)) return;
            if (config?.SpawnPoints == null || config.SpawnPoints.Count == 0)
            {
                player.ChatMessage("No spawn points configured.");
                return;
            }

            for (int i = 0; i < config.SpawnPoints.Count; i++)
            {
                var spawn = config.SpawnPoints[i];
                player.ChatMessage($"Spawn {i}: Position: {spawn.Position}, Radius: {spawn.Radius}, Type: {(spawn.IsFixed ? "Fixed" : "Safe")}");
            }
        }
        [ChatCommand("removespawn")]
        private void CmdRemoveSpawn(BasePlayer player, string command, string[] args)
        {
            if (player == null || !HasPermission(player)) return;
            if (config?.SpawnPoints == null || config.SpawnPoints.Count == 0)
            {
                player.ChatMessage("No spawn points configured.");
                return;
            }

            if (args.Length < 1 || !int.TryParse(args[0], out int index) || index < 0 || index >= config.SpawnPoints.Count)
            {
                player.ChatMessage("Usage: /removespawn <index>. Use /listspawns to see available indices.");
                return;
            }

            config.SpawnPoints.RemoveAt(index);
            SaveConfig();
            player.ChatMessage($"Spawn point at index {index} has been removed.");
        }

        [ChatCommand("mini")]
        private void CmdSpawnMini(BasePlayer player, string command, string[] args)
        {
            if (player == null || !HasPermission(player)) return;
            SpawnAndMountMinicopter(player, player.transform.position);
        }
        private bool HasPermission(BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, PermissionAdmin)) return true;
            player.ChatMessage("You don't have permission to use this command.");
            return false;
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null) return;
            DestroyExistingMinicopter(player.userID);

            SpawnPoint chosenSpawn = GetValidSpawnPoint();
                if (chosenSpawn != null)
                {
                    Vector3 spawnPosition = GetRandomPositionInRadius(chosenSpawn.Position, chosenSpawn.Radius);
                    SpawnAndMountMinicopter(player, spawnPosition);
                }
                PrintWarning($"No valid spawn point found for player {player.userID} on respawn");
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            DestroyExistingMinicopter(player.userID);
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player.IsDead()) player.Respawn();
        }

        private void DestroyExistingMinicopter(ulong userID)
        {
            if (playerMinicopters.TryGetValue(userID, out NetworkableId existingMinicopterId))
            {
                Minicopter existingMinicopter = BaseNetworkable.serverEntities.Find(existingMinicopterId) as Minicopter;
                if (existingMinicopter != null && !existingMinicopter.IsDestroyed) existingMinicopter.Kill();
                playerMinicopters.Remove(userID);
            }
        }

        private SpawnPoint GetValidSpawnPoint()
        {
            if (config?.SpawnPoints == null || config.SpawnPoints.Count == 0)
            {
                PrintError("No spawn points configured. Please add spawn points using the /setspawn command.");
                return null;
            }

            return config.SpawnPoints
                .Where(sp => sp != null && (sp.IsFixed || IsSafeSpawn(sp.Position)))
                .OrderBy(_ => Guid.NewGuid())
                .FirstOrDefault();
        }

        private bool IsSafeSpawn(Vector3 position)
        {
            return true;
        }

        private Vector3 GetRandomPositionInRadius(Vector3 center, float radius)
        {
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * radius;
            return new Vector3(center.x + randomCircle.x, center.y, center.z + randomCircle.y);
        }

        private Minicopter SpawnMinicopter(Vector3 position)
        {
            Minicopter minicopter = GameManager.server.CreateEntity(MinicopterPrefab, position) as Minicopter;
            if (minicopter == null) return null;

            minicopter.Spawn();
            AddFuelToMinicopter(minicopter);
            return minicopter;
        }

        private void SpawnAndMountMinicopter(BasePlayer player, Vector3 position)
        {
            DestroyExistingMinicopter(player.userID);
            Minicopter minicopter = SpawnMinicopter(position);
            if (minicopter != null)
            {
                playerMinicopters[player.userID] = minicopter.net.ID;
                timer.Once(0.5f, () => MountPlayerOnMinicopter(player, minicopter));
            }
        }

        private void MountPlayerOnMinicopter(BasePlayer player, Minicopter minicopter)
        {
            if (player.IsSleeping())
            {
                player.EndSleeping();
                PrintWarning($"Woke up player {player.UserIDString} from sleep");
            }
            PrintWarning($"Attempting to mount player {player.UserIDString} on minicopter");
            player.Teleport(minicopter.transform.position + Vector3.up);
            
            mountingTimers[player.userID] = System.Diagnostics.Stopwatch.StartNew();

            NextTick(() =>
            {
                var seat = minicopter.mountPoints[0].mountable as BaseMountable;
                if (seat != null)
                {
                    seat.MountPlayer(player);
                    minicopter.SendNetworkUpdateImmediate();
                    player.SendNetworkUpdateImmediate();
                    player.ClientRPCPlayer(null, player, "ForcePositionTo", player.transform.position);
                    PrintWarning($"Mount command issued and network update forced for player {player.UserIDString}");
                    
                    timer.Once(0.5f, () =>
                    {
                        if (player != null && !player.IsDestroyed)
                        {
                            player.ClientRPCPlayer(null, player, "ForcePositionTo", player.transform.position);
                            PrintWarning($"Delayed network update sent for player {player.UserIDString}");
                        }
                    });
                }
                else
                {
                    PrintError($"Failed to find a valid seat for player {player.UserIDString}");
                    mountingTimers.Remove(player.userID);
                }
            });
        }

        private void OnEntityMounted(BaseMountable mountable, BasePlayer player)
        {
            if (mountingTimers.TryGetValue(player.userID, out System.Diagnostics.Stopwatch timer))
            {
                timer.Stop();
                PrintWarning($"Player {player.UserIDString} mounted after {timer.ElapsedMilliseconds}ms");
                mountingTimers.Remove(player.userID);

                player.ClientRPCPlayer(null, player, "ForcePositionTo", player.transform.position);
                PrintWarning($"Additional network update sent for player {player.UserIDString} after successful mount");
            }
        }

        private void AddFuelToMinicopter(Minicopter minicopter)
        {
            var fuelSystem = minicopter.GetFuelSystem();
            if (fuelSystem != null)
            {
                fuelSystem.AddFuel(100000);
                var container = (fuelSystem as EntityFuelSystem)?.fuelStorageInstance.Get(true);
                if (container != null)
                {
                    container.inventory.SetFlag(ItemContainer.Flag.IsLocked, true);
                    container.SetFlag(BaseEntity.Flags.Locked, true);
                }
            }
        }
    }
}