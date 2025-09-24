using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oxide.Plugins.NotifyExtensionMethods;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
	[Info("Notify", "Mevent", "1.0.13")]
	[Description("Adds custom notifications for players")]
	public class Notify : RustPlugin
	{
		#region Fields

		[PluginReference] private Plugin ImageLibrary = null;

		private const string Layer = "UI.Notify";

		private static Notify _instance;

		private readonly Dictionary<ulong, NotifyComponent> _notifications =
			new Dictionary<ulong, NotifyComponent>();

		private class NotifyData
		{
			public string Message;

			public int Type;

			public readonly string Uid = CuiHelper.GetGuid();

			public float StartTime;
		}

		private const string
			PermSeeNotify = "notify.see",
			PermNotify = "notify.notify",
			PermPlayerNotify = "notify.player",
			PermAllPlayersNotify = "notify.allplayer";

		#endregion

		#region Config

		private static Configuration _config;

		private class Configuration
		{
			[JsonProperty(PropertyName = "Display type (Overlay/Hud)")]
			public string DisplayType = "Overlay";

			[JsonProperty(PropertyName = "Height")]
			public float Height = 50;

			[JsonProperty(PropertyName = "Width")] public float Width = 260;

			[JsonProperty(PropertyName = "X Margin")]
			public float XMargin = 20;

			[JsonProperty(PropertyName = "Y Margin")]
			public float YMargin = 5;

			[JsonProperty(PropertyName = "Y Indent")]
			public float ConstYSwitch = -50f;

			[JsonProperty(PropertyName = "Display notifications on the top right?")]
			public bool IsUpperRight = true;

			[JsonProperty(PropertyName = "Notify Cooldown")]
			public float Cooldown = 10f;

			[JsonProperty(PropertyName = "Max Notifications On Screen")]
			public int MaxNotificationsOnScreen = 10;

			[JsonProperty(PropertyName = "Send text message to chat if player doesn't have notification permission")]
			public bool SendTextMessageOnDontHavePermission = true;

			[JsonProperty(PropertyName = "Notifications (type - settings)",
				ObjectCreationHandling = ObjectCreationHandling.Replace)]
			public Dictionary<int, NotifyType> Types = new Dictionary<int, NotifyType>
			{
				[0] = new NotifyType
				{
					Enabled = true,
					BackgroundImage = string.Empty,
					BackgroundColor = new IColor("#000000",
						98),
					EnableGradient = true,
					GradientColor = new IColor("#4B68FF",
						35),
					Sprite = "assets/content/ui/ui.background.transparent.linearltr.tga",
					Material = "Assets/Icons/IconMaterial.mat",
					IconColor = new IColor("#4B68FF",
						100),
					IconText = "!",
					TitleKey = "Notification",
					FadeIn = 0.1f,
					FadeOut = 1f,
					Effect = "assets/bundled/prefabs/fx/notice/item.select.fx.prefab",
					Image = new ImageSettings
					{
						AnchorMin = "0 0",
						AnchorMax = "0 0",
						OffsetMin = "12.5 12.5",
						OffsetMax = "37.5 37.5",
						Enabled = false,
						Image = string.Empty
					},
					UseCustomWidth = false,
					CustomWidth = 0,
					UseCustomHeight = false,
					CustomHeight = 0,
					UseCommand = false,
					Command = string.Empty,
					CloseAfterUsingCommand = false,
					IconSettings = new TextSettings
					{
						AnchorMin = "0 0.5",
						AnchorMax = "0 0.5",
						OffsetMin = "12.5 -12.5",
						OffsetMax = "37.5 12.5",
						FontSize = 12,
						IsBold = false,
						Align = TextAnchor.MiddleCenter,
						Color = new IColor("#FFFFFF",
							100),
					},
					TitleSettings = new TextSettings
					{
						AnchorMin = "0 0.5",
						AnchorMax = "1 1",
						OffsetMin = "47.5 0",
						OffsetMax = "0 0",
						FontSize = 12,
						IsBold = false,
						Align = TextAnchor.LowerLeft,
						Color = new IColor("#FFFFFF",
							50)
					},
					TextSettings = new TextSettings
					{
						AnchorMin = "0 0",
						AnchorMax = "1 0.5",
						OffsetMin = "47.5 0",
						OffsetMax = "0 0",
						FontSize = 10,
						IsBold = false,
						Align = TextAnchor.UpperLeft,
						Color = new IColor("#FFFFFF",
							100),
					},
					UseCustomCooldown = false,
					Cooldown = 0
				},
				[1] = new NotifyType
				{
					Enabled = true,
					BackgroundImage = string.Empty,
					BackgroundColor = new IColor("#000000", 98),
					EnableGradient = true,
					GradientColor = new IColor("#FF6060", 35),
					Sprite = "assets/content/ui/ui.background.transparent.linearltr.tga",
					Material = "Assets/Icons/IconMaterial.mat",
					IconColor = new IColor("#FF6060", 100),
					IconText = "X",
					TitleKey = "Error",
					FadeIn = 0.1f,
					FadeOut = 1f,
					Effect = "assets/bundled/prefabs/fx/notice/item.select.fx.prefab",
					Image = new ImageSettings
					{
						AnchorMin = "0 0", AnchorMax = "0 0",
						OffsetMin = "12.5 12.5",
						OffsetMax = "37.5 37.5",
						Enabled = false,
						Image = string.Empty
					},
					UseCustomWidth = false,
					CustomWidth = 0,
					UseCustomHeight = false,
					CustomHeight = 0,
					UseCommand = false,
					Command = string.Empty,
					CloseAfterUsingCommand = false,
					IconSettings = new TextSettings
					{
						AnchorMin = "0 0.5",
						AnchorMax = "0 0.5",
						OffsetMin = "12.5 -12.5",
						OffsetMax = "37.5 12.5",
						FontSize = 12,
						IsBold = false,
						Align = TextAnchor.MiddleCenter,
						Color = new IColor("#4B68FF", 100),
					},
					TitleSettings = new TextSettings
					{
						AnchorMin = "0 0.5",
						AnchorMax = "1 1",
						OffsetMin = "47.5 0",
						OffsetMax = "0 0",
						FontSize = 12,
						IsBold = false,
						Align = TextAnchor.LowerLeft,
						Color = new IColor("#FFFFFF", 50)
					},
					TextSettings = new TextSettings
					{
						AnchorMin = "0 0",
						AnchorMax = "1 0.5",
						OffsetMin = "47.5 0",
						OffsetMax = "0 0",
						FontSize = 10,
						IsBold = false,
						Align = TextAnchor.UpperLeft,
						Color = new IColor("#FFFFFF", 100),
					},
					UseCustomCooldown = false,
					Cooldown = 0
				},
				[2130354] = new NotifyType
				{
					Enabled = true,
					BackgroundImage = "https://i.ibb.co/x1npBZr/image.png",
					BackgroundColor = new IColor("#FFFFFF", 100),
					EnableGradient = false,
					GradientColor = new IColor("#202224", 80),
					Sprite = string.Empty,
					Material = string.Empty,
					IconColor = new IColor("#EF5125", 100),
					IconText = "!",
					TitleKey = "AwardAvailable",
					FadeIn = 0.1f,
					FadeOut = 1f,
					Effect = "assets/bundled/prefabs/fx/notice/item.select.fx.prefab",
					Image = new ImageSettings
					{
						AnchorMin = "0 0.5",
						AnchorMax = "0 0.5",
						OffsetMin = "12.5 -16",
						OffsetMax = "44.5 16",
						Enabled = true,
						Image = "https://i.ibb.co/xLsSknw/image.png"
					},
					UseCustomWidth = true,
					CustomWidth = 280,
					UseCustomHeight = true,
					CustomHeight = 60,
					UseCommand = true,
					Command = "daily",
					CloseAfterUsingCommand = true,
					IconSettings = new TextSettings
					{
						AnchorMin = "0 0.5",
						AnchorMax = "0 0.5",
						OffsetMin = "12.5 -16",
						OffsetMax = "44.5 16",
						FontSize = 16,
						IsBold = false,
						Align = TextAnchor.MiddleCenter,
						Color = new IColor("#FFFFFF", 100),
					},
					TitleSettings = new TextSettings
					{
						AnchorMin = "0 0.5",
						AnchorMax = "1 1",
						OffsetMin = "47.5 0",
						OffsetMax = "-20 0",
						FontSize = 18,
						IsBold = true,
						Align = TextAnchor.LowerRight,
						Color = new IColor("#DCDCDC", 50)
					},
					TextSettings = new TextSettings
					{
						AnchorMin = "0 0",
						AnchorMax = "1 0.5",
						OffsetMin = "47.5 0",
						OffsetMax = "-20 0",
						FontSize = 14,
						IsBold = true,
						Align = TextAnchor.UpperRight,
						Color = new IColor("#DCDCDC", 100),
					},
					UseCustomCooldown = true,
					Cooldown = 60
				},
				[2130355] = new NotifyType
				{
					Enabled = true,
					BackgroundImage = "https://i.ibb.co/x1npBZr/image.png",
					BackgroundColor = new IColor("#FFFFFF", 100),
					EnableGradient = false,
					GradientColor = new IColor("#202224", 80),
					Sprite = string.Empty,
					Material = string.Empty,
					IconColor = new IColor("#EF5125", 100),
					IconText = "!",
					TitleKey = "AwardCooldown",
					FadeIn = 0.1f,
					FadeOut = 1f,
					Effect = "assets/bundled/prefabs/fx/notice/item.select.fx.prefab",
					Image = new ImageSettings
					{
						AnchorMin = "0 0.5", AnchorMax = "0 0.5",
						OffsetMin = "12.5 -12.5",
						OffsetMax = "37.5 12.5",
						Enabled = false,
						Image = string.Empty
					},
					UseCustomWidth = true,
					CustomWidth = 280,
					UseCustomHeight = true,
					CustomHeight = 60,
					UseCommand = true,
					Command = "daily",
					CloseAfterUsingCommand = true,
					IconSettings = new TextSettings
					{
						AnchorMin = "0 0.5",
						AnchorMax = "0 0.5",
						OffsetMin = "12.5 -16",
						OffsetMax = "44.5 16",
						FontSize = 16,
						IsBold = false,
						Align = TextAnchor.MiddleCenter,
						Color = new IColor("#FFFFFF", 100),
					},
					TitleSettings = new TextSettings
					{
						AnchorMin = "0 0.5",
						AnchorMax = "1 1",
						OffsetMin = "47.5 0",
						OffsetMax = "-20 0",
						FontSize = 18,
						IsBold = true,
						Align = TextAnchor.LowerRight,
						Color = new IColor("#DCDCDC", 100)
					},
					TextSettings = new TextSettings
					{
						AnchorMin = "0 0",
						AnchorMax = "1 0.5",
						OffsetMin = "47.5 0",
						OffsetMax = "-20 0",
						FontSize = 14,
						IsBold = true,
						Align = TextAnchor.UpperRight,
						Color = new IColor("#DCDCDC", 100)
					},
					UseCustomCooldown = true,
					Cooldown = 0.9f
				},
				[2130356] = new NotifyType
				{
					Enabled = true,
					BackgroundImage = "https://i.ibb.co/x1npBZr/image.png",
					BackgroundColor = new IColor("#FFFFFF", 100),
					EnableGradient = false,
					GradientColor = new IColor("#202224", 80),
					Sprite = string.Empty,
					Material = string.Empty,
					IconColor = new IColor("#EF5125", 100),
					IconText = "!",
					TitleKey = "AwardReceived",
					FadeIn = 0.1f,
					FadeOut = 1f,
					Effect = "assets/bundled/prefabs/fx/notice/item.select.fx.prefab",
					Image = new ImageSettings
					{
						AnchorMin = "0 0.5", AnchorMax = "0 0.5",
						OffsetMin = "12.5 -12.5",
						OffsetMax = "37.5 12.5",
						Enabled = false,
						Image = string.Empty
					},
					UseCustomWidth = true,
					CustomWidth = 280,
					UseCustomHeight = true,
					CustomHeight = 60,
					UseCommand = true,
					Command = "daily",
					CloseAfterUsingCommand = true,
					IconSettings = new TextSettings
					{
						AnchorMin = "0 0.5",
						AnchorMax = "0 0.5",
						OffsetMin = "12.5 -16",
						OffsetMax = "44.5 16",
						FontSize = 16,
						IsBold = false,
						Align = TextAnchor.MiddleCenter,
						Color = new IColor("#FFFFFF", 100),
					},
					TitleSettings = new TextSettings
					{
						AnchorMin = "0 0.5",
						AnchorMax = "1 1",
						OffsetMin = "47.5 0",
						OffsetMax = "-20 0",
						FontSize = 18,
						IsBold = true,
						Align = TextAnchor.LowerRight,
						Color = new IColor("#DCDCDC", 100)
					},
					TextSettings = new TextSettings
					{
						AnchorMin = "0 0",
						AnchorMax = "1 0.5",
						OffsetMin = "47.5 0",
						OffsetMax = "-20 0",
						FontSize = 14,
						IsBold = true,
						Align = TextAnchor.UpperRight,
						Color = new IColor("#DCDCDC", 100)
					},
					UseCustomCooldown = true,
					Cooldown = 60
				}
			};

			public VersionNumber Version;
		}

		protected override void LoadConfig()
		{
			base.LoadConfig();
			try
			{
				_config = Config.ReadObject<Configuration>();
				if (_config == null) throw new Exception();

				if (_config.Version < Version)
					UpdateConfigValues();

				SaveConfig();
			}
			catch (Exception ex)
			{
				PrintError("Your configuration file contains an error. Using default configuration values.");
				LoadDefaultConfig();
				Debug.LogException(ex);
			}
		}

		private void UpdateConfigValues()
		{
			if (_config.Version == default(VersionNumber) || _config.Version < new VersionNumber(1, 0, 9))
			{
				var baseConfig = new Configuration();

				var maxNotificationsOnScreen = Config.Get("Max Notifications On Screen");
				if (maxNotificationsOnScreen == null)
				{
					_config.MaxNotificationsOnScreen = baseConfig.MaxNotificationsOnScreen;

					_config.SendTextMessageOnDontHavePermission = baseConfig.SendTextMessageOnDontHavePermission;

					foreach (var notifyType in _config.Types)
					{
						switch (notifyType.Key)
						{
							default:
							{
								NotifyType newNotify;
								if (baseConfig.Types.TryGetValue(notifyType.Key, out newNotify))
								{
									notifyType.Value.Enabled = newNotify.Enabled;
									notifyType.Value.BackgroundImage = newNotify.BackgroundImage;
									notifyType.Value.CloseAfterUsingCommand = newNotify.CloseAfterUsingCommand;
									notifyType.Value.Command = newNotify.Command;
									notifyType.Value.CustomHeight = newNotify.CustomHeight;
									notifyType.Value.CustomWidth = newNotify.CustomWidth;
									notifyType.Value.IconSettings = newNotify.IconSettings;
									notifyType.Value.TextSettings = newNotify.TextSettings;
									notifyType.Value.TitleSettings = newNotify.TitleSettings;
									notifyType.Value.UseCommand = newNotify.UseCommand;
									notifyType.Value.UseCustomCooldown = newNotify.UseCustomCooldown;
									notifyType.Value.UseCustomHeight = newNotify.UseCustomHeight;
									notifyType.Value.UseCustomWidth = newNotify.UseCustomWidth;

									var oldTitleColor = Config.Get<IColor>("Notifications (type - settings)",
										notifyType.Key.ToString(), "Title Color");
									if (oldTitleColor != null)
										notifyType.Value.TitleSettings.Color = oldTitleColor;

									var oldTextColor = Config.Get<IColor>("Notifications (type - settings)",
										notifyType.Key.ToString(), "Text Color");
									if (oldTitleColor != null)
										notifyType.Value.TextSettings.Color = oldTextColor;

									var oldIconTextColor = Config.Get<IColor>("Notifications (type - settings)",
										notifyType.Key.ToString(), "Icon Text Color");
									if (oldIconTextColor != null)
										notifyType.Value.IconColor = oldIconTextColor;
								}

								break;
							}
						}
					}

					NotifyType dailyRewardsNotify;
					if (baseConfig.Types.TryGetValue(2130354, out dailyRewardsNotify))
						_config.Types.TryAdd(2130354, dailyRewardsNotify);

					if (baseConfig.Types.TryGetValue(2130355, out dailyRewardsNotify))
						_config.Types.TryAdd(2130355, dailyRewardsNotify);
				}

				PrintWarning("Config update detected! Updating config values...");
			}

			if (_config.Version != default(VersionNumber))
			{
				if (_config.Version < new VersionNumber(1, 0, 11))
				{
					var enableNewNotify = _config.Types.ContainsKey(2130354) && _config.Types[2130354].Enabled &&
					                      _config.Types.ContainsKey(2130355) && _config.Types[2130355].Enabled;

					_config.Types.TryAdd(2130356, new NotifyType
					{
						Enabled = enableNewNotify,
						BackgroundImage = "https://i.ibb.co/x1npBZr/image.png",
						BackgroundColor = new IColor("#FFFFFF", 100),
						EnableGradient = false,
						GradientColor = new IColor("#202224", 80),
						Sprite = string.Empty,
						Material = string.Empty,
						IconColor = new IColor("#EF5125", 100),
						IconText = "!",
						TitleKey = "AwardReceived",
						FadeIn = 0.1f,
						FadeOut = 1f,
						Effect = "assets/bundled/prefabs/fx/notice/item.select.fx.prefab",
						Image = new ImageSettings
						{
							AnchorMin = "0 0.5", AnchorMax = "0 0.5",
							OffsetMin = "12.5 -12.5",
							OffsetMax = "37.5 12.5",
							Enabled = false,
							Image = string.Empty
						},
						UseCustomWidth = true,
						CustomWidth = 280,
						UseCustomHeight = true,
						CustomHeight = 60,
						UseCommand = true,
						Command = "daily",
						CloseAfterUsingCommand = true,
						IconSettings = new TextSettings
						{
							AnchorMin = "0 0.5",
							AnchorMax = "0 0.5",
							OffsetMin = "12.5 -16",
							OffsetMax = "44.5 16",
							FontSize = 16,
							IsBold = false,
							Align = TextAnchor.MiddleCenter,
							Color = new IColor("#FFFFFF", 100),
						},
						TitleSettings = new TextSettings
						{
							AnchorMin = "0 0.5",
							AnchorMax = "1 1",
							OffsetMin = "47.5 0",
							OffsetMax = "-20 0",
							FontSize = 18,
							IsBold = true,
							Align = TextAnchor.LowerRight,
							Color = new IColor("#DCDCDC", 100)
						},
						TextSettings = new TextSettings
						{
							AnchorMin = "0 0",
							AnchorMax = "1 0.5",
							OffsetMin = "47.5 0",
							OffsetMax = "-20 0",
							FontSize = 14,
							IsBold = true,
							Align = TextAnchor.UpperRight,
							Color = new IColor("#DCDCDC", 100)
						},
						UseCustomCooldown = true,
						Cooldown = 60
					});
				}
			}

			_config.Version = Version;
			PrintWarning("Config update completed!");
		}

		private class TextSettings : InterfacePosition
		{
			[JsonProperty(PropertyName = "fontSize")]
			public int FontSize;

			[JsonProperty(PropertyName = "Is Bold?")]
			public bool IsBold;

			[JsonProperty(PropertyName = "Align")] [JsonConverter(typeof(StringEnumConverter))]
			public TextAnchor Align;

			[JsonProperty(PropertyName = "Color")] public IColor Color;
		}

		private class NotifyType
		{
			#region Fields

			[JsonProperty(PropertyName = "Enabled")]
			public bool Enabled;

			[JsonProperty(PropertyName = "Background Image")]
			public string BackgroundImage;

			[JsonProperty(PropertyName = "Background Color")]
			public IColor BackgroundColor;

			[JsonProperty(PropertyName = "Enable Gradient?")]
			public bool EnableGradient;

			[JsonProperty(PropertyName = "Gradient Color")]
			public IColor GradientColor;

			[JsonProperty(PropertyName = "Sprite")]
			public string Sprite;

			[JsonProperty(PropertyName = "Material")]
			public string Material;

			[JsonProperty(PropertyName = "Icon Color")]
			public IColor IconColor;

			[JsonProperty(PropertyName = "Icon Text")]
			public string IconText;

			[JsonProperty(PropertyName = "Title Key (lang)")]
			public string TitleKey;

			[JsonProperty(PropertyName = "Fade Out")]
			public float FadeOut;

			[JsonProperty(PropertyName = "Fade In")]
			public float FadeIn;

			[JsonProperty(PropertyName = "Sound Effect (empty - disable)")]
			public string Effect;

			[JsonProperty(PropertyName = "Image Settings")]
			public ImageSettings Image;

			[JsonProperty(PropertyName = "Use custom width")]
			public bool UseCustomWidth;

			[JsonProperty(PropertyName = "Custom width")]
			public float CustomWidth;

			[JsonProperty(PropertyName = "Use custom height")]
			public bool UseCustomHeight;

			[JsonProperty(PropertyName = "Custom height")]
			public float CustomHeight;

			[JsonProperty(PropertyName = "Use command")]
			public bool UseCommand;

			[JsonProperty(PropertyName = "Command")]
			public string Command;

			[JsonProperty(PropertyName = "Close after using the command?")]
			public bool CloseAfterUsingCommand;

			[JsonProperty(PropertyName = "Icon Settings")]
			public TextSettings IconSettings;

			[JsonProperty(PropertyName = "Title Settings")]
			public TextSettings TitleSettings;

			[JsonProperty(PropertyName = "Text Settings")]
			public TextSettings TextSettings;

			[JsonProperty(PropertyName = "Use custom cooldown")]
			public bool UseCustomCooldown;

			[JsonProperty(PropertyName = "Cooldown")]
			public float Cooldown;

			#endregion

			public float Get(BasePlayer player,
				ref CuiElementContainer container,
				NotifyData data,
				float ySwitch)
			{
				var width = UseCustomWidth ? CustomWidth : _config.Width;
				var height = UseCustomHeight ? CustomHeight : _config.Height;

				var layer = BackgroundUI(ref container, data.Uid, ySwitch, width, height);

				if (EnableGradient)
					GradientUI(ref container, layer);

				IconUI(ref container, layer);

				TitleUI(player, ref container, layer);

				MessageUI(ref container, data.Message, layer);

				if (UseCommand) ButtonUI(ref container, layer, CloseAfterUsingCommand ? layer : string.Empty);

				return height;
			}

			#region UI.Components

			private void ButtonUI(ref CuiElementContainer container,
				string parent, string closeLayer)
			{
				var button = new CuiButtonComponent
				{
					Color = "0 0 0 0",
					Command = Command
				};

				if (!string.IsNullOrEmpty(closeLayer))
					button.Close = closeLayer;

				container.Add(new CuiElement
				{
					Parent = parent,
					Components =
					{
						button,
						new CuiRectTransformComponent()
					}
				});
			}

			private void MessageUI(ref CuiElementContainer container, string message, string parent)
			{
				container.Add(new CuiLabel
				{
					RectTransform =
					{
						AnchorMin = TextSettings.AnchorMin,
						AnchorMax = TextSettings.AnchorMax,
						OffsetMin = TextSettings.OffsetMin,
						OffsetMax = TextSettings.OffsetMax
					},
					Text =
					{
						Text = $"{message}",
						Align = TextSettings.Align,
						Font = TextSettings.IsBold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf",
						FontSize = TextSettings.FontSize,
						Color = TextSettings.Color.Get(),
						FadeIn = FadeIn
					},
					FadeOut = FadeOut
				}, parent);
			}

			private void TitleUI(BasePlayer player, ref CuiElementContainer container, string parent)
			{
				container.Add(new CuiLabel
				{
					RectTransform =
					{
						AnchorMin = TitleSettings.AnchorMin,
						AnchorMax = TitleSettings.AnchorMax,
						OffsetMin = TitleSettings.OffsetMin,
						OffsetMax = TitleSettings.OffsetMax
					},
					Text =
					{
						Text = _instance.Msg(player, TitleKey),
						Align = TitleSettings.Align,
						Font = TitleSettings.IsBold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf",
						FontSize = TitleSettings.FontSize,
						Color = TitleSettings.Color.Get(),
						FadeIn = FadeIn
					},
					FadeOut = FadeOut
				}, parent);
			}

			private void IconUI(ref CuiElementContainer container, string parent)
			{
				if (Image?.Enabled == true && !string.IsNullOrEmpty(Image.Image))
				{
					container.Add(new CuiElement
					{
						Name = parent + ".Icon",
						Parent = parent,
						Components =
						{
							new CuiRawImageComponent
							{
								Png = _instance.ImageLibrary.Call<string>("GetImage", Image.Image)
							},
							new CuiRectTransformComponent
							{
								AnchorMin = Image.AnchorMin,
								AnchorMax = Image.AnchorMax,
								OffsetMin = Image.OffsetMin,
								OffsetMax = Image.OffsetMax
							}
						}
					});
				}
				else
				{
					container.Add(new CuiPanel
					{
						RectTransform =
						{
							AnchorMin = IconSettings.AnchorMin,
							AnchorMax = IconSettings.AnchorMax,
							OffsetMin = IconSettings.OffsetMin,
							OffsetMax = IconSettings.OffsetMax
						},
						Image =
						{
							Color = IconColor.Get(),
							FadeIn = FadeIn
						},
						FadeOut = FadeOut
					}, parent, parent + ".Icon");

					container.Add(new CuiLabel
					{
						RectTransform =
						{
							AnchorMin = "0 0", AnchorMax = "1 1"
						},
						Text =
						{
							Text = $"{IconText}",
							Align = IconSettings.Align,
							Font = IconSettings.IsBold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf",
							FontSize = IconSettings.FontSize,
							Color = IconSettings.Color.Get(),
							FadeIn = FadeIn
						},
						FadeOut = FadeOut
					}, parent + ".Icon");
				}
			}

			private void GradientUI(ref CuiElementContainer container, string parent)
			{
				container.Add(new CuiPanel
				{
					RectTransform =
					{
						AnchorMin = "0 0", AnchorMax = "1 1"
					},
					Image =
					{
						Color = GradientColor.Get(),
						Sprite = Sprite,
						Material = Material,
						FadeIn = FadeIn
					},
					FadeOut = FadeOut
				}, parent);
			}

			private string BackgroundUI(ref CuiElementContainer container, string uid,
				float ySwitch,
				float width,
				float height)
			{
				var name = Layer + $".Notify.{uid}";

				if (!string.IsNullOrEmpty(BackgroundImage))
				{
					container.Add(new CuiElement
					{
						Name = name,
						Parent = Layer,
						DestroyUi = name,
						Components =
						{
							new CuiRawImageComponent
							{
								Png = _instance.ImageLibrary.Call<string>("GetImage", BackgroundImage),
								Color = BackgroundColor.Get(),
								FadeIn = FadeIn
							},
							new CuiRectTransformComponent
							{
								AnchorMin = _config.IsUpperRight ? "1 1" : "0 0",
								AnchorMax = _config.IsUpperRight ? "1 1" : "0 0",
								OffsetMin = _config.IsUpperRight
									? $"{-width - _config.XMargin} {ySwitch - height}"
									: $"{_config.XMargin} {ySwitch - height}",
								OffsetMax = _config.IsUpperRight
									? $"{-_config.XMargin} {ySwitch}"
									: $"{_config.XMargin + width} {ySwitch}"
							}
						}
					});
				}
				else
				{
					container.Add(new CuiPanel
					{
						RectTransform =
						{
							AnchorMin = _config.IsUpperRight ? "1 1" : "0 0",
							AnchorMax = _config.IsUpperRight ? "1 1" : "0 0",
							OffsetMin = _config.IsUpperRight
								? $"{-width - _config.XMargin} {ySwitch - height}"
								: $"{_config.XMargin} {ySwitch - height}",
							OffsetMax = _config.IsUpperRight
								? $"{-_config.XMargin} {ySwitch}"
								: $"{_config.XMargin + width} {ySwitch}"
						},
						Image =
						{
							Color = BackgroundColor.Get(),
							FadeIn = FadeIn
						},
						FadeOut = FadeOut
					}, Layer, name, name);
				}

				return name;
			}

			#endregion
		}

		private class InterfacePosition
		{
			public string AnchorMin;

			public string AnchorMax;

			public string OffsetMin;

			public string OffsetMax;
		}

		private class ImageSettings : InterfacePosition
		{
			[JsonProperty(PropertyName = "Enabled")]
			public bool Enabled;

			[JsonProperty(PropertyName = "Image")] public string Image;
		}

		private class IColor
		{
			[JsonProperty(PropertyName = "HEX")] public string Hex;

			[JsonProperty(PropertyName = "Opacity (0 - 100)")]
			public float Alpha;

			public string Get()
			{
				if (string.IsNullOrEmpty(Hex)) Hex = "#FFFFFF";

				var str = Hex.Trim('#');
				if (str.Length != 6) throw new Exception(Hex);
				var r = byte.Parse(str.Substring(0, 2), NumberStyles.HexNumber);
				var g = byte.Parse(str.Substring(2, 2), NumberStyles.HexNumber);
				var b = byte.Parse(str.Substring(4, 2), NumberStyles.HexNumber);

				return $"{(double) r / 255} {(double) g / 255} {(double) b / 255} {Alpha / 100}";
			}

			public IColor(string hex, float alpha)
			{
				Hex = hex;
				Alpha = alpha;
			}
		}

		protected override void SaveConfig()
		{
			Config.WriteObject(_config);
		}

		protected override void LoadDefaultConfig()
		{
			_config = new Configuration();
		}

		#endregion

		#region Hooks

		private void OnServerInitialized()
		{
			_instance = this;

			LoadImages();

			RegisterPermissions();

			RegisterCommands();
		}

		private void Unload()
		{
			_notifications.Values.ToList().ForEach(notify =>
			{
				if (notify != null)
					notify.Kill();
			});

			foreach (var player in BasePlayer.activePlayerList)
				CuiHelper.DestroyUi(player, Layer);

			_instance = null;
			_config = null;
		}

		#endregion

		#region Commands

		private void CmdShowNotify(IPlayer cov, string command, string[] args)
		{
			if (!cov.IsServer && !cov.HasPermission(PermNotify)) return;

			var player = cov.Object as BasePlayer;
			if (player == null) return;

			int type;
			if (args.Length < 2 || !int.TryParse(args[0], out type))
			{
				cov.Reply(Msg(cov.Id, SyntaxNotify, command));
				return;
			}

			var message = string.Join(" ", args.Skip(1));
			if (string.IsNullOrEmpty(message)) return;

			SendNotify(player, type, message);
		}

		private void CmdShowPlayerNotify(IPlayer cov, string command, string[] args)
		{
			if (!cov.IsServer && !cov.HasPermission(PermPlayerNotify)) return;

			int type;
			if (args.Length < 3 || !int.TryParse(args[1], out type))
			{
				cov.Reply(Msg(cov.Id, SyntaxPlayerNotify, command));
				return;
			}

			var target = covalence.Players.FindPlayer(args[0])?.Object as BasePlayer;
			if (target == null)
			{
				cov.Reply(Msg(cov.Id, NotFound, args[0]));
				return;
			}

			var message = string.Join(" ", args.Skip(2));
			if (string.IsNullOrEmpty(message)) return;

			SendNotify(target, type, message);
		}

		private void CmdShowAllPlayerNotify(IPlayer cov, string command, string[] args)
		{
			if (!cov.IsServer && !cov.HasPermission(PermAllPlayersNotify)) return;

			int type;
			if (args.Length < 2 || !int.TryParse(args[0], out type))
			{
				cov.Reply(Msg(cov.Id, SyntaxAllPlayerNotify, command));
				return;
			}

			var message = string.Join(" ", args.Skip(1));
			if (string.IsNullOrEmpty(message)) return;

			SendNotifyAllPlayers(type, message);
		}

		#endregion

		#region Component

		private class NotifyComponent : FacepunchBehaviour
		{
			#region Fields

			private BasePlayer _player;

			private readonly List<NotifyData> _notifies = new List<NotifyData>();

			private CuiElementContainer container = new CuiElementContainer();

			#endregion

			#region Main

			private void Awake()
			{
				_player = GetComponent<BasePlayer>();

				_instance._notifications[_player.userID] = this;

				Invoke(NotificationsController, 1);
			}

			private void OnDestroy()
			{
				CancelInvoke();

				if (_player != null)
				{
					CuiHelper.DestroyUi(_player, Layer);

					_instance?._notifications.Remove(_player.userID);
				}
			}

			public void Kill()
			{
				DestroyImmediate(this);
			}

			#endregion

			#region Utils

			private void MainUi()
			{
                _instance.NextTick(() =>
				{
					var ySwitch = _config.ConstYSwitch;

					container.Clear();

					container.Add(new CuiPanel
					{
						RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1" },
						Image = { Color = "0 0 0 0" }
					}, _config.DisplayType, Layer, Layer);

					_notifies.Take(_config.MaxNotificationsOnScreen).ForEach(notify =>
					{
						var height = NotifyUi(ref container, notify, ySwitch);

						ySwitch = ySwitch - height - _config.YMargin;
					});

					CuiHelper.AddUi(_player, container);
				});
			}

			private float NotifyUi(ref CuiElementContainer cont, NotifyData data, float ySwitch)
			{
				return _config.Types[data.Type]?.Get(_player, ref cont, data, ySwitch) ?? 0f;
			}

			public void AddNotify(NotifyData data)
			{
				NotifyType notifyType;
				if (!_config.Types.TryGetValue(data.Type, out notifyType) ||
				    notifyType == null ||
				    notifyType.Enabled == false) return;

				_notifies.Add(data);

				if (_notifies.Count == 1)
					_notifies[0].StartTime = Time.time;

				MainUi();

				if (!string.IsNullOrEmpty(notifyType.Effect))
					SendEffect(notifyType.Effect);
			}

			private void RemoveNotify(int index = 0)
			{
				_notifies.RemoveAt(index);

				if (_notifies.Count == 0)
				{
					Kill();
					return;
				}

				_notifies[0].StartTime = Time.time;

				MainUi();
			}

			private void NotificationsController()
			{
				CancelInvoke(NotificationsController);
				if (_notifies.Count == 0)
				{
					Kill();
					return;
				}

				var notify = _notifies[0];

				var cooldown = _config.Types[notify.Type].UseCustomCooldown
					? _config.Types[notify.Type].Cooldown
					: _config.Cooldown;

				if (Time.time - notify.StartTime >= cooldown)
					RemoveNotify();

				Invoke(NotificationsController, 1);
			}

			private void SendEffect(string effect)
			{
				EffectNetwork.Send(new Effect(effect, _player, 0, new Vector3(), new Vector3()), _player.Connection);
			}

			#endregion
		}

		#endregion

		#region Utils

		private void LoadImages()
		{
			if (ImageLibrary == null || !ImageLibrary.IsLoaded)
			{
				PrintError("IMAGE LIBRARY IS NOT INSTALLED!");
			}
			else
			{
				var imagesList = new Dictionary<string, string>();

				foreach (var notifyType in _config.Types.Values)
				{
					if (!string.IsNullOrEmpty(notifyType.BackgroundImage))
						imagesList.TryAdd(notifyType.BackgroundImage, notifyType.BackgroundImage);

					if (notifyType.Image?.Enabled == true && !string.IsNullOrEmpty(notifyType.Image.Image))
						imagesList.TryAdd(notifyType.Image.Image, notifyType.Image.Image);
				}

				ImageLibrary?.Call("ImportImageList", Title, imagesList, 0UL, true);
			}
		}

		private void RegisterCommands()
		{
			AddCovalenceCommand("notify.show", nameof(CmdShowNotify));

			AddCovalenceCommand("notify.player", nameof(CmdShowPlayerNotify));

			AddCovalenceCommand("notify.allplayers", nameof(CmdShowAllPlayerNotify));
		}

		private void RegisterPermissions()
		{
			permission.RegisterPermission(PermSeeNotify, this);
			permission.RegisterPermission(PermNotify, this);
			permission.RegisterPermission(PermPlayerNotify, this);
			permission.RegisterPermission(PermAllPlayersNotify, this);
		}

		#endregion

		#region API

		private void SendNotifyAllPlayers(int type, string message)
		{
			foreach (var player in BasePlayer.activePlayerList)
				SendNotify(player, type, message);
		}

		private void SendNotify(string userId, int type, string message)
		{
			SendNotify(BasePlayer.FindByID(ulong.Parse(userId)), type, message);
		}

		private void SendNotify(ulong userId, int type, string message)
		{
			SendNotify(BasePlayer.FindByID(userId), type, message);
		}

		private void SendNotify(BasePlayer player, int type, string message)
		{
			if (player == null) return;

			if (!permission.UserHasPermission(player.UserIDString, PermSeeNotify))
			{
				if (_config.SendTextMessageOnDontHavePermission)
					player.ChatMessage(message);
				return;
			}

			var notify = GetComponent(player);
			if (notify == null) return;

			var data = new NotifyData
			{
				Type = type,
				Message = message
			};

			notify.AddNotify(data);
		}

		private NotifyComponent GetComponent(BasePlayer player)
		{
			NotifyComponent component;
			return _notifications.TryGetValue(player.userID, out component)
				? component
				: player.gameObject.AddComponent<NotifyComponent>();
		}

		#endregion

		#region API

		private bool TryToggleType(int type, bool newValue)
		{
			NotifyType notifyNotifyType;
			if (!_config.Types.TryGetValue(type, out notifyNotifyType) || notifyNotifyType.Enabled == newValue)
				return false;

			notifyNotifyType.Enabled = newValue;
			SaveConfig();
			return true;
		}

		#endregion

		#region Lang

		private const string
			NotFound = "NotFound",
			SyntaxAllPlayerNotify = "SyntaxAllPlayerNotify",
			SyntaxPlayerNotify = "SyntaxPlayerNotify",
			SyntaxNotify = "SyntaxNotify";

		protected override void LoadDefaultMessages()
		{
			lang.RegisterMessages(new Dictionary<string, string>
			{
				["Notification"] = "Notification",
				["Error"] = "Error",
				["AwardAvailable"] = "NEW AWARD AVAILABLE!",
				["AwardCooldown"] = "AWARD WILL BE AVAILABLE",
				["AwardReceived"] = "REWARD RECEIVED",
				[SyntaxNotify] = "Error syntax! Use: /{0} [type] [message]",
				[SyntaxPlayerNotify] = "Error syntax! Use: /{0} [steamid] [type] [message]",
				[SyntaxAllPlayerNotify] = "Error syntax! Use: /{0} [type] [message]",
				[NotFound] = "Player '{0}' not found!"
			}, this);

			lang.RegisterMessages(new Dictionary<string, string>
			{
				["Notification"] = "Оповещение",
				["Error"] = "Ошибка",
				["AwardAvailable"] = "ДОСТУПНА НОВАЯ НАГРАДА!",
				["AwardCooldown"] = "НАГРАДА БУДЕТ ДОСТУПНА",
				["AwardReceived"] = "НАГРАДА ПОЛУЧЕНА",
				[SyntaxNotify] = "Ошибка синтаксиса! Используйте: /{0} [type] [message]",
				[SyntaxPlayerNotify] = "Ошибка синтаксиса! Используйте: /{0} [steamid] [type] [message]",
				[SyntaxAllPlayerNotify] = "Ошибка синтаксиса! Используйте: /{0} [type] [message]",
				[NotFound] = "Игрок '{0}' не найден!"
			}, this, "ru");
		}

		private string Msg(BasePlayer player, string key, params object[] obj)
		{
			return string.Format(lang.GetMessage(key, this, player.UserIDString), obj);
		}

		private string Msg(string player, string key, params object[] obj)
		{
			return string.Format(lang.GetMessage(key, this, player), obj);
		}

		#endregion
	}
}

#region Extension Methods

namespace Oxide.Plugins.NotifyExtensionMethods
{
	// ReSharper disable ForCanBeConvertedToForeach
	// ReSharper disable LoopCanBeConvertedToQuery
	public static class ExtensionMethods
	{
		public static List<T> Take<T>(this List<T> source, int count)
		{
			return source.GetRange(0, Mathf.Min(count, source.Count));
		}
	}
}

#endregion Extension Methods
