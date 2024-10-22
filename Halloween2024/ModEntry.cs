using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.Menus;
using StardewValley.Pathfinding;

/*
debug ebi Custom_blueberry_H2024_
patch reload blueberry.H2024.CP
cs return string.Join(`, `, Game1.player.eventsSeen);
cs return string.Join(`, `, Game1.player.mailReceived);
*/

namespace Halloween2024
{
	public class ModEntry : Mod
	{
		public enum MapNumber
		{
			None = 0,
			Purple = 1,
			Red = 2,
			Blue = 3,
			Orange = 4
		}

		public enum MapEnding
		{
			None = 0,
			Success = 1,
			Fail = 2,
			Quit = 3
		}

		public class ModState
		{
			public int MapNumber;
			public Vector2 MapGoal;

			public int StartMs;
			public int EndMs;
			public bool IsNaughty;

			public LightSource? PlayerLight;
			public Texture2D? Texture;
			public Texture2D? MinimapTexture;
			public Color MinimapColor;

			public bool IsMinimapHovered;
		}

		public static PerScreen<ModState> State { get; private set; }
			= new(() => new());
		public static Dictionary<string, string>? Strings { get; private set; }
		public static bool EventUp
			=> Game1.currentLocation?.currentEvent?.playerControlSequenceID?.StartsWith(ModEntry.Strings.GetValueSafe("ID")) ?? false;
		public static bool EventMap
			=> Game1.currentLocation is not null
			&& Game1.currentLocation.IsTemporary
			&& Game1.currentLocation.getMapProperty(ModEntry.Strings.GetValueSafe("ID")) is not null;
		public static int TotalMs
			=> (int)Game1.currentGameTime.TotalGameTime.TotalMilliseconds;
		public static bool IsNightmare
			=> bool.Parse(ModEntry.Strings.GetValueSafe("NIGHTMARE"));
		public static bool IsUnderwater
			=> Game1.currentLocation?.HasMapPropertyWithValue("indoorWater") ?? false;

		public const string ContentPackUniqueId = "blueberry.H2024.CP";
		public const string StringsAssetKey = "Mods/blueberry/H2024/Strings";

		public static ModEntry Instance { get; private set; }

		public override void Entry(IModHelper helper)
		{
			ModEntry.Instance = this;

			if (!helper.ModRegistry.IsLoaded(ModEntry.ContentPackUniqueId))
			{
				this.Monitor.Log("Can't load without my CP component! Did you copy BOTH folders in?", LogLevel.Error);
				return;
			}

			Harmony harmony = new(helper.ModRegistry.ModID);
			harmony.PatchAll();

			helper.Events.GameLoop.SaveLoaded += this.GameLoop_SaveLoaded;
			helper.Events.Player.Warped += this.Player_Warped;
			helper.Events.Display.Rendered += this.Display_Rendered;
			helper.Events.GameLoop.Saving += this.GameLoop_Saving;
		}

