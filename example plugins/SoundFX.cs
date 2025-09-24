using UnityEngine;
using Oxide.Core.Libraries.Covalence;
using System.Collections.Generic;
using Oxide.Core;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("Sound FX", "Lincoln", "1.0.3")]
    [Description("Simulate various Rust sound effects")]

    class SoundFX : RustPlugin
    {
        private const string permUse = "soundfx.use";
        private const string permBypassCooldown = "soundfx.bypasscooldown.use";
        private readonly Hash<string, float> cooldowns = new Hash<string, float>();

        #region Variables
        private List<string> ricochetEffects = new List<string>()
        {
            "assets/bundled/prefabs/fx/ricochet/ricochet1.prefab",
            "assets/bundled/prefabs/fx/ricochet/ricochet2.prefab",
            "assets/bundled/prefabs/fx/ricochet/ricochet3.prefab",
            "assets/bundled/prefabs/fx/ricochet/ricochet4.prefab",
        };

        string explosion = "assets/bundled/prefabs/fx/explosions/explosion_03.prefab";
        string vomit = "assets/bundled/prefabs/fx/gestures/drink_vomit.prefab";
        string landmine = "assets/bundled/prefabs/fx/weapons/landmine/landmine_explosion.prefab";
        string scream = "assets/bundled/prefabs/fx/player/beartrap_scream.prefab";
        string fallDamage = "assets/bundled/prefabs/fx/player/fall-damage.prefab";
        string howl = "assets/bundled/prefabs/fx/player/howl.prefab";
        string lick = "assets/bundled/prefabs/fx/gestures/lick.prefab";
        string headshot = "assets/bundled/prefabs/fx/headshot_2d.prefab";
        string chatter = "assets/prefabs/npc/scientist/sound/chatter.prefab";
        string manDown = "assets/prefabs/npc/scientist/sound/responddeath.prefab";
        string roger = "assets/prefabs/npc/scientist/sound/respondok.prefab";
        string takeCover = "assets/prefabs/npc/scientist/sound/takecover.prefab";
        string slurp = "assets/bundled/prefabs/fx/gestures/drink_tea.prefab";

        string fish = "assets/prefabs/misc/decor_dlc/huntingtrophy_fish/effects/hunting-trophy-fish-song.prefab";
        string test = "assets/prefabs/npc/murderer/sound/breathing.prefab"; //for testing purposes only
        #endregion

        #region PluginConfig
        //Creating a config file
        private static PluginConfig config;
        private class PluginConfig
        {
            [JsonProperty(PropertyName = "Max Cooldown: ")] public float maxCooldown { get; set; }
            [JsonProperty(PropertyName = "Max Radius: ")] public float maxRadius { get; set; }


            public static PluginConfig DefaultConfig() => new PluginConfig()
            {
                maxCooldown = 5,
                maxRadius = 5
            };

        }
        protected override void LoadDefaultConfig()
        {
            PrintWarning("New configuration file created.");
            config = PluginConfig.DefaultConfig();
        }
        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<PluginConfig>();
            SaveConfig();
        }
        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }
        #endregion

        #region Permissions
        private void Init()
        {
            permission.RegisterPermission(permUse, this);
            permission.RegisterPermission(permBypassCooldown, this);
        }
        #endregion

        private bool hasPermission(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, permUse))
            {
                ChatMessage(player, "NoPerm");
                return false;
            }
            return true;
        }

        bool OnCoolDown(BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, permBypassCooldown)) return false;

            if (!cooldowns.ContainsKey(player.UserIDString))
            {
                cooldowns.Add(player.UserIDString, 0f);
            }

            if (config.maxCooldown > 0 && cooldowns[player.UserIDString] + config.maxCooldown > Interface.Oxide.Now)
            {
                ChatMessage(player, "Cooldown", config.maxCooldown);
                foreach (KeyValuePair<string, float> kvp in cooldowns) Puts("Key: {0}, Value: {1}", kvp.Key, kvp.Value);
                return true;
            }
            cooldowns[player.UserIDString] = Interface.Oxide.Now;
            return false;
        }

        #region Commands
        [ChatCommand("fx")]
        private void fxCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null || !hasPermission(player)) return;

            if (args.IsNullOrEmpty())
            {
                ChatMessage(player, "Help");
                return;
            }

            if (OnCoolDown(player))
            {
                return;
            }

            Vector3 pos = player.transform.position + UnityEngine.Random.insideUnitSphere * config.maxRadius;

            switch (args[0].ToLower())
            {
                case "ricochet":
                    SendEffect(ricochetEffects.GetRandom(), pos);
                    break;

                case "scream":
                    SendEffect(scream, pos);
                    break;

                case "explosion":
                    SendEffect(explosion, pos);
                    break;

                case "vomit":
                    SendEffect(vomit, pos);
                    break;

                case "slurp":
                    SendEffect(slurp, pos);
                    break;

                case "landmine":
                    SendEffect(landmine, pos);
                    break;

                case "fall":
                    SendEffect(fallDamage, pos);
                    break;

                case "howl":
                    SendEffect(howl, pos);
                    break;

                case "lick":
                    SendEffect(lick, pos);
                    break;

                case "headshot":
                    SendEffect(headshot, pos);
                    break;

                case "chatter":
                    SendEffect(chatter, pos);
                    break;

                case "mandown":
                    SendEffect(manDown, pos);
                    break;

                case "roger":
                    SendEffect(roger, pos);
                    break;

                case "takecover":
                    SendEffect(takeCover, pos);
                    break;

                case "help":
                    ChatMessage(player, "Help");
                    break;

                case "test":
                    SendEffect(test, pos);
                    break;

                case "fish":
                    SendEffect(fish, pos);
                    break;

                default:
                    ChatMessage(player, "Invalid");
                    break;
            }
        }

        #endregion

        #region Helpers
        private void SendEffect(string prefabName, Vector3 pos) => Effect.server.Run(prefabName, pos);
        #endregion

        #region Localization

        private void ChatMessage(BasePlayer player, string messageName, params object[] args) =>
            player.ChatMessage(string.Format(GetMessage(player.IPlayer, messageName), args));

        private string GetMessage(IPlayer player, string messageName, params object[] args) =>
            GetMessage(player.Id, messageName, args);

        private string GetMessage(string playerId, string messageName, params object[] args)
        {
            var message = lang.GetMessage(messageName, this, playerId);
            return args.Length > 0 ? string.Format(message, args) : message;
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Help"] = "<size=18 ><color=#ffc34d>SoundFX</color></size>" +
                "\n<color=#9999ff>/fx ricochet</color> Bullet ricochet." +
                "\n<color=#9999ff>/fx scream</color> Wounded scream." +
                "\n<color=#9999ff>/fx explosion</color> Grenade explosion." +
                "\n<color=#9999ff>/fx slurp</color> Slurping sound effect." +
                "\n<color=#9999ff>/fx vomit</color> Vomit sound effect." +
                "\n<color=#9999ff>/fx landmine</color> Land mine explosion." +
                "\n<color=#9999ff>/fx fall</color> Breaking your knees." +
                "\n<color=#9999ff>/fx howl</color> Wolf howling." +
                "\n<color=#9999ff>/fx lick</color> Gross licking/slurping sound." +
                "\n<color=#9999ff>/fx headshot</color> Headshot sound effect " +
                "\n<color=#9999ff>/fx roger</color> Scientist radio 'roger that' " +
                "\n<color=#9999ff>/fx takecover</color> Scientist radio 'take cover' " +
                "\n<color=#9999ff>/fx mandown</color> Scientist radio 'man down'" +
                "\n<color=#9999ff>/fx chatter</color> Scientist radio chatter. " +
                "\n <color=#9999ff>/fx fish</color> Fish song.",
                ["NoPerm"] = "<color=#ffc34d>SoundFX</color>: You don't have permission to use that.",
                ["Cooldown"] = "<color=#ffc34d>SoundFX</color>: You are still on a {0} second cooldown.",
                ["Invalid"] = "<color=#ffc34d>SoundFX</color> Not a valid FX command."

            }, this, "en");
        }
        #endregion

        private void Unload()
        {
            config = null;
        }
    }
}