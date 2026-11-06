# Third-party notices

Kriegsspiel Table is made by Bruno Firme; its source is published under the Kriegsspiel Table License (the
PolyForm Noncommercial License 1.0.0 with additional terms; see `LICENSE`). It is built on the work below, which keeps its own copyright and license. A release carries this
file beside the executable.

| Component | Used for | Copyright | License |
|---|---|---|---|
| [Godot Engine](https://godotengine.org) | the engine the app runs on | © 2014-present Godot Engine contributors; © 2007-2014 Juan Linietsky, Ariel Manzur | MIT |
| [.NET runtime](https://github.com/dotnet/runtime) | carried in a release so nothing needs installing | © .NET Foundation and contributors | MIT |
| [MoonSharp](https://github.com/moonsharp-devs/moonsharp) | runs Lua scripts on the host | © 2014-2016 Marco Mastropaolo | BSD 3-Clause |
| [Loretta](https://github.com/LorettaDevs/Loretta) | reads module Lua to translate it | © 2021 GGG KILLER | MIT |
| [LibTiff.Net](https://github.com/BitMiracle/libtiff.net) | reads TIFF map sheets | © 2008-2024 Bit Miracle | BSD 3-Clause |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | the converter's picture work | © Microsoft Corporation | MIT |
| [Skia](https://skia.org) | the graphics library inside SkiaSharp | © 2011 Google LLC | BSD 3-Clause |
| [PDFtoImage](https://github.com/sungaila/PDFtoImage) | reads PDF map sheets | © David Sungaila | MIT |
| [PDFium](https://pdfium.googlesource.com/pdfium/) | the PDF library inside PDFtoImage | © 2014 The PDFium Authors | BSD 3-Clause |
| [AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET) | reads the Unity asset bundles a digital tabletop module hangs its models on | © 2020 nesrak1 | MIT |
| [AssetRipper.TextureDecoder](https://github.com/AssetRipper/TextureDecoder) | decodes the textures inside those bundles | © 2022-2026 ds5678 | MIT |
| [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) | the Workshop, in Steam builds only | © Riley Labrecque | MIT |
| Steamworks SDK libraries (`src/App/native/`) | the Workshop, in Steam builds only | © Valve Corporation | Steamworks SDK Access Agreement |
| 1889 USGS topographic map, Fredericksburg sheet | the app icon (`tools/icon-map.jpg`) | U.S. Geological Survey | Public domain |
| [Kenney](https://kenney.nl) Casino Audio and Impact Sounds | the table's sounds (`src/App/sounds/`, cut and layered from them) | Kenney Vleugels | CC0 1.0 (public domain) |
| [Poly Haven](https://polyhaven.com) HDRIs: Combination Room (Sergej Majboroda); Aristea Wreck, Kloppenheim 05, Rogland Clear Night (Greg Zaal); Chinese Garden, Lilienstein (Andreas Mischok); Horn-koppe Spring (Grzegorz Wronkowski); Dry Orchard Meadow (Alexander Scholten); Kloofendal 48d Partly Cloudy (Pure Sky), Kloppenheim 02 (Pure Sky), Qwantani Moon Noon (Pure Sky) (Greg Zaal, Jarod Guest); Belfast Sunset (Pure Sky) (Greg Zaal, Dimitrios Savva, Jarod Guest) | the backdrops around the table (`src/App/backdrops/`: their tonemapped pictures at 8192×4096, re-encoded as WebP) | their photographers | CC0 1.0 (public domain) |
| [ambientCG](https://ambientcg.com) HDRIs: Day Sky HDRI 022 A, Night Sky HDRI 002 | the island beach (brightened) and the starry night backdrops (`src/App/backdrops/`) | Lennart Demes | CC0 1.0 (public domain) |
| Hamilton Grange National Memorial 360° photo of Alexander Hamilton's study, via [NPGallery](https://npgallery.nps.gov/) and [Wikimedia Commons](https://commons.wikimedia.org/wiki/File:Explore_Hamilton_Grange_National_Memorial_digitally._(cf6bbd94-6391-44f9-867b-91ae265bba15).JPG) | the general's study backdrop (`src/App/backdrops/generals-study.webp`, re-encoded as WebP) | U.S. National Park Service | Public domain |

Godot carries third-party components of its own (FreeType, ENet, mbedTLS and others); they are listed in
Godot's [COPYRIGHT.txt](https://github.com/godotengine/godot/blob/master/COPYRIGHT.txt).

## MIT License

Applies to Godot Engine, the .NET runtime, Loretta, SkiaSharp, PDFtoImage, AssetsTools.NET,
AssetRipper.TextureDecoder and Steamworks.NET, each under its own copyright line above.

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## BSD 3-Clause License

Applies to MoonSharp, LibTiff.Net, Skia and PDFium, each under its own copyright line above.

```
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```
