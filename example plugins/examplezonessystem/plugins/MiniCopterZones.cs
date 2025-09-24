using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;



namespace Oxide.Plugins
{

    

    [Info("MiniCopterZones", "Zaddish & TimeTu", "1.3.0")]
    [Description("Creates configurable zones for mini-copters to fly through, with visible and customizable zone markers")]
    class MiniCopterZones : RustPlugin
    {
            private class SpatialPartition
            {
                private const int CELL_SIZE = 100;
                private Dictionary<Vector3Int, HashSet<Zone>> grid = new Dictionary<Vector3Int, HashSet<Zone>>();

                public void AddZone(Zone zone)
                {
                    Bounds zoneBounds = GetZoneBounds(zone);
                    Vector3Int minCell = WorldToCell(zoneBounds.min);
                    Vector3Int maxCell = WorldToCell(zoneBounds.max);

                    for (int x = minCell.x; x <= maxCell.x; x++)
                    {
                        for (int y = minCell.y; y <= maxCell.y; y++)
                        {
                            for (int z = minCell.z; z <= maxCell.z; z++)
                            {
                                Vector3Int cell = new Vector3Int(x, y, z);
                                if (!grid.TryGetValue(cell, out HashSet<Zone> zones))
                                {
                                    zones = new HashSet<Zone>();
                                    grid[cell] = zones;
                                }
                                zones.Add(zone);
                            }
                        }
                    }
                }

                public List<Zone> GetNearbyZones(Vector3 position)
                {
                    Vector3Int cell = WorldToCell(position);
                    HashSet<Zone> nearbyZones = new HashSet<Zone>();

                    for (int x = -1; x <= 1; x++)
                    {
                        for (int y = -1; y <= 1; y++)
                        {
                            for (int z = -1; z <= 1; z++)
                            {
                                Vector3Int neighborCell = new Vector3Int(cell.x + x, cell.y + y, cell.z + z);
                                if (grid.TryGetValue(neighborCell, out HashSet<Zone> zonesInCell))
                                {
                                    nearbyZones.UnionWith(zonesInCell);
                                }
                            }
                        }
                    }

                    return new List<Zone>(nearbyZones);
                }

                public void RemoveZone(Zone zone)
                {
                    Bounds zoneBounds = GetZoneBounds(zone);
                    Vector3Int minCell = WorldToCell(zoneBounds.min);
                    Vector3Int maxCell = WorldToCell(zoneBounds.max);

                    for (int x = minCell.x; x <= maxCell.x; x++)
                    {
                        for (int y = minCell.y; y <= maxCell.y; y++)
                        {
                            for (int z = minCell.z; z <= maxCell.z; z++)
                            {
                                Vector3Int cell = new Vector3Int(x, y, z);
                                if (grid.TryGetValue(cell, out HashSet<Zone> zones))
                                {
                                    zones.Remove(zone);
                                    if (zones.Count == 0)
                                    {
                                        grid.Remove(cell);
                                    }
                                }
                            }
                        }
                    }
                }

                private Vector3Int WorldToCell(Vector3 position)
                {
                    return new Vector3Int(
                        Mathf.FloorToInt(position.x / CELL_SIZE),
                        Mathf.FloorToInt(position.y / CELL_SIZE),
                        Mathf.FloorToInt(position.z / CELL_SIZE)
                    );
                }

                private Bounds GetZoneBounds(Zone zone)
                {
                    return zone.GetBounds();
                }
            }

            private SpatialPartition spatialPartition;

            private void InitializeSpatialPartition()
            {
                spatialPartition = new SpatialPartition();
                foreach (var zone in storedData.Zones.Values)
                {
                    spatialPartition.AddZone(zone);
                }
            }

            private void RebuildSpatialPartition()
            {
                spatialPartition = new SpatialPartition();
                foreach (var zone in storedData.Zones.Values)
                {
                    spatialPartition.AddZone(zone);
                }
            }
        
        
        #region Fields

        private const string PermissionUse = "minicopterzones.use";
        private const string PermissionAdmin = "minicopterzones.admin";

        private StoredData storedData;
        private Dictionary<ulong, int> playerPoints = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, Dictionary<string, DateTime>> playerCooldowns = new Dictionary<ulong, Dictionary<string, DateTime>>();

        private const string BLANK_SPHERE = "assets/prefabs/visualization/sphere.prefab";
        private const string WHITE_WHERE = "assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab";
        private const string GREEN_SPHERE = "assets/bundled/prefabs/modding/events/twitch/br_sphere_green.prefab";
        private const string RED_SPHERE = "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab";
        private const string PURPLE_SPHERE = "assets/bundled/prefabs/modding/events/twitch/br_sphere_purple.prefab";
        private readonly Dictionary<string, List<BaseEntity>> zoneEntities = new Dictionary<string, List<BaseEntity>>();

