# ADR-0004: Login Google por OpenID Connect (authorization code + PKCE + state + nonce) com callback no backend e estado pendente no servidor

- Status: Accepted (2026-10-23)
- Date: 2026-10-22
- Deciders: architect (proposta), aprovação do usuário no gate da fase 3 (feature `conta-jogador`)

## Context

`conta-jogador` exige login social Google com `state`/PKCE (NFR Security); conta nova via Google só é criada após aceite dos termos e declaração de maioridade, numa tela posterior ao retorno do Google (decisão do usuário: tela S4, só para conta nova); vínculo com conta email+senha só após prova da senha (REQ-008, REQ-036), com estado pendente de 10 min e uso único (decisão do usuário); exclusão de conta sem senha local exige login Google nos últimos 5 min seguido de confirmação final (decisão do usuário). A foto do Google é ignorada (REQ-022). Falha do Google não pode afetar o login por email+senha.

## Decision

1. **Cliente confidencial no backend**: o navegador navega para `GET /api/v1/auth/google/start?intent=signin|reauth[&returnTo=/caminho]`; o backend gera `state`, `nonce` e `code_verifier` (PKCE S256), guarda-os num cookie de correlação `__Host-dl_oauth` (criptografado e assinado pelo ASP.NET Data Protection, `HttpOnly`, `SameSite=Lax`, `Max-Age` 10 min, apagado no retorno) e redireciona ao endpoint de autorização do Google com escopos `openid email profile`. Na reautenticação, adiciona `prompt=login` e `max_age=300`.
2. `redirect_uri` = `GET /api/v1/auth/google/callback` (mesma origem, via reverse proxy). O backend valida `state`, troca o código no endpoint de token com o `code_verifier`, valida o `id_token` (assinatura pelas chaves JWKS do Google com cache, `iss`, `aud`, `exp`, `nonce`, `email_verified`; na reautenticação, `auth_time` dentro dos últimos 5 min) e converte o resultado no value object `GoogleVerifiedIdentity` (subject, email, emailVerified, nome). Nenhum token do Google é guardado.
3. Implementação própria e pequena atrás da porta `IGoogleIdentityVerifier`, usando `HttpClient` e `Microsoft.IdentityModel` (validação de JWT e descoberta OIDC). Não usar o handler de autenticação Google do ASP.NET Core, porque os desfechos (cadastro pendente, vínculo pendente, reautenticação) não são "login direto num cookie".
4. Desfechos do callback (todos redirecionam para a tela S3 `/entrar/google/retorno?resultado=<código>`, que encaminha o usuário):
   - `subject` já vinculado: nova sessão (REQ-007) -> `ok`.
   - email verificado sem conta: cria **cadastro Google pendente** -> `concluir` (S4).
   - email verificado de conta com senha sem vínculo: cria **vínculo Google pendente** -> `vincular` (S5).
   - cancelado, erro, `state` inválido, email não verificado, ou email de conta sem senha local com outro `subject`: nada criado -> `falhou` (REQ-009).
   - reautenticação: `subject` igual ao vinculado ao Jogador da sessão e `auth_time` recente -> marca `reauthenticated_at` na sessão -> `reautenticado`; caso contrário `reautenticacao-falhou`.
5. **Estado pendente no servidor** (tabela `accounts.pending_google_auth`): token opaco de 256 bits no cookie `__Host-dl_pending` (`HttpOnly`, `SameSite=Lax`, `Max-Age` 10 min), só o SHA-256 no banco; TTL de 10 min; uso único (o registro é apagado atomicamente na conclusão bem-sucedida; erros de validação no S4 ou senha incorreta no S5 não consomem o registro, que continua sujeito ao TTL e ao rate limit).

## Alternatives considered

- **`redirect_uri` no frontend** (SPA recebe `code` e `state` e os envia à API): expõe o código ao JavaScript e ao histórico do navegador sem ganho.
- **Estado pendente em cookie criptografado (sem tabela)**: não garante uso único nem invalidação no servidor.
- **Aceite de termos antes do clique no botão Google**: rejeitada pelo usuário (decisão da fase 3: aceite em S4, só para conta nova).
- **Handler `AddGoogle`/`AddOpenIdConnect` do ASP.NET Core**: adequado para login direto, mas exigiria contornar eventos para os desfechos pendentes; mais difícil de testar com fakes.

## Consequences

- É preciso registrar um cliente OAuth no Google Cloud (gratuito) por ambiente, com o `redirect_uri` de cada um; `client_id`/`client_secret` ficam em configuração secreta, nunca no repositório.
- O e-mail e o nome do Google ficam no máximo 10 min em `pending_google_auth` (dado pessoal transitório, expurgado por TTL).
- Os testes usam um servidor OIDC falso em memória (chaves de teste) para o adapter e um `IGoogleIdentityVerifier` falso para os casos de uso.