		public static void SetUpEventLocation(GameLocation where)
		{
			ModEntry.Instance.Helper.GameContent.InvalidateCache(ModEntry.StringsAssetKey);
			ModEntry.Strings = ModEntry.LoadStrings();

			Color strToRgb(string str) => str.Split(' ').ToList().ConvertAll(int.Parse).ToArray() is int[] i ? new(i[0], i[1], i[2]) : Color.Transparent;

			string error;
			string[] args = where.Map.Properties[ModEntry.Strings.GetValueSafe("ID")].ToString().Split('/');
			ArgUtility.TryGetInt(args, 0, out ModEntry.State.Value.MapNumber, out error);
			if (error is not null)
				ModEntry.Instance.Monitor.Log(error, LogLevel.Error);
			ArgUtility.TryGetVector2(args[1].Split(' '), 0, out ModEntry.State.Value.MapGoal, out error);
			if (error is not null)
				ModEntry.Instance.Monitor.Log(error, LogLevel.Error);
			Color minimapColour = strToRgb(args[2]);
			Color ambientColour = strToRgb(args[3]);
			Color nightmareColour = strToRgb(args[4]);
			Color playerLightColour = strToRgb(args[5]);

			Farmer who = Game1.player;

			where.currentEvent.aboveMapSprites ??= [];
			where.currentEvent.underwaterSprites ??= [];
			where.currentEvent.npcControllers ??= [];

			// Player light
			ModEntry.CreatePlayerLight(where, who, playerLightColour);

			// Ambient light
			if (ModEntry.IsNightmare)
			{
				where.LightLevel = 0.25f;
				Game1.ambientLight = nightmareColour;
			}
			else
			{
				where.LightLevel = 0f;
				Game1.ambientLight = ambientColour;
			}

			// Water
			if (ModEntry.IsUnderwater)
			{
				ModEntry.State.Value.Texture = Game1.temporaryContent.Load<Texture2D>("LooseSprites\\swimShadow");
				Game1.player.swimming.Value = true;
				Game1.player.bathingClothes.Value = true;
				Game1.player.canOnlyWalk = false;
			}

			// Unique map features
			if (ModEntry.State.Value.MapNumber == (int)MapNumber.Orange)
			{
				int count = 12 + Game1.random.Next(8);
				for (int i = 0; i < count; ++i)
				{
					Vector2 tile = Vector2.Zero;
					do tile = where.getRandomTile(); while (!where.isTilePassable(tile) || !where.isTileOnMap(tile));
					ModEntry.CreatePumpkinMan(where, tile);
				}
			}

			// Minimap
			ModEntry.CreateMinimap(where, minimapColour);
		}

		public static void EndEvent()
		{
			ModEntry.State.Value.EndMs = 0;
			ModEntry.State.Value.StartMs = 0;
			ModEntry.State.Value.PlayerLight = null;

			Game1.player.swimming.Value = false;
			Game1.player.bathingClothes.Value = false;
			ModEntry.State.Value.Texture = null;
		}

		public static void RepeatUnfinishedEvents(Farmer who)
		{
			string id = ModEntry.Strings.GetValueSafe("ID");
			who.eventsSeen.RemoveWhere(s => s.StartsWith(id) && !who.mailReceived.Contains(s));
		}

		public static Dictionary<string, string> LoadStrings()
		{
			return Game1.content.Load<Dictionary<string, string>>(ModEntry.StringsAssetKey);
		}

		public static void CreatePlayerLight(GameLocation where, Farmer who, Color color)
		{
			string lightSourceId = ModEntry.Strings.GetValueSafe("ID");
			where.removeLightSource(lightSourceId);
			ModEntry.State.Value.PlayerLight = new LightSource(
				id: lightSourceId,
				textureIndex: 1,
				position: new Vector2(who.Position.X + 24f, who.Position.Y + 48f),
				radius: 10f,
				color: new Color(color.R, color.G, color.B, (byte)225),
				lightContext: LightSource.LightContext.None,
				playerID: who.UniqueMultiplayerID);
			where.sharedLights.AddLight(ModEntry.State.Value.PlayerLight);
		}

		public static void CreateMinimap(GameLocation where, Color color)
		{
			Point size = new(x: where.Map.DisplayWidth / Game1.tileSize, y: where.Map.DisplayHeight / Game1.tileSize);
			Color[] data = new Color[size.X * size.Y];
			for (int x = 0; x < size.X; ++x)
			{
				for (int y = 0; y < size.Y; ++y)
				{
					data[(x % size.X) + (y * size.X)] = where.Map.HasTileAt(new(x, y), "Buildings")
						? (ModEntry.State.Value.MapGoal.X == x && ModEntry.State.Value.MapGoal.Y == y)
							? new Color(255 - color.R, 255 - color.G, 255 - color.B)
							: Color.Black
						: color;
				}
			}
			if (ModEntry.State.Value.MinimapTexture is not Texture2D minimap || size.X != minimap.Width || size.Y != minimap.Height)
			{
				Texture2D texture = new(Game1.graphics.GraphicsDevice, size.X, size.Y);
				ModEntry.State.Value.MinimapTexture = texture;
			}
			ModEntry.State.Value.MinimapTexture.SetData(data);
			ModEntry.State.Value.MinimapColor = color;
		}