        private Dictionary<ulong, string> playerUIs = new Dictionary<ulong, string>();

        private Dictionary<ulong, PlayerHelicopter> playerCurrentMinicopter = new Dictionary<ulong, PlayerHelicopter>();

        private Timer uiUpdateTimer;
        

        #endregion

        #region Configuration

        private ConfigData configData;

        class ConfigData
        {
            [JsonProperty(PropertyName = "UI Position")]
            public string UIPosition = "BottomLeft";

            [JsonProperty(PropertyName = "UI Offset X")]
            public float UIOffsetX = 1670.5f;

            [JsonProperty(PropertyName = "UI Offset Y")]
            public float UIOffsetY = 153f;

            [JsonProperty(PropertyName = "UI Width")]
            public float UIWidth = 228.0f;

            [JsonProperty(PropertyName = "UI Height")]
            public float UIHeight = 30.0f;

            [JsonProperty(PropertyName = "UI Background Color")]
            public string UIBackgroundColor = "199 65 43 1";

            [JsonProperty(PropertyName = "UI Text Color")]
            public string UITextColor = "1 1 1 1";

            [JsonProperty(PropertyName = "Streak Multipliers")]
            public Dictionary<int, float> StreakMultipliers { get; set; } = new Dictionary<int, float>
            {
                { 5, 2f },
                { 10, 2.5f }
            };

            [JsonProperty(PropertyName = "Speed Multipliers")]
            public Dictionary<float, float> SpeedMultipliers { get; set; } = new Dictionary<float, float>
            {
                { 35, 1.5f  },
                { 37, 2.0f  },
                { 38, 2.5f  },
                { 40, 3.5f  },
                { 41, 4.0f  },
                { 42, 5.0f  },
                { 43, 8.0f  },
                { 45, 13.0f }
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                configData = Config.ReadObject<ConfigData>();
            }
            catch
            {
                LoadDefaultConfig();
            }

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            configData = new ConfigData();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(configData, true);
        }

        #endregion

        #region Data Management

        private class StoredData
        {
            public int Version = 1;
            public Dictionary<string, Zone> Zones = new Dictionary<string, Zone>();
            public Dictionary<ulong, PlayerData> Players = new Dictionary<ulong, PlayerData>();
        }

        private class PlayerData
        {
            public int Points { get; set; }
            public int Streak { get; set; }
            public DateTime LastPointTime { get; set; }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private class Zone
        {
            [JsonProperty]
            public string Name;

            [JsonProperty]
            public Vector3 Position;

            [JsonProperty]
            public string SerializedRotation;

            [JsonIgnore]
            public Quaternion Rotation
            {
                get => QuaternionFromString(SerializedRotation);
                set => SerializedRotation = QuaternionToString(value);
            }

            [JsonProperty]
            public float Size;

            [JsonProperty]
            [JsonConverter(typeof(StringEnumConverter))]
            public ZoneType Type;

            [JsonProperty]
            public int Stack = 1;

            [JsonProperty]
            public string Color = "white";

            [JsonProperty]
            public string boostDirection = "forward";

            [JsonProperty]
            public string boostAmount = "10";

            [JsonProperty]
            public int PointValue = 1;

            public Bounds GetBounds()
            {
                Bounds bounds = new Bounds(Position, Vector3.zero);
                bounds.Expand(Size);
                return bounds;
            }
        }

        private enum ZoneType
        {
            Points,
            Booster
        }
        
        private static string QuaternionToString(Quaternion quaternion)
        {
            return $"{quaternion.x},{quaternion.y},{quaternion.z},{quaternion.w}";
        }

        private static Quaternion QuaternionFromString(string quaternionString)
        {
            string[] values = quaternionString.Split(',');
            return new Quaternion(
                float.Parse(values[0]),
                float.Parse(values[1]),
                float.Parse(values[2]),
                float.Parse(values[3])
            );
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("MiniCopterZones", storedData);
        }

        private void LoadData()
        {
            try
            {
                storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("MiniCopterZones");
            }
            catch
            {
                storedData = new StoredData();
            }

            if (storedData == null)
            {
                storedData = new StoredData();
            }
        }

        private void Init()
        {
            permission.RegisterPermission(PermissionUse, this);
            permission.RegisterPermission(PermissionAdmin, this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            InitializeZones();
            InitializeUIForAllPlayers();
            StartUIUpdateTimer();
            InitializeSpatialPartition();
        }

        private void InitializeUIForAllPlayers()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (permission.UserHasPermission(player.UserIDString, PermissionUse))
                {
                    DestroyUI(player);
                    CreateUI(player);
                }
            }
        }

