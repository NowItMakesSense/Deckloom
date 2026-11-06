# Design system do Deckloom (v0.1)

Status: **v0.1 - emenda da direção de arte (ADR-0014).** Criado como v0 na feature `conta-jogador` (primeira feature com UI). Cresce por feature: cada feature nova acrescenta aqui apenas o que realmente precisar, sem duplicar componentes. Dono: agente `frontend` do AISEF (ADR-0017), com aprovação do usuário para mudanças de direção de arte. Alterações visuais de marca dependem de confirmação do usuário (ver "Decisões de marca").

Histórico:
- **v0** (2026-10-22/23): tokens iniciais, inventário e padrões de interação.
- **v0.1** (2026-10-23): papéis de cor `ground`/`surface`; escala de raios apertada (4/6/8); `<h1>` fixo em 24/32; rótulo de campo 14/500; superfícies e materiais (1.6); tokens de movimento e regras de redução (1.7); shell sem barra lateral e medidas (1.5); sombras de menu e diálogo; comportamento de movimento e casos extremos nos componentes; padrão 3.7 (texto extremo). Nada de v0 que a feature já aprovou foi removido; o que mudou está marcado "(v0.1)".

Escopo: web responsivo (React + TypeScript), sem app nativo. Somente tema claro (`color-scheme: light`). Sem dependência externa de fonte, CDN ou biblioteca de ícones carregada de terceiros (também evita requisição a terceiros por questão de LGPD).

Meta de acessibilidade para tudo aqui: WCAG 2.1 AA.

Tipo de tela deste produto até agora: **tela de aplicativo** (estilo "Achromatic ledger", shell "No sidebar", ver `docs/specs/conta-jogador/design.md` A.8). Quando surgir uma página (landing) ou uma tela com conteúdo vivo (canvas, mídia), a direção dela é decidida na feature e registrada aqui.

## 1. Tokens

Nomes sugeridos como variáveis CSS (`--color-*`, `--space-*`, ...). As marcações "neutro-padrão" da v0 foram **confirmadas pelo usuário em 2026-10-23** (U16, ver seção 4) e agora são decisão de marca.

### 1.1 Cor

As razões de contraste abaixo são aproximadas (cálculo manual de luminância relativa). O `qa` deve confirmar com ferramenta de contraste antes do gate da fase 6; qualquer par que falhe deve ser corrigido aqui, não na tela.

Cor por **papéis**; o código só referencia papéis. Cor vai nos dados, no estado e em, no máximo, um controle (a ação principal); o cromo (barra, painéis, bordas) fica sem cor. Até quatro matizes por tela (azul, vermelho, verde, âmbar). Exceção registrada: a marca "G" do Google (ver `design.md` A.8.6, E4).

Neutros:

| Token | Valor | Uso |
|---|---|---|
| `--color-ground` (v0.1; era `--color-bg-subtle`) | #F9FAFB | fundo da tela e da barra superior |
| `--color-surface` (v0.1; era `--color-bg`) | #FFFFFF | painéis, formulários e diálogos (um degrau de tom acima do fundo, sem borda e sem sombra em repouso) |
| `--color-bg`, `--color-bg-subtle` | aliases de `surface` e `ground` | mantidos para compatibilidade com v0; não usar em código novo |
| `--color-text` | #111827 | texto principal (aprox. 17:1 sobre `surface`; aprox. 16:1 sobre `ground`) |
| `--color-text-muted` | #4B5563 | texto de apoio, dicas (aprox. 7,5:1 sobre `surface`; aprox. 7,1:1 sobre `ground`) |
| `--color-border-decor` | #D1D5DB | divisores decorativos (linha única de 1px: "ou", zona de perigo). **Nunca** para borda de controle (aprox. 1,6:1) |
| `--color-border-control` | #6B7280 | borda de input, checkbox, radio (aprox. 4,8:1; exigido >= 3:1) |
| `--color-skeleton` (v0.1) | #E5E7EB | blocos planos de carregamento (decorativo, sem exigência de contraste) |
| `--color-scrim` | rgba(17,24,39,0.6) | fundo atrás de diálogo (opaco o bastante; sem desfoque) |

