# ADR-0011: Processamento do avatar com SkiaSharp (MIT, código nativo) com validação antes da decodificação

- Status: Accepted (2026-10-23)
- Date: 2026-10-23
- Deciders: usuário (escolha da opção b, 2026-10-23), a partir das opções do architect (feature `conta-jogador`)

## Context

REQ-019, REQ-020 e REQ-049 (recusa de imagem animada ou acima de 4096 px por lado / 16.777.216 pixels (4096 x 4096)): aceitar JPEG, PNG ou WebP de até 2 MB, validar por conteúdo (não pela extensão), redimensionar e reencodar no servidor removendo metadados (EXIF). A política de segurança exige dependências de imagem auditadas e com versão fixada. O architect havia recomendado ImageSharp (gerenciada) condicionada à licença; o usuário escolheu SkiaSharp.

## Decision

Adapter `SkiaAvatarImageProcessor` atrás de `IAvatarImageProcessor`, com esta sequência:
1. **Antes de qualquer decodificação**: rejeitar se o corpo passar de 2.097.152 bytes; identificar o tipo pelos bytes iniciais (JPEG `FF D8 FF`; PNG `89 50 4E 47 0D 0A 1A 0A`; WebP `RIFF....WEBP`), ignorando extensão e `Content-Type` declarados; qualquer outra assinatura (GIF, executáveis, etc.) é rejeitada.
2. **Dimensões pelo cabeçalho**: `SKCodec.Create` lê apenas o cabeçalho; rejeitar se o formato detectado pelo codec divergir da assinatura (REQ-020); rejeitar (REQ-049) se a imagem for animada (mais de um quadro), se `width > 4096 || height > 4096`, ou se `(long)width * height > 16_777_216` - 16.777.216 pixels (4096 x 4096), defesa em profundidade contra bomba de descompressão. Comparações estritas: 4096 x 4096 é aceita; 4097 x 4096 é recusada.
3. **Decodificação limitada**: decodificar com `SKCodec.GetPixels` para um bitmap de tamanho conhecido (ou com escala de amostragem do codec quando a imagem for maior que 512 px, reduzindo memória); aplicar a orientação EXIF à imagem antes de descartar os metadados.
4. **Redimensionar** para 256x256 com recorte central (`SKSamplingOptions` de alta qualidade).
5. **Reencodar** em WebP (qualidade 85) a partir apenas dos pixels: o encoder não recebe nenhum metadado de origem, então EXIF/XMP/ICC são descartados.
6. Saída maior que 256 KB (não esperado) é rejeitada como erro interno.
7. Todo o processamento roda com limite de concorrência (semáforo) e timeout de 5 s.

Operação: versão de `SkiaSharp` e de `SkiaSharp.NativeAssets.Linux.NoDependencies` fixadas em `Directory.Packages.props`; imagem Docker da API inclui os binários nativos do pacote (sem depender de bibliotecas do sistema); atualização acompanhada pelas auditorias de dependências do CI (`dotnet list package --vulnerable`) e por avisos de segurança do Skia/libwebp/libjpeg-turbo/libpng.

## Alternatives considered

- **(a) SixLabors.ImageSharp**: 100% gerenciada (menor superfície de falhas de memória), mas sob a licença Six Labors Split (paga acima de US$ 1 milhão de receita anual).
- **(c) Magick.NET**: muito completa, porém ImageMagick nativo tem grande superfície de ataque e exige política de formatos restrita.

## Consequences

- Licença MIT sem custo; desempenho alto.
- Superfície de ataque de código nativo (decoders em C/C++): mitigada pela validação de assinatura e tamanho antes da decodificação, limites de dimensão pelo cabeçalho, versão fixada e atualização rápida. Risco residual marcado para revisão do `security` na fase 7.
- Testes do adapter usam fixtures reais (JPEG com EXIF/GPS e orientação, PNG, WebP, GIF, WebP animado, PNG animado, arquivo falso com extensão `.png`, 2,1 MB, 4096 x 4096 aceita, 4097 x 4096 e 4096 x 4097 recusadas, imagem com cabeçalho declarando 20000 x 20000) e precisam dos binários nativos no ambiente de CI.