        private void InitializeZones()
        {
            UnityEngine.Object.FindObjectsOfType<ZoneEntity>().ToList().ForEach(UnityEngine.Object.Destroy);
            foreach (var entities in zoneEntities.Values)
            {
                foreach (var entity in entities)
                {
                    if (entity != null && !entity.IsDestroyed)
                        entity.Kill();
                }
            }
            zoneEntities.Clear();

            foreach (var zone in storedData.Zones.Values)
            {
                CreateZoneEntity(zone);
                CreateZoneVisual(zone);
            }

            var minicopters = UnityEngine.Object.FindObjectsOfType<PlayerHelicopter>();
            foreach (var minicopter in minicopters)
            {
                var componentsToRemove = minicopter.GetComponents<MonoBehaviour>()
                    .Where(c => c.GetType().Name == "MiniCopterComponent")
                    .ToList();

                if (componentsToRemove.Count > 0)
                {
                    PrintWarning($"Found {componentsToRemove.Count} existing MiniCopterComponent(s) by name on {minicopter.ShortPrefabName} {minicopter.net.ID}");
                    foreach (var component in componentsToRemove)
                    {
                        UnityEngine.Object.DestroyImmediate(component);
                    }
                }
                else
                {
                    var allComponents = minicopter.GetComponents<MonoBehaviour>();
                    PrintWarning($"Checking {allComponents.Length} MonoBehaviour components on {minicopter.ShortPrefabName} {minicopter.net.ID}");
                    foreach (var component in allComponents)
                    {
                        if (component.GetType().FullName.Contains("MiniCopterComponent"))
                        {
                            PrintWarning($"Found MiniCopterComponent by full name: {component.GetType().FullName}");
                            UnityEngine.Object.DestroyImmediate(component);
                            componentsToRemove.Add(component);
                        }
                    }
                }

                minicopter.gameObject.AddComponent<MiniCopterComponent>();
            }

            Puts($"Initialized {storedData.Zones.Count} zones.");
        }

        private void Unload()
        {
            StopUIUpdateTimer();

            SaveData();
            
            foreach (var entities in zoneEntities.Values)
            {
                foreach (var entity in entities)
                {
                    if (entity != null && !entity.IsDestroyed)
                        entity.Kill();
                }
            }
            zoneEntities.Clear();

            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyUI(player);
            }


        }

        

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            var miniCopter = entity as PlayerHelicopter;
            if (miniCopter == null) return;

            miniCopter.gameObject.AddComponent<MiniCopterComponent>();
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            var miniCopter = entity as PlayerHelicopter;
            if (miniCopter == null) return;