Marca (**decisão U16: azul**):

| Token | Valor | Uso |
|---|---|---|
| `--color-primary` | #1D4ED8 | botão primário (no máximo um preenchido por tela), link, anel de foco, preset selecionado, dado em destaque (aprox. 6,7:1 sobre branco; aprox. 6,4:1 sobre `ground`) |
| `--color-primary-hover` | #1E40AF | hover/active do primário (aprox. 8,7:1) |
| `--color-on-primary` | #FFFFFF | texto sobre `--color-primary` (aprox. 6,7:1) |
| `--color-primary-subtle` | #EFF6FF | fundo de alerta informativo, item selecionado e Tag de dado |
| `--color-on-primary-subtle` (v0.1) | #1E40AF | texto sobre `--color-primary-subtle` (aprox. 8,0:1) |

Onde o azul pode aparecer (qualquer outro uso é defeito): botão primário, links, anel de foco, anel do preset selecionado, Tags de dado (Formatos), Alert informativo. Nunca em barra, painel, ícone decorativo ou texto corrido.

Semânticas (sempre acompanhadas de ícone e texto; nunca só cor, WCAG 1.4.1):

| Token | Valor | Fundo | Contraste texto/fundo |
|---|---|---|---|
| `--color-danger` | #B91C1C | `--color-danger-subtle` #FEF2F2 | aprox. 6,0:1 (sobre #FEF2F2); aprox. 6,5:1 sobre branco |
| `--color-success` | #15803D | `--color-success-subtle` #F0FDF4 | aprox. 4,8:1 (sobre #F0FDF4); aprox. 5,0:1 sobre branco |
| `--color-warning` | #92400E | `--color-warning-subtle` #FFFBEB | aprox. 6,5:1 |
| `--color-info` | = `--color-primary` | `--color-primary-subtle` | aprox. 6,2:1 |

Estado desabilitado: isento de contraste pela WCAG, mas usar `--color-text-muted` com opacidade nunca abaixo de 3:1 para continuar legível; **não** usar desabilitado para ocultar informação essencial.

Preferências do usuário (v0.1): `prefers-contrast: more` dá borda de 1px `--color-border-control` a painéis e diálogos e usa `--color-text` no texto de apoio; `prefers-reduced-transparency: reduce` não altera nada (o sistema não usa superfície translúcida; o scrim é escuro e quase opaco).

### 1.2 Tipografia (**decisão U16: pilha de fontes do sistema**)

Família: `system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif`. Monoespaçada (só para código ou IDs reais, se necessária): `ui-monospace, "Cascadia Mono", Menlo, monospace`. Uma única família no produto.

| Token | Tamanho / altura de linha | Peso | Uso |
|---|---|---|---|
| `--text-sm` | 14px / 20px | 400 | dicas, contador, rodapé (mínimo permitido) |
| `--text-label` (v0.1) | 14px / 20px | 500 | rótulo de campo e de grupo |
| `--text-base` | 16px / 24px | 400 | corpo, campos, botões (peso 600 em botões) |
| `--text-lg` | 18px / 28px | 600 | rótulo de seção; wordmark usa 20px/28px, peso 700 (v0.1) |
| `--text-xl` | 20px / 28px | 600 | h2 |
| `--text-2xl` | 24px / 32px | 700 | **h1 em todas as larguras (v0.1)**; o único uso do peso 700 |
| `--text-3xl` | 30px / 36px | 700 | **descontinuado em v0.1** (não usar; título de tela de aplicativo é um substantivo a ~24px) |

