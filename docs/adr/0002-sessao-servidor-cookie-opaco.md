# ADR-0002: Sessão no servidor com token opaco em cookie HttpOnly, proteção CSRF por token antiforgery

- Status: Accepted (2026-10-23)
- Date: 2026-10-22
- Deciders: architect (proposta), aprovação do usuário no gate da fase 3 (feature `conta-jogador`)

## Context

Requisitos aprovados de `conta-jogador`: logout invalida a sessão no servidor e impede reutilização (REQ-010); redefinição de senha encerra todas as sessões (REQ-030); exclusão encerra todas as sessões (REQ-024); sessão curta de 30 min de inatividade (REQ-012) e "lembrar-me" de 30 dias invalidável a qualquer momento (REQ-035). O frontend é uma SPA React servida na mesma origem da API (reverse proxy). Proteção contra CSRF e XSS é exigida (NFR Security).

## Decision

1. **Sessão armazenada no servidor** (tabela `accounts.player_session`). O cliente recebe apenas um **token opaco de 256 bits** (gerador criptográfico), em cookie `__Host-dl_session` com `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`. O servidor guarda **somente o SHA-256 do token** (token de alta entropia dispensa hash lento).
2. Sem "lembrar-me": cookie de sessão do navegador (sem `Max-Age`) e validade no servidor até 30 min sem requisição; cada requisição autenticada atualiza `last_seen_at`. Com "lembrar-me": cookie com `Max-Age` de 30 dias e `expires_at` absoluto = criação + 30 dias no servidor.
3. Um novo token é emitido em todo login, cadastro, vínculo Google e conclusão de cadastro Google (sem fixação de sessão). Logout, redefinição de senha e exclusão revogam no servidor.
4. **CSRF**: `SameSite=Lax` + token antiforgery do ASP.NET Core (cookie `__Host-dl_csrf` + cabeçalho `X-CSRF-Token`, obtido em `GET /api/v1/csrf`), exigido em toda requisição que altera estado, mais verificação do cabeçalho `Origin` contra a origem pública configurada quando o cabeçalho está presente (ver Emenda 2). `Lax` (e não `Strict`) porque o retorno do Google é uma navegação de topo vinda de outro site e precisa levar os cookies de correlação OAuth e, na reautenticação, o cookie de sessão.
5. Nenhum token de sessão em `localStorage`/`sessionStorage`; o frontend nunca lê o token.
6. Não usar ASP.NET Core Identity: seu modelo de usuário conflita com o agregado `Player` do domínio; a autenticação da sessão é um `AuthenticationHandler` próprio que consulta `ISessionRepository`.

## Alternatives considered

- **JWT de acesso + refresh token**: stateless, mas revogação imediata (REQ-010, REQ-030) exige lista de revogação no servidor, anulando a vantagem e adicionando complexidade (rotação, armazenamento do refresh).
- **Cookie de autenticação do ASP.NET Core com ticket criptografado (sem tabela)**: não permite "encerrar todas as sessões do Jogador" sem um carimbo de segurança consultado a cada requisição, que acaba sendo uma consulta ao banco de qualquer forma.
- **SameSite=Strict sem token antiforgery**: quebraria o retorno do Google e depende só do navegador.

## Consequences

- Uma consulta e um `UPDATE` por requisição autenticada (aceitável sem meta de throughput, D14; otimizável com cache curto se necessário).
- Escala horizontal não exige afinidade de sessão (estado no banco), mas as chaves do ASP.NET Data Protection (antiforgery e cookie OAuth) precisam ser compartilhadas entre instâncias quando houver mais de uma.
- O frontend precisa buscar o token CSRF ao iniciar e após login/logout. (ver Emenda 1)
- Em desenvolvimento, `__Host-`/`Secure` funcionam em `http://localhost` nos navegadores atuais; HTTPS local é recomendado.

## Emenda 1 (2026-10-29)

- Status: Accepted (2026-10-29), decisão do usuário (opção "Manter").
- Escopo: altera apenas a consequência "o frontend precisa buscar o token CSRF ao iniciar e após login/logout". Os itens 1 a 6 da decisão continuam válidos.