		public static NPC CreatePumpkinMan(GameLocation where, Vector2 tile)
		{
			NPC npc = new(
				sprite: new AnimatedSprite("Characters\\Monsters\\Wilderness Golem", 0, 16, 24),
				position: tile * Game1.tileSize,
				facingDir: 0,
				name: "AnsweringMachine")
			{
				farmerPassesThrough = true
			};
			where.currentEvent.actors.Add(npc);
			return npc;
		}

		public static void TryCreateSprite(Event e)
		{
			GameLocation where = Game1.currentLocation;
			Farmer who = Game1.player;

			float spriteChance = MathF.Sqrt(Game1.viewport.Height) / 5000f;
			if (Game1.random.NextDouble() < spriteChance)
			{
				Vector2 zero = new(Game1.viewport.X, Game1.viewport.Y);

				if (ModEntry.State.Value.MapNumber == (int)MapNumber.Purple)
				{
					bool isDot = Game1.random.NextDouble() < 0.75;
					Rectangle r = who.GetBoundingBox();
					r.Inflate(Game1.tileSize * 7, Game1.tileSize * 7);
					Vector2 global = Utility.getRandomPositionInThisRectangle(r, Game1.random);
					Vector2 motion = new(0, 0.05f);
					float scale = (float)(0.75f + Game1.random.NextDouble() * 0.5f);
					e.aboveMapSprites.Add(new TemporaryAnimatedSprite(
						textureName: "LooseSprites/Cursors",
						sourceRect: isDot ? new(666, 866, 1, 1) : new(377, 1438, 10, 10),
						position: global,
						flipped: false,
						alphaFade: 0f,
						color: new(250, 125, 175))
					{
						alpha = 0.0001f,
						alphaFade = -0.005f * 1000f / Game1.viewport.Width * scale,
						alphaFadeFade = -0.00001f * 1000f / Game1.viewport.Width * scale,
						motion = motion * scale,
						acceleration = new Vector2(0f, 0f),
						interval = 999999,
						layerDepth = global.Y / 10000f,
						rotationChange = (float)(-0.5 + Game1.random.NextDouble()) * scale / 10,
						scale = Game1.pixelZoom * scale * (isDot ? 3 : 1)
					});

					if (Game1.random.NextDouble() < spriteChance * 10)
						Game1.playSound(new [] { "dogs", "distantTrain", "thunder_small" }[Game1.random.Next(3)]);
				}
				else if (ModEntry.State.Value.MapNumber == (int)MapNumber.Red)
				{
					if (Game1.random.NextDouble() < 0.25)
						return;

					Vector2 local = new Vector2(x: Game1.random.NextDouble() < 0.5 ? 0 : Game1.viewport.Width - 32, y: Game1.random.Next(Game1.viewport.Height));
					float speed = 3f;
					Rectangle r = who.GetBoundingBox();
					r.Inflate(Game1.tileSize * 2, Game1.tileSize * 2);
					Vector2 motion = Utility.getVelocityTowardPoint(zero + local, Utility.getRandomPositionInThisRectangle(r, Game1.random), speed);
					float scale = (float)(0.75f + Game1.random.NextDouble() * 0.5f);
					if (Game1.random.NextDouble() < 0.25)
					{
						e.aboveMapSprites.Add(new TemporaryAnimatedSprite(
							textureName: "Characters/Monsters/Bat",
							sourceRect: new Rectangle(0, 0, 16, 32),
							position: local + zero,
							flipped: false,
							alphaFade: 0f,
							color: new(250, 125, 175))
						{
							alphaFade = 0.001f * 1000f / Game1.viewport.Width,
							alpha = 1f,
							motion = motion * scale,
							acceleration = new Vector2(0f, 0f),
							interval = 60,
							animationLength = 4,
							totalNumberOfLoops = 999999,
							layerDepth = local.Y / 10000f,
							scale = 4f * scale
						});
						if (Game1.random.NextDouble() < spriteChance * 10)
							Game1.playSound("batScreech");
					}
					else
					{
						e.aboveMapSprites.Add(new TemporaryAnimatedSprite(
							textureName: "Characters/Monsters/Carbon Ghost",
							sourceRect: new Rectangle(0, 0, 16, 24),
							position: local + zero,
							flipped: false,
							alphaFade: 0f,
							color: new(250, 25, 50))
						{
							alphaFade = 0.001f * 1000f / Game1.viewport.Width,
							alpha = 1f * scale,
							motion = motion * scale / 4f,
							acceleration = new Vector2(0f, 0f),
							interval = 500 * scale,
							animationLength = 4,
							totalNumberOfLoops = 999999,
							layerDepth = local.Y / 10000f,
							scale = 4f * scale,
							yPeriodic = true,
							yPeriodicLoopTime = 2000 * scale,
							yPeriodicRange = 24 * scale
						});
						if (Game1.random.NextDouble() < spriteChance * 10)
							Game1.playSound("ghost");
					}
				}
				else if (ModEntry.State.Value.MapNumber == (int)MapNumber.Blue)
				{
					if (Game1.random.NextDouble() < spriteChance * 10)
						Game1.playSound("rainsound");

					if (Game1.random.NextDouble() < 0.333)
					{
						int count = 3 + Game1.random.Next(4);
						int style = Game1.random.Next(4);
						Vector2 local = new Vector2(x: Game1.random.NextDouble() < 0.5 ? 0 : Game1.viewport.Width - 32, y: Game1.random.Next(Game1.viewport.Height));
						float speed = 5f;
						Rectangle r = who.GetBoundingBox();
						r.Inflate(Game1.tileSize * 2, Game1.tileSize * 2);
						for (int i = 0; i < count; ++i)
						{
							Rectangle fishbox = new((zero + local).ToPoint(), Point.Zero);
							fishbox.Inflate(Game1.tileSize / 2 * count, Game1.tileSize / 2 * count);
							Vector2 motion = Utility.getVelocityTowardPoint(
								startingPoint: Utility.getRandomPositionInThisRectangle(fishbox, Game1.random),
								endingPoint: Utility.getRandomPositionInThisRectangle(r, Game1.random),
								speed: speed);
							float scale = (float)(0.75f + Game1.random.NextDouble() * 0.5f);
							e.underwaterSprites.Add(new TemporaryAnimatedSprite(
								textureName: "LooseSprites/temporary_sprites_1",
								sourceRect: new Rectangle(128 + 16 * style, 0, 16, 16),
								position: local + zero,
								flipped: local.X < Game1.viewport.Width / 2,
								alphaFade: 0f,
								color: Color.Lerp(Color.CornflowerBlue, Color.DarkBlue, scale) * scale)
							{
								alphaFade = 0.0005f * 1000f / Game1.viewport.Width,
								alpha = 1f * scale,
								motion = motion * scale / 4f,
								acceleration = new Vector2(0f, 0f),
								interval = 999999,
								layerDepth = local.Y / 10000f,
								scale = 4f * scale,
								yPeriodic = true,
								yPeriodicLoopTime = 2000 * scale,
								yPeriodicRange = 16 * scale
							});
						}
					}
					else
					{
						Rectangle r = who.GetBoundingBox();
						r.Inflate(Game1.tileSize * 7, Game1.tileSize * 7);
						Vector2 global = Utility.getRandomPositionInThisRectangle(r, Game1.random);
						float speed = -1f;
						float scale = (float)(0.5f + Game1.random.NextDouble() * 0.5f);
						e.underwaterSprites.Add(new TemporaryAnimatedSprite(
							textureName: "LooseSprites/temporary_sprites_1",
							sourceRect: new Rectangle(132 + Game1.random.Next(3) * 8, 20, 8, 8),
							position: global,
							flipped: false,
							alphaFade: 0.002f,
							color: Color.White)
						{
							alphaFade = 0.001f - speed / 300f * scale,
							alpha = 1.25f * scale,
							motion = new Vector2(0f, speed) * scale,
							acceleration = new Vector2(0f, 0f),
							interval = 999999,
							layerDepth = global.Y / 10000f,
							scale = 4f * scale,
							scaleChange = 0.01f * scale,
							xPeriodic = true,
							xPeriodicLoopTime = 1500f * scale,
							xPeriodicRange = 12 * scale
						});
					}
				}
				
				else if (ModEntry.State.Value.MapNumber == (int)MapNumber.Orange)
				{
					if (e.actors.Count < 30 && !e.underwaterSprites.Any() && !e.actors.Any(actor => Vector2.Distance(actor.Tile, who.Tile) < 10))
					{
						Rectangle r = new(who.TilePoint, Point.Zero);
						r.Inflate(5, 5);
						Vector2 tile = Vector2.Zero;
						do tile = Utility.getRandomPositionInThisRectangle(r, Game1.random); while (!where.isTilePassable(tile) || !where.isTileOnMap(tile) || tile == who.Tile);
						e.underwaterSprites.Add(new TemporaryAnimatedSprite(
							textureName: "Characters/Monsters/Wilderness Golem",
							sourceRect: new Rectangle(0, 96, 16, 24),
							position: tile * Game1.tileSize - new Vector2(0, 8 * Game1.pixelZoom),
							flipped: false,
							alphaFade: 0f,
							color: Color.White)
						{
							alpha = 1f,
							interval = 150,
							animationLength = 7,
							layerDepth = tile.Y * Game1.tileSize / 10000f,
							scale = Game1.pixelZoom
						});
						DelayedAction.functionAfterDelay(() =>
						{
							ModEntry.CreatePumpkinMan(where, tile);
							e.underwaterSprites.Clear();
						}, 150 * 7);
						Game1.playSound("Duggy");
					}

					if (Game1.random.NextDouble() < spriteChance * 10)
						Game1.playSound("croak");
				}
			}
		}