Regras (v0.1): tracking 0 em todos os tamanhos do sistema (negativo só a partir de 32px, que o produto ainda não usa); altura de linha de pelo menos 1,4 em nomes e textos de usuário (scripts altos como tailandês e vietnamita não podem ser cortados; nunca `overflow: hidden` com altura fixa em texto); `font-variant-numeric: tabular-nums` em todo número que alinha ou atualiza (contador da bio, quantidades); formatar números com `Intl.NumberFormat("pt-BR")` e plurais com `Intl.PluralRules`; sentence case; sem maiúsculas espaçadas, sem eyebrow, sem numeração de índice.

Unidades em `rem` (base 16px) para respeitar a ampliação de texto do navegador (WCAG 1.4.4). Campos de formulário com no mínimo 16px (evita zoom automático em iOS).

### 1.3 Espaçamento (base 4px)

`--space-0` 0, `-1` 4px, `-2` 8px, `-3` 12px, `-4` 16px, `-6` 24px, `-8` 32px, `-12` 48px, `-16` 64px.

Padding de painel: `--space-6` (a partir de 640px) e `--space-4` abaixo; espaço entre campos: `--space-4`.

### 1.4 Raio, borda, sombra

- **Escala apertada (v0.1), escolhida uma vez:** `--radius-sm` 4px (chips/Tags, elementos internos), `--radius-md` 6px (inputs, botões, alertas; era 8px na v0), `--radius-lg` 8px (painéis, menu e diálogos; era 12px na v0), `--radius-full` 9999px (avatar). O raio interno é sempre menor que o externo (concêntrico). Nenhum outro valor.
- Borda de controle: 1px `--color-border-control`; em erro: 2px `--color-danger`.
- Anel de foco: `outline: 3px solid var(--color-primary); outline-offset: 2px;` (>= 3:1 contra o fundo adjacente). Nunca `outline: none` sem substituto equivalente.
- Sombras **só em quem flutua**, sem borda ao mesmo tempo (v0.1): `--shadow-dialog`: `0 1px 2px rgba(17,24,39,0.08), 0 12px 32px rgba(17,24,39,0.18)`; `--shadow-menu`: `0 1px 2px rgba(17,24,39,0.08), 0 8px 24px rgba(17,24,39,0.14)`. Nada em repouso tem sombra.

### 1.5 Layout e breakpoints

- Mobile-first. `sm` < 640px (1 coluna), `md` 640-1023px, `lg` >= 1024px.
- **Shell "No sidebar" (v0.1):** barra superior de 56px sobre `--color-ground`, sem borda; wordmark à esquerda; ações à direita. Abaixo, uma coluna centralizada: formulários e diálogos até 28rem; telas de leitura do próprio objeto (Perfil) até 36rem; padding lateral `--space-4`.
- Conteúdo deve funcionar sem rolagem horizontal a partir de 320px de largura CSS (WCAG 1.4.10).
- Área de toque mínima 44x44px em controles (regra do projeto; vai além do AA da WCAG 2.1); controles com altura mínima de 44px.
- Um diálogo mais alto que a janela rola por inteiro (`max-block-size` da janela menos `--space-8`, `overflow: auto`, `overscroll-behavior: contain`).

### 1.6 Superfícies e materiais (v0.1)

| Nível | Token | Como se separa | Uso |
|---|---|---|---|
| Fundo | `--color-ground` | - | tela e barra superior |
| Superfície | `--color-surface` | degrau de tom sobre o fundo; **sem borda e sem sombra** | painel de formulário, perfil |
| Flutuante | `--color-surface` + `--shadow-menu` / `--shadow-dialog` | sombra, **sem borda** | AccountMenu, Dialog; o Dialog usa `--color-scrim` atrás |
| Controle | `--color-surface` + borda de controle | linha de 1px | input, select, checkbox |

Regras: borda **ou** sombra, nunca as duas; sem gradiente (em fundo, botão ou texto); sem brilho; sem vidro nem desfoque de fundo (o material de vidro só é escolhido em `design.md` para controles flutuando sobre conteúdo vivo, o que este produto ainda não tem); só tema claro. Linhas únicas de 1px (`--color-border-decor`) são permitidas no "ou" e na zona de perigo.

