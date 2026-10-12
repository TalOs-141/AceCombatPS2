# Third-party assets

Everything in this folder was made by someone else. Each source gets its own
subfolder; nothing here is edited by hand.

## ambientCG

- Source: https://ambientcg.com
- License: Creative Commons CC0 1.0 Universal (public domain dedication).
  Commercial use allowed, no attribution required.
  https://docs.ambientcg.com/license/
- Downloaded: 2026-10-11, the 1K JPG variant of each material.
- Kept: only the colour map and the OpenGL normal map from each archive.

| Folder | Material | Used for |
|---|---|---|
| `Facade001` | Glass curtain wall | Downtown towers |
| `Facade005` | Glass curtain wall, small panes | Downtown towers |
| `Facade006` | White concrete office facade | Mid-rise offices, airport buildings |
| `Facade009` | Facade at night with lit rooms | Dark glass towers |
| `Road007` | Two-lane road with markings | All roads |
| `Asphalt031` | Plain asphalt | Paved ground under the city and port |
| `Grass001` | Grass | Grassland on the terrain |
| `Rock058` | Rock face | Cliffs and steep slopes (brightened) |
| `Concrete034` | Concrete | Aprons, piers, quay walls |
| `RoofingTiles013A` | Slate roof slabs | Grey house roofs; surface relief on all house roofs |

The world builder (`Assets/Editor/WorldBuilder.cs`) picks these up automatically
and falls back to generated textures if a folder is missing.

## Quaternius

- Source: "Ultimate Spaceships" (May 2021) by Quaternius, https://quaternius.com,
  downloaded from https://opengameart.org/content/lowpoly-spaceships-pack
- License: Creative Commons CC0 1.0 Universal. Commercial use allowed, no
  attribution required (crediting Quaternius is appreciated).
- Downloaded: 2026-10-11. Kept: the FBX model and one texture set of all eleven ships.

| Folder | Used for |
|---|---|
| `UltimateSpaceships/Spitfire` | Enemy aircraft |
| `UltimateSpaceships/Striker` | Default player aircraft, and the ones parked on the carrier |
| The other nine ship folders | Selectable player aircraft (pause menu) |

The graphics builder (`Assets/Editor/GraphicsBuilder.cs`) falls back to the
generated jet if these folders are missing.

## NASA

- Source: NASA 3D Resources, https://github.com/nasa/NASA-3D-Resources
  (folders `3D Models/Global Hawk` and `3D Models/Space Shuttle (D)`).
- Terms: NASA describes these as free and without copyright, subject to the
  NASA Images and Media Usage Guidelines
  (https://www.nasa.gov/nasa-brand-center/images-and-media/).
  Those guidelines restrict use of the NASA insignia and forbid implying
  NASA endorsement. The Space Shuttle's texture shows the NASA logo, so
  check the guidelines (or repaint the texture) before any commercial release.
- Downloaded: 2026-10-12, as `.glb` files, imported through Unity's glTFast
  and Draco packages.

| Folder | Used for |
|---|---|
| `NASA/GlobalHawk` | Selectable player aircraft |
| `NASA/SpaceShuttle` | Selectable player aircraft |

NASA's X-57 model was not used: it is a 1.4-million-triangle engineering
model with no materials.
