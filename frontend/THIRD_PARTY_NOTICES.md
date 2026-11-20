# Avisos de terceiros

## lucide-animated (icones animados)

Os arquivos de `src/design-system/icons/` (exceto `AnimatedIcon.tsx`, `index.ts` e `sources.ts`) foram copiados do
projeto lucide-animated (https://lucide-animated.com), repositorio https://github.com/pqoqubbw/icons, e sao
distribuidos sob a licenca MIT abaixo. Os icones sao baseados no Lucide (ISC); a lista dos copiados esta em
`src/design-system/icons/sources.ts`.

```
MIT License

Copyright (c) 2024-2026 pqoqubbw

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

## three 0.186.1 e @types/three 0.186.0

O `three` (motor 3D, licenca MIT, copyright 2010-2026 three.js authors, https://threejs.org) e o motor do palco WebGL unico da landing (carregado sob demanda, fora do bundle inicial). `@types/three` (so tipos, MIT, DefinitelyTyped) e dependencia de desenvolvimento e nao
entra no bundle.

## Modelos 3D da landing

Os modelos glTF de `public/models` sao arte propria do Deckloom, gerada por script no Blender (`design/3d/`), sobre texturas escaneadas do Poly Haven (CC0), ou modelos prontos do Poly Haven (CC0: castical, pedras com musgo e toco) com malha e texturas reduzidas (`design/3d/import_ph.py`). A origem, o autor e a licenca de cada arquivo e de cada textura de terceiros ficam em `public/models/SOURCES.md`. O decodificador meshopt e o GLTFLoader vem do proprio `three` (MIT). O `gltf-validator` (Apache-2.0, Khronos Group) e dependencia de desenvolvimento, usada so nos testes, e nao entra no bundle.