### 1.7 Movimento (v0.1)

Telas de aplicativo não coreografam entrada: movimento é feedback. A decisão por elemento está em cada `design.md` (tabela "Especificação de movimento"); aqui ficam os tokens e as regras.

Tokens:

```css
--ease-out: cubic-bezier(0.23, 1, 0.32, 1);          /* entradas, saídas, pressionar */
--ease-in-out: cubic-bezier(0.77, 0, 0.175, 1);       /* mover ou transformar na tela */
--duration-press: 120ms;                              /* pressionar (100-160ms) */
--duration-hover: 150ms;                              /* hover e mudança de cor */
--duration-fade: 160ms;                               /* Alert, RateLimitNotice, pré-visualização (150-160ms) */
--duration-menu-in: 150ms;  --duration-menu-out: 100ms;
--duration-dialog-in: 200ms; --duration-dialog-out: 150ms;
--duration-spin: 800ms;                               /* uma volta do spinner (linear) */
```

Regras:
- Anime só `transform` e `opacity` (a única adição é a transição de **cor** em hover, nomeada, ver `design.md` A.8.6 E3). Nunca `width`, `height`, `top`, `left`, `margin`, `padding`; nunca `transition: all`; `will-change` só depois de medir um problema.
- Easing: entrar/sair e pressionar = `--ease-out`; cor/hover = `ease`; movimento contínuo (spinner) = `linear`; **nunca ease-in** em UI.
- Saída mais rápida que a entrada; nunca partir de `scale(0)` (menu e diálogo partem de `scale(0.97)` com `opacity: 0`); menu cresce do gatilho (`transform-origin` no gatilho); diálogo fica centrado.
- Todo pressionável: `:active { transform: scale(0.97); transition: transform var(--duration-press) var(--ease-out); }`. Hover só dentro de `@media (hover: hover) and (pointer: fine)`.
- Não anima: foco, erros de campo, troca de rota, skeleton, estados de checkbox/radio, expansão de texto (nunca animar altura), troca de avatar após salvar.
- Interrompível: diálogo e menu usam CSS transitions (retargetam a partir do valor atual); `@keyframes` só no spinner.
- Reduced motion (`@media (prefers-reduced-motion: reduce)`): "mais suave, não nenhum": remove deslocamento, escala e rotação; mantém `opacity` e cor que ajudam a entender. Spinner vira arco estático mais texto. Escrito junto da animação, nunca depois.

## 2. Inventário de componentes (v0, necessários à feature `conta-jogador`)

Cada componente nasce acessível; critérios testáveis ficam em `design.md` > UX da feature (ver `docs/specs/conta-jogador/ux-draft.md` e A.8).

