# Environment textures

Scanned PBR sets from [ambientCG](https://ambientcg.com), licence CC0 1.0 (public domain), 1K JPG:

| Folder | Used for | Source |
| --- | --- | --- |
| PaintedPlaster011 | walls, painted lower half | https://ambientcg.com/a/PaintedPlaster011 |
| PaintedPlaster015 | walls, upper half | https://ambientcg.com/a/PaintedPlaster015 |
| Tiles110 | floor | https://ambientcg.com/a/Tiles110 |
| Plaster007 | ceiling | https://ambientcg.com/a/Plaster007 |

`*_Mask.png` is packed from the set's Roughness and AmbientOcclusion maps by
`Assets/Editor/EnvironmentTextures.cs` (G = occlusion, A = smoothness), the way URP Lit reads them.
`LensDirt.png` is painted in code by the same script.
