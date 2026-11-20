# Ferramentas do projeto (aisef-tooling)

Atualizado em 2026-11-06. Escolha do usuário no chat: Graphify (visual, só local) e codebase-memory-mcp.

## Grafo de código

| Nome | Necessidade | Fonte e sinais | Risco | Instalação |
|---|---|---|---|---|
| Graphify (`graphifyy` 0.9.103) | Visualizar o grafo do projeto (pedido do usuário); docs, ADRs e `.aisef/` | Graphify-Labs/graphify (Apache-2.0, fonte oficial) | Baixo com `GRAPHIFY_BACKEND=none`: nada sai da máquina | **Instalado** com `pip install --user graphifyy`. Grafo gerado com `graphify update .` (só AST, sem LLM): `graphify-out/graph.html`, `GRAPH_REPORT.md`, `graph.json`. `graphify-out/` fica fora do git (`.gitignore`). Atualizar após mudanças: `GRAPHIFY_BACKEND=none graphify update .` (binário em `%APPDATA%\Python\Python314\Scripts`, fora do PATH) |
| codebase-memory-mcp | Grafo de código para os agentes (chamadas, impacto) | DeusData/codebase-memory-mcp (MIT) | Médio: binário nativo, daemon e monitor de arquivos, índice em `~/.cache/codebase-memory-mcp/`, consulta de versão ao GitHub sem dados do projeto | **Pendente**: o classificador de segurança negou rodar o `install.ps1` baixado (`-ExecutionPolicy Bypass`). O usuário roda o instalador com `--skip-config`; depois `claude mcp add --scope project codebase-memory-mcp -- <binário>` |

## Modelagem 3D da landing (T-L05, APR-049, 2026-11-13)

Objetivo: modelar a deck box, o pedestal, o altar, as cartas, os tokens e o portal de graça, num PC modesto. Hardware informado pelo usuário: Dell Inspiron 3583, Intel de 8ª geração para notebook com UHD 620, **8 GB de RAM com cerca de 1,4 GB livre**. Regra: o pesado roda na nuvem; o PC só modela e exporta.

| Nome | Necessidade | Fonte e sinais | Risco | Instalação |
|---|---|---|---|---|
| Blender 5.2 LTS | Modelagem, materiais e exportação glTF | blender.org (GPL) | Baixo; no limite de RAM (fechar Docker e navegador, texturas 1K, sem Cycles) | **Instalado pelo usuário** em 2026-11-13 |
| MCP for Blender (`mcp-for-blender`, antigo blender-mcp) | O Claude controla o Blender (Python, capturas do viewport, Poly Haven) | ahujasid/mcp-for-blender (MIT, cerca de 32 mil estrelas) | Médio: executa Python dentro do Blender; telemetria a desligar com `DISABLE_TELEMETRY=true` | **Pendente**, com aprovação do usuário quando a T-L05 começar: addon no Blender + `claude mcp add blender uvx mcp-for-blender` |
| Poly Haven | Texturas PBR, HDRI e peças | polyhaven.com (**CC0**), pelo `search_assets` do MCP | Baixo | Vem com o MCP (ligar "Poly Haven" na barra lateral) |
| TRELLIS.2 (Microsoft) | Gerar GLB com PBR a partir de imagem (deck box, pedestal) | huggingface.co/microsoft/TRELLIS.2-4B (**MIT**), Space gratuito com fila | Baixo; depende da fila e da cota grátis | Conta gratuita criada pelo usuário; `HF_TOKEN` no `.env` (ignorado pelo git) |
| gltf-transform | Comprimir glTF (meshopt, WebP/KTX2) | donmccurdy/glTF-Transform (MIT) | Baixo | `npx` na T-L05, com versão exata |

Descartados para a T-L05:
- **Hunyuan3D**: a licença proíbe usar ou exibir os resultados na UE, no Reino Unido e na Coreia do Sul.
- **Tripo, Meshy, Hyper3D Rodin, Sketchfab e Poly Pizza**: licença não conferida, ou variável por modelo.
- **TRELLIS.2 local**: pede uma GPU NVIDIA com 24 GB ou mais.

## Considerados e descartados
- Skill `/graphify` no projeto e hook do Graphify: não instalados; o hook `graph_nudge` do AISEF já cobre o lembrete.
- Servidor MCP do Graphify: não instalado (comando não documentado no README); a visualização usa o `graph.html`.
- Extração semântica com modelo externo: recusada pelo usuário (só local).
