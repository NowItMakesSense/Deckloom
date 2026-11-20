# Design system do Deckloom (v1.0)

Status: **v1.0 - Grimório arcano, para o app inteiro (REQ-L03 v5 e REQ-L10 v2, feature `landing`, tarefa T-L01).** Substitui a direção "Achromatic ledger" (azul, tema único, sem vidro) e a "ficha de coleção" da landing. Cresce por feature: cada feature nova acrescenta aqui apenas o que realmente precisar, sem duplicar componentes. Dono: agente `frontend` do AISEF (ADR-0017), com aprovação do usuário para mudanças de direção de arte. Alterações visuais de marca dependem de confirmação do usuário (ver "Decisões de marca").

Histórico:

- **v1.0.3** (2026-11-14, tarefa T-L05, REQ-L14): os objetos 3D da landing passam a ser modelos glTF próprios (Blender 5.2, scripts em `design/3d/`, carregados sob demanda de `public/models`). Decisões do usuário: **runas** são glifos élficos caligráficos dentro de anéis de selo, com núcleo violeta e filete de ouro, maiores e em menor número; o brilho das runas da cena inteira (deck box, portal, caminho e altar, `WORLD.runeGlow`) passa do verde-amarelado para o violeta da marca `#9a77ff`. Deck box aprovada na v4: madeira escura entalhada com trepadeira, latão envelhecido com pátina e rebites, cinta gravada e friso trançado na tampa. O símbolo do Deckloom nos modelos será trocado pelo logo novo da FEAT-LOGO (T-G03).
- **v1.0.2** (2026-11-13, tarefa T-L02 v9, REQ-L10 v5, REQ-L12 v5 e REQ-L13): a landing vira um **palco WebGL único** (three, sem ShaderGradient, R3F, camera-controls nem three-stdlib): floresta élfica em silhuetas, deck box, altar, runas, quatro ganchos, mesa dos cinco jogos, epílogo e planos com animejs, com fallback estático (1.12, 1.14 e 1.15). A v1.0.1 (jornada com gradiente e R3F) foi substituída.
- **v1.0.1** (2026-11-12, tarefa T-L02, REQ-L10 v2, REQ-L11 e REQ-L12): a landing vira uma jornada 3D (ShaderGradient atrás, modelos 3D na frente, câmera com camera-controls guiada pelo scroll, uma pose por seção) e os quatro ganchos viram cards com moldura própria (1.12 e 1.14); `three` deixa de ser só do hero. Decisões do usuário em 2026-11-11 (APR-045).
- **v1.0** (2026-11-06): identidade "Grimório arcano" decidida com o usuário para o app inteiro: fantasia ilustrada, violeta como cor de marca com dourado como metal, temas claro e escuro (segue o sistema, com troca pelo usuário), vidro (glassmorphism) bem presente, Cinzel Decorative nos títulos e Albert Sans no texto, ornamentos desenhados em código, animação com anime.js e uma jornada 3D (Three.js) na landing inteira. Ajustes do usuário ao revisar o visual: a fonte de títulos anterior saiu (agora Cinzel Decorative, sem caixa alta forçada), mais fantasia, mais vidro e mais movimento. Tokens em `tokens.css`, fontes em `fonts.css`, infraestrutura de tema em `frontend/src/app/theme/`. A página estática de revisão visual usada nesta versão foi aposentada (APR-045): a landing é construída direto no app.
- **v0.2** (2026-11-06): direção "ficha de coleção" da landing, **descartada pelo usuário** (não foi consultado e achou fraca). Removidos o escopo `.landing`, o arquivo de tokens dela e as fontes da v0.2.
- **v0.1** (2026-10-23): papéis de cor `ground`/`surface`; escala de raios apertada (4/6/8); `<h1>` fixo em 24/32; rótulo de campo 14/500; superfícies e materiais; tokens de movimento e regras de redução; shell sem barra lateral e medidas; sombras de menu e diálogo; comportamento de movimento e casos extremos nos componentes; padrão 3.7 (texto extremo).
- **v0** (2026-10-22/23): tokens iniciais, inventário e padrões de interação.

Escopo: web responsivo (React + TypeScript), sem app nativo. Dois temas (claro e escuro) em todo o app e na landing. Sem dependência externa de fonte, CDN ou biblioteca de ícones carregada de terceiros (também evita requisição a terceiros por questão de LGPD). Sem arte nem nomes de carta dos jogos (são de terceiros): as cartas desenhadas são originais. As únicas marcas de terceiros são os logos oficiais dos cinco jogos e os símbolos de mana, empacotados no projeto e tratados em 1.13 (com o risco RISK-FEAT-LANDING-6).

Meta de acessibilidade para tudo aqui: WCAG 2.1 AA.

Tipo de tela: **tela de aplicativo** e **página pública** compartilham os mesmos tokens. A página pública (landing) segue a direção "Dressed as something else" da skill (o Deckloom vestido de grimório antigo); as telas do app seguem as regras de tela de aplicativo da skill, com vidro e fantasia nas superfícies.

## Ideia

**Nesta página, a coleção do jogador (as cartas que ele já tem) vira um grimório antigo, e cada deck, um encantamento escrito nele.** O produto fala de montar decks com cartas que a pessoa já possui; a categoria emprestada é a do livro raro e do antiquário de feitiços: capa violeta, páginas de vidro, fita de marcador e selos de ouro, títulos em capitais altas e estreitas, texto calmo.

A coisa viva da landing é o próprio deck: tocar em "Tenho +1" numa carta que falta fecha o deck na hora (47 de 60 vira 48 de 60, e a cotação das lojas perde uma cópia). A coisa viva do app é o tema: o sistema decide, e o jogador troca quando quiser.

## 1. Tokens

Todos os tokens vivem em `frontend/src/design-system/tokens.css`; fontes em `fonts.css`. O código só referencia papéis; nenhuma cor ou fonte literal fora dos tokens.

### 1.1 Cor

Cor por **papéis**: um fundo (`ground`), uma tinta (`text`), um acento (`primary`) por tema; o dourado é **metal** (selo, filete, fita, moldura de carta, aro), nunca texto corrido. Neutros com viés violeta (canal azul acima do vermelho): nada de cinza puro. Cor vai nos dados, no estado e na ação principal; o cromo (barra, painéis) é vidro neutro.

Os dois temas têm os mesmos nomes. O claro vive em `:root`; o escuro em `:root[data-theme="dark"]` (escolha do jogador) e em `@media (prefers-color-scheme: dark)` com `:root:not([data-theme="light"])` (sistema). Os dois blocos escuros são idênticos (o teste confere).

Tema claro ("velino violeta") e tema escuro ("noite"):

| Token                       | Claro                 | Escuro              | Uso                                                                                      |
| --------------------------- | --------------------- | ------------------- | ---------------------------------------------------------------------------------------- |
| `--color-ground`            | #f3effa               | #120d1e             | fundo da tela e da navegação                                                             |
| `--color-surface`           | #fbf9ff               | #1c1530             | campos, painéis sólidos, diálogos de formulário (um degrau acima do fundo)               |
| `--color-text`              | #1b1230               | #ece7fa             | texto principal e títulos (a tinta; nunca violeta)                                       |
| `--color-text-muted`        | #4a3f6b               | #b9aedb             | texto de apoio, dicas                                                                    |
| `--color-border-decor`      | #d9d0ee               | #3a2f5c             | divisores decorativos (linha única de 1px). **Nunca** borda de controle                  |
| `--color-border-control`    | #6f6395               | #8f82b8             | borda de input, checkbox, radio (exigido >= 3:1)                                         |
| `--color-skeleton`          | #e6def5               | #2a2145             | blocos planos de carregamento e trilho do medidor                                        |
| `--color-primary`           | #5b2fc4               | #9a77ff             | violeta de marca: botão primário, link, anel de foco, item selecionado, dado em destaque |
| `--color-primary-hover`     | #4a22a8               | #b39bff             | hover/active do primário                                                                 |
| `--color-on-primary`        | #ffffff               | #120d1e             | texto sobre `--color-primary`                                                            |
| `--color-primary-subtle`    | #ece6fb               | #2a1f4d             | fundo de alerta informativo, item selecionado, Tag de dado                               |
| `--color-on-primary-subtle` | #3d1d96               | #d6c8ff             | texto sobre `--color-primary-subtle`                                                     |
| `--color-danger`            | #b91c1c               | #ff8f8f             | erro, ação destrutiva                                                                    |
| `--color-danger-subtle`     | #fdecef               | #3a1219             | fundo de alerta de erro                                                                  |
| `--color-danger-hover`      | #991b1b               | #ffb0b0             | hover do perigo                                                                          |
| `--color-success`           | #15803d               | #6fd49a             | sucesso                                                                                  |
| `--color-success-subtle`    | #e9f7ee               | #0f2c1c             | fundo de alerta de sucesso                                                               |
| `--color-warning`           | #92400e               | #f0b35a             | aviso                                                                                    |
| `--color-warning-subtle`    | #fdf3dc               | #33230a             | fundo de alerta de aviso                                                                 |
| `--color-info`              | = `--color-primary`   | = `--color-primary` | alerta informativo                                                                       |
| `--color-scrim`             | rgba(18, 13, 30, 0.6) | rgba(5, 3, 12, 0.7) | fundo atrás de diálogo (escuro e quase opaco; é a única translucidez de `--color-*`)     |
| `--color-gold`              | #b88a2e               | #d9ae55             | **metal**: selo "Em breve", moldura de carta, fita, aro, filete grosso. Não é texto      |
| `--color-gold-subtle`       | #f3e6c4               | #3a2d12             | fundo do selo e das barras de carta                                                      |
| `--color-on-gold`           | #1b1230               | #120d1e             | texto sobre `--color-gold` (preenchimento)                                               |
| `--color-on-gold-subtle`    | #6b4a0a               | #e8c46f             | texto sobre `--color-gold-subtle` (o único dourado que vira texto)                       |
| `--color-night`             | #120d1e               | #120d1e             | polo fixo: faixa escura e rodapé nos dois temas                                          |
| `--color-moon`              | #efe9ff               | #efe9ff             | polo fixo: tinta sobre `--color-night`                                                   |

