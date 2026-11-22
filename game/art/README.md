# Art

This is prototype art for "Option A", a vector solarpunk look. No art direction has been decided yet. Every file is a white (`#ffffff`) SVG on a transparent background. The game tints it at runtime with colors from `scripts/Screens/Palette.cs`. `scripts/Visuals/ArtLibrary.cs` finds each file by name, so new art needs no code change.

| Path | Holds |
| --- | --- |
| `icons/pieces/<id>.svg` | One icon per piece; `<id>` is its `id` in `data/pieces.json`. A piece with no icon falls back to one for its first tag. |
| `icons/enemies/<id>.svg` | One icon per enemy; `<id>` is its `id` in `data/enemies.json`. |
| `icons/effects/<name>.svg` | `damage`, `block`, `energy`, `draw`, `repair`, `focus`, `grow`. Card faces draw the number inside it. |
| `icons/intents/heavy_attack.svg` | The enemy's heavy attack. Attack and block intents reuse the `damage` and `block` icons. |
| `ui/energy_badge.svg` | The cost medallion on a card, 64×64. |
| `ui/card_frame.svg`, `ui/panel_frame.svg` | 9-slice borders, 96×96. The corner margin is a quarter of the texture's width (`BloomTheme.CreateFrameOverlay`). |
| `ui/button_frame.svg` | A 9-slice button border. Not used yet. |

A `.png` with the same name works in place of an `.svg`.

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