| Componente | Estados / variantes | Notas de acessibilidade |
|---|---|---|
| **Button** | primário (preenchido azul; no máximo um por tela), secundário (contorno neutro, texto de tinta), perigo (preenchido `--color-danger`; na zona de perigo do Perfil, contorno vermelho), link; default, hover, focus, active, disabled, loading (`aria-busy="true"`, rótulo mantido, não troca de largura) (v0.1: variantes e movimento MOT-01/02/07) | elemento `<button>`; texto visível = nome acessível; loading não remove foco; rótulo longo quebra em duas linhas sem cortar, altura >= 44px |
| **TextField** (texto, email) | default, focus, preenchido, erro, desabilitado | `<label>` visível sempre associado; dica e erro ligados por `aria-describedby`; `aria-invalid` em erro; `autocomplete` correto; valor longo rola por dentro do campo |
| **PasswordField** | = TextField + botão "Mostrar senha" | botão com `aria-pressed`; colar permitido; `autocomplete="current-password"` ou `"new-password"` |
| **Textarea com contador** | default, erro, acima do limite | contador ligado por `aria-describedby`, sem leitura a cada tecla; contador com `tabular-nums` e `Intl.NumberFormat` (v0.1) |
| **Select** | default, erro | `<select>` nativo (permite adicionar valores sem mudar o componente) |
| **Checkbox** | marcado/desmarcado, erro | `<input type="checkbox">` nativo + label clicável; links dentro do label não alteram o estado; rótulo longo quebra com a caixa alinhada à primeira linha |
| **CheckboxGroup** | default, vazio (sem itens), erro | `<fieldset>` + `<legend>` |
| **RadioGroup** | default, selecionado, erro | `<fieldset>` + `<legend>` + radios nativos (setas navegam) |
| **FormError** (erro de campo) | visível/oculto | ícone + texto `--color-danger`; id referenciado pelo campo; sem animação (MOT-04) |
| **ErrorSummary** | oculto/visível | `tabindex="-1"`, recebe foco ao enviar com erros; lista de links para os campos; sem animação |
| **Alert** | info, sucesso, aviso, erro | sucesso/info: `role="status"`; erro de página (não de campo): `role="alert"`; ícone + texto + título opcional; entra por `opacity` 160ms (MOT-05) |
| **RateLimitNotice** (aviso de atraso) | oculto/visível | região `role="status"` (polite) sempre presente no DOM; texto inserido quando a resposta passa de 2 s; entra por `opacity` (MOT-06); o espaço é reservado no momento do envio |
| **Dialog de confirmação** | confirmar/cancelar, com ou sem campo (senha), enviando, erro | `role="dialog"` ou `"alertdialog"`, `aria-modal="true"`, `aria-labelledby`, foco preso, Esc cancela, foco retorna ao gatilho, fundo inerte; painel `scale(0.97)` a 1 mais `opacity` 200/150ms, scrim por `opacity` (MOT-12/13); rola por inteiro se mais alto que a janela (v0.1) |
| **Galeria de avatares** (era "AvatarPicker"; componente removido em 2026-11-05, T-183: nunca foi usado, a tela S10 monta a galeria no `AvatarDialog`, e os estilos `avatar-picker__*` vivem em `AvatarDialog.css`) | galeria de presets + envio de arquivo; selecionado, enviando, erro, carregando (círculos cinza), vazio/falha (mensagem, upload continua) | presets = RadioGroup com nome acessível por avatar; grade de 4 colunas (3 a 320px), alvos de 44px; ver envio de arquivo |
| **Envio de arquivo** (era "FileUpload"; componente removido em 2026-11-05, T-183; sem pré-visualização no S10, estilos `file-upload__*` em `AvatarDialog.css`) | vazio, arquivo escolhido (pré-visualização), enviando, erro | `<input type="file" accept="image/jpeg,image/png,image/webp">` nativo com label; sem arrastar-e-soltar no v0; validação do cliente é só de conveniência (servidor é a autoridade); nome de arquivo e pré-visualização `object-fit: cover` no quadro de 96px (v0.1) |
| **Avatar** | tamanhos 32, 40, 96 px; imagem; carregando; falha | `alt` descritivo quando a imagem é informação; `alt=""` quando ao lado de texto equivalente. v0.1: `flex-shrink: 0`, `width`/`height` fixos; carregando = círculo `--color-skeleton`; falha = círculo neutro com a primeira letra do nome (primeiro elemento de texto, seguro para emoji), mesmo nome acessível |
| **Skeleton / Spinner** | carregando | contêiner com `aria-busy="true"`; spinner acompanhado de texto ("Carregando..."). v0.1: Skeleton = blocos planos `--color-skeleton` no tamanho real, **sem brilho passando** (MOT-09); spinner gira 800ms linear, estático em reduced motion (MOT-08) |
| **AppHeader + AccountMenu** | visitante (Entrar / Criar conta), Jogador (avatar + menu: Perfil, Sair) | menu como botão de divulgação (`aria-expanded`), não `role="menu"`; fecha com Esc. v0.1: barra de 56px; wordmark texto 20px/700 em cor de tinta; nome de exibição no botão cortado no fim com reticências (some abaixo de 480px, fica só o avatar); nome completo no painel do menu e em `aria-label`; painel com `--shadow-menu`, cresce do gatilho (MOT-11) |
| **SkipLink** | visível ao receber foco | primeiro elemento focável: "Ir para o conteúdo principal" |
| **Link** | default, visited-neutro, focus | sublinhado sempre visível no corpo de texto; links externos indicam "(abre em nova aba)" |
| **Divider com texto** ("ou") | - | decorativo; linha única de 1px; não substitui estrutura |
| **Tag / Chip somente leitura** (v0.1: cor de dado) | - | usado para listar Formatos; é texto, não botão; fundo `--color-primary-subtle`, texto `--color-on-primary-subtle`, raio `sm`, sem hover; nome longo quebra dentro da Tag |
| **Panel** (v0.1) | - | superfície `--color-surface` sobre `--color-ground`, raio `lg`, sem borda e sem sombra; `prefers-contrast: more` acrescenta borda |
| **BioText** (v0.1) | recolhido, expandido, vazio | texto simples com quebras de linha preservadas (`white-space: pre-line`), `overflow-wrap: anywhere`, `dir="auto"`; limitado a 10 linhas com botão "Mostrar tudo" (`aria-expanded`, sem animar altura); vazio = "Nenhuma bio." em `--color-text-muted` |