O violeta de marca do tema escuro (#9a77ff) é um pouco mais claro que o #8d66ff aprovado pelo usuário na revisão visual: é o mínimo que dá 4,5:1 sobre o vidro e sobre `--color-primary-subtle`; o matiz é o mesmo.

Pares declarados e contraste (claro / escuro; WCAG 2.1 AA, conferidos em `tokens.test.ts`):

| Texto / elemento sobre fundo                                              | Claro | Escuro |
| ------------------------------------------------------------------------- | ----- | ------ |
| `--color-text` sobre `--color-ground`                                     | 15,8  | 15,8   |
| `--color-text` sobre `--color-surface`                                    | 17,1  | 14,5   |
| `--color-text-muted` sobre `--color-ground`                               | 8,4   | 9,2    |
| `--color-text-muted` sobre `--color-surface`                              | 9,1   | 8,5    |
| `--color-primary` (link, foco) sobre `--color-ground`                     | 7,0   | 5,9    |
| `--color-primary` sobre `--color-surface`                                 | 7,6   | 5,4    |
| `--color-on-primary` sobre `--color-primary`                              | 7,9   | 5,9    |
| `--color-on-primary` sobre `--color-primary-hover`                        | 10,1  | 8,2    |
| `--color-on-primary-subtle` sobre `--color-primary-subtle`                | 9,5   | 9,7    |
| `--color-danger` sobre `--color-surface`                                  | 6,2   | 8,0    |
| `--color-danger` sobre `--color-danger-subtle`                            | 5,7   | 7,5    |
| `--color-success` sobre `--color-success-subtle`                          | 4,5   | 8,3    |
| `--color-warning` sobre `--color-warning-subtle`                          | 6,4   | 8,2    |
| `--color-on-gold-subtle` sobre `--color-gold-subtle`                      | 6,5   | 8,0    |
| `--color-on-gold` sobre `--color-gold`                                    | 5,7   | 9,2    |
| `--color-border-control` sobre `--color-surface` (nao texto, exigido 3:1) | 5,2   | 5,0    |

Semânticas (sempre acompanhadas de ícone e texto; nunca só cor, WCAG 1.4.1): `--color-danger`, `--color-success`, `--color-warning` e `--color-info`.

Onde o violeta pode aparecer (qualquer outro uso é defeito): botão primário, links, anel de foco, item selecionado, medidor de progresso, Tags de dado, Alert informativo. Nunca em barra, painel, ícone decorativo ou texto corrido.

Estado desabilitado: isento de contraste pela WCAG, mas usar `--color-text-muted` com opacidade nunca abaixo de 3:1 para continuar legível; **não** usar desabilitado para ocultar informação essencial.

Preferências do usuário: `prefers-contrast: more` usa `--color-text` no texto de apoio (também no tema escuro: o bloco repete os seletores escuros, que têm mais especificidade) e `prefers-reduced-transparency: reduce` troca o vidro pelo sólido de reserva (1.6).

### 1.2 Tipografia (Cinzel Decorative e Albert Sans)

Duas famílias, cada uma com um trabalho que nunca troca:

- **Títulos: Cinzel Decorative** (`--font-display`), peso `--display-weight` (700) nos títulos de seção e de painel e `--display-weight-strong` (900) na manchete, no wordmark e nos números de destaque ("47 de 60"). Capitais romanas com remates ornamentados, de lombada de grimório; as minúsculas já desenham versaletes, então **sem caixa alta forçada** (nunca `text-transform`). Tracking `--display-tracking` (0), altura de linha `--display-leading` (1,05). Pilha: `"Cinzel Decorative", "Iowan Old Style", "Palatino Linotype", Palatino, Georgia, serif`. É larga: a manchete do hero tem no máximo 2 linhas em 1440, 768 e 390px (cerca de 15 caracteres por linha).
- **Texto: Albert Sans** (`--font-sans`), pesos 400 (corpo) e 500 (ênfase), altura de linha `--text-leading` (1,55), tracking 0. Pilha: `"Albert Sans", system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif`. Monoespaçada só para código ou IDs reais; não há uma fonte monoespaçada no sistema.

Escala fluida da skill, cinco passos no máximo (páginas públicas, blocos de destaque e a landing):

| Token          | Valor                                       | Uso                                 |
| -------------- | ------------------------------------------- | ----------------------------------- |
| `--step-small` | clamp(0.8125rem, 0.79rem + 0.1vw, 0.875rem) | notas, legendas, rodapé             |
| `--step-body`  | clamp(1.0625rem, 1rem + 0.25vw, 1.1875rem)  | parágrafos                          |
| `--step-lead`  | clamp(1.25rem, 1.1rem + 0.7vw, 1.625rem)    | frase de apoio                      |
| `--step-head`  | clamp(2rem, 1.3rem + 3.2vw, 4rem)           | título de seção (Cinzel Decorative) |

O título principal da landing (manchete) usa o tamanho do bloco de abertura da direção (até 11rem, em `clamp()`), fora desses quatro passos.

Escala do app (telas de trabalho, formulários, diálogos; nada abaixo de 14px nos componentes):

| Token          | Tamanho / altura de linha | Peso         | Uso                                               |
| -------------- | ------------------------- | ------------ | ------------------------------------------------- |
| `--text-sm`    | 14px / 20px               | 400          | dicas, contador, rodapé (mínimo permitido no app) |
| `--text-label` | 14px / 20px               | 500          | rótulo de campo e de grupo                        |
| `--text-base`  | 16px / 24px               | 400          | corpo, campos, botões (peso 500 em botões)        |
| `--text-lg`    | 18px / 28px               | 500          | rótulo de seção                                   |
| `--text-xl`    | 20px / 28px               | 700 (Cinzel) | h2, título de painel                              |
| `--text-2xl`   | 24px / 32px               | 700 (Cinzel) | **h1 em todas as larguras**                       |

Regras: o h1 e o h2 usam Cinzel Decorative (regra global em `tokens.css`), só nos pesos 700 e 900, nunca em itálico nem em texto corrido. Tracking 0 no display e no texto. Altura de linha de pelo menos 1,4 em nomes e textos de usuário (scripts altos como tailandês e vietnamita não podem ser cortados; nunca `overflow: hidden` com altura fixa em texto). `font-variant-numeric: tabular-nums` em todo número que alinha ou atualiza (contador da bio, quantidades, "47 de 60"). Números com `Intl.NumberFormat("pt-BR")` e plurais com `Intl.PluralRules`. Sentence case no texto e nos títulos; nada em caixa alta forçada. Sem eyebrow, sem numeração de índice, sem monoespaçada.

Unidades em `rem` (base 16px) para respeitar a ampliação de texto do navegador (WCAG 1.4.4). Campos de formulário com no mínimo 16px (evita zoom automático em iOS).

**Fontes: origem e licença.** Auto-hospedadas em `frontend/public/fonts/<família>/` (servidas pelo app, nenhuma requisição a terceiros), cada pasta com o `OFL.txt` da família. Licença das duas: SIL Open Font License 1.1. Os woff2 do subconjunto latin (cobre pt-BR) foram baixados do Google Fonts apenas na obtenção, uma vez, e versionados no repositório. `fonts.css` declara cada `@font-face` com `font-display: swap`.

| Família           | Arquivo                                                                                   | Eixos                          | Repositório oficial                                                                                                                                               | Licença |
| ----------------- | ----------------------------------------------------------------------------------------- | ------------------------------ | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------- |
| Cinzel Decorative | `cinzel-decorative/CinzelDecorative-700-latin.woff2` e `CinzelDecorative-900-latin.woff2` | pesos 700 e 900 (estáticos)    | https://github.com/NDISCOVER/Cinzel (copyright Natanael Gama; cópia no Google Fonts: https://github.com/google/fonts/tree/main/ofl/cinzeldecorative)              | OFL 1.1 |
| Albert Sans       | `albert-sans/AlbertSans-var-latin.woff2`                                                  | peso 100-900 (cobre 400 e 500) | https://github.com/usted/Albert-Sans (copyright The Albert Sans Project Authors; cópia no Google Fonts: https://github.com/google/fonts/tree/main/ofl/albertsans) | OFL 1.1 |

Guarda automática: `cssRules.test.ts` reprova URL externa em qualquer CSS do frontend e exige que cada `@font-face` aponte para arquivo existente em `public/fonts` com `OFL.txt` na pasta da família.

### 1.3 Espaçamento (base 4px)

`--space-0` 0, `-1` 4px, `-2` 8px, `-3` 12px, `-4` 16px, `-6` 24px, `-8` 32px, `-12` 48px, `-16` 64px.

Padding de painel: `--space-6` (a partir de 640px) e `--space-4` abaixo; espaço entre campos: `--space-4`.

### 1.4 Raio, borda, sombra, foco

- **Escala de raios apertada, escolhida uma vez:** `--radius-sm` 4px (chips/Tags, selo), `--radius-md` 6px (inputs, botões, alertas), `--radius-lg` 8px (painéis de vidro, menu e diálogos, moldura de carta), `--radius-full` 9999px (avatar). O raio interno é sempre menor que o externo (concêntrico). Nenhum outro valor.
- Borda de controle: 1px `--color-border-control`; em erro: 2px `--color-danger`. A borda luminosa do vidro (`--glass-edge`) é só dos painéis de vidro do app, não de controles.
- Anel de foco: `outline: 3px solid var(--color-primary); outline-offset: 2px;` (>= 3:1 contra o fundo adjacente nos dois temas). O dourado **não** é anel de foco: #b88a2e dá 2,8:1 sobre o fundo claro. Nunca `outline: none` sem substituto equivalente. `::selection` usa `--color-primary` com `--color-on-primary`.
- Sombras **só em quem flutua** (AccountMenu e Dialog), sem borda de controle ao mesmo tempo: `--shadow-menu` e `--shadow-dialog`, com valores por tema (claro: tinta violeta a 10%/18-22%; escuro: preto a 40%/50-55%). Nada em repouso tem sombra; profundidade vem do vidro e do degrau de tom, nunca de brilho.

### 1.5 Layout e breakpoints

- Mobile-first. `sm` < 640px (1 coluna), `md` 640-1023px, `lg` >= 1024px.
- **Shell "No sidebar" (v0.1, navegação revista na v1.0):** a navegação é a pílula de vidro flutuante e centralizada de 1.13 (`--glass-bg`), sem borda de controle, com a logo e as ações dentro dela; não há barra de ponta a ponta. Abaixo, uma coluna centralizada: formulários e diálogos até 28rem; telas de leitura do próprio objeto (Perfil) até 36rem; padding lateral `--space-4`.
- Conteúdo deve funcionar sem rolagem horizontal a partir de 320px de largura CSS (WCAG 1.4.10).
- Área de toque mínima 44x44px em controles (regra do projeto; vai além do AA da WCAG 2.1); controles com altura mínima de 44px.
- Um diálogo mais alto que a janela rola por inteiro (`max-block-size` da janela menos `--space-8`, `overflow: auto`, `overscroll-behavior: contain`).

### 1.6 Vidro (glassmorphism) e superfícies

O vidro é **bem presente**: o app inteiro tem navegação, painéis, menus, diálogos, cards e formulários em vidro sobre a textura de fantasia (1.6.4). Cada superfície de vidro é: fundo translúcido + desfoque do que está atrás + saturação, sempre com um **sólido opaco de reserva**.

**1.6.1 Tokens de vidro** (por tema):

| Token                  | Claro                     | Escuro                   | Papel                                                                           |
| ---------------------- | ------------------------- | ------------------------ | ------------------------------------------------------------------------------- |
| `--glass-bg`           | rgba(255, 255, 255, 0.66) | rgba(34, 26, 60, 0.66)   | vidro comum: painéis, cards, navegação, listas                                  |
| `--glass-bg-strong`    | rgba(255, 255, 255, 0.84) | rgba(30, 23, 52, 0.86)   | vidro forte: menus, diálogos, formulários, a página do grimório                 |
| `--glass-edge`         | rgba(91, 47, 196, 0.3)    | rgba(190, 165, 255, 0.3) | borda luminosa de 1px dos painéis de vidro do app                               |
| `--glass-solid`        | #f7f4fd                   | #1f1836                  | **fallback** opaco de `--glass-bg`                                              |
| `--glass-solid-strong` | #fbf9ff                   | #241c3f                  | **fallback** opaco de `--glass-bg-strong`                                       |
| `--glass-worst`        | #5b2fc4                   | #9a77ff                  | o **pior fundo previsto** atrás de texto sobre vidro (o violeta de marca cheio) |
| `--glass-blur`         | 18px                      | 18px                     | raio do desfoque (igual nos dois temas)                                         |
| `--glass-saturate`     | 1.5                       | 1.5                      | saturação do que passa pelo vidro (multiplicador)                               |

Receita da superfície (feita pelos componentes na T-L01C; a landing e as telas do app usam a mesma):

```css
.glass {
  background-color: var(--glass-bg);
  backdrop-filter: blur(var(--glass-blur)) saturate(var(--glass-saturate));
}
@supports not (backdrop-filter: blur(1px)) {
  .glass {
    background-color: var(--glass-solid);
  }
}
```

**1.6.2 Onde o vidro vale:** navegação (a pílula flutuante centralizada de 1.13, nunca uma barra de ponta a ponta), painéis e cards do app, listas de jogos, menu da conta, diálogos (com `--glass-bg-strong` e `--color-scrim` atrás), formulários e a página do grimório da landing. **Onde não vale:** texto corrido de leitura longa sobre fundo variável sem camada forte, campos de entrada (o campo é sólido em `--color-surface` com borda de controle dentro do vidro), cartas desenhadas e o selo.

**1.6.3 Limites do vidro:**

- Texto sobre vidro precisa de 4,5:1 no **pior fundo** (`--glass-worst`) e no **fallback** sem `backdrop-filter` (`--glass-solid*`). O `tokens.test.ts` compõe cada `rgba` sobre o pior fundo e sobre `--color-ground`/`--color-surface` e falha abaixo de 4,5:1.
- Sem `backdrop-filter` (navegador sem suporte, modo de economia) o fundo é sempre o sólido opaco do mesmo papel; `prefers-reduced-transparency: reduce` faz o mesmo por escolha do usuário (`--glass-bg` passa a `var(--glass-solid)`).
- No máximo três camadas de vidro empilhadas (navegação, painel, menu); vidro dentro de vidro só para menu/diálogo (forte) sobre painel (comum). Desfoque custa desempenho: nunca em lista de centenas de itens e nunca animar `backdrop-filter`.
- O vidro não carrega informação sozinho: a separação é também de tom e de borda; o estado nunca depende da translucidez.
- Sem arte de terceiros atrás do vidro.

Contraste de texto sobre vidro, no pior fundo (`--glass-worst`), texto / apoio:

| Vidro               | Claro      | Escuro     |
| ------------------- | ---------- | ---------- |
| `--glass-bg`        | 9,8 / 5,2  | 7,9 / 4,6  |
| `--glass-bg-strong` | 13,7 / 7,3 | 11,7 / 6,8 |

Em fundo comum (`--color-ground`), texto / apoio / link (`--color-primary`):

| Vidro               | Claro            | Escuro           |
| ------------------- | ---------------- | ---------------- |
| `--glass-bg`        | 17,2 / 9,1 / 7,6 | 14,3 / 8,4 / 5,3 |
| `--glass-bg-strong` | 17,5 / 9,3 / 7,8 | 14,4 / 8,4 / 5,3 |

No fallback sólido, texto / apoio / link:

| Fallback               | Claro            | Escuro           |
| ---------------------- | ---------------- | ---------------- |
| `--glass-solid`        | 16,4 / 8,7 / 7,3 | 14,0 / 8,2 / 5,2 |
| `--glass-solid-strong` | 17,1 / 9,1 / 7,6 | 13,2 / 7,7 / 4,9 |

**1.6.4 Textura de fantasia** (fundo atrás do vidro): brilho violeta e dourado em camadas radiais (`--aura-violet` e `--aura-gold`, misturados a no máximo 45% e 30% sobre `--color-ground`) mais um campo de estrelas de 1px em duas grades, e, na landing, um aro arcano dourado girando devagar atrás da página do grimório. A textura é só decoração: nunca carrega informação, e o contraste é medido no pior fundo de vidro, não no brilho.

| Token           | Claro   | Escuro  |
| --------------- | ------- | ------- |
| `--aura-violet` | #8d66ff | #5b2fc4 |
| `--aura-gold`   | #e0b866 | #b88a2e |

**1.6.5 Níveis de superfície:**

| Nível           | Material                                        | Separação                            | Uso                                                      |
| --------------- | ----------------------------------------------- | ------------------------------------ | -------------------------------------------------------- |
| Fundo           | `--color-ground` + textura                      | -                                    | tela                                                     |
| Vidro comum     | `--glass-bg`                                    | tom, desfoque e borda `--glass-edge` | navegação, painéis, cards                                |
| Vidro forte     | `--glass-bg-strong`                             | idem                                 | menu, diálogo, formulário, a página do grimório          |
| Flutuante       | vidro forte + `--shadow-menu`/`--shadow-dialog` | sombra                               | AccountMenu, Dialog (o Dialog usa `--color-scrim` atrás) |
| Controle        | `--color-surface` + borda de controle           | linha de 1px                         | input, select, checkbox                                  |
| Faixa invertida | `--color-night` + `--color-moon`                | campo chapado                        | rodapé                                                   |

Regras: borda **ou** sombra, nunca as duas em controle; gradiente só na textura de fantasia (1.6.4), nunca em botão, texto ou fundo de controle; brilho só na textura; dourado só como metal.

### 1.7 Movimento

Telas de aplicativo não coreografam entrada: movimento é feedback. A landing tem uma jogada de assinatura (a manchete sobe de trás da linha de base, 900ms, a segunda linha 110ms depois) e entradas por seção. A decisão por elemento está em cada `design.md` (tabela "Especificação de movimento"); aqui ficam os tokens e as regras.

**Biblioteca:** as animações usam **anime.js** (https://animejs.com, MIT), instalado com versão exata: **animejs 4.5.0** (`frontend/package.json`; empacotada pelo app, sem CDN). Os componentes do app passam a usá-lo na T-L01B. Sempre com fallback para `prefers-reduced-motion`: a classe que esconde estados iniciais só liga com a biblioteca carregada e sem redução de movimento, então sem script o conteúdo já está visível e parado. CSS puro continua valendo para feedback simples (pressionar, hover, menu e diálogo). Os ícones animados (1.10) respeitam a mesma regra.

Tokens:

```css
--ease-out: cubic-bezier(0.23, 1, 0.32, 1); /* entradas, saídas, pressionar */
--ease-in-out: cubic-bezier(
  0.77,
  0,
  0.175,
  1
); /* mover ou transformar na tela */
--duration-press: 120ms; /* pressionar (100-160ms) */
--duration-hover: 150ms; /* hover e mudança de cor */
--duration-fade: 160ms; /* Alert, RateLimitNotice, pré-visualização (150-160ms) */
--duration-menu-in: 150ms;
--duration-menu-out: 100ms;
--duration-dialog-in: 200ms;
--duration-dialog-out: 150ms;
--duration-spin: 800ms; /* uma volta do spinner (linear) */
```

Regras:

- Anime só `transform` e `opacity` (a única adição é a transição de **cor** em hover, nomeada). Nunca `width`, `height`, `top`, `left`, `margin`, `padding`; nunca `transition: all`; `will-change` só depois de medir um problema. Nunca animar `backdrop-filter`.
- Easing: entrar/sair e pressionar = `--ease-out`; cor/hover = `ease`; movimento contínuo (spinner, aro arcano) = `linear`; **nunca ease-in** em UI; sem bounce nem elástico.
- Saída mais rápida que a entrada; nunca partir de `scale(0)` (menu e diálogo partem de `scale(0.97)` com `opacity: 0`); menu cresce do gatilho (`transform-origin` no gatilho); diálogo fica centrado.
- Todo pressionável: `:active { transform: scale(0.97); transition: transform var(--duration-press) var(--ease-out); }`. Hover só dentro de `@media (hover: hover) and (pointer: fine)`.
- Entrada por seção (landing): as partes chegam em ordem de leitura (título, texto, figura), escalonadas de 80ms, subindo até 24px com `opacity`, uma vez; o conteúdo nunca depende da animação para aparecer (sem script ou com `prefers-reduced-motion` tudo já está visível).
- Não anima: foco, erros de campo, troca de rota, skeleton, estados de checkbox/radio, expansão de texto (nunca animar altura), troca de avatar após salvar.
- Interrompível: diálogo e menu usam CSS transitions (retargetam a partir do valor atual); `@keyframes` só no spinner, na manchete e no aro.
- Reduced motion (`@media (prefers-reduced-motion: reduce)`): "mais suave, não nenhum": remove deslocamento, escala e rotação (o aro para; a manchete já aparece); mantém `opacity` e cor que ajudam a entender. Spinner vira arco estático mais texto. Escrito junto da animação, nunca depois.

### 1.8 Troca de tema

O tema **segue o sistema** (`prefers-color-scheme`); o jogador pode trocar, e a escolha **persiste**. Infraestrutura em `frontend/src/app/theme/` (o botão de troca é da T-L01C):

- Sem escolha, nenhum atributo no `<html>`: o CSS decide pelo sistema, inclusive se o sistema mudar com a página aberta.
- Escolha explícita (`light` ou `dark`): `data-theme` no `<html>` e a chave `deckloom.theme` no `localStorage`. Voltar para "Sistema" apaga a chave e o atributo. Todo acesso ao `localStorage` está em `try/catch` (modo privado e cookies bloqueados não quebram o app: a escolha só vale até recarregar).
- `applyStoredTheme()` roda em `main.tsx` **antes** de montar o React, então o primeiro quadro já sai no tema salvo. Não há script inline em `index.html`: a CSP do nginx é `script-src 'self'`, que o bloquearia.
- `index.html` declara `color-scheme` (`light dark`) e duas `theme-color` por `prefers-color-scheme`; cada bloco de tema define `color-scheme` para os controles nativos e as barras de rolagem.
- `useTheme()` devolve `preference` (`system`, `light` ou `dark`), `theme` (o que está sendo visto), `setPreference` e `toggle`.

### 1.9 Selo "Em breve"

O selo marca o que ainda não existe (Decks, Coleção, Meta, Lojas, a IA e os alertas; ver a feature `landing`). Os cinco jogos entram juntos: nenhum jogo leva selo diferente dos outros. É metal: fundo `--color-gold-subtle`, texto `--color-on-gold-subtle`, um losango de 8px em `--color-gold` à esquerda, raio `--radius-sm`, tamanho `--step-small` (na landing) ou `--text-sm` (dentro do app), peso 500, texto "Em breve" em sentence case. **Não é interativo** (não parece botão, não recebe foco, nada que ele marca é link para rota inexistente); o estado não depende só de cor (o texto está sempre presente). Discreto: **um selo por seção** (na landing, no cabeçalho do fragmento da seção de recurso; nunca no hero nem no exemplo principal), ao lado do título.

### 1.10 Ícones

Os ícones vêm do **lucide-animated** (MIT), copiados para `frontend/src/design-system/icons` com o aviso de licença (T-L01B), sem requisição a terceiros. Regras: cor herdada do texto (`currentColor`); ícone decorativo `aria-hidden`; ícone sozinho tem nome acessível; animação só em `transform`/`opacity`, desligada com `prefers-reduced-motion`; nunca ícone dentro de quadrado colorido nem acima de card de funcionalidade; nenhum ícone de outra fonte. Nenhum ícone representa símbolo de jogo.

### 1.11 Ornamentos e texturas (desenhados em código)

Toda a fantasia é desenhada para o Deckloom em SVG, canvas ou WebGL; nenhum ornamento usa arte dos jogos (os logos e os símbolos de mana de 1.13 são arquivos à parte), e nenhum ornamento carrega informação (sempre `aria-hidden`).

- **Molduras com cantos ornamentados**: quatro cantos dourados (`--color-gold`, filete duplo, floreio e losango) nos painéis de vidro principais (grimório, listas, tela do app).
- **Filete com florão**: sob os títulos de seção, com um brilho dourado que percorre o filete devagar (anime.js; parado com redução de movimento).
- **Runas originais**: oito runas de traços retos (caixa 10x14) criadas para o Deckloom, usadas em anéis que giram devagar (sigilo do rodapé, anel sob o grimório 3D) e nas páginas do grimório.
- **Selo de cera**: violeta com aro e o sigilo do Deckloom (um fio atravessando um D) em `--color-gold-subtle`; marca alertas e o rodapé.
- **Página do grimório com cara de livro**: capa de couro violeta com borda dourada, pilha de páginas, vinco central, fita de marcador, cabeçalho corrido em versaletes e capitular na anotação.
- **Verso de carta do Deckloom** (63:88): campo `--aura-violet`, moldura dourada dupla, sigilo e anel de runas. Nunca uma carta de jogo.
- **Texturas**: pergaminho no tema claro (manchas e fibras abaixo de 7% de opacidade) e noite estrelada sobre couro no tema escuro (grão e estrelas que cintilam devagar), desenhadas em canvas atrás de todo o conteúdo.

### 1.12 Palco WebGL único da landing (three)

A landing é um **palco WebGL único** (REQ-L10 v6, REQ-L12 v5), do topo da página até o mural de avisos dos planos, guiado pelo scroll, **sem cortes nem fades genéricos**. Uma seção alta (`.stage`, 3080 svh) leva um quadro preso (`position: sticky`) com um só canvas; sobre ele fica o texto em HTML real. Uma única função de câmera (`camPose(raw, t)`) cobre o trajeto inteiro, e nenhum objeto aparece ou some dentro do quadro. O motor é o **three 0.186.1** (versão exata, `@types/three` só em tipos), carregado **sob demanda** por import dinâmico (`stage/Stage.tsx` importa `./engine/createStage`), fora do bundle inicial; a animação dos planos usa **animejs 4.5.0** (1.15). Nenhuma outra biblioteca 3D entra: não há gradiente de fundo, R3F nem biblioteca de câmera, e nada é baixado de terceiros.

| Pacote       | Versão (exata) | Papel                                                                     |
| ------------ | -------------- | ------------------------------------------------------------------------- |
| three        | 0.186.1        | o palco inteiro: floresta, deck box, deck, tokens e profundidade de campo |
| @types/three | 0.186.0        | só tipos (devDependency), sem código no bundle                            |
| animejs      | 4.5.0          | a entrada animada da seção de planos (e o resto do movimento, 1.7)        |

**Arquitetura: funções puras e cena.** Tudo o que decide o que se vê é **função pura do progresso do scroll** (`raw`, de 0 a `END` = 1,1), sem three nem DOM, e testada: `stage/phases.ts` (fases da floresta e da caixa, `END`, `PAN0`, `PAN1`, `mesaK`), `stage/camera.ts` (`camPose`), `stage/deck.ts` (cartas), `stage/explosion.ts`, `stage/tokens.ts` (simulação determinística), `stage/fire.ts` (velas, tochas, tremulação, brasas), `stage/board.ts` (mural de avisos, enquadramento e pan final, layout do texto dos avisos), `stage/silhouettes.ts` (geradores de silhueta e samambaias), `stage/mist.ts` (faixas de névoa), `stage/clearing.ts` (imagem estática) e `stage/overlays.ts` (legendas e botões em HTML). A cena (`stage/engine/*`, o único chunk que importa o three) só lê esses números e os aplica aos objetos a cada quadro. Constantes da linha do tempo (fração do scroll): floresta 0 a 0,20; runas da caixa despertam até 0,25; tampa abre até 0,29; cartas sobem até 0,33; introdução até 0,38; quatro ganchos de 0,38 a 0,60; reagrupar até 0,62; mesa até 0,92; epílogo até 1; **pan final** de 1,005 a 1,075 até o mural de avisos, que fica parado até 1,1 (o scroll é multiplicado por `END`).

**Cores.** Toda cor do palco vem de uma **paleta única em código** (`stage/palette.ts`: `FOREST`, `SKY`, `WORLD`, `LIGHTS`, `MIST`, `FIRE`, `BOARD`, `WOOD`, `CARD`, `MANA`, `STATIC_GROUND` e o fator `NIGHT`). O renderer fica **sem tonemapping** (`NoToneMapping`): as silhuetas aparecem exatamente nas cores da paleta, pois o ACES as lavava. O texto em HTML usa só os tokens do sistema (1.1).

**A floresta élfica.** Camadas de silhueta (`ShapeGeometry`, `MeshBasicMaterial` sem fog) em planos a várias profundidades, com **perspectiva atmosférica** (técnica do parallax do Firewatch, arte própria): da mais distante, clara e azulada, à mais próxima, quase preta. De trás para a frente: montanhas com agulhas élficas, a cidade antiga com pontes e domos (janelas douradas), árvores gigantes, um anel de árvores, o **muro de ruínas** com arcos e musgo atrás da clareira, o portal de troncos, o **portal de pedra** com arco ogival, as ruínas e o corredor do caminho e o dossel que a câmera sobrevoa. Os geradores (`pine`, `giant`, `tower`, `bridge`, `dome`, `band`) usam sementes fixas: a floresta é igual a cada carga. O céu é um degradê em shader com o sol baixo; há névoa de distância (o `Fog` do three, na cor da noite) e um **véu de névoa** em faixas, raios de luz, poeira, folhas ao vento, samambaias que balançam e vaga-lumes, tudo em movimento mesmo com o scroll parado. Sequência: sobrevoo das copas revelando a cidade, descida entre as árvores, travessia do portal de pedra e caminho de pedra até a clareira.

**A noite (artefato v5 aprovado).** A clareira é noturna: céu (`#090618` no alto, `#261b4f` no meio, horizonte `#8a5d3e`), névoa (`#1a1634`) e silhuetas e samambaias multiplicadas por `NIGHT` = 0,6; as janelas acesas (cores claras, `#f...`) continuam claras. As luzes ambiente e do sol baixam (hemisférica 0,3, direcional 0,95, contraluz 18), os raios de luz e a poeira ficam mais fracos. As **faixas de névoa** voltaram por decisão do usuário (2026-11-13: "religue as névoas, mas quero manter a floresta escura. Quero de noite basicamente"). Elas formam um **véu de névoa**: bruma noturna entre as camadas de silhueta, que dá profundidade como no parallax do Firewatch. A cor é `MIST.color` (`#30344f`): azul frio e dessaturado, um pouco mais claro que as silhuetas da noite e mais escuro que o horizonte. A mistura é **normal** (não aditiva) e a opacidade é a sorteada × `MIST.opacity` (1,3; teto 0,34, miolo do sprite 0,9). As faixas derivam devagar (`mistDrift`, até 6 unidades) e somem quando a câmera chega a menos de 4 a 14 unidades delas (`mistNearFade`: dentro da bruma não se vê o véu). Ficam perceptíveis sobretudo diante das silhuetas escuras e na base das árvores, e de leve na clareira. Testes puros (`stage/mist.ts`) garantem a faixa de cor, a opacidade moderada e uma diferença de pelo menos 6 níveis no miolo de uma faixa sobre as silhuetas escuras. Medido nas capturas (abertura, clareira e mesa, a 1366 e 375 px, mesmo quadro com e sem névoa): o quadro inteiro varia de −1,6% a +4,4%, e a região da faixa sobre silhueta fica entre 6,3 e 8,7 níveis na abertura e na clareira e entre 3,9 e 5,3 na mesa (de leve). Nenhuma faixa atravessa o mural de avisos. A luz frontal sobe de 0,4 para 0,9 **só enquanto as cartas dos ganchos viram** (para a face continuar legível). O fogo vira a luz principal.

**Fogo: velas e tochas.** Uma **chama procedural** (`engine/flame.ts`: plano com shader aditivo que ondula com o tempo e se inclina com o vento, pé azul nas velas, sem textura) que encara a câmera **girando só no eixo Y**; altura, brilho e inclinação tremulam (`fire.flicker`), e o halo é um sprite aditivo. **Velas** de cera com escorridos e poça, em **pequenos grupos** logo depois do anel de runas do altar, **no máximo oito**, sem cobrir as inscrições e **longe da trajetória dos tokens** (`placeCandles` confere a trilha simulada); elas crescem enquanto as runas da caixa despertam. **Tochas em suportes de ferro** (tripé, haste e cesto) nos quatro cantos da clareira, **tochas de parede** no portal de pedra e nos postes do mural, e quatro chamas distantes no muro de ruínas (profundidade). Cada grupo tem **luz pontual que tremula** e balança alguns centímetros, com **brasas** subindo das tochas. Na explosão do deck as chamas abaixam e se inclinam para fora da caixa. **Sombras** só nas duas tochas de trás e **só fora do celular** (`fireShadows`).

**As runas.** Uma escrita só (`stage/runes.ts`, 40 runas por semente) nas laterais da deck box, no arco do portal, no caminho e no altar. As **nove runas do portal acendem uma a uma** conforme a câmera passa (`archRuneGlow`) e o **rastro de 18 runas do caminho** acende à frente da câmera (`pathRuneGlow`); depois da floresta ficam todas acesas.

**A deck box.** Caixa de madeira entalhada num pedestal de pedra com raízes (canvas procedural, sem imagem de fora), com cantoneiras e fecho de metal. As runas despertam (`awake`), os vaga-lumes convergem, o **fecho e as travas soltam antes** da tampa, a **tampa abre pela dobradiça** (até cerca de 112 graus) com luz dourada e verde (luz pontual, brilho em plano aditivo e partículas de magia), e as cartas sobem da caixa.

**O altar.** A laje de pedra (a mesa) com um círculo entalhado e runas. **Desperta com a caixa** (emissão das runas) e um **anel de luz** corre pelo altar quando a tampa abre e outro quando o deck explode; a floresta reage (folhas empurradas, véu de névoa afinando pela metade no pico, samambaias balançando, chamas abaixando e se inclinando).

**O deck e os quatro ganchos.** O baralho tem 44 cartas; só as quatro dos ganchos têm face (as demais, o verso do Deckloom, nenhuma carta repete a seção dos jogos). As cartas sobem da caixa escalonadas, formam o deck em destaque (câmera orbitando, flutuação ambiente) e, **uma por vez**, cada carta sai do deck, aproxima, **vira 180 graus**, revela o gancho (brilho que atravessa a face) e vai para a pilha. A legenda em HTML aparece quando a carta é revelada (1.14).

**A mesa e o epílogo.** A câmera mergulha, o deck **explode** (cada carta com trajetória, rotação e velocidade próprias; as que vão ao fundo saem por cima do quadro) e caem no altar **cinco medalhões** (logos oficiais dos cinco jogos, de arquivo local) e **seis fichas de mana** (fonte Mana local). A física é **simulada uma vez, com passo fixo e semente fixa** (determinística): entradas em instantes e origens distintos (os de trás por cima do muro de ruínas), pouso com quicar, girar, deslizar e tombar, colisões entre si e com a deck box, repouso sem interpenetração. O medalhão do Pokémon pousa à frente da deck box, à direita (nunca escondido atrás dela). A câmera da mesa **recua** pela linha de visada depois que os tokens começam a cair (`mesaK`: 1,45 no desktop, 3,3 no celular, para todos caberem no quadro em pé); no celular os tokens de trás nascem colados atrás do muro de ruínas e os da frente sobem atrás da câmera, para continuarem entrando de fora do quadro. No **epílogo** a câmera volta pelo caminho e emoldura a clareira pelo arco do portal.

**O mural de avisos (planos).** Depois do portal (raw 1) a câmera faz o **pan final**: desliza para a esquerda numa curva contínua (Bézier quadrática, `board.panCam`), girando o olhar junto, até o **mural de avisos** ao lado do portal: quadro de tábuas com vigas entalhadas, postes, telhado de telhas com musgo, a placa "MURAL DE AVISOS" com o símbolo do Deckloom e duas tochas nos postes. Os dois planos (Grátis e Arcano R$ 19,90/mês) estão **desenhados em textura de canvas** como avisos de pergaminho presos com tachas (o Arcano com o selo do Deckloom); o texto vem de `board.noticeLayout` e é **medido para caber**. O enquadramento final (`boardView`) mostra o quadro inteiro, do pé ao telhado, livre da ilha de navegação e dos botões. No celular o mural é mais estreito e alto, os avisos ficam empilhados e cada um põe título e preço na mesma linha. Os botões ficam em HTML (`.stage__board-cta`: "Criar conta grátis" e "Quero o Arcano", ambos para `/cadastro`, com a nota de que a assinatura ainda não é cobrada) e entram no fim do pan; antes disso ficam `visibility: hidden` e `inert`. A lista dos planos fica em HTML só para leitor de tela (`#planos`, no fim do palco), que é também o alvo do link "Planos" da navegação e do rodapé. Nenhuma samambaia nem faixa de névoa atravessa o corredor do pan; o título e a legenda dos jogos saem quando a câmera deixa o portal.

**Profundidade de campo.** Em pós-processamento: a cena vai para um alvo com textura de profundidade e um passe em tela cheia desfoca por distância ao plano de foco (foco no alvo da câmera, na caixa ou na carta ativa; abertura por fase), sem tonemapping.

- **Desempenho**: `devicePixelRatio` limitado a 1,5; menos partículas e brasas no celular (largura abaixo de 760px, ou tablet em pé) e fov maior em janela em pé; sombras das tochas só fora do celular; o laço só desenha quando a seção está na janela e para ao desmontar. O canvas é `aria-hidden`. Geometrias, materiais, texturas, o alvo de profundidade e o renderer são **liberados ao desmontar** (`disposeObject3D`, `dof.dispose()`, `renderer.dispose()`), e um teste confere isso.
- **Texto AA**: o texto fica em painéis de vidro forte (`--glass-bg-strong`, conferido no pior fundo) ou em papel opaco (`--color-surface`, dentro dos cards) e nunca depende do palco para ser lido. A página rola normalmente; a câmera acompanha o scroll com amortecimento (12% por quadro).
- **Camada HTML por progresso** (`stage/overlays.ts`): a dica de rolar, a frase de abertura do baralho, as quatro legendas, as barras de progresso, os jogos e os botões do mural recebem opacidade e deslocamento como função pura do progresso. A legenda invisível fica `inert` (fora da ordem de foco). No celular a legenda mostra o texto da carta e esconde o exemplo de interface (que continua no fallback).
- **Auditoria de continuidade**: fora de produção o palco expõe `window.__deckloom.audit(passos)`, que amostra a câmera e confere que nenhum passo desloca mais de 0,6 nem gira mais de 4 graus e que nenhuma carta ou token aparece ou some dentro do quadro sem estar oculto pelo cenário. O E2E roda 4000 passos, de 0 a `END` (inclui o pan até o mural), a 1366x768 e 375x812.

#### Fallback estático

Sem WebGL, com **`prefers-reduced-motion`** ou se o palco falhar ao montar: **a imagem estática da clareira** (SVG desenhado em código com as mesmas silhuetas e a mesma paleta, vista da última pose da câmera do sobrevoo), o título principal visível num painel de vidro, **os ganchos em lista** (os mesmos cards em HTML, com exemplos de interface), **os jogos em grade** (logos oficiais) e **os planos numa section** (1.15). A decisão é uma função pura testada (`stage/capabilities.ts`, no bundle inicial, sem three); o three nunca é baixado nesses casos. Mudar `prefers-reduced-motion` com a página aberta monta ou desmonta o palco. Enquanto o chunk do palco carrega, a mesma imagem estática serve de cartaz dentro do quadro. No palco animado o título principal existe em HTML mas fica oculto visualmente (não há painel de título sobre a abertura), e o hero não tem cartão de criar conta.

### 1.13 Marca, navegação, rodapé, logos dos jogos e símbolos de mana

Decisões do usuário de 2026-11-07 (REQ-L03 v5), na ordem em que aparecem na landing e no app. O que o usuário ainda não decidiu está marcado como pendente: não há valor inventado aqui.

**Logo do Deckloom.** Símbolo (uma carta violeta com borda dourada, um D dourado e um fio que a atravessa) mais o nome. Arquivos em `frontend/public/brand/deckloom/`: `deckloom-logo.svg` (símbolo + nome, para fundo claro), `deckloom-logo-on-dark.svg` (para fundo escuro) e `deckloom-symbol.svg` (só o símbolo, 48x48). O favicon é `frontend/public/favicon.svg` (símbolo recortado, 32x32). A logo aparece na navegação, no rodapé e no favicon, com nome acessível "Deckloom" e sem texto duplicado ao lado.

- **Área de respiro**: pendente de decisão do usuário (a medida ainda não foi definida).
- **Tamanho mínimo**: pendente de decisão do usuário (símbolo e logo completa ainda sem medida mínima definida).
- **Fundo**: a variante certa para o tema (`deckloom-logo.svg` no claro, `deckloom-logo-on-dark.svg` no escuro); nunca recolorir, esticar ou aplicar efeito.

**Navegação "dynamic island".** Pílula flutuante centralizada no topo, em vidro (1.6), com a logo, os links e as ações. No celular colapsa em um botão de menu (`aria-expanded`, fecha com Esc e ao navegar). Criar conta (`/cadastro`) é o botão primário, com ícone (1.10); Entrar (`/entrar`) é secundário; o controle de tema é um só (1.8). O hero não tem cartão de criar conta, e a landing não tem faixa final de chamada nem bloco de pré-visualização do app.

**Rodapé.** Logo do Deckloom, links de navegação e a nota de marcas (o controle de tema é um só, o da navegação: o rodapé não tem outro) (exigida pelo REQ-L03). Texto da nota de marcas, **provisório e sujeito à revisão jurídica**: "Magic: The Gathering, Pokémon TCG, Yu-Gi-Oh!, One Piece Card Game e Disney Lorcana são marcas de seus titulares. O Deckloom não é afiliado nem endossado por eles."

**Logos oficiais dos jogos.** Os cinco jogos entram juntos, com os logos oficiais em `frontend/public/brand/games/` (origem, titular e licença declarada de cada arquivo em `SOURCES.md`). Sem CDN e sem URL externa; cada logo tem texto alternativo com o nome do jogo; os de contorno escuro ficam sobre uma placa clara nos dois temas, sem alterar as cores oficiais. A decisão é do usuário e o uso comercial é o risco **RISK-FEAT-LANDING-6**: a **revisão jurídica é obrigatória antes de publicar em produção**; se ela não autorizar, troca-se por wordmarks próprios no lugar dos arquivos.

**Símbolos de mana.** Os exemplos de cartas de Magic mostram os símbolos de mana com a fonte **mana-font 1.18.0** (versão exata em `frontend/package.json`), copiada em `frontend/public/brand/mana/` (`mana.woff2`, recorte `mana.css`, `OFL.txt`, `LICENSE-MIT.txt`, `SOURCES.md`). Fonte e CSS de Andrew Gioia (OFL 1.1 e MIT); os desenhos dos símbolos são de Wizards of the Coast. Os símbolos são decorativos (`aria-hidden`) e o custo tem um nome acessível em texto ("Custo de mana: 2 genérico, azul, preto"). Mesmo risco e mesma revisão jurídica dos logos.

### 1.14 Cards de gancho

Os quatro ganchos da landing (REQ-L01 v6, nesta ordem: quanto do deck você já tem, o meta contra a sua coleção, compre só o que falta, IA que explica o porquê de cada carta e alerta de queda de preço) são **cards com moldura própria do Deckloom**, inspirada na anatomia de uma carta de Magic, **sem copiar a moldura da Wizards of the Coast** (REQ-L11). Cada card é um `<article data-hook>` nomeado pelo título. No palco ele é a legenda da carta revelada (1.12); no fallback é uma seção da lista. O conteúdo é HTML real, acessível e legível sem o palco.

| Parte (`data-part`) | O que é                                                                                         | Material                                                  |
| ------------------- | ----------------------------------------------------------------------------------------------- | --------------------------------------------------------- |
| moldura             | borda de metal (`--color-gold`) e vidro forte por trás das faixas                               | metal e `--glass-bg-strong`, com sólido de reserva        |
| `title`             | faixa de título: o `<h2>` do gancho e, onde a mana ficaria, o selo "Em breve"                   | `--color-primary-subtle` e `--color-on-primary-subtle`    |
| `art`               | janela de arte: o símbolo do Deckloom em um anel de runas, um acento por gancho                 | campo `--color-primary`, filete de metal, tudo decorativo |
| `type`              | linha de tipo: "Recurso" e o recurso do gancho (Coleção e Decks, Meta, Lojas, Sugestões com IA) | `--color-gold-subtle` e `--color-on-gold-subtle`          |
| `text`              | caixa de texto: a frase do gancho e o exemplo com dados do mock                                 | papel opaco `--color-surface`, texto em `--color-text`    |

Regras:

- **Proporção**: cartão de 26rem de largura máxima; a altura cresce com o conteúdo (sem corte nem rolagem interna no fallback).
- **Selo**: todo recurso dos cards ainda não existe, então cada card leva "Em breve" na faixa de título (um por card). Nada nos cards é link; os únicos links da página são Criar conta (`/cadastro`), Entrar (`/entrar`), o início (`/`), os botões dos planos (`/cadastro`) e âncoras da própria página.
- **Exemplos coerentes**: os números vêm de um único mock (`mock/deck-mock.ts`), todos inventados e marcados como exemplo no card. O deck de exemplo tem 60 cartas, 47 já na coleção e 13 faltando; cada "Tenho +1" soma uma cópia, tira uma do que falta e atualiza, na mesma página, a cotação das lojas do gancho 3. Os símbolos de mana do custo vêm da fonte Mana local (1.13) e são renderizados com JSX, nunca com HTML interpolado. O teste confere que tem + faltam = total antes e depois de cada "Tenho +1".
- **Faces das cartas 3D**: a face das quatro cartas do palco é desenhada em canvas com a mesma anatomia (faixa de título, janela de arte com o símbolo, linha de tipo, caixa de texto) e os mesmos textos dos cards em HTML (`HOOKS`); a legenda em HTML é a fonte acessível.
- **Entrada**: no palco, as quatro partes de cada card entram escalonadas pelo progresso do scroll (`overlays.ts`). No fallback entram com `revealOnEnter` (anime.js, 1.7) quando a seção entra na janela, uma vez; só o script as esconde, então sem script, sem `IntersectionObserver` ou com `prefers-reduced-motion` o conteúdo já está visível e parado.
- **Sem sombra e sem gradiente em CSS**: a moldura é borda; a separação é de tom e de filete, como no resto do sistema.

### 1.15 Planos

Depois dos cinco jogos, a landing mostra os **planos** do jogador (REQ-L13 v3): **Grátis** (salva até 5 decks, R$ 0) e **Arcano**, o pago, a **R$ 19,90 por mês** (sugestões com IA, decks ilimitados, alertas de preço e sem anúncios). **No palco animado** eles ficam no **mural de avisos** ao fim do pan final (1.12): avisos desenhados no canvas, botões em HTML ("Criar conta grátis" e "Quero o Arcano", ambos para `/cadastro`) e a lista em HTML para leitor de tela; a section abaixo não é renderizada. **No fallback** fica a section descrita aqui. Cada plano é um `<article>` nomeado pelo título, dentro da seção `#planos` (nomeada por "Comece grátis"). **Todo botão leva a `/cadastro`** ("Começar grátis" e "Quero o Arcano", com ícone; nenhum repete o texto "Criar conta" da navegação): ainda não há pagamento do jogador, então a landing não cobra nem coleta dado de pagamento (a nota da seção diz isso) e o plano pago leva o selo "Em breve". O nome "Arcano" é provisório, a confirmar com o usuário.

- **Material**: cartões em `--color-surface` com borda `--color-border-decor` sobre o fundo da página; o plano pago tem um filete dourado desenhado pela animação (`stroke-dashoffset`) e o botão primário.
- **Movimento (animejs)**: a linha do tempo anda com o scroll (`onScroll`, `sync` 0,35): o cabeçalho sobe, os cartões entram em perspectiva (`rotateX`, `z`), o filete se desenha, o preço conta até R$ 19,90 e a lista aparece. Com mouse o cartão inclina em 3D seguindo o ponteiro. Com `prefers-reduced-motion` nada é escondido nem movido e o preço já aparece inteiro.
- **Contraste**: texto sobre o fundo da página e sobre `--color-surface` nos dois temas, conferido em `landing.contrast.test.ts` e no axe.

## 2. Inventário de componentes (v0, necessários à feature `conta-jogador`)

Cada componente nasce acessível; critérios testáveis ficam em `design.md` > UX da feature (ver `docs/specs/conta-jogador/ux-draft.md` e A.8).

| Componente                                                                                                                                                                                                         | Estados / variantes                                                                                                                                                                                                                                                                                                                                    | Notas de acessibilidade                                                                                                                                                                                                                                                                                                                                                              |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Button**                                                                                                                                                                                                         | primário (preenchido violeta; no máximo um por tela), secundário (contorno neutro, texto de tinta), perigo (preenchido `--color-danger`; na zona de perigo do Perfil, contorno vermelho), link; default, hover, focus, active, disabled, loading (`aria-busy="true"`, rótulo mantido, não troca de largura) (v0.1: variantes e movimento MOT-01/02/07) | elemento `<button>`; texto visível = nome acessível; loading não remove foco; rótulo longo quebra em duas linhas sem cortar, altura >= 44px                                                                                                                                                                                                                                          |
| **TextField** (texto, email)                                                                                                                                                                                       | default, focus, preenchido, erro, desabilitado                                                                                                                                                                                                                                                                                                         | `<label>` visível sempre associado; dica e erro ligados por `aria-describedby`; `aria-invalid` em erro; `autocomplete` correto; valor longo rola por dentro do campo                                                                                                                                                                                                                 |
| **PasswordField**                                                                                                                                                                                                  | = TextField + botão "Mostrar senha"                                                                                                                                                                                                                                                                                                                    | botão com `aria-pressed`; colar permitido; `autocomplete="current-password"` ou `"new-password"`                                                                                                                                                                                                                                                                                     |
| **Textarea com contador**                                                                                                                                                                                          | default, erro, acima do limite                                                                                                                                                                                                                                                                                                                         | contador ligado por `aria-describedby`, sem leitura a cada tecla; contador com `tabular-nums` e `Intl.NumberFormat` (v0.1)                                                                                                                                                                                                                                                           |
| **Select**                                                                                                                                                                                                         | default, erro                                                                                                                                                                                                                                                                                                                                          | `<select>` nativo (permite adicionar valores sem mudar o componente)                                                                                                                                                                                                                                                                                                                 |
| **Checkbox**                                                                                                                                                                                                       | marcado/desmarcado, erro                                                                                                                                                                                                                                                                                                                               | `<input type="checkbox">` nativo + label clicável; links dentro do label não alteram o estado; rótulo longo quebra com a caixa alinhada à primeira linha                                                                                                                                                                                                                             |
| **CheckboxGroup**                                                                                                                                                                                                  | default, vazio (sem itens), erro                                                                                                                                                                                                                                                                                                                       | `<fieldset>` + `<legend>`                                                                                                                                                                                                                                                                                                                                                            |
| **RadioGroup**                                                                                                                                                                                                     | default, selecionado, erro                                                                                                                                                                                                                                                                                                                             | `<fieldset>` + `<legend>` + radios nativos (setas navegam)                                                                                                                                                                                                                                                                                                                           |
| **FormError** (erro de campo)                                                                                                                                                                                      | visível/oculto                                                                                                                                                                                                                                                                                                                                         | ícone + texto `--color-danger`; id referenciado pelo campo; sem animação (MOT-04)                                                                                                                                                                                                                                                                                                    |
| **ErrorSummary**                                                                                                                                                                                                   | oculto/visível                                                                                                                                                                                                                                                                                                                                         | `tabindex="-1"`, recebe foco ao enviar com erros; lista de links para os campos; sem animação                                                                                                                                                                                                                                                                                        |
| **Alert**                                                                                                                                                                                                          | info, sucesso, aviso, erro                                                                                                                                                                                                                                                                                                                             | sucesso/info: `role="status"`; erro de página (não de campo): `role="alert"`; ícone + texto + título opcional; entra por `opacity` 160ms (MOT-05)                                                                                                                                                                                                                                    |
| **RateLimitNotice** (aviso de atraso)                                                                                                                                                                              | oculto/visível                                                                                                                                                                                                                                                                                                                                         | região `role="status"` (polite) sempre presente no DOM; texto inserido quando a resposta passa de 2 s; entra por `opacity` (MOT-06); o espaço é reservado no momento do envio                                                                                                                                                                                                        |
| **Dialog de confirmação**                                                                                                                                                                                          | confirmar/cancelar, com ou sem campo (senha), enviando, erro                                                                                                                                                                                                                                                                                           | `role="dialog"` ou `"alertdialog"`, `aria-modal="true"`, `aria-labelledby`, foco preso, Esc cancela, foco retorna ao gatilho, fundo inerte; painel `scale(0.97)` a 1 mais `opacity` 200/150ms, scrim por `opacity` (MOT-12/13); rola por inteiro se mais alto que a janela (v0.1)                                                                                                    |
| **Galeria de avatares** (era "AvatarPicker"; componente removido em 2026-11-05, T-183: nunca foi usado, a tela S10 monta a galeria no `AvatarDialog`, e os estilos `avatar-picker__*` vivem em `AvatarDialog.css`) | galeria de presets + envio de arquivo; selecionado, enviando, erro, carregando (círculos cinza), vazio/falha (mensagem, upload continua)                                                                                                                                                                                                               | presets = RadioGroup com nome acessível por avatar; grade de 4 colunas (3 a 320px), alvos de 44px; ver envio de arquivo                                                                                                                                                                                                                                                              |
| **Envio de arquivo** (era "FileUpload"; componente removido em 2026-11-05, T-183; sem pré-visualização no S10, estilos `file-upload__*` em `AvatarDialog.css`)                                                     | vazio, arquivo escolhido (pré-visualização), enviando, erro                                                                                                                                                                                                                                                                                            | `<input type="file" accept="image/jpeg,image/png,image/webp">` nativo com label; sem arrastar-e-soltar no v0; validação do cliente é só de conveniência (servidor é a autoridade); nome de arquivo e pré-visualização `object-fit: cover` no quadro de 96px (v0.1)                                                                                                                   |
| **Avatar**                                                                                                                                                                                                         | tamanhos 32, 40, 96 px; imagem; carregando; falha                                                                                                                                                                                                                                                                                                      | `alt` descritivo quando a imagem é informação; `alt=""` quando ao lado de texto equivalente. v0.1: `flex-shrink: 0`, `width`/`height` fixos; carregando = círculo `--color-skeleton`; falha = círculo neutro com a primeira letra do nome (primeiro elemento de texto, seguro para emoji), mesmo nome acessível                                                                      |
| **Skeleton / Spinner**                                                                                                                                                                                             | carregando                                                                                                                                                                                                                                                                                                                                             | contêiner com `aria-busy="true"`; spinner acompanhado de texto ("Carregando..."). v0.1: Skeleton = blocos planos `--color-skeleton` no tamanho real, **sem brilho passando** (MOT-09); spinner gira 800ms linear, estático em reduced motion (MOT-08)                                                                                                                                |
| **AppHeader + AccountMenu**                                                                                                                                                                                        | visitante (Entrar / Criar conta), Jogador (avatar + menu: Perfil, Sair)                                                                                                                                                                                                                                                                                | menu como botão de divulgação (`aria-expanded`), não `role="menu"`; fecha com Esc. v0.1: barra de vidro de 56px; wordmark em Cinzel Decorative 900, 20px, cor de tinta; nome de exibição no botão cortado no fim com reticências (some abaixo de 480px, fica só o avatar); nome completo no painel do menu e em `aria-label`; painel com `--shadow-menu`, cresce do gatilho (MOT-11) |
| **SkipLink**                                                                                                                                                                                                       | visível ao receber foco                                                                                                                                                                                                                                                                                                                                | primeiro elemento focável: "Ir para o conteúdo principal"                                                                                                                                                                                                                                                                                                                            |
| **Link**                                                                                                                                                                                                           | default, visited-neutro, focus                                                                                                                                                                                                                                                                                                                         | sublinhado sempre visível no corpo de texto; links externos indicam "(abre em nova aba)"                                                                                                                                                                                                                                                                                             |
| **Divider com texto** ("ou")                                                                                                                                                                                       | -                                                                                                                                                                                                                                                                                                                                                      | decorativo; linha única de 1px; não substitui estrutura                                                                                                                                                                                                                                                                                                                              |
| **Tag / Chip somente leitura** (v0.1: cor de dado)                                                                                                                                                                 | -                                                                                                                                                                                                                                                                                                                                                      | usado para listar Formatos; é texto, não botão; fundo `--color-primary-subtle`, texto `--color-on-primary-subtle`, raio `sm`, sem hover; nome longo quebra dentro da Tag                                                                                                                                                                                                             |
| **Panel** (v0.1)                                                                                                                                                                                                   | -                                                                                                                                                                                                                                                                                                                                                      | vidro (`--glass-bg`) sobre o fundo, com borda `--glass-edge`, raio `lg`, sem sombra; fallback `--glass-solid`; `prefers-contrast: more` acrescenta borda de controle                                                                                                                                                                                                                 |
| **BioText** (v0.1)                                                                                                                                                                                                 | recolhido, expandido, vazio                                                                                                                                                                                                                                                                                                                            | texto simples com quebras de linha preservadas (`white-space: pre-line`), `overflow-wrap: anywhere`, `dir="auto"`; limitado a 10 linhas com botão "Mostrar tudo" (`aria-expanded`, sem animar altura); vazio = "Nenhuma bio." em `--color-text-muted`                                                                                                                                |

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

## 4. Decisões de marca

Decisões da v0 (confirmadas em 2026-10-23, decisão U16 do design de `conta-jogador`) foram **revistas pelo usuário em 2026-11-06** para a v1.0:

| #   | Tema                     | Decisão                                                                                            | Substitui                     |
| --- | ------------------------ | -------------------------------------------------------------------------------------------------- | ----------------------------- |
| M1  | Cor primária             | **Violeta** (#5b2fc4 claro, #9a77ff escuro) com **dourado** como metal                             | o azul da v0.1                |
| M2  | Tipografia               | **Cinzel Decorative** (títulos) e **Albert Sans** (texto), OFL, empacotadas no projeto             | pilha de fontes do sistema    |
| M3  | Tema                     | **Claro e escuro**: segue o sistema, troca pelo usuário, persiste                                  | somente claro                 |
| M4  | Nome na tela             | **Logo própria** (símbolo + nome) na navegação, no rodapé e no favicon (1.13)                      | wordmark em sistema, 20px/700 |
| M5  | Arte dos avatares preset | Placeholders neutros (decidido; sem arte de terceiros)                                             | -                             |
| M6  | Moldura de carta         | Cartas desenhadas em código (63:88, moldura dourada), originais, sem arte nem nome dos jogos       | adiada                        |
| M7  | Material                 | **Vidro** (glassmorphism) bem presente em navegação, painéis, menus, diálogos, cards e formulários | sem vidro                     |
| M8  | Fantasia                 | Ilustrada, "Grimório arcano" (capa violeta, fita e selo dourados, aro arcano), sem arte dos jogos  | direção "ficha de coleção"    |

## 5. Exceções às regras da skill

O usuário pediu explicitamente três coisas que a skill `design-taste-frontend` proíbe por padrão. Cada uma é liberada **só** para o que está dito aqui; todo o resto das regras da skill vale (sem eyebrow, sem monoespaçada, sem numeração, sem linhas finas decorativas, sem sombra em repouso, sem ícone em quadrado, manchete em duas linhas no máximo, margens iguais). No `lint.mjs` a exceção vai no comentário `taste-skill-ignore` da linha; no `inspect.mjs`, no parâmetro `--allow glass,gradient,palette-purple`.

- **Exceção: glass (vidro).** A skill diz "sem vidro". O usuário decidiu vidro bem presente em todo o app (navegação, painéis, menus, diálogos, cards e formulários), e o produto é uma ferramenta em que o vidro sobre uma textura de fantasia é a identidade. Mitigação: contraste medido no pior fundo e no fallback (1.6.3), sólido de reserva, `prefers-reduced-transparency`, nunca animar o desfoque. Regra liberada: `glass`.
- **Exceção: violeta como cor de marca.** A skill proíbe violeta e índigo (paleta de páginas geradas). O usuário escolheu o violeta como cor de marca do Grimório arcano, com dourado como metal. Mitigação: um acento por tema, violeta só na ação principal e nos dados (1.1), neutros com viés violeta mas nunca cinza puro, dourado nunca texto. Regra liberada: `palette-purple`.
- **Exceção: textura de fantasia (gradiente e brilho).** A skill proíbe gradiente e brilho. A fantasia ilustrada pede um fundo com brilho violeta e dourado e estrelas por trás do vidro (1.6.4); sem isso o vidro não tem o que desfocar. Mitigação: gradiente só radial e só no fundo da página, nunca em botão, texto ou superfície de controle; tudo `aria-hidden`/decorativo, sem informação. Regra liberada: `gradient`.

Fora dessas três, a landing passa limpa nas regras. Os avisos que sobram do `inspect.mjs` são os de fotografia e vídeo (`no-real-pictures`, `no-footage`): a landing é feita de fragmentos de interface do produto e desenhos em código, sem fotografias nem arte de terceiros, por decisão do usuário.
