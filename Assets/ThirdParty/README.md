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

The world builder (`Assets/Editor/WorldBuilder.cs`) picks these up automatically
and falls back to generated textures if a folder is missing.