		public static void DrawTimer(SpriteBatch b,Farmer who, int ms)
		{
			string which = ModEntry.Strings.GetValueSafe(ModEntry.State.Value.MapNumber.ToString());
			string time = Utility.getMinutesSecondsStringFromMilliseconds(ms);
			string[] texts = [which, time];
			string s = texts.MaxBy(s => Game1.dialogueFont.MeasureString(s).X);

			Vector2 zero = new(Game1.viewport.X, Game1.viewport.Y);
			Point timeSize = Game1.dialogueFont.MeasureString(time).ToPoint();
			Point textSize = Game1.dialogueFont.MeasureString($"{s}\n{s}").ToPoint();
			Point boxPadding = new(16, 8);
			Point boxSize = new(x: textSize.X + boxPadding.X * 2, y: textSize.Y + boxPadding.Y * 2);
			Point boxPosition = new(x: Game1.viewport.Width / 2 - boxSize.X / 2, y: 0);

			Color color = ModEntry.IsNightmare ? ModEntry.State.Value.MinimapColor : Color.White;
			float alpha = Math.Min(0.8f, Vector2.Distance(who.Position - zero, boxPosition.ToVector2() + boxSize.ToVector2() / 2) / boxSize.X);

			b.Draw(
				texture: Game1.fadeToBlackRect,
				destinationRectangle: new Rectangle(boxPosition, boxSize),
				color: Color.Black * alpha);
			Game1.drawWithBorder(
				message: texts[0],
				borderColor: Color.Black * alpha,
				insideColor: color * alpha,
				position: (boxPosition + boxPadding).ToVector2(),
				rotate: 0f,
				scale: 1f,
				layerDepth: 1f,
				tiny: false);
			Game1.drawWithBorder(
				message: texts[1],
				borderColor: Color.Black * alpha,
				insideColor: color * alpha,
				position: (boxPosition + boxPadding).ToVector2()
					+ new Vector2(x: textSize.X - timeSize.X, y: textSize.Y) / 2,
				rotate: 0f,
				scale: 1f,
				layerDepth: 1f,
				tiny: false);
		}

