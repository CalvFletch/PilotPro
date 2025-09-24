using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Plugins;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("Toastify", "yUN", "2.0.2")]
    [Description("Create custom toast notifications")]

    public partial class Toastify : RustPlugin
    {
        #region Fields

        [PluginReference]
        private Plugin ImageLibrary = null;

        private static Toastify ins;

        private const string permissionName = "toastify.use";

        private string renderLayer = null;
        private bool floatLeft = false;

        private class Toast
        {
            public string id = Guid.NewGuid().ToString();
            public string toastId;
            public string title;
            public string content;
            public float duration;
            public bool showing;
        }

        #endregion

        #region Hooks

        private void OnServerInitialized()
        {
            ins = this;

            permission.RegisterPermission(permissionName, this);

            floatLeft = config.General.Float.ToLower() == "left";
            renderLayer = config.General.RenderLayer;

            if (ImageLibrary != null)
            {
                CacheImages();
            }
        }

        private void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player.TryGetComponent<Toaster>(out var component))
                {
                    UnityEngine.Object.Destroy(component);
                }
            }

            ins = null;
        }

        #endregion

        #region Helpers

        #region ImageLibrary

        private void CacheImages()
        {
            Dictionary<string, string> importImages = new Dictionary<string, string>();

            foreach (var pair in config.Toasts)
            {
                ToastConfig toastConfig = pair.Value;

                if (IsImageUrl(toastConfig.Icon?.Url))
                {
                    importImages.Add($"toastify.{pair.Key}.icon", toastConfig.Icon.Url);
                }

                if (toastConfig.Background != null)
                {
                    if (IsImageUrl(toastConfig.Background.ImageUrl))
                    {
                        importImages.Add($"toastify.{pair.Key}.background", toastConfig.Background.ImageUrl);
                    }

                    if (toastConfig.Background.Decorations != null && toastConfig.Background.Decorations.Length > 0)
                    {
                        foreach (var decoration in toastConfig.Background.Decorations)
                        {
                            if (IsImageUrl(decoration.ImageUrl))
                            {
                                importImages.Add(decoration.ImageUrl, decoration.ImageUrl);
                            }
                        }
                    }
                }
            }

            if (importImages.Count > 0)
            {
                ImageLibrary.CallHook("ImportImageList", Title, importImages, 0ul, true);
            }
        }

        private string GetImage(string name)
        {
            if (ImageLibrary == null)
            {
                return null;
            }

            return ImageLibrary.Call<string>("GetImage", name, 0ul, true);
        }

        #endregion

        private Toaster GetToaster(BasePlayer player)
        {
            return player.TryGetComponent<Toaster>(out var toaster)
                ? toaster
                : player.gameObject.AddComponent<Toaster>();
        }

        private bool IsImageUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                return false;
            }

            return true;
        }

        #endregion

        #region API

        [HookMethod("SendToast")]
        private string SendToast(BasePlayer player, string toastId, string title, string content = null, float duration = 0f)
        {
            if (!config.Toasts.ContainsKey(toastId))
            {
                throw new Exception("Invalid Toast ID");
            }

            Toast toast = new Toast()
            {
                toastId = toastId,
                title = content == null ? null : title,
                content = content == null ? title : content,
                duration = duration
            };

            Toaster toaster = GetToaster(player);
            toaster.ShowToast(toast);

            string effect = config.Toasts[toastId].EffectPrefab;
            if (!string.IsNullOrEmpty(effect))
            {
                Effect.server.Run(effect, player, 0u, Vector3.zero, Vector3.zero);
            }

            return toast.id;
        }

        [HookMethod("DestroyToast")]
        private void DestroyToast(BasePlayer player, string toastId)
        {
            Toaster toaster = player.GetComponent<Toaster>();
            if (toaster != null)
            {
                toaster.DestroyToast(toastId);
            }
        }

        #endregion

        #region MonoBehaviour

        private class Toaster : MonoBehaviour
        {
            private BasePlayer player;

            private List<Toast> queue = new List<Toast>();

            private int ShowingCount { get => queue.Count((x) => x.showing); }
            private int RenderLimit { get => Mathf.Min(ins.config.General.MaxToasts, queue.Count); }

            private void Awake()
            {
                player = GetComponent<BasePlayer>();
            }

            private void OnDestroy()
            {
                foreach (Toast toast in queue.Where((x) => x.showing).ToArray())
                {
                    CuiHelper.DestroyUi(player, toast.id);
                }

                queue.Clear();
            }

            private void UpdateToasts()
            {
                for (int i = 0; i < RenderLimit; i++)
                {
                    Toast toast = queue[i];
                    StartCoroutine(RenderToast(toast, i));
                }
            }

            private IEnumerator RenderToast(Toast toast, int index)
            {
                ToastConfig settings = ins.config.Toasts[toast.toastId];

                PrintDebug("Rendering a new toast[{0}] ({1})", index, toast.toastId);

                float y = index * (settings.Height + ins.config.General.Spacing);
                ins.RenderToast(player, toast, y);

                PrintDebug("Toast[{0}] rendered with content:\n{1}", index, toast.content);

                if (!toast.showing)
                {
                    queue[index].showing = true;

                    // Wait the duration
                    yield return CoroutineEx.waitForSeconds(toast.duration > 0f ? toast.duration : settings.Duration);

                    // Destroy the ui
                    DestroyToast(toast.id);
                }
            }

            public void ShowToast(Toast toast)
            {
                queue.Add(toast);
                if (ShowingCount < RenderLimit)
                {
                    StartCoroutine(RenderToast(toast, ShowingCount));
                }
            }

            public void DestroyToast(string id)
            {
                int index = queue.FindIndex((x) => x.id == id);
                if (index > -1)
                {
                    PrintDebug("Destroying toast[{0}] ID: {1}", index, id);
                    queue.RemoveAt(index);
                    CuiHelper.DestroyUi(player, id);
                    UpdateToasts();
                }
            }
        }

        #endregion

        #region UI

        private void RenderToast(BasePlayer player, Toast toast, float startY)
        {
            if (string.IsNullOrEmpty(toast.id))
            {
                toast.id = CuiHelper.GetGuid();
            }

            // Set the settings to shortname variables
            ToastConfig s = config.Toasts[toast.toastId];
            ToastBackgroundSettings bg = s.Background;
            ToastIconSettings icon = s.Icon;
            ToastTitleSettings title = s.Title;
            ToastTextSettings text = s.Text;

            float mx = config.General.MarginX;
            float mt = config.General.MarginTop;
            float fade = toast.showing ? 0f : .15f;

            // Instantiate the ui builder
            UIBuilder ui = new UIBuilder();

            string anchor = floatLeft ? "0 1 0 1" : "1 1 1 1";
            string offset = null;

            if (floatLeft)
            {
                offset = $"{mx} -{s.Height + startY + mt} {mx + s.Width} -{startY + mt}";
            }
            else
            {
                offset = $"-{mx + s.Width} -{s.Height + startY + mt} -{mx} -{startY + mt}";
            }

            ui.AddPanel(renderLayer, "0 0 0 0", anchor, offset, name: toast.id);

            // Background color
            if (!string.IsNullOrEmpty(bg.Color))
            {
                ui.AddPanel(
                    toast.id,
                    UIBuilder.ParseColor(bg.Color, bg.Opacity),
                    "0 0 1 1",
                    "0 0 0 0",
                    bg.Material,
                    bg.Sprite,
                    fade
                );
            }
            // Background image
            if (!string.IsNullOrEmpty(bg.ImageUrl))
            {
                ui.AddImage(toast.id, GetImage($"toastify.{toast.toastId}.background") ?? bg.ImageUrl, null, "0 0 1 1", "0 0 0 0", fade);
            }

            // Background decorations
            if (bg.Decorations != null && bg.Decorations.Length > 0)
            {
                int i = 0;
                foreach (var d in bg.Decorations)
                {
                    string name = $"{toast.id}.decoration.{i++}";
                    string color = !string.IsNullOrEmpty(d.BackgroundColor)
                        ? UIBuilder.ParseColor(d.BackgroundColor)
                        : null;

                    float x = d.PosX, y = d.PosY, w = d.Width, h = d.Height;
                    ui.AddPanel(
                        toast.id,
                        color,
                        "0 0 0 0",
                        $"{x} {y} {x + w} {y + h}",
                        fadeIn: fade,
                        name: name
                    );

                    if (!string.IsNullOrEmpty(d.ImageUrl))
                    {
                        ui.AddImage(name, GetImage(d.ImageUrl) ?? d.ImageUrl, null, "0 0 1 1", "0 0 0 0", fade);
                    }
                }
            }

            // Add the icon
            if (!string.IsNullOrEmpty(icon.Url))
            {
                float x = icon.PosX, y = icon.PosY, size = icon.Size;

                string iconImage = IsImageUrl(icon.Url)
                    ? GetImage($"toastify.{toast.toastId}.icon") ?? icon.Url
                    : icon.Url;

                ui.AddImage(
                    toast.id,
                    iconImage,
                    icon.Color != null ? UIBuilder.ParseColor(icon.Color) : null,
                    "0 0 0 0",
                    $"{x} {y} {x + size} {y + size}",
                    fade
                );
            }

            // Add the title
            if (!string.IsNullOrEmpty(title.LangKey))
            {
                float x = title.PosX, y = title.PosY, w = title.Width, h = title.Height;
                ui.AddText(
                    toast.id,
                    string.IsNullOrEmpty(toast.title) ? Msg(player, title.LangKey) : toast.title,
                    "0 0 0 0",
                    $"{x} {y} {x + w} {y + h}",
                    title.FontSize,
                    title.Font,
                    title.Align,
                    fade,
                    0f
                );
            }

            // Add the text
            if (!string.IsNullOrEmpty(toast.content))
            {
                float x = text.PosX, y = text.PosY, w = text.Width, h = text.Height;
                ui.AddText(
                    toast.id,
                    toast.content,
                    "0 0 0 0",
                    $"{x} {y} {x + w} {y + h}",
                    text.FontSize,
                    text.Font,
                    text.Align,
                    fade,
                    0f
                );
            }

            // Add a transparent button
            if (!string.IsNullOrEmpty(s.Command) || s.Closable)
            {
                ui.AddButton(
                    toast.id,
                    $"toastify.execute {toast.id} {s.Closable} {(string.IsNullOrEmpty(s.Command) ? "" : s.Command)}".TrimEnd(),
                    "0 0 1 1",
                    "0 0 0 0"
                );
            }

            ui.Add(player);
        }

        #endregion

        #region Commands

        [ConsoleCommand("toastify")]
        private void ConsoleCommandToastify(ConsoleSystem.Arg arg)
        {
            if (arg == null)
            {
                return;
            }

            BasePlayer player = arg.Player();
            if (!arg.IsRcon && player == null)
            {
                return;
            }

            if (player != null && !player.IsAdmin && !permission.UserHasPermission(player.UserIDString, permissionName))
            {
                arg.ReplyWith(Msg(player, "NoPermission"));
                return;
            }

            if (!arg.HasArgs(3))
            {
                arg.ReplyWith(Msg(player, "SyntaxError"));
                return;
            }

            string toastId = arg.Args[0];
            if (!config.Toasts.ContainsKey(toastId))
            {
                arg.ReplyWith(Msg(player, "InvalidToast", toastId));
                return;
            }

            BasePlayer target = BasePlayer.Find(arg.Args[1]);
            if (target == null)
            {
                arg.ReplyWith(Msg(player, "PlayerNotFound"));
                return;
            }

            SendToast(target, toastId, string.Join(' ', arg.Args.Skip(2)));
            arg.ReplyWith(Msg(player, "ToastSent", target.displayName));
        }

        [ConsoleCommand("toastify.global")]
        private void ConsoleCommandToastifyGlobal(ConsoleSystem.Arg arg)
        {
            if (arg == null)
            {
                return;
            }

            BasePlayer player = arg.Player();
            if (!arg.IsRcon && player == null)
            {
                return;
            }

            if (player != null && !player.IsAdmin && !permission.UserHasPermission(player.UserIDString, permissionName))
            {
                arg.ReplyWith(Msg(player, "NoPermission"));
                return;
            }

            if (!arg.HasArgs(2))
            {
                arg.ReplyWith(Msg(player, "SyntaxErrorGlobal"));
                return;
            }

            string toastId = arg.Args[0];
            if (!config.Toasts.ContainsKey(toastId))
            {
                arg.ReplyWith(Msg(player, "InvalidToast", toastId));
                return;
            }

            string content = string.Join(' ', arg.Args.Skip(1));
            foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
            {
                SendToast(activePlayer, toastId, content);
            }

            arg.ReplyWith(Msg(player, "ToastSentGlobal", BasePlayer.activePlayerList.Count));
        }

        [ConsoleCommand("toastify.execute")]
        private void ConsoleCommandExecute(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg?.Player();
            if (player == null || !arg.HasArgs(2))
            {
                return;
            }

            bool close = arg.Args[1].ToLower() == "true";
            string toastId = arg.Args[0];
            string command = string.Join(' ', arg.Args.Skip(2));

            Toaster toaster = player.GetComponent<Toaster>();
            if (toaster == null)
            {
                return;
            }

            if (close)
            {
                toaster.DestroyToast(toastId);
            }

            if (!string.IsNullOrEmpty(command))
            {
                rust.RunServerCommand(command);
            }
        }

        #endregion

        #region Configuration

        private Configuration config;

        #region Toast config

        private class ToastTextSettings
        {
            [JsonProperty("Text align (combination with: 'top middle bottom' and 'left center right')")]
            public string Align { get; set; }

            [JsonProperty("Font")]
            public string Font { get; set; }

            [JsonProperty("Font size")]
            public int FontSize { get; set; }

            [JsonProperty("Width")]
            public float Width { get; set; }

            [JsonProperty("Height")]
            public float Height { get; set; }

            [JsonProperty("Position X")]
            public float PosX { get; set; }

            [JsonProperty("Position Y")]
            public float PosY { get; set; }
        }

        private class ToastTitleSettings
        {
            [JsonProperty("Lang key")]
            public string LangKey { get; set; }

            [JsonProperty("Text align (combination with: 'top middle bottom' and 'left center right')")]
            public string Align { get; set; }

            [JsonProperty("Font")]
            public string Font { get; set; }

            [JsonProperty("Font size")]
            public int FontSize { get; set; }

            [JsonProperty("Width")]
            public float Width { get; set; }

            [JsonProperty("Height")]
            public float Height { get; set; }

            [JsonProperty("Position X")]
            public float PosX { get; set; }

            [JsonProperty("Position Y")]
            public float PosY { get; set; }
        }

        private class ToastIconSettings
        {
            [JsonProperty("Url")]
            public string Url { get; set; }

            [JsonProperty("Color")]
            public string Color { get; set; }

            [JsonProperty("Size")]
            public float Size { get; set; }

            [JsonProperty("Position X")]
            public float PosX { get; set; }

            [JsonProperty("Position Y")]
            public float PosY { get; set; }
        }

        private class ToastBackgroundDecorationSettings
        {
            [JsonProperty("Width")]
            public float Width { get; set; }

            [JsonProperty("Height")]
            public float Height { get; set; }

            [JsonProperty("Position X")]
            public float PosX { get; set; }

            [JsonProperty("Position Y")]
            public float PosY { get; set; }

            [JsonProperty("Background color")]
            public string BackgroundColor { get; set; }

            [JsonProperty("Image URL")]
            public string ImageUrl { get; set; }
        }

        private class ToastBackgroundSettings
        {
            [JsonProperty("Color")]
            public string Color { get; set; }

            [JsonProperty("Opacity")]
            public float Opacity { get; set; }

            [JsonProperty("Image URL")]
            public string ImageUrl { get; set; }

            [JsonProperty("Material")]
            public string Material { get; set; }

            [JsonProperty("Sprite")]
            public string Sprite { get; set; }

            [JsonProperty("Decorations")]
            public ToastBackgroundDecorationSettings[] Decorations { get; set; }
        }

        private class ToastConfig
        {
            [JsonProperty("Default duration")]
            public float Duration { get; set; }

            [JsonProperty("Width")]
            public float Width { get; set; }

            [JsonProperty("Height")]
            public float Height { get; set; }

            [JsonProperty("Background")]
            public ToastBackgroundSettings Background { get; set; }

            [JsonProperty("Icon settings")]
            public ToastIconSettings Icon { get; set; }

            [JsonProperty("Title settings")]
            public ToastTitleSettings Title { get; set; }

            [JsonProperty("Text settings")]
            public ToastTextSettings Text { get; set; }

            [JsonProperty("Close on click?")]
            public bool Closable { get; set; }

            [JsonProperty("Command on click")]
            public string Command { get; set; }

            [JsonProperty("Effect prefab")]
            public string EffectPrefab { get; set; }
        }

        #endregion

        private class GeneralConfig
        {
            [JsonProperty("Render layer")]
            public string RenderLayer { get; set; }

            [JsonProperty("Float side (left or right)")]
            public string Float { get; set; }

            [JsonProperty("Max toasts in the screen")]
            public int MaxToasts { get; set; }

            [JsonProperty("Margin X")]
            public float MarginX { get; set; }

            [JsonProperty("Margin top")]
            public float MarginTop { get; set; }

            [JsonProperty("Spacing between toasts")]
            public float Spacing { get; set; }
        }

        private class Configuration
        {
            [JsonProperty("General")]
            public GeneralConfig General { get; set; }

            [JsonProperty("Toasts", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, ToastConfig> Toasts { get; set; }

            [JsonProperty("Debug mode")]
            public bool Debug { get; set; }
        }

        private Configuration GetBaseConfig()
        {
            return new Configuration()
            {
                General = new GeneralConfig()
                {
                    RenderLayer = "Overlay",
                    Float = "right",
                    MaxToasts = 8,
                    MarginTop = 24f,
                    MarginX = 24f,
                    Spacing = 6f
                },
                Toasts = new Dictionary<string, ToastConfig>()
                {
                    ["success"] = new ToastConfig()
                    {
                        Width = 260f,
                        Height = 60f,
                        Duration = 10f,
                        Closable = true,
                        EffectPrefab = "assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab",
                        Background = new ToastBackgroundSettings()
                        {
                            Color = "#5b7038",
                            Opacity = 1f,
                            Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                            Decorations = new ToastBackgroundDecorationSettings[]
                            {
                                new ToastBackgroundDecorationSettings()
                                {
                                    Width = 60f,
                                    Height = 60f,
                                    PosX = 0f,
                                    PosY = 0f,
                                    BackgroundColor = "0 0 0 0.4"
                                }
                            }
                        },
                        Icon = new ToastIconSettings()
                        {
                            Color = "#c4ff61",
                            PosX = 16f,
                            PosY = 16f,
                            Size = 28f,
                            Url = "assets/icons/check.png"
                        },
                        Title = new ToastTitleSettings()
                        {
                            LangKey = "ToastSuccessTitle",
                            FontSize = 14,
                            Width = 176f,
                            Height = 18f,
                            PosX = 72f,
                            PosY = 36f,
                            Align = "middle left"
                        },
                        Text = new ToastTextSettings()
                        {
                            Font = "robotocondensed-regular.ttf",
                            FontSize = 11,
                            Width = 176f,
                            Height = 28f,
                            PosX = 72f,
                            PosY = 6f,
                            Align = "top left"
                        }
                    },
                    ["error"] = new ToastConfig()
                    {
                        Width = 260f,
                        Height = 60f,
                        Duration = 10f,
                        Closable = true,
                        EffectPrefab = "assets/prefabs/weapons/toolgun/effects/repairerror.prefab",
                        Background = new ToastBackgroundSettings()
                        {
                            Color = "#cd412b",
                            Opacity = 1f,
                            Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                            Decorations = new ToastBackgroundDecorationSettings[]
                            {
                                new ToastBackgroundDecorationSettings()
                                {
                                    Width = 60f,
                                    Height = 60f,
                                    PosX = 0f,
                                    PosY = 0f,
                                    BackgroundColor = "0 0 0 0.4"
                                }
                            }
                        },
                        Icon = new ToastIconSettings()
                        {
                            Color = "#ff9989",
                            PosX = 16f,
                            PosY = 16f,
                            Size = 28f,
                            Url = "https://i.ibb.co/xm7ZwPh/x-512.png"
                        },
                        Title = new ToastTitleSettings()
                        {
                            LangKey = "ToastErrorTitle",
                            FontSize = 14,
                            Width = 176f,
                            Height = 18f,
                            PosX = 72f,
                            PosY = 36f,
                            Align = "middle left"
                        },
                        Text = new ToastTextSettings()
                        {
                            Font = "robotocondensed-regular.ttf",
                            FontSize = 11,
                            Width = 176f,
                            Height = 28f,
                            PosX = 72f,
                            PosY = 6f,
                            Align = "top left"
                        }
                    }
                },
                Debug = false
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new Exception();
                }
            }
            catch
            {
                Interface.Oxide.LogWarning("Your configuration file is corrupted; Using default configuration instead;");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        protected override void LoadDefaultConfig()
        {
            config = GetBaseConfig();
        }

        #endregion

        #region Utils

        private static void PrintDebug(string message, params object[] args)
        {
            if (ins.config.Debug)
            {
                Interface.Oxide.LogDebug(message, args);
            }
        }

        #endregion

        #region Localization

        private string Msg(BasePlayer player, string key, params object[] args)
        {
            return string.Format(lang.GetMessage(key, this, player?.UserIDString), args);
        }

        protected override void LoadDefaultMessages()
        {
            Dictionary<string, string> messages = new Dictionary<string, string>()
            {
                { "NoPermission", "You don't have permission to use this command." },
                { "SyntaxError", "toastify [toast_id] [player] [message]" },
                { "SyntaxErrorGlobal", "toastify.global [toast_id] [message]" },
                { "InvalidToast", "No toast was found with this id" },
                { "PlayerNotFound", "No player was found with this name or id" },
                { "ToastSent", "You sent a toast to {0}!" },
                { "ToastSentGlobal", "You sent a toast to {0} players!" }
            };

            // Create a new lang message for each toast title
            foreach (ToastConfig toastConfig in config.Toasts.Values)
            {
                if (toastConfig.Title != null && !string.IsNullOrEmpty(toastConfig.Title.LangKey))
                {
                    if (!messages.ContainsKey(toastConfig.Title.LangKey))
                    {
                        messages.Add(toastConfig.Title.LangKey, "CHANGE IT IN THE LANG FILE");
                    }
                }
            }

            lang.RegisterMessages(messages, this);
        }

        #endregion
    }
}

namespace Oxide.Plugins
{
    partial class Toastify : RustPlugin
    {
        private class UIBuilder
        {
            private enum ImageType
            {
                Url,
                ImageId,
                Asset
            };

            private Dictionary<string, TextAnchor> textAligns = new Dictionary<string, TextAnchor>()
            {
                ["top left"] = TextAnchor.UpperLeft,
                ["top center"] = TextAnchor.UpperCenter,
                ["top right"] = TextAnchor.UpperRight,
                ["middle left"] = TextAnchor.MiddleLeft,
                ["middle center"] = TextAnchor.MiddleCenter,
                ["middle right"] = TextAnchor.MiddleRight,
                ["bottom left"] = TextAnchor.LowerLeft,
                ["bottom center"] = TextAnchor.LowerCenter,
                ["bottom right"] = TextAnchor.LowerRight
            };

            public CuiElementContainer container { get; private set; }

            public UIBuilder()
            {
                container = new CuiElementContainer();
            }

            public void Add(BasePlayer player)
            {
                CuiHelper.AddUi(player, container);
            }

            public void AddPanel(string parent, string color, string anchor, string offset, string material = null, string sprite = null, float fadeIn = 0f, float fadeOut = 0f, string name = null)
            {
                name ??= CuiHelper.GetGuid();

                CuiElement element = new CuiElement()
                {
                    Parent = parent,
                    Name = name,
                    DestroyUi = name,
                    FadeOut = fadeOut,
                    Components = {
                        ParseRect(anchor, offset),
                        new CuiImageComponent()
                        {
                            Color = color,
                            FadeIn = fadeIn,
                            Material = material,
                            Sprite = sprite
                        }
                    }
                };

                container.Add(element);
            }

            public void AddImage(string parent, string image, string color, string anchor, string offset, float fadeIn = 0f, float fadeOut = 0f, string name = null)
            {
                name ??= CuiHelper.GetGuid();

                ImageType imageType = ImageType.Url;
                if (image.StartsWith("assets/"))
                {
                    imageType = ImageType.Asset;
                }
                else if (image.All(char.IsNumber))
                {
                    imageType = ImageType.ImageId;
                }

                CuiElement element = new CuiElement();
                element.Name = name;
                element.Parent = parent;
                element.DestroyUi = name;
                element.Components.Add(ParseRect(anchor, offset));

                if (imageType == ImageType.Url)
                {
                    element.Components.Add(new CuiRawImageComponent()
                    {
                        Color = color,
                        FadeIn = fadeIn,
                        Url = image
                    });
                }
                else
                {
                    element.Components.Add(new CuiRawImageComponent()
                    {
                        Color = color,
                        FadeIn = fadeIn,
                        Material = "assets/icons/iconmaterial.mat",
                        Png = imageType == ImageType.ImageId ? image : null,
                        Sprite = imageType == ImageType.Asset ? image : null
                    });
                }

                container.Add(element);
            }

            public void AddText(string parent, string text, string anchor, string offset, int fontSize = 12, string font = null, string textAlign = "middle left", float fadeIn = 0f, float fadeOut = 0f, string name = null)
            {
                name ??= CuiHelper.GetGuid();

                CuiElement element = new CuiElement()
                {
                    Parent = parent,
                    Name = name,
                    DestroyUi = name,
                    FadeOut = fadeOut,
                    Components = {
                        ParseRect(anchor, offset),
                        new CuiTextComponent()
                        {
                            Text = text,
                            Align = textAligns.ContainsKey(textAlign.ToLower()) ? textAligns[textAlign.ToLower()] : TextAnchor.MiddleLeft,
                            FontSize = fontSize,
                            Font = font ?? "robotocondensed-bold.ttf",
                            FadeIn = fadeIn
                        }
                    }
                };

                container.Add(element);
            }

            public void AddButton(string parent, string command, string anchor, string offset)
            {
                CuiElement element = new CuiElement()
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = parent,
                    Components = {
                        ParseRect(anchor, offset),
                        new CuiButtonComponent()
                        {
                            Color = "0 0 0 0",
                            Command = command
                        }
                    }
                };

                container.Add(element);
            }

            public static string ParseColor(string hex, float? alpha = null)
            {
                Color color;
                if (!ColorUtility.TryParseHtmlString(hex, out color))
                {
                    return hex;
                }

                return $"{color.r} {color.g} {color.b} {(alpha == null ? color.a : alpha)}";
            }

            private CuiRectTransformComponent ParseRect(string anchor, string offset)
            {
                string[] anchors = anchor.Split(' ');
                string[] offsets = offset.Split(' ');

                return new CuiRectTransformComponent()
                {
                    AnchorMin = $"{anchors[0]} {anchors[1]}",
                    AnchorMax = $"{anchors[2]} {anchors[3]}",
                    OffsetMin = $"{offsets[0]} {offsets[1]}",
                    OffsetMax = $"{offsets[2]} {offsets[3]}"
                };
            }
        }
    }
} 