## 3. Padrões de interação

### 3.1 Validação de formulário
- Rótulos sempre visíveis; placeholder nunca substitui rótulo.
- Dicas de regra visíveis desde o início (ex.: "Mínimo de 8 caracteres"); sem medidor de força e sem regras de composição de senha (REQ-003).
- Primeira validação ocorre **no envio**. Depois do primeiro envio com erro, cada campo com erro é revalidado ao sair do campo (blur) e ao digitar até o erro sumir. Campos sem erro não mostram erro enquanto o usuário digita.
- A validação do cliente é conveniência; o servidor é a autoridade. Erros do servidor por campo são mapeados ao mesmo campo e ao ErrorSummary.
- Erros dizem o que corrigir ("A senha precisa ter pelo menos 8 caracteres"), em pt-BR, tratando o usuário por "você".
- Botão de envio não é desabilitado por formulário inválido (evita explicar por que está inativo); é desabilitado/`aria-busy` apenas durante o envio, impedindo duplo envio.
- Dados digitados são mantidos quando o servidor rejeita o envio, exceto senhas, que são limpas em falhas de autenticação.

### 3.2 Foco
- Ao navegar para uma nova tela (rota), o foco vai para o `<h1>` (`tabindex="-1"`) e o título do documento é atualizado.
- Envio com erros: foco no ErrorSummary. Envio com sucesso que permanece na tela: foco na mensagem de sucesso (ou no `<h1>`); que muda de tela: regra de nova tela.
- Abrir diálogo: foco para dentro (primeiro campo ou o botão menos destrutivo); fechar: foco volta ao elemento que abriu.
- Retorno de redirecionamento externo (Google): foco no `<h1>` da tela de destino.

### 3.3 Resumo de erros
- Aparece acima do formulário apenas após envio com erro(s); título "Corrija os campos abaixo" (ou texto específico), lista de links "Campo: mensagem" que movem o foco ao campo correspondente. Cada erro também aparece junto ao campo (mesmo texto).
- Erro que não é de campo (ex.: credenciais inválidas) aparece como Alert de erro no topo do formulário e recebe o foco no lugar do ErrorSummary.

