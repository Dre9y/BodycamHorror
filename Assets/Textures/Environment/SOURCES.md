# Environment textures

Scanned PBR sets from [ambientCG](https://ambientcg.com), licence CC0 1.0 (public domain), 1K JPG:

| Folder | Used for | Source |
| --- | --- | --- |
| PaintedPlaster011 | walls, painted lower half | https://ambientcg.com/a/PaintedPlaster011 |
| PaintedPlaster015 | walls, upper half | https://ambientcg.com/a/PaintedPlaster015 |
| Tiles110 | floor | https://ambientcg.com/a/Tiles110 |
| Plaster007 | ceiling | https://ambientcg.com/a/Plaster007 |
| PaintedMetal006 | pipes: green paint flaking to rust | https://ambientcg.com/a/PaintedMetal006 |
| Rust002 | valve wheel: rusted iron | https://ambientcg.com/a/Rust002 |
| Metal021 | metal doors and their frames | https://ambientcg.com/a/Metal021 |

`*_Mask.png` is packed from the set's Roughness and AmbientOcclusion maps by
`Assets/Editor/EnvironmentTextures.cs` (R = metalness when the set has it, G = occlusion, A = smoothness), the way URP Lit reads them.
`LensDirt.png` is painted in code by the same script.