            foreach (var kvp in playerCurrentMinicopter.ToList())
            {
                if (kvp.Value == miniCopter)
                {
                    var player = BasePlayer.Find(kvp.Key.ToString());
                    if (player != null)
                    {
                        ResetPlayerStreak(player);
                    }
                    playerCurrentMinicopter.Remove(kvp.Key);
                }
            }
        }

        private void ResetPlayerStreak(BasePlayer player)
        {
            var playerData = GetPlayerData(player.userID);
            if (playerData.Streak > 0)
            {
                playerData.Streak = 0;
                SaveData();
                UpdateUI(player);
                player.ChatMessage("Your streak has been reset!");
                playerData.LastPointTime = DateTime.UtcNow;
                DestroyUI(player);
                CreateUI(player);
            }
        }

        private void CreateZoneEntity(Zone zone)
        {
            var zoneEntity = new GameObject($"MCZ_{zone.Name}").AddComponent<ZoneEntity>();
            zoneEntity.transform.position = zone.Position;
            zoneEntity.transform.rotation = zone.Rotation;
            zoneEntity.Size = zone.Size;
            zoneEntity.Type = zone.Type;
        }

        private void CreateZoneVisual(Zone zone)
        {
            List<BaseEntity> visualEntities = new List<BaseEntity>();
            zoneEntities[zone.Name] = visualEntities;

            for (int i = 0; i < zone.Stack; i++)
            {
                BaseEntity visualEntity;
                visualEntity = GameManager.server.CreateEntity(BLANK_SPHERE, zone.Position) as SphereEntity;
                (visualEntity as SphereEntity).currentRadius = zone.Size;
                (visualEntity as SphereEntity).lerpSpeed = 0f;

                visualEntity.enableSaving = false;
                visualEntity.Spawn();
                visualEntity.name = $"MCZ_{zone.Name}";
                visualEntities.Add(visualEntity);
            }
        }


        #endregion
        #region Helpers
        private void SendEffect(string prefabName, Vector3 pos)
        {   

            Effect.server.Run(prefabName, pos);
            Debug.Log($"Effect sent: PrefabName={prefabName}, Position={pos}");
        }
        #endregion
        #region Commands

        [ChatCommand("eject")]
        private void CmdEjectPlayer(BaseVehicle vehicle, BasePlayer player)
        {
            if (vehicle == null || player == null) return;

            var mountedPassenger = vehicle.mountPoints[0].mountable._mounted as BasePlayer;
            if (mountedPassenger == null) return;

            var toDismount = (BasePlayer.Find(mountedPassenger.UserIDString) as BasePlayer);

            if (toDismount.IsAdmin || permission.UserHasGroup(toDismount.UserIDString, "admin")) {
                player.ChatMessage("You can't eject an admin smh.");
                return;
            } else {
                mountedPassenger.EnsureDismounted();
                player.ChatMessage($"Ejected {toDismount.displayName}.");
            }
            
        }

        [ChatCommand("mczpoints")]
        private void CmdMiniCopterZonePoints(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                player.ChatMessage("You don't have permission to use this command.");
                return;
            }

            int points = GetPlayerPoints(player.userID);
            player.ChatMessage($"Your current MiniCopterZones points: {points}");
        }

        [ChatCommand("mczui")]
        private void CmdMiniCopterZoneUI(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionAdmin))
            {
                player.ChatMessage("You don't have permission to use this command.");
                return;
            }

            if (args.Length == 0)
            {
                player.ChatMessage("Usage: /mczui <position|offsetx|offsety|width|height|reload>");
                return;
            }

            switch (args[0].ToLower())
            {
                case "position":
                    if (args.Length < 2)
                    {
                        player.ChatMessage("Usage: /mczui position <TopLeft|TopRight|BottomLeft|BottomRight|Center>");
                        return;
                    }
                    configData.UIPosition = args[1];
                    break;
                case "offsetx":
                    if (args.Length < 2 || !float.TryParse(args[1], out configData.UIOffsetX))
                    {
                        player.ChatMessage("Usage: /mczui offsetx <value>");
                        return;
                    }
                    break;
                case "offsety":
                    if (args.Length < 2 || !float.TryParse(args[1], out configData.UIOffsetY))
                    {
                        player.ChatMessage("Usage: /mczui offsety <value>");
                        return;
                    }
                    break;
                case "width":
                    if (args.Length < 2 || !float.TryParse(args[1], out configData.UIWidth))
                    {
                        player.ChatMessage("Usage: /mczui width <value>");
                        return;
                    }
                    break;
                case "height":
                    if (args.Length < 2 || !float.TryParse(args[1], out configData.UIHeight))
                    {
                        player.ChatMessage("Usage: /mczui height <value>");
                        return;
                    }
                    break;
                case "reload":
                    LoadConfig();
                    foreach (var p in BasePlayer.activePlayerList)
                    {
                        DestroyUI(p);
                    }
                    break;
                default:
                    player.ChatMessage("Invalid command. Use: /mczui <position|offsetx|offsety|width|height|reload>");
                    return;

                
            }

            SaveConfig();
            player.ChatMessage("UI settings updated. Refreshing UI for all players.");
            InitializeUIForAllPlayers();
        }
        
        [ChatCommand("mcz")]
        private void CmdMiniCopterZone(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionAdmin))
            {
                player.ChatMessage("You don't have permission to use this command.");
                return;
            }

            if (args.Length == 0)
            {
                player.ChatMessage("Usage: /mcz <create|remove|list|reinit|update>");
                return;
            }

            switch (args[0].ToLower())
            {
                case "create":
                    if (args.Length < 4)
                    {
                        player.ChatMessage("Usage: /mcz create <name> <type> <size> [stack] [color]");
                        player.ChatMessage("For points: /mcz create <name> points <size> [stack] [color] [pointvalue]");
                        player.ChatMessage("For boost: /mcz create <name> boost <size> [stack] [color] [direction] [amount]");
                        return;
                    }
                    string name = args[1];
                    string type = args[2].ToLower();
                    float size = float.Parse(args[3]);
                    int stack = args.Length >= 5 ? int.Parse(args[4]) : 1;
                    string color = args.Length >= 6 ? args[5] : "red";

                    if (type == "points")
                    {
                        int pointValue = args.Length >= 7 ? int.Parse(args[6]) : 1;
                        CreateZone(player, name, type, size, stack, color, pointValue: pointValue);
                    }
                    else if (type == "boost")
                    {
                        string direction = args.Length >= 7 ? args[6] : "forward";
                        string amount = args.Length >= 8 ? args[7] : "10";
                        CreateZone(player, name, type, size, stack, color, direction, amount);
                    }
                    else
                    {
                        player.ChatMessage("Invalid zone type. Use 'points' or 'boost'.");
                    }
                    break;
                case "remove":
                    if (args.Length < 2)
                    {
                        player.ChatMessage("Usage: /mcz remove <name>");
                        return;
                    }
                    RemoveZone(player, args[1]);
                    break;
                case "list":
                    ListZones(player);
                    break;
                case "reinit":
                    ReinitializeZones(player);
                    break;
                case "update":
                    if (args.Length < 4)
                    {
                        player.ChatMessage("Usage: /mcz update <name> <property> <value>");
                        return;
                    }
                    UpdateZone(player, args[1], args[2], args[3]);
                    break;
                default:
                    player.ChatMessage("Invalid command. Use: /mcz <create|remove|list|reinit|update>");
                    break;
            }
        }

        #endregion

        #region Zone Management

        private void CreateZone(BasePlayer player, string name, string type, float size, int stack = 1, string color = "red", string boostDirection = "", string boostAmount = "", int pointValue = 1)
        {   
            if (name == "all") {
                player.ChatMessage("You can't name a zone 'all'.");
                return;
            }
            if (storedData.Zones.ContainsKey(name))
            {
                player.ChatMessage($"A zone with the name '{name}' already exists.");
                return;
            }

            var zoneType = type.ToLower() == "points" ? ZoneType.Points : ZoneType.Booster;
            var newZone = new Zone
            {
                Name = name,
                Position = player.transform.position,
                Rotation = player.transform.rotation,
                Size = size,
                Type = zoneType,
                Stack = stack,
                Color = color,
                boostDirection = boostDirection,
                boostAmount = boostAmount,
                PointValue = pointValue
            };

            storedData.Zones[name] = newZone;
            SaveData();

            CreateZoneEntity(newZone);
            CreateZoneVisual(newZone);
            
            spatialPartition.AddZone(newZone);

            if (zoneType == ZoneType.Points)
            {
                player.ChatMessage($"Created a new points zone named '{name}' at your current position with {pointValue} point(s).");
            }
            else
            {
                player.ChatMessage($"Created a new boost zone named '{name}' at your current position with direction {boostDirection} and amount {boostAmount}.");
            }
        }

        private void RemoveZone(BasePlayer player, string name)
        {

            if (name == "all") {
                foreach (var zone in storedData.Zones.Values)
                {
                    if (zoneEntities.TryGetValue(zone.Name, out var ve))
                    {
                        foreach (var entity in ve)
                        {
                            if (entity != null && !entity.IsDestroyed)
                                entity.Kill();
                                spatialPartition.RemoveZone(zone);
                        }
                        zoneEntities.Remove(zone.Name);
                    }
                }
                storedData.Zones.Clear();
                SaveData();
                player.ChatMessage("Removed all zones.");
                return;
            }

            if (!storedData.Zones.ContainsKey(name))
            {
                player.ChatMessage($"No zone found with the name '{name}'.");
                return;
            }

            

            if (zoneEntities.TryGetValue(name, out var visualEntities))
            {
                foreach (var entity in visualEntities)
                {
                    if (entity != null && !entity.IsDestroyed)
                        entity.Kill();
                }
                zoneEntities.Remove(name);
            }

            
            storedData.Zones.Remove(name);
            
            SaveData();
            RebuildSpatialPartition();
            player.ChatMessage($"Removed zone '{name}'.");
        }
        private void ListZones(BasePlayer player)
        {
            if (storedData.Zones.Count == 0)
            {
                player.ChatMessage("No zones have been created yet.");
                return;
            }

            player.ChatMessage("List of zones:");
            var pLocation = player.transform.position;
            int count = 0;
            foreach (var zone in storedData.Zones.Values)
            {
                float distance = Vector3.Distance(pLocation, zone.Position);
                player.ChatMessage($"- {zone.Name} ({zone.Type}) at {zone.Position} ({distance}m away)");
                count++;
                if (count >= 5)
                    break;
            }
        }

        private void ReinitializeZones(BasePlayer player)
        {
            InitializeZones();
            
            player.ChatMessage($"Reinitialized {storedData.Zones.Count} zones.");
        }

        private void UpdateZone(BasePlayer player, string name, string property, string value)
        {   
            
            if (!storedData.Zones.TryGetValue(name, out Zone zone))
            {
                player.ChatMessage($"No zone found with the name '{name}'.");
                return;
            }
            
            switch (property.ToLower())
            {
                case "size":
                    zone.Size = float.Parse(value);
                    break;
                case "stack":
                    zone.Stack = int.Parse(value);
                    break;
                case "color":
                    zone.Color = value;
                    break;
                case "direction":
                    zone.boostDirection = value;
                    break;
                case "amount":
                    zone.boostAmount = value;
                    break;
                case "pointvalue":
                    zone.PointValue = int.Parse(value);
                    break;
                default:
                    player.ChatMessage($"Invalid property '{property}'. Valid properties are: size, stack, color");
                    return;
            }

            SaveData();

            if (zoneEntities.TryGetValue(name, out var visualEntities))
            {
                foreach (var entity in visualEntities)
                {
                    if (entity != null && !entity.IsDestroyed)
                        entity.Kill();
                }
                zoneEntities.Remove(name);
            }
            spatialPartition.RemoveZone(zone);  

            CreateZoneVisual(zone);
            
            spatialPartition.AddZone(zone);
            
            player.ChatMessage($"Updated zone '{name}'. New {property}: {value}");
        }

        #endregion

        #region UI Management

        private void StartUIUpdateTimer()
        {
            StopUIUpdateTimer();
            uiUpdateTimer = timer.Every(1f, UpdateAllPlayersUI);
        }

        private void StopUIUpdateTimer()
        {
            if (uiUpdateTimer != null && !uiUpdateTimer.Destroyed)
            {
                uiUpdateTimer.Destroy();
            }
        }   

        private void UpdateAllPlayersUI()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (permission.UserHasPermission(player.UserIDString, PermissionUse))
                {
                    UpdateUI(player);
                }
            }
        }

        private void CreateUI(BasePlayer player)
        {
            DestroyUI(player);

            var ui = new CuiElementContainer();

            string anchorMin, anchorMax;
            CalculateUIPosition(out anchorMin, out anchorMax);

            var panel = ui.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = anchorMin, AnchorMax = anchorMax },
                Image = { Color = configData.UIBackgroundColor }
            }, "Hud", "MCZPointsPanel");

            ui.Add(new CuiLabel
            {
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                Text = { Text = "", Color = configData.UITextColor, Align = TextAnchor.MiddleCenter, FontSize = 14 }
            }, panel, "MCZPointsText");

            CuiHelper.AddUi(player, ui);
            playerUIs[player.userID] = panel;

            UpdateUIText(player);
        }

        private void UpdateUI(BasePlayer player)
        {
            if (!playerUIs.ContainsKey(player.userID))
            { 
                CreateUI(player);
            }
            else
            {
                UpdateUIText(player);
            }
        }

        private void UpdateUIText(BasePlayer player)
        {
            var playerData = GetPlayerData(player.userID);
            string streakText = playerData.Streak > 1 ? $"<color=#FFFF00> (x{playerData.Streak})</color>" : "";

            TimeSpan timeRemaining = playerData.LastPointTime.AddSeconds(60) - DateTime.UtcNow;
            string timerColor = timeRemaining.TotalSeconds <= 30 ? "#FF0000" : "#FFFFFF";
            string timerText = "";
            if (playerData.Streak > 0)
            {
            timerText = timeRemaining.TotalSeconds > 0 ? $"<color={timerColor}><size=10>{timeRemaining.Seconds:D2}s</size></color>" : "";
            }

            var elements = new CuiElementContainer();
            elements.Add(new CuiLabel
            {
            Text = { Text = $"HP: {playerData.Points}{streakText} {timerText}", Color = configData.UITextColor, Align = TextAnchor.MiddleCenter, FontSize = 14 }
            }, "MCZPointsPanel", "MCZPointsText");

            CuiHelper.DestroyUi(player, "MCZPointsText");

            CuiHelper.AddUi(player, elements);
        }

        private void CalculateUIPosition(out string anchorMin, out string anchorMax)
        {
            float xMin, xMax, yMin, yMax;

            switch (configData.UIPosition.ToLower())
            {
                case "topleft":
                    xMin = configData.UIOffsetX / 1920f;
                    yMin = 1 - ((configData.UIOffsetY + configData.UIHeight) / 1080f);
                    xMax = (configData.UIOffsetX + configData.UIWidth) / 1920f;
                    yMax = 1 - (configData.UIOffsetY / 1080f);
                    break;
                case "topright":
                    xMin = 1 - ((configData.UIOffsetX + configData.UIWidth) / 1920f);
                    yMin = 1 - ((configData.UIOffsetY + configData.UIHeight) / 1080f);
                    xMax = 1 - (configData.UIOffsetX / 1920f);
                    yMax = 1 - (configData.UIOffsetY / 1080f);
                    break;
                case "bottomleft":
                    xMin = configData.UIOffsetX / 1920f;
                    yMin = configData.UIOffsetY / 1080f;
                    xMax = (configData.UIOffsetX + configData.UIWidth) / 1920f;
                    yMax = (configData.UIOffsetY + configData.UIHeight) / 1080f;
                    break;
                case "bottomright":
                    xMin = 1 - ((configData.UIOffsetX + configData.UIWidth) / 1920f);
                    yMin = configData.UIOffsetY / 1080f;
                    xMax = 1 - (configData.UIOffsetX / 1920f);
                    yMax = (configData.UIOffsetY + configData.UIHeight) / 1080f;
                    break;
                default:
                    xMin = 0.5f - (configData.UIWidth / 3840f);
                    yMin = 0.5f - (configData.UIHeight / 2160f);
                    xMax = 0.5f + (configData.UIWidth / 3840f);
                    yMax = 0.5f + (configData.UIHeight / 2160f);
                    break;
            }

            anchorMin = $"{xMin} {yMin}";
            anchorMax = $"{xMax} {yMax}";
        }

        private void DestroyUI(BasePlayer player)
        {
            string panelName;
            if (playerUIs.TryGetValue(player.userID, out panelName))
            {
            CuiHelper.DestroyUi(player, panelName);
            playerUIs.Remove(player.userID);
            }
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                CreateUI(player);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            DestroyUI(player);
        }

        #endregion

        #region Point Management

        private PlayerData GetPlayerData(ulong playerId)
        {
            if (!storedData.Players.TryGetValue(playerId, out PlayerData playerData))
            {
                playerData = new PlayerData();
                storedData.Players[playerId] = playerData;
            }
            return playerData;
        }

        private int GetPlayerPoints(ulong playerId)
        {
            return playerPoints.ContainsKey(playerId) ? playerPoints[playerId] : 0;
        }

        private void SetPlayerPoints(ulong playerId, int points)
        {
            var playerData = GetPlayerData(playerId);
            playerData.Points = points;
            SaveData();

            var player = BasePlayer.Find(playerId.ToString());
            if (player != null)
            {
                UpdateUI(player);
            }
        }

        private void AwardPoints(BasePlayer player, PlayerHelicopter miniCopter, Zone zone)
        {
            SendEffect("assets/prefabs/deployable/playerioents/app/smartalarm/sound/smart-alarm-activate.asset", player.transform.position);

            ulong playerId = player.userID;
            var playerData = GetPlayerData(playerId);

            var rigidbody = miniCopter.GetComponent<Rigidbody>();
            float speed = rigidbody.velocity.magnitude;
            float minSpeed = 9f; // m/s

            if (speed < minSpeed)
            {
                return;
            }

            if ((DateTime.UtcNow - playerData.LastPointTime).TotalSeconds > 60)
            {
                ResetPlayerStreak(player);
            }

            playerData.Streak++;
            playerData.LastPointTime = DateTime.UtcNow;

            float multiplier = 1f;
            foreach (var kvp in configData.StreakMultipliers.OrderByDescending(x => x.Key))
            {
                if (playerData.Streak >= kvp.Key)
                {
                    multiplier = kvp.Value;
                    break;
                }
            }

            int pointsToAward = Mathf.RoundToInt(zone.PointValue * multiplier);
            foreach (var kvp in configData.SpeedMultipliers.OrderByDescending(x => x.Key))
            {
                if (speed >= kvp.Key)
                {
                    pointsToAward = Mathf.RoundToInt(pointsToAward * kvp.Value);
                    break;
                }
            }
            playerData.Points += pointsToAward;

            SaveData();
            DestroyUI(player);
            UpdateUI(player);

            var green = "<color=#00FF00>";
            var white = "<color=#FFFFFF>";
            var yellow = "<color=#FFFF00>";
            var pink = "<color=#FF00FF>";
            player.ChatMessage($"[HP]{green}+{pointsToAward}{white} Points {yellow}(x{playerData.Streak}) {pink}{speed:F1} m/s");
        }

        #endregion

        #region Zone Logic

        private void OnMiniCopterEnterZone(BaseVehicle miniCopter, Zone zone)
        {
            var playerHelicopter = miniCopter as PlayerHelicopter;
            if (playerHelicopter == null) return;

            var driver = playerHelicopter.mountPoints[0].mountable._mounted as BasePlayer;
            if (driver == null || !permission.UserHasPermission(driver.UserIDString, PermissionUse)) return;

            if (IsOnCooldown(driver.userID, zone.Name)) return;

            switch (zone.Type)
            {
                case ZoneType.Points:
                    AwardPoints(driver, playerHelicopter, zone);
                    break;
                case ZoneType.Booster:
                    BoostMiniCopter(playerHelicopter, driver, zone);
                    break;
            }

            SetCooldown(driver.userID, zone.Name);
        }

        private void BoostMiniCopter(BaseVehicle miniCopter, BasePlayer driver, Zone zone)
        {
            if (driver == null || miniCopter == null) return;

            var rigidbody = miniCopter.GetComponent<Rigidbody>();
            var currentVelocity = rigidbody.velocity;
            var lookDirection = driver.eyes.HeadForward();
            var boostAmount = float.Parse(zone.boostAmount);

            Vector3 boostDirection;
            switch (zone.boostDirection)
            {
                case "forward":
                    boostDirection = miniCopter.transform.forward;
                    break;
                case "up":
                    boostDirection = miniCopter.transform.up;
                    break;
                case "down":
                    boostDirection = -miniCopter.transform.up;
                    break;
                case "left":
                    boostDirection = -miniCopter.transform.right;
                    break;
                case "right":
                    boostDirection = miniCopter.transform.right;
                    break;
                case "look":
                    boostDirection = lookDirection;
                    break;
                default:
                    boostDirection = lookDirection;
                    break;
            }

            var boost = (boostDirection).normalized * boostAmount;
            rigidbody.velocity = currentVelocity + boost;

            miniCopter.SendNetworkUpdateImmediate();
            driver.ChatMessage("You've been boosted through a zone!");
        }

        #endregion

        #region ZoneEntity

        private class ZoneEntity : MonoBehaviour
        {
            public float Size { get; set; }
            public ZoneType Type { get; set; }

        }

        #endregion

        #region Cooldown Management

        private bool IsOnCooldown(ulong playerId, string zoneName)
        {
            if (!playerCooldowns.TryGetValue(playerId, out var cooldowns)) return false;
            if (!cooldowns.TryGetValue(zoneName, out var lastEntry)) return false;

            return (DateTime.UtcNow - lastEntry).TotalSeconds < 10;
        }

        private void SetCooldown(ulong playerId, string zoneName)
        {
            if (!playerCooldowns.TryGetValue(playerId, out var cooldowns))
            {
                cooldowns = new Dictionary<string, DateTime>();
                playerCooldowns[playerId] = cooldowns;
            }

            cooldowns[zoneName] = DateTime.UtcNow;
        }

        #endregion

        #region MiniCopter Component

        private class MiniCopterComponent : MonoBehaviour
        {
            private MiniCopterZones plugin;
            private PlayerHelicopter miniCopter;
            private HashSet<BasePlayer> currentPassengers = new HashSet<BasePlayer>();
            private HashSet<string> passedZones = new HashSet<string>();
            private float updateInterval = 0.1f;
            private float lastUpdateTime = 0f;

            private void Awake()
            {
                plugin = Interface.Oxide.RootPluginManager.GetPlugin("MiniCopterZones") as MiniCopterZones;
                miniCopter = GetComponent<PlayerHelicopter>();
            }

            private void FixedUpdate()
            {
                if (Time.time - lastUpdateTime < updateInterval)
                    return;

                lastUpdateTime = Time.time;

                UpdatePassengers();

                var nearbyZones = plugin.spatialPartition.GetNearbyZones(miniCopter.transform.position);
                foreach (var zone in nearbyZones)
                {
                    if (IsInZone(zone))
                    {
                        if (!passedZones.Contains(zone.Name))
                        {
                            passedZones.Add(zone.Name);
                            plugin.OnMiniCopterEnterZone(miniCopter, zone);
                        }
                    }
                    else
                    {
                        passedZones.Remove(zone.Name);
                    }
                }
            }

            private void UpdatePassengers()
            {
                var newPassengers = new HashSet<BasePlayer>();
                foreach (var mountPoint in miniCopter.mountPoints)
                {
                    if (mountPoint.mountable?._mounted != null)
                    {
                        var player = mountPoint.mountable._mounted;
                        newPassengers.Add(player);
                        
                        plugin.playerCurrentMinicopter[player.userID] = miniCopter;
                    }
                }

                foreach (var player in currentPassengers)
                {
                    if (!newPassengers.Contains(player))
                    {
                        plugin.playerCurrentMinicopter.Remove(player.userID);
                        plugin.ResetPlayerStreak(player);
                    }
                }

                currentPassengers = newPassengers;
            }

            private bool IsInZone(Zone zone)
            {
                var zoneTransform = new GameObject().transform;
                zoneTransform.position = zone.Position;
                zoneTransform.rotation = zone.Rotation;

                var localPos = zoneTransform.InverseTransformPoint(miniCopter.transform.position);
                return localPos.magnitude <= zone.Size / 2f;
            }
        }

        #endregion
    }
}