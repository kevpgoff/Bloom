# Bloom Art Assets (Option A: Vector & Botanical-Tech Prototype)

All prototype art here is crisp, monochrome white (`#ffffff`) SVG vector art, tinted at runtime by `Palette.cs` and loaded through `scripts/Visuals/ArtLibrary.cs`.

## Drop-in Replacement Guide

`ArtLibrary` checks for both `.svg` and `.png` files by ID, so you can drop assets from **Game-Icons.net**, **Kenney Fantasy UI Borders**, **Kenney UI Pack**, or custom vectors directly into these folders without editing code:

```text
art/
  ui/
    card_frame.svg       9-slice card border (96x96, 24px corner margins)
    panel_frame.svg      9-slice panel border (96x96, 24px corner margins)
    button_frame.svg     9-slice button border (96x96, 20px corner margins)
    energy_badge.svg     Card cost / energy medallion (64x64)
  icons/
    pieces/<piece_id>.svg    Matches `id` in game/data/pieces.json
    effects/<effect>.svg     damage, block, energy, draw, repair, focus, grow
    intents/heavy_attack.svg Heavy lane attack warning icon
    enemies/<enemy_id>.svg   Matches `id` in game/data/enemies.json
```

### Tips for External Packs
- **Game-Icons.net**: Download white-on-transparent SVGs and drop them into `icons/pieces/` or `icons/effects/` using the piece/effect ID as filename.
- **Kenney Fantasy UI Borders**: Copy any white 9-slice border SVG/PNG into `ui/card_frame.svg` or `ui/panel_frame.svg` (adjust 9-slice margins in `scripts/Visuals/BloomTheme.cs` if your replacement uses different corner sizes).
- **Godot Import**: If you open the project in the Godot Editor, set **Import → SVG → Scale** to `2.0`–`4.0` and enable **Mipmaps** for crisp scaling on high-DPI phones. Even before editor import, `ArtLibrary` rasterizes SVGs cleanly at 3x scale automatically.