		public static void DrawMinimap(SpriteBatch b, Farmer who, int ms)
		{
			if (ModEntry.State.Value.MinimapTexture is Texture2D minimap && !ModEntry.IsNightmare)
			{
				Vector2 v = Vector2.Zero;
				float baseScale = 2f;
				float hoverScale = 4f;
				Vector2 size = minimap.Bounds.Size.ToVector2() * (ModEntry.State.Value.IsMinimapHovered ? hoverScale : baseScale);
				float alpha = Math.Min(0.666f, Vector2.Distance(who.Position, new(Game1.tileSize)) / size.X);
				ModEntry.State.Value.IsMinimapHovered = alpha >= 0.5f && new Rectangle(v.ToPoint(), size.ToPoint()).Contains(Game1.getMousePosition());
				float scale = ModEntry.State.Value.IsMinimapHovered
					? hoverScale
					: baseScale;
				b.Draw(
					texture: minimap,
					position: v,
					sourceRectangle: null,
					color: Color.White * (ModEntry.State.Value.IsMinimapHovered ? 1f : alpha),
					rotation: 0f,
					origin: Vector2.Zero,
					scale: scale,
					effects: SpriteEffects.None,
					layerDepth: 0f);
				b.Draw(
					texture: Game1.fadeToBlackRect,
					position: v + who.Tile * scale,
					sourceRectangle: null,
					color: Color.Red,
					rotation: 0f,
					origin: Vector2.Zero,
					scale: scale,
					effects: SpriteEffects.None,
					layerDepth: 0f);
			}
		}

