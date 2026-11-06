# ADR-0016: Proxy confiável - `X-Forwarded-For` aceito só de proxies listados em `Forwarding:TrustedProxies`

- Status: Accepted (2026-10-31); nota de 2026-11-01 (ver "Nota 2026-11-01 (SEC-02, SEC-04, SEC-18, SEC-19, R-36)")
- Date: 2026-10-31
- Deciders: usuário (emenda do `design.md` da feature `conta-jogador` aprovada a partir do achado R-32 da fase 6, que pedia registrar a decisão do R-14)

## Context

A API fica atrás de um reverse proxy (no Docker Compose de desenvolvimento, o nginx do serviço `web`; em produção, um reverse proxy com TLS ainda a definir na fase 8). O IP do cliente alimenta a chave de IP do atraso progressivo (ADR-0005) e o limitador de concorrência por IP em `/api/v1/auth/**` (T-091). Sem uma regra de confiança, ou a API vê só o IP do proxy (todos os clientes compartilham a mesma chave de IP), ou aceita o `X-Forwarded-For` de qualquer remetente (o cliente forja o próprio IP e escapa das chaves de IP). O código do R-14 passou a ler `Forwarding:TrustedProxies` sem emenda do design (achado R-32).

## Decision

1. A API só aceita o `X-Forwarded-For` quando a conexão vem de um proxy listado em `Forwarding:TrustedProxies`: lista de IPs ou CIDRs. Sem a chave, a lista é só loopback (`127.0.0.1` e `::1`). Uma entrada que não seja IP nem CIDR válido impede a API de iniciar (falha no start, não em tempo de requisição).
2. O middleware `ForwardedHeaders` processa **só** o `X-Forwarded-For` (nem `X-Forwarded-Proto` nem `X-Forwarded-Host`). As redes e proxies conhecidos padrão do ASP.NET Core são limpos antes de aplicar a lista. De remetente fora da lista, o cabeçalho é ignorado e vale o IP da conexão.
3. No Docker Compose, o `web` (nginx) fala com a `api` pela rede fixa `edge` (`172.28.10.0/24`), e essa sub-rede é a lista confiável (`Forwarding__TrustedProxies__0`). O nginx sobrescreve o `X-Forwarded-For` com `$remote_addr`, sem acrescentar ao valor enviado pelo cliente.
4. Em produção (fase 8), a lista passa a ser o endereço ou a sub-rede do reverse proxy com TLS.

## Alternatives considered

- **Confiar no `X-Forwarded-For` de qualquer remetente**: zero configuração, mas qualquer cliente forja o IP e anula as chaves de IP do atraso progressivo e do limitador. Rejeitada.
- **Ignorar o `X-Forwarded-For` e usar só o IP da conexão**: atrás do proxy, todos os clientes teriam o IP do nginx e compartilhariam a mesma chave de IP (um cliente abusivo atrasaria todos). Rejeitada.
- **Manter as redes conhecidas padrão do ASP.NET Core (loopback) e só acrescentar a lista**: o padrão inclui `127.0.0.0/8`, que não é o proxy real no compose e amplia a confiança sem necessidade. Rejeitada em favor de limpar e aplicar só a lista.
- **Processar também `X-Forwarded-Proto`/`X-Forwarded-Host`**: nenhum fluxo do MVP depende deles (a origem pública vem de `Auth:PublicOrigin`); aceitar mais cabeçalhos só aumenta a superfície. Rejeitada.

## Consequences

- Nova chave de configuração `Forwarding:TrustedProxies` (lista "Configuração" do `design.md`); uma entrada inválida derruba o start.
- O compose ganha a rede `edge` com sub-rede fixa; mudar a sub-rede exige mudar a lista junto.
- A implantação de produção (fase 8) precisa configurar a lista com o proxy real; sem isso, a API só confia em loopback e todos os clientes compartilham o IP do proxy nas chaves de IP.
- O proxy precisa sobrescrever (não acrescentar) o `X-Forwarded-For`; isso é requisito de qualquer proxy usado em produção.

