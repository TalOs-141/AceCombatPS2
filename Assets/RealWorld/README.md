# Real-world map data

## San Diego

A 20 km square centred on San Diego Bay (UTM zone 11N, 483600 E / 3620250 N),
at true scale.

| File | What | Source |
|---|---|---|
| `SanDiego/Imagery/aerial_X_Z.jpg` | 16 aerial photo tiles, 5 km each, 1.25 m per pixel. X counts west to east, Z south to north. | USGS NAIP Imagery service |
| `SanDiego/elevation_2049.bytes` | Ground height, 2049 x 2049 samples of 32-bit float metres, south row first | USGS 3D Elevation Program (3DEP) |

| `SanDiego/BackdropSource/backdrop_X_Z.jpg` | 4 aerial tiles of the surrounding 64 km, 32 km each, 8 m per pixel (black where there is no US coverage) | USGS NAIP Imagery service |
| `SanDiego/backdrop_elevation_513.bytes` | Ground height for the surrounding 64 km, 513 x 513 samples, -9999 where there is no data | USGS 3DEP |
| `SanDiego/buildings.bytes` | About 100,000 building outlines with heights and roof triangles, converted to map coordinates | OpenStreetMap |

The imagery and elevation come from The National Map and are in the public domain with no
restrictions on use. Downloaded 2026-10-11. USGS asks for this acknowledgment:

> Map services and data available from U.S. Geological Survey, National Geospatial Program.

Terms: https://www.usgs.gov/faqs/what-are-terms-uselicensing-map-services-and-data-national-map

Everything else in the folder (terrain tiles, layers, the shore depth map, the
ocean material) is generated from these by `Assets/Editor/RealWorldBuilder.cs`
(menu: AceCombat > Build San Diego).

### OpenStreetMap

Building outlines are from OpenStreetMap, downloaded 2026-10-11 through the
Overpass API, and are licensed under the Open Database License (ODbL). Any
release of the game must show this credit:

> Map data (c) OpenStreetMap contributors, https://www.openstreetmap.org/copyright

Only about 2,500 buildings have a recorded height; the rest are estimated from
their type and footprint size.
