# Art

The game's art is "Option A", a vector solarpunk look in the "Holo Grove" style: a dark teal night, with the ship drawn as a lit hologram (see `docs/decisions.md`). Every image is a white (`#ffffff`) SVG on a transparent background. The game tints it at runtime with colors from `scripts/Screens/Palette.cs`. `scripts/Visuals/ArtLibrary.cs` finds each file by name, so new art needs no code change.

| Path | Holds |
| --- | --- |
| `icons/pieces/<id>.svg` | One icon per piece; `<id>` is its `id` in `data/pieces.json`. A piece with no icon falls back to one for its first tag. |
| `icons/enemies/<id>.svg` | One icon per enemy; `<id>` is its `id` in `data/enemies.json`. |
| `icons/effects/<name>.svg` | `damage`, `block`, `energy`, `draw`, `repair`, `focus`, `grow`. Card faces draw the number inside it. |
| `icons/intents/heavy_attack.svg` | The enemy's heavy attack. Attack and block intents reuse the `damage` and `block` icons. |
| `ui/energy_badge.svg` | The cost badge on a card: a hex cell, 64×64. The card draws the cost on it. |
| `ui/card_frame.svg`, `ui/panel_frame.svg` | 9-slice corner brackets laid over cards and panels, 96×96. Their edges are empty, because the card or panel draws its own border. The corner margin is a quarter of the texture's width (`BloomTheme.CreateFrameOverlay`). |
| `ui/button_frame.svg` | Every button in the theme: a dark chamfered plate with brackets on two corners, 96×96. `BloomTheme` uses it as a `StyleBoxTexture` with 20 px margins and tints it for each state. |
| `textures/glow.svg` | A soft white radial glow, 128×128, tinted and drawn behind glyphs. Imported at `svg/scale=1.0` with mipmaps. |
| `fonts/Saira.ttf` | The one typeface, a variable font with width and weight axes. `BloomTheme` makes every font from it: Saira at width 100 for text, Saira Semi Condensed (width 87.5) for headings, numbers and labels. Godot ignores the axis names, so `BloomTheme` keys them by their numeric OpenType tags. |

A `.png` with the same name works in place of an `.svg`.

## Icon style

Icons are line art on a 64×64 view box, drawn like the ship's hologram:

- **Strokes:** main lines 2.8, secondary lines 1.4–1.9 at 60–95% opacity, round caps and joins. Thinner lines vanish on the card's emblem, which is about 34 px.
- **Accents:** dashed halos and inner rims (dashes about 2.3 on, 2.9 off), and filled nodes (radius about 2) at joints and line ends, like the root traces on the deck.
- **Fills:** none, or white at 6–8% opacity behind a main outline. The only dark fill is `#0a1512`, which tinting leaves dark.
- **Effect icons** keep a dark well (`#0a1512` at 45–70% opacity) centered on (32, 32): the card draws the number there, centered, at 43% of the icon's size.
- **Enemy icons** carry more detail than the rest, because the combat screen shows the enemy large, in a target-lock reticle (`Screens/EnemyEmblem.cs`).

The backdrop is drawn in code: `Visuals/BloomBackdrop.cs` draws the sky, horizon glow, root veins, HUD rings and scanlines once, and `Visuals/BackdropMotes.cs` animates the pollen motes and the radar sweep. The motes, the sweep and the enemy's reticle all stand still in low-processor mode, so the engine can idle.

Combat effects are drawn in code too, by `Feedback/FxLayer.cs`: glowing motes, streaks and hex shards made from `textures/glow.svg`, plus rings, bolts, flashes and ghost cards, all in Palette colors and blended additively so they read as light. Floating numbers use Saira bold with a dark outline. `Feedback/CombatFeedback.cs` decides which effect plays for each thing that happens in a fight. These effects still play in low-processor mode, because each one ends on its own; the layer stops updating once nothing is playing.

## Import settings

`ArtLibrary` loads only through Godot's importer, so the editor, a run from the command line and an exported build all use the same textures. Commit each file's `.import` next to it.

- **Icons**, including `ui/energy_badge.svg`: `svg/scale=3.0` and `mipmaps/generate=true`. Icons are drawn up to about 76 px on a 720 px wide screen, so a 64 px import looks soft on phones. `ArtLibrary` warns in the log about any icon narrower than 128 px.
- **9-slice frames**: keep `svg/scale=1.0`. A 9-slice draws its corners in texture pixels, so a larger import draws larger corners.

To set these, open the file in the editor's Import dock. From the command line, run `godot --headless --path game --import` once to write the `.import` file, then copy the two lines above into it from an existing icon's `.import`, and run the import again.

The project draws 2D textures with mipmaps (`rendering/textures/canvas_textures/default_texture_filter` is Linear Mipmap), so large icons stay smooth when they are drawn small.

## Art from other people

The game is sold, so every file not made for it needs a license that allows that, and some licenses require a credit. Before adding such a file, add a line for it to `CREDITS.md` in this folder: the file, its author, where it came from and its license.

- [Kenney](https://kenney.nl) packs are CC0: no credit is required, but list them anyway.
- [Game-icons.net](https://game-icons.net) icons are CC BY 3.0: the game must credit each author.