		public static void UpdatePumpkinMen(Event e, GameLocation where, Farmer who)
		{
			foreach (NPC npc in e.actors)
			{
				if (Utility.isOnScreen(npc.Position, npc.Sprite.SpriteWidth) && npc.timerSinceLastMovement > 1000 && Game1.random.NextDouble() < 0.01)
				{
					Vector2 tile = Vector2.Zero;
					Rectangle r = new(npc.TilePoint, Point.Zero);
					r.Inflate(5, 5);
					do tile = tile = Utility.getRandomPositionInThisRectangle(r, Game1.random); while (!where.isTilePassable(tile));
					tile = Utility.getRandomAdjacentOpenTile(npc.Tile, where);

					var points = PathFindController.findPath(
						startPoint: npc.TilePoint,
						endPoint: tile.ToPoint(),
						endPointFunction: PathFindController.isAtEndPoint,
						location: where,
						character: npc,
						limit: 500);
					if (points is not null && !e.npcControllers.Any(c => c.puppet.TilePoint == npc.TilePoint))
					{
						NPCController control = new(
							n: npc,
							path: points.ToList().ConvertAll(point => (point - npc.TilePoint).ToVector2()),
							loop: false,
							endBehavior: null);
						e.npcControllers.Add(control);
						if (Game1.random.NextDouble() < 0.15 && (!ModEntry.IsNightmare || Vector2.Distance(npc.Tile, who.Tile) < 7))
						{
							string text;
							if (Vector2.Distance(npc.Tile, who.Tile) < 3)
								text = "!";
							else if (e.actors.Any(actor => actor != npc && Vector2.Distance(npc.Tile, actor.Tile) < 3))
								text = "<";
							else
								text = Game1.random.NextDouble() < 0.5 ? ".." : "?";
							npc.showTextAboveHead(text, style: 2);
						}
						npc.timerSinceLastMovement = 0;
					}
				}
			}
		}

		private void GameLoop_SaveLoaded(object? sender, StardewModdingAPI.Events.SaveLoadedEventArgs e)
		{
			ModEntry.Strings = ModEntry.LoadStrings();
		}

		private void GameLoop_Saving(object? sender, StardewModdingAPI.Events.SavingEventArgs e)
		{
			ModEntry.RepeatUnfinishedEvents(Game1.player);
		}

		private void Player_Warped(object? sender, StardewModdingAPI.Events.WarpedEventArgs e)
		{
			if (!ModEntry.EventMap)
				return;

			ModEntry.SetUpEventLocation(e.NewLocation);
		}