## Nota 2026-11-01 (SEC-02, SEC-04, SEC-18, SEC-19, R-36)

- Origem: achados SEC-02 e SEC-04 (corrigidos no código em 2026-10-31), SEC-18 (corrigido em 2026-11-01, commit `37c6df8`), SEC-19 (falta de emenda de spec) e R-36 (valor escalar ignorado; decisão do usuário em 2026-11-01, correção pendente no código, T-149) em `docs/specs/conta-jogador/findings-ledger.md`; emenda aprovada pelo usuário na triagem do retro (`docs/specs/conta-jogador/retro.md`, 2026-11-01). A decisão acima não muda: continua só `X-Forwarded-For`, só dos proxies listados. Esta nota registra duas consequências que a decisão original não tratava.
- **Chave de IP.** O IP resolvido pelo `ForwardedHeaders` não é usado como está: `ClientIpKey` (único ponto da API que lê `Connection.RemoteIpAddress`) o normaliza (IPv4 inteiro, IPv4 mapeado como IPv4, IPv6 truncado em `/64`, `unknown` sem endereço) antes de ele virar a chave de IP do atraso progressivo e a partição do limitador. Ver a nota de 2026-11-01 na ADR-0005.
- **Esquema (HTTPS).** Como o `X-Forwarded-Proto` continua ignorado (alternativa rejeitada acima), atrás do proxy a API vê `Request.IsHttps = false`. Em vez de passar a confiar no `X-Forwarded-Proto` (sugestão original do SEC-04), os cookies deixam de depender do esquema: sessão, `__Host-dl_oauth` e `__Host-dl_pending` já saíam com `Secure` fixo; o cookie antiforgery `__Host-dl_csrf` passa a usar `SecurePolicy = SameAsRequest` (o antiforgery do ASP.NET Core não lança em HTTP) com um `CookieBuilder` próprio (`SecureCookieBuilder`) que fixa `Secure` sempre. Nenhum cabeçalho do cliente rebaixa um cookie.
  - Alternativa tentada e substituída: derivar o `Secure` do antiforgery de `Auth:PublicOrigin` (https -> `Secure`). Com origem http em Development o cookie saía sem `Secure` e o navegador o recusava pelo prefixo `__Host-`, quebrando toda mutação no compose (SEC-18, `403 csrf_failed`).
  - Alternativa rejeitada: aceitar `X-Forwarded-Proto` dos proxies confiáveis. Aumenta a superfície e não é necessária, porque nenhum fluxo do MVP depende do esquema visto pela API (a origem pública vem de `Auth:PublicOrigin`).
  - Consequência: no compose de dev por http, o app precisa ser acessado por `http://localhost` (contexto seguro, em que o navegador aceita cookies `Secure`/`__Host-`); por outro nome de host em http, os cookies são recusados e o CSRF falha fechado. Fora de Development, `Auth:PublicOrigin` http já impede o start (SEC-06).
- **Item 1, valor escalar (R-36, decisão do usuário em 2026-11-01).** "Entrada inválida impede o start" passa a cobrir também o valor escalar: `Forwarding:TrustedProxies` só é aceita como lista (`Forwarding__TrustedProxies__0`, `__1`...); um valor direto na chave, em qualquer fonte de configuração (variável de ambiente sem índice, ex.: `Forwarding__TrustedProxies=172.28.10.0/24`, ou string em `appsettings.json`), derruba o start com `InvalidOperationException`, em vez de ser ignorado e cair no padrão loopback (o que, atrás do proxy, voltaria a dar a todos os clientes a mesma chave de IP). Chave ausente continua significando só loopback. Alternativa rejeitada: aceitar o escalar como uma entrada única (esconde o erro de formato e não cobre listas separadas por vírgula).
- Detalhe em `docs/specs/conta-jogador/design.md`, "Deployment topology" (sub-itens da emenda 2026-11-01), "Configuração" e "Contracts > Cookies".