### Decisão

O token antiforgery é **anônimo**: a validação usa o principal anônimo (`AnonymousAntiforgery`) e o token fica vinculado **somente ao cookie `__Host-dl_csrf`**, não ao Jogador autenticado. Consequências diretas:

- O mesmo token vale antes e depois do login; o logout autenticado usa o mesmo token.
- O frontend obtém o token em `GET /api/v1/csrf` uma vez, ao iniciar, e **não** precisa buscar outro após login ou logout.
- A proteção continua sendo: cookie `__Host-dl_csrf` (HttpOnly, Secure, SameSite=Strict, Path=/) + cabeçalho `X-CSRF-Token` + verificação do cabeçalho `Origin` contra `Auth:PublicOrigin`; CORS continua desabilitado.

### Justificativa

O antiforgery padrão do ASP.NET Core vincula o token à identidade do usuário; com isso, o token emitido para o visitante deixa de valer no instante em que o login cria a sessão, e o token do Jogador deixa de valer no logout, na expiração da sessão ou na revogação remota (redefinição de senha, exclusão, logout em outra aba). Isso gera `403 csrf_failed` espúrios em cenários comuns (várias abas, sessão expirada durante a edição, revogação feita em outro dispositivo) e obriga o frontend a ressincronizar o token a cada mudança de sessão. O vínculo à identidade não acrescenta proteção real contra CSRF: o atacante de outro site não lê o cookie (HttpOnly) nem o corpo de `GET /api/v1/csrf` (mesma origem, sem CORS), e não consegue enviar o cabeçalho `X-CSRF-Token` nem um `Origin` válido.

### Riscos residuais aceitos

- O token CSRF **não é rotacionado** no login nem no logout; vale enquanto o cookie `__Host-dl_csrf` existir (sessão do navegador).
- Para usar o token, um atacante precisa lê-lo, o que exige XSS na origem do Deckloom; com XSS, qualquer proteção CSRF já está anulada (o script age na mesma origem), então a falta de rotação não aumenta a superfície. A mitigação de XSS continua sendo CSP, texto puro e cabeçalhos de segurança.
- Em computador compartilhado, pessoas que usam o mesmo perfil de navegador compartilham o mesmo token CSRF. Isso não dá acesso à sessão de outra pessoa (o cookie de sessão é emitido de novo em todo login e revogado no logout) e não permite CSRF vindo de outro site.

### Alternativa rejeitada

- **Vincular o token ao usuário autenticado e exigir novo token após login/logout**: rotaciona o token a cada mudança de sessão, mas é mais frágil com várias abas, sessão expirada e revogação remota (falhas `403` que o frontend precisaria tratar com nova busca e reenvio), sem ganho real de segurança pelos motivos acima.

## Emenda 2 (2026-10-31)

- Status: Accepted (2026-10-31), decisão do usuário (achado R-35 da fase 6).
- Escopo: altera apenas a verificação do cabeçalho `Origin` do item 4 da decisão e da Emenda 1. A exigência do token antiforgery (cookie `__Host-dl_csrf` + cabeçalho `X-CSRF-Token`) continua exatamente como está.

### Decisão

Em toda requisição que altera estado (POST/PUT/DELETE):

- Com o cabeçalho `Origin` presente, ele precisa ser igual a `Auth:PublicOrigin`; diferente -> `403 csrf_failed`.
- Sem o cabeçalho `Origin`, a requisição é aceita só se o token antiforgery for válido; token ausente ou inválido -> `403 csrf_failed`.

### Justificativa e risco residual aceito

O token antiforgery já é a barreira principal e continua obrigatório; o atacante de outro site não consegue lê-lo nem enviar o cabeçalho `X-CSRF-Token`. A falta de `Origin` remove só uma camada de defesa em profundidade, sem abrir a mutação a CSRF. Aceitar a ausência evita recusar clientes ou intermediários que não enviam o cabeçalho. O comportamento sem `Origin` deve ficar fixado num teste.