### 3.4 Estados assíncronos
- Carregamento de dados: Skeleton com `aria-busy` (blocos planos, sem brilho, sem deslocamento de layout); se levar > 2 s, texto "Carregando..." visível.
- Envio: botão em loading; se a resposta passar de 2 s, RateLimitNotice anuncia que a resposta está demorando por segurança (sem contagem regressiva, sem revelar o motivo exato, sem dizer se a conta existe).
- Falha de rede/servidor (5xx, timeout): Alert de erro **dentro do bloco que falhou** com ação "Tentar de novo"; dados do formulário preservados.
- Sessão expirada (401): redireciona ao login com Alert "Sua sessão expirou. Entre novamente."

### 3.5 Ações destrutivas
- Sempre em Dialog de confirmação, com texto do que será perdido, botão de perigo com rótulo específico ("Excluir minha conta definitivamente") e botão "Cancelar" ao lado. Nunca confirmar com Enter acidental: o botão de perigo não é o foco inicial.

### 3.6 Texto e tom
- pt-BR, tratamento "você", frases curtas, sem jargão técnico, sem culpar o usuário. Mensagens de autenticação seguem as versões genéricas definidas na feature (não enumerar contas).

### 3.7 Texto de usuário e casos extremos (v0.1)
- Todo texto vindo do usuário (nome, bio, nome de arquivo, email) é renderizado como texto, nunca como HTML; entra em contêiner com `min-width: 0` e `overflow-wrap: anywhere`; `dir="auto"` em nome e bio.
- **Quebrar** o que precisa ser lido por inteiro para identificar (nome, email, Tag); **cortar no fim** com reticências só metadado secundário (nome ao lado do avatar na barra), sempre com o valor completo em outro lugar (painel do menu, `aria-label`); **limitar linhas** em prévias longas (bio: 10 linhas mais "Mostrar tudo"); **nunca cortar** número, quantidade ou data.
- Itens de tamanho fixo (avatar, ícone, ação final) têm `flex-shrink: 0`; o texto ao lado tem `min-width: 0`.
- Toda tela é conferida com fixtures de pior caso (vazio, um, enorme, longo, curto, faltando, estranho, lento, erro) a 320, 768 e 1280px e texto a 200%; as fixtures só existem no build de desenvolvimento.

## 4. Decisões de marca (confirmadas pelo usuário em 2026-10-23 - decisão U16 do design de `conta-jogador`: azul #1D4ED8, fonte do sistema, só tema claro, wordmark em texto)

| # | Tema | Decisão | Opções consideradas | Observação |
|---|---|---|---|---|
| M1 | Cor primária | **Azul #1D4ED8 (decidido)** | (a) azul #1D4ED8; (b) violeta #6D28D9; (c) verde-azulado #0F766E | trocar é mudar um token |
| M2 | Tipografia | **Pilha de fontes do sistema (decidido)** | (a) sistema; (b) fonte web auto-hospedada; (c) outra fonte de marca | zero peso, zero requisição externa; reavaliar com identidade visual |
| M3 | Tema escuro | **Somente claro (decidido)** | (a) só claro; (b) segue `prefers-color-scheme`; (c) alternância manual | reavaliar quando as telas de deck existirem (exige novo conjunto de pares AA e escuros neutros verdadeiros) |
| M4 | Nome/logotipo na tela | **Texto "Deckloom" como wordmark (decidido)** | (a) texto; (b) logotipo a ser fornecido | v0.1: 20px/28px, peso 700, cor de tinta (não azul), sem tracking |
| M5 | Arte dos avatares preset | **Placeholders neutros (decidido)** | (a) placeholders; (b) conjunto ilustrado temático Magic (direitos de arte/marcas); (c) ilustrações próprias | evitar arte de terceiros sem licença |
| M6 | Reforço da "face" de marca nas telas de entrada | **Decidido (2026-10-23): só o wordmark de texto por ora; moldura de carta (63:88, desenhada em código) adiada para quando existirem telas de deck** | (a) só o wordmark; (b) wordmark com "loom" em azul; (c) moldura de carta desenhada em código, sem arte | (b) descartada (acento estilizado numa palavra) |