		private void Display_Rendered(object? sender, StardewModdingAPI.Events.RenderedEventArgs e)
		{
			if (ModEntry.State.Value.StartMs == ModEntry.State.Value.EndMs)
				return;

			SpriteBatch b = e.SpriteBatch;
			Farmer who = Game1.player;
			int ms = ModEntry.State.Value.EndMs > 0 ? ModEntry.State.Value.EndMs : ModEntry.TotalMs - ModEntry.State.Value.StartMs;

			ModEntry.DrawTimer(b, who, ms);
			ModEntry.DrawMinimap(b, who, ms);

			// Unique map features
			if (who.swimming.Value && ModEntry.State.Value.Texture is Texture2D texture)
			{
				int frame = ms / 120 % 10;
				b.Draw(
					texture: texture,
					position: Game1.GlobalToLocal(Game1.viewport, who.Position + new Vector2(0f, who.Sprite.SpriteHeight / 4 * 4)),
					sourceRectangle: new Rectangle(16 * frame, 0, 16, 16),
					color: Color.DarkBlue,
					rotation: 0f,
					origin: Vector2.Zero,
					scale: Game1.pixelZoom,
					effects: SpriteEffects.None,
					layerDepth: 0f);
			}
		}

		[HarmonyPatch]
		public static class HarmonyPatches
		{
			[HarmonyPostfix]
			[HarmonyPatch(typeof(Event))]
			[HarmonyPatch("setUpPlayerControlSequence")]
			public static void Event_SetUpPlayerControlSequence_Postfix(Event __instance)
			{
				if (!ModEntry.EventUp)
					return;

				ModEntry.State.Value.EndMs = 0;
				ModEntry.State.Value.StartMs = ModEntry.TotalMs;
				ModEntry.State.Value.IsNaughty = false;
			}

			[HarmonyPostfix]
			[HarmonyPatch(typeof(Event))]
			[HarmonyPatch("UpdateBeforeNextCommand")]
			public static void Event_UpdateBeforeNextCommand_Postfix(Event __instance)
			{
				if (!ModEntry.EventUp)
					return;

				GameLocation where = Game1.currentLocation;
				Farmer who = Game1.player;

				if (ModEntry.State.Value.PlayerLight is LightSource light)
				{
					light.position.Value = new(who.Position.X + 24f, who.Position.Y + 48f);
				}

				// Unique map features
				if (ModEntry.State.Value.MapNumber == (int)MapNumber.Orange)
				{
					ModEntry.UpdatePumpkinMen(__instance, where, who);
				}

				ModEntry.TryCreateSprite(e: __instance);
			}

			[HarmonyPostfix]
			[HarmonyPatch(typeof(Event))]
			[HarmonyPatch("receiveActionPress")]
			public static void Event_ReceiveActionPress_Postfix(Event __instance, int xTile, int yTile)
			{
				if (!ModEntry.EventUp || ModEntry.State.Value.IsMinimapHovered)
					return;

				Farmer who = Game1.player;

				void end(MapEnding ending)
				{
					__instance.EndPlayerControlSequence();
					__instance.CurrentCommand += (int)ending;

					ModEntry.State.Value.MinimapTexture = null;
					ModEntry.State.Value.EndMs = ModEntry.TotalMs - ModEntry.State.Value.StartMs;
				}

				if (ModEntry.State.Value.MapGoal.X == xTile && ModEntry.State.Value.MapGoal.Y == yTile)
				{
					// Goal
					end(ModEntry.State.Value.IsNaughty ? MapEnding.Fail : MapEnding.Success);
					DelayedAction.functionAfterDelay(
						func: ModEntry.EndEvent,
						delay: 7000);
				}
				else
				{
					// Quit
					Game1.activeClickableMenu = new ConfirmationDialog(
						message: ModEntry.Strings.GetValueSafe("QUIT"),
						onConfirm: (Farmer who) =>
						{
							Game1.exitActiveMenu();
							end(MapEnding.Quit);
							ModEntry.EndEvent();
						});
				}
			}

			[HarmonyPostfix]
			[HarmonyPatch(typeof(Event))]
			[HarmonyPatch("checkForCollision")]
			public static void Event_CheckForCollision_Postfix(Event __instance, Rectangle position, Farmer who)
			{
				if (!ModEntry.EventUp)
					return;

				// Cheat check
				if (Game1.currentLocation.IsOutOfBounds(position) || !Game1.currentLocation.isTilePassable(who.Tile))
				{
					ModEntry.State.Value.IsNaughty = true;
				}
			}
		}
	}
}
