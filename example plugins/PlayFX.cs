using System;
using System.Collections.Generic;
using System.Linq;
using Network;
using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Play FX", "misticos", "2.0.1")]
    [Description("Play any effect on a player, such as an explosion sound.")]
    class PlayFX : CovalencePlugin
    {
        #region Variables

        private const string PermissionUse = "playfx.use";
        
        #endregion
        
        #region Configuration

        private Configuration _config;

        private class Configuration
        {
            [JsonProperty(PropertyName = "Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public string[] Commands = { "playfx.run", "playfx" };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
                SaveConfig();
            }
            catch
            {
                PrintError("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        protected override void LoadDefaultConfig() => _config = new Configuration();

        #endregion
        
        #region Hooks

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"Command: Syntax", "Syntax:\n" +
                                    "player (Name or ID) (Prefab) - Play this prefab for one player\n" +
                                    "all (Prefab) - Play this prefab for all players"},
                {"Command: User Not Found", "The user you've specified was not found."},
                {"Command: Prefab Not Found", "Prefab was not found."},
                {"Command: Effect Played", "The effect has been played."},
                {"Command: No Permission", "You do not have enough permissions."}
            }, this);
        }

        private void Init()
        {
            permission.RegisterPermission(PermissionUse, this);
            
            AddCovalenceCommand(_config.Commands, nameof(CommandRun));
        }

        #endregion
        
        #region Commands

        private void CommandRun(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(PermissionUse))
            {
                player.Reply(GetMsg("Command: No Permission", player.Id));
                return;
            }
            
            if (args.Length < 2)
                goto syntax;

            IEnumerable<BasePlayer> targets;
            string effect;
            
            switch (args[0].ToLower())
            {
                case "player":
                {
                    if (args.Length < 3)
                        goto syntax;
                    
                    var target = players.FindPlayer(args[1])?.Object as BasePlayer;
                    if (target == null)
                    {
                        player.Reply(GetMsg("Command: User Not Found", player.Id));
                        return;
                    }

                    effect = string.Join(" ", args.Skip(2));
                    targets = new[] {target};
                    break;
                }

                case "all":
                {
                    effect = string.Join(" ", args.Skip(1));
                    targets = BasePlayer.activePlayerList;
                    break;
                }
                
                default:
                    goto syntax;
            }

            var effectId = StringPool.Get(effect);
            if (effectId == 0)
            {
                player.Reply(GetMsg("Command: Prefab Not Found", player.Id));
                return;
            }
            
            RunEffect(targets, effect, effectId);

            player.Reply(GetMsg("Command: Effect Played", player.Id));
            return;
            
            syntax:
            player.Reply(GetMsg("Command: Syntax", player.Id));
        }
        
        #endregion
        
        #region Helpers

        private Effect _effect = new Effect {broadcast = true};

        private void RunEffect(IEnumerable<BasePlayer> targets, string prefab, uint prefabId)
        {
            _effect.Init(Effect.Type.Generic, Vector3.zero, Vector3.zero);
            _effect.pooledString = prefab;
            _effect.pooledstringid = prefabId;

            foreach (var target in targets)
            {
                var write = Net.sv.StartWrite();
                
                write.PacketID(Message.Type.Effect);

                _effect.entity = target.net.ID;
                _effect.worldPos = target.transform.position;
                _effect.WriteToStream(write);

                write.Send(new SendInfo(target.net.connection));
            }
        }

        private string GetMsg(string key, string id = null) => lang.GetMessage(key, this, id);

        #endregion
    }
}