# Security report: conta-jogador

Status: Approved | Date: 2026-10-31 (re-revisão das correções e verificação de fechamento, mesma data; gate da fase 7 aprovado pelo usuário em 2026-10-31, cabeçalho corrigido em 2026-11-01) | Reviewed by: security (fase 7)

Draft: na verificação de fechamento de 2026-10-31 não há achado Critical/High `Open` ou `Confirmed` nas linhas SEC do ledger, nem Medium SEC aberto (SEC-05 e SEC-06 verificados como `Fixed`). Seguem abertos só Low SEC (SEC-07..SEC-14, SEC-16..SEC-21). O gate e a aprovação deste relatório são decididos pelo orquestrador.

## Scope
- Feature `conta-jogador`, branch `feat/conta-jogador`: `main...HEAD` (merge-base `c315e0f`, HEAD `a167641`) mais as alterações não commitadas da árvore de trabalho em 2026-10-31 (correções da fase 6: R-14, R-15, R-31, R-33, R-34, R-38 etc.).
- Re-revisão (2026-10-31): correções de SEC-01, SEC-02, SEC-03, SEC-04, SEC-06 (partes a e c) e do limite de corpo do nginx, lidas no código e nos testes da árvore de trabalho não commitada: `AccessThrottle`, `AccessAttemptKeys`, `IAccessAttemptStore`, `EfAccessAttemptStore`, `EfUnitOfWork`, `LoginWithPassword`, `RequestPasswordReset`, `LinkGoogleWithPassword`, `DeleteMyAccount`, `ProgressiveDelayPolicy`/`ProgressiveDelayOptions`, `ModuleOptionsValidators`, `AccountsModuleExtensions`, `ApiHost`, `PublicOriginCookieBuilder`, `ClientIpKey`, `HttpLimitsServiceCollectionExtensions`, `AuthEndpoints`, `GoogleEndpoints`, `MeDeletionEndpoints`, `DomainErrorProblemMapper`, `MailKitSmtpTransport`, `BodySizeLimitMiddleware`, `E2eHostProgram`, `docker-compose.yml`, `nginx.conf`, ADR-0005 (emenda SEC-01 e ajuste E7), ADR-0016, REQ-032 emendado e o design.md nas linhas da emenda.
- Lidos linha a linha: composição e pipeline da API (`ApiHost`, `TrustedProxyList`, `ModuleOptionsValidators`, `AccountsModuleExtensions`), todos os endpoints (`AuthEndpoints`, `GoogleEndpoints`, `SafeReturnTo`, `MeProfileEndpoints`, `AvatarEndpoints`, `MeDeletionEndpoints`, `CatalogEndpoints`), middlewares (`CsrfProtectionMiddleware`, `AnonymousAntiforgery`, `SecurityHeadersMiddleware`, `BodySizeLimitMiddleware`, `UnhandledExceptionMiddleware`, limitador), cookies (`SessionCookieService`, `GoogleCookieService`), `TraceJsonConsoleFormatter`; Application: `AuthenticateSession`, `HandleGoogleCallback`, `DeleteMyAccount`, `RequestPasswordReset`, `AccessThrottle`; Domain: `Session`, `Password`; Infrastructure: `GoogleOidcClient`, `GoogleIdTokenValidator`, `GoogleIdentityMapper`, `Pbkdf2PasswordHasher`, `SecureTokenGenerator`, `Sha256TokenHasher`, `HmacKeyedHasher`, `PasswordResetQueue`, `MailKitSmtpTransport`, `AccountsDesignTimeDbContextFactory`, migrations (só os `GRANT`); frontend: `routes.ts`, `googleSignin.ts`, `ResetPasswordPage.tsx` (token), `index.html`, `nginx.conf`, `Dockerfile`s, `docker-compose.yml`, `db/init/01-roles.sh`, `Database/roles/privilege-matrix.md`, `.github/workflows/ci.yml`, host E2E (`E2eHostProgram`, `E2eEnvironment`, `E2eServiceCollectionExtensions`).
- Não relidos linha a linha (cobertos pelas fatias RS-1..RS-5 do `reviewer` e pelos testes citados): demais VOs do Domain, `CompleteGoogleSignup`, `RegisterWithPassword`, `UpdateMyProfile`, `LoginWithPassword` (só os pontos de throttle), `SkiaAvatarImageProcessor`/`SkiaAvatarCodec`, componentes do design system.
- Verificação de fechamento (2026-10-31): SEC-05 (lista local de senhas comuns, emenda de REQ-038) e SEC-06 parte (b) (autenticação SMTP), lidos no código e nos testes da árvore de trabalho: `CommonPasswordList`, `Resources/common-passwords.txt` (só cabeçalho e metadados; o conteúdo não foi impresso), `Deckloom.Accounts.Domain.csproj`, `Password`, `ValidationErrorCollector`, `RegisterWithPassword`, `ResetPassword`, `LoginWithPassword` e `LinkGoogleWithPassword` (só a validação da senha), `DomainErrorProblemMapper`, `messages.ts` (texto do `too_common`), `ISmtpSession`, `MailKitSmtpTransport`, `SmtpPasswordResetSender`, `AccountsModuleExtensions` (construção do transporte), `docker-compose.yml` (só nomes das chaves SMTP), e os testes `CommonPasswordListTests`, `CommonPasswordRejectionTests`, `MailKitSmtpTransportTests`.
- Nível ASVS: o projeto não define um nível. Revisado contra **ASVS 4.0.3 nível 2** (app de consumidor com autenticação própria e dados pessoais sob LGPD), com itens L1 sinalizados. **Pergunta ao orquestrador/usuário: confirmar L2 como alvo.**
- Análise estática apenas. Nada foi executado contra banco, compose ou serviços externos.

## Ferramentas executadas
| Ferramenta | Comando | Resultado |
|---|---|---|
| Auditoria npm (produção) | `npm --prefix frontend audit --omit=dev` | 0 vulnerabilidades (critical 0, high 0, moderate 0, low 0); 8 dependências de produção |
| Auditoria npm (completa) | `npm --prefix frontend audit` | 14 moderate, 0 high, 0 critical, 0 low - todas de desenvolvimento, uma única advisory raiz: `minimatch` GHSA-g3hv-75wg-m249 (DoS por "globparts amplification") via cadeia do ESLint/typescript-eslint/eslint-plugin-react/jsx-a11y; a correção sugerida pelo npm é downgrade semver-major (inviável). SEC-12 |
| Auditoria NuGet | `dotnet list backend/Deckloom.sln package --vulnerable --include-transitive` | 0 pacotes vulneráveis nos 14 projetos (inclui transitivos; `SkiaSharp` 3.119.1, `MailKit` 4.18.2, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3) |
| Varredura de segredos (árvore) | script local só de leitura sobre `git ls-files -co --exclude-standard` (760 arquivos; regras: chave privada, AWS, Google API key, client secret e client id do Google, token GitHub/Slack, JWT, `password=`/`pwd=`, atribuição de segredo), imprimindo só arquivo:linha:regra | 0 segredos reais. 118 ocorrências, todas falsos positivos: nomes de variável (`var password = ...`), ids de campo do front, constantes sintéticas de teste (`backend/tests/**`, `frontend/e2e/**`, `*.test.tsx`), `appsettings.E2E.json` (valores sintéticos do host E2E), `db/init/01-roles.sh:24,27` (variável do cliente SQL, sem literal) |
| Varredura de cadeias de conexão | Grep por URL com credencial (`esquema://user:pass@`) e por `Host=...;...Password=` | URL com credencial: só o texto de teste do próprio `.claude/hooks/verify-guard.py`. `Host=...Password=`: `docker-compose.yml:31` e `design.md` (senha por `${DECKLOOM_APP_DB_PASSWORD}`, sem literal); testes `CompositionTests.cs`, `DatabaseConnectionOptionsTests.cs`, `StartupRoleCheckTests.cs`, `SyntheticHostSettings.cs:22` (hosts sintéticos `*.internal`/`*.invalid`, valor sintético usado em teste de redação; não é credencial) |
| Varredura do histórico git | `git log --all -p` (82.227 linhas acrescentadas, sem `package-lock.json` e sem `docs/knowledge-graph`), regras de alto sinal | 0 ocorrências |
| SAST | Não há ferramenta SAST configurada para a stack (nem no CI). Revisão manual dirigida (acima) e analisadores .NET do build. | Lacuna registrada em SEC-11 |
| Guarda do banco | `python .claude/hooks/verify-guard.py` | 22 PASS, 0 falhas |
| Auditoria npm (produção), re-revisão | `npm --prefix frontend audit --omit=dev` | 0 vulnerabilidades |
| Auditoria npm (completa), re-revisão | `npm --prefix frontend audit --json` (metadados) | 14 moderate, 0 high, 0 critical, 0 low (inalterado, SEC-12) |
| Auditoria NuGet, re-revisão | `dotnet list backend/Deckloom.sln package --vulnerable --include-transitive` | 0 pacotes vulneráveis nos 14 projetos |
| Mudança de dependências, re-revisão | `git diff HEAD --stat` em `backend/Directory.Packages.props`, `frontend/package.json`, `frontend/package-lock.json` | Sem diff: nenhuma dependência nova ou alterada |
| Varredura de segredos, re-revisão | Regras de alto sinal (chave privada, AWS, Google API key, client secret do Google, token GitHub/Slack) nas linhas acrescentadas de `git diff HEAD` | 0 ocorrências |
| Testes de segurança, re-revisão | `dotnet test backend/Deckloom.sln --filter` (nomes com `SEC0`, `AccessThrottle`, `CsrfTests`, `CompositionTests`, `ClientIpKey`, `T005_nginx`) | 171/171 aprovados (Domain 7, Application 61, Arquitetura 5, Api 98). Suíte completa e frontend não reexecutados |
| Guarda do banco, re-revisão | `python .claude/hooks/verify-guard.py`; `git diff HEAD --stat -- .claude` | 0 falhas; `.claude/` sem diff |
| Auditoria NuGet, fechamento | `dotnet list backend/Deckloom.sln package --vulnerable --include-transitive` | 0 pacotes vulneráveis nos 14 projetos (inclui transitivos) |
| Auditoria npm (produção), fechamento | `npm --prefix frontend audit --omit=dev` | 0 vulnerabilidades |
| Mudança de dependências, fechamento | `git diff HEAD --stat` em `backend/Directory.Packages.props`, `frontend/package.json`, `frontend/package-lock.json` e `.claude` | Sem diff |
| Testes de segurança, fechamento | `dotnet test backend/Deckloom.sln --filter` (`CommonPassword`, `MailKitSmtpTransportTests`, `CompositionTests`) | 86/86 aprovados (Domain 11, Application 8, Infrastructure 5, Api 62). Suíte completa e frontend não reexecutados |
| Guarda do banco, fechamento | `python .claude/hooks/verify-guard.py` | 0 falhas |

Observação: quatro chamadas desta revisão foram bloqueadas pelo hook `block-db-access.py` porque o texto do comando continha padrões de nome de arquivo de segredo, de esquema de URL de banco, o nome de um cliente de banco e o padrão de diretório de segredos (os dois últimos dentro do texto deste relatório). Não houve contorno: as varreduras foram refeitas sem esses padrões no comando, a parte de cadeias de conexão foi feita com a ferramenta Grep, e o relatório foi reescrito em linguagem neutra (sem nomear o cliente SQL).

## ASVS checklist
Legenda: Pass / Fail / N/A / Não verificado. Achados com ID estão no `findings-ledger.md`.

### V1 Arquitetura e modelo de ameaças
| Req. | Descrição | Status | Evidência |
|---|---|---|---|
| 1.1.2 | Modelo de ameaças por feature | Pass | design.md "Threat model (STRIDE-lite)" (linhas 1677-1722); itens (R) revisados abaixo |
| 1.4.1 | Controle de acesso aplicado em camada confiável no servidor | Pass | Todos os endpoints protegidos chamam `AuthenticateSession` (ex.: `MeProfileEndpoints.cs:43`, `AvatarEndpoints.cs:38`, `MeDeletionEndpoints.cs:37`); `AuthorizationByUseCaseTests` |
| 1.14.1 | Segregação de componentes com níveis de confiança diferentes | Pass | `docker-compose.yml`: `db` sem porta publicada, `web` só na rede `edge`, API sem credencial de superusuário |

### V2 Autenticação
| Req. | Descrição | Status | Evidência |
|---|---|---|---|
| 2.1.1 | Senha com no mínimo 8 caracteres (L1) | Pass | `Password.cs:7`; `PasswordTests` |
| 2.1.2 | Senhas de 64+ caracteres permitidas, máximo <= 128 (L1) | Pass | `Password.cs:8` (128) |
| 2.1.3 | Sem truncamento (L1) | Pass | `Password.cs:24-38` (sem trim/truncamento; acima de 128 é recusada) |
| 2.1.4 | Qualquer caractere Unicode imprimível (L1) | Pass | `Password.cs:30` conta code points (`EnumerateRunes`) |
| 2.1.5 | Usuário pode trocar a senha (L1) | Fail | Só existe redefinição por email; nenhuma troca autenticada. SEC-08 |
| 2.1.6 | Troca exige senha atual e nova (L1) | N/A | Não há troca de senha (ver 2.1.5) |
| 2.1.7 | Senha checada contra lista de senhas vazadas (L1) | Com ressalva (parcial) | Fechamento 2026-10-31: lista local embutida (`Resources/common-passwords.txt`, 790 entradas, recurso embutido em `Deckloom.Accounts.Domain.csproj`, sem chamada externa), comparação exata e sensível a maiúsculas (`HashSet` com `StringComparer.Ordinal`, `CommonPasswordList.cs:16-17,24`), aplicada só ao definir senha: `ValidationErrorCollector.CollectNewPassword` (`ValidationErrorCollector.cs:34-44`) chamado só em `RegisterWithPassword.cs:31` (campo `password`) e `ResetPassword.cs:41` (campo `newPassword`); login (`LoginWithPassword.cs:45`) e vínculo Google (`LinkGoogleWithPassword.cs:51`) usam `Password.Create` direto, como manda a emenda de REQ-038. Na redefinição a senha é validada antes do token, então a recusa não consome o link (`ResetPassword.cs:39-45`); no cadastro a recusa vem antes da consulta de email existente (sem sinal de enumeração). O erro leva só `code`/`field` (`DomainErrorProblemMapper` agrupa códigos por campo) e o evento de redefinição recusada só `reason=validation`; a mensagem do front é texto fixo. Testes: `CommonPasswordListTests` (6, inclusive `Matching_is_case_sensitive`), `CommonPasswordRejectionTests.Registration_rejects_a_listed_password_on_the_password_field_without_hashing_or_creating`, `Registration_error_does_not_contain_the_password`, `Registration_matching_is_case_sensitive`, `Reset_rejects_a_listed_password_on_the_new_password_field_and_does_not_consume_the_token`, `Reset_matching_is_case_sensitive`, `Login_with_a_listed_password_on_an_existing_account_is_not_rejected_by_this_rule`. Ressalva: a lista foi escrita à mão pelo `implementer` e não vem de dados de vazamento (o cabeçalho do arquivo diz isso), então não cobre corpora reais de senhas vazadas; resíduo em SEC-20 (Low). Sem teste de API do `400 validation_failed` com `too_common` (o mapeamento é genérico, coberto por outros códigos). SEC-05 Fixed |
| 2.1.8 | Medidor de força de senha (L1) | Fail (risco aceito) | Não há medidor no S1/S7. Risco aceito pelo usuário (2026-10-31); registrado em SEC-21 (Low). SEC-05 |
| 2.1.9 | Sem regras de composição (L1) | Pass | design.md:160; `PasswordTests` (`aaaaaaaa` aceito) |
| 2.1.11 | Colar e gerenciadores de senha permitidos (L1) | Pass | design.md:160 ("colar permitido") |
| 2.1.12 | Opção de mostrar a senha (L1) | Não verificado | Componente de senha do design system não relido |
| 2.2.1 | Anti-automação eficaz contra força bruta e credential stuffing (L1; ex.: <= 100 falhas/hora por conta) | Pass | Re-revisão: evento provisório gravado antes de contar, esperar e verificar; orçamento de 30 tentativas por conta em 60 min, `AccountHourlyBudget` 1..100 validado no start (`AccessThrottle.cs:26-69`, `ProgressiveDelayPolicy.cs`, `ProgressiveDelayOptionsValidator` em `ModuleOptionsValidators.cs`). Testes: `AccessThrottleTests.SEC01_parallel_attempts_from_many_ips_on_one_account_never_admit_more_than_the_budget`, `SEC01_attempt_number_thirty_is_admitted_and_number_thirty_one_is_over_the_budget`, `LoginWithPasswordTests.SEC01_parallel_logins_on_one_account_from_many_ips_verify_the_password_at_most_thirty_times`, `LoginSessionLogoutTests.SEC01_login_over_the_account_budget_is_429_too_many_attempts_without_verifying_or_a_retry_hint`. Chave IPv6 em /64 (`ClientIpKey.cs`). SEC-01 e SEC-02 Fixed; resíduo SEC-16 (Low) |
| 2.2.2 | Autenticadores fracos (SMS/email) só como secundários | N/A | Sem MFA nem OTP |
| 2.2.3 | Notificação ao usuário após mudança de credencial (redefinição, vínculo Google) (L1) | Fail | Nenhum email após redefinição concluída ou vínculo Google. SEC-08 |
| 2.4.1 | Senha armazenada com KDF aprovada, com sal (L2) | Pass | `Pbkdf2PasswordHasher.cs` (PBKDF2-SHA256), ADR-0003 |
| 2.4.2 | Sal de pelo menos 32 bits, único (L2) | Pass | `Pbkdf2PasswordHasher.cs:12` (16 bytes, `RandomNumberGenerator`) |
| 2.4.3 | PBKDF2 com >= 100.000 iterações (L2) | Pass | `PasswordHashingOptions.cs` (600.000) |
| 2.4.5 | Comparação em tempo constante | Pass | `Pbkdf2PasswordHasher.cs:57` (`FixedTimeEquals`) |
| 2.5.1 | Segredo de recuperação não enviado em claro (L1) | Pass | Email leva só o link com token de uso único (`RequestPasswordReset.cs:70`) |
| 2.5.2 | Sem perguntas secretas (L1) | Pass | Inexistentes |
| 2.5.4 | Sem contas padrão/compartilhadas (L1) | Pass | Inexistentes |
| 2.5.6 | Recuperação por mecanismo seguro (token aleatório, expira, uso único) (L1) | Pass | 256 bits, 60 min, uso único, só hash guardado, todas as sessões revogadas (`PasswordResetEndpointsTests.REQ030_confirm_revokes_every_session_of_the_player`) |
| 2.5.7 | Perda de fator OTP/MFA | N/A | Sem MFA |
| 2.7.x / 2.8.x | Autenticadores fora de banda e OTP | N/A | Não usados |
| 2.10.4 | Segredos de serviço fora do código | Pass | Varredura de segredos sem ocorrência real; configuração só por ambiente (design.md:1407, 1622) |

Autenticação federada (Google OIDC), verificada contra o modelo de ameaças:
| Item | Status | Evidência |
|---|---|---|
| `state` comparado em tempo constante, cookie de correlação cifrado (Data Protection), uso único, 10 min | Pass | `HandleGoogleCallback.cs:217-225`; `GoogleCookieService.cs:24-71`; `GoogleEndpoints.cs:104`; `GoogleEndpointsTests.REQ006_callback_always_deletes_the_oauth_cookie_so_it_is_single_use` |
| PKCE S256 e `nonce` | Pass | `GoogleEndpoints.cs:228-230`; `GoogleOidcClient.cs:231-238`; `GoogleIdTokenValidator.cs:74-75` |
| Validação do `id_token`: assinatura RS256 contra JWKS, `iss`, `aud`, `exp`, `nonce` | Pass | `GoogleIdTokenValidator.cs:38-80` (sem `alg=none`; só RS256). Ressalvas já registradas: R-39 (`iss` sem esquema), R-40 (JWKS sem cache) |
| Só `email_verified=true` (booleano JSON) | Pass | `GoogleIdentityMapper.cs:30-31`; `HandleGoogleCallback.cs:87-90` |
| Pré-sequestro por email igual: vínculo só com prova de senha | Pass | `HandleGoogleCallback.cs:164-189`; `GoogleLinkEndpointTests` |
| Reautenticação: `prompt=login`, `max_age=300`, `auth_time` <= 5 min, sessão e `PlayerId` do cookie conferidos, subject igual ao vinculado | Pass | `GoogleOidcClient.cs:239-243`; `HandleGoogleCallback.cs:105-160`; `GoogleEndpointsTests.REQ042_callback_reauth_with_a_subject_of_another_account_fails_and_marks_nothing` |
| `redirect_uri` fixo derivado de `Auth:PublicOrigin` | Pass | `GoogleOidcClient.cs:176,233` |

### V3 Sessão
| Req. | Descrição | Status | Evidência |
|---|---|---|---|
| 3.1.1 | Token de sessão nunca na URL (L1) | Pass | Só cookie `__Host-dl_session` |
| 3.2.1 | Token novo a cada autenticação (L1) | Pass | `LoginSessionLogoutTests.REQ035_login_issues_a_new_token_each_time_without_session_fixation` |
| 3.2.2 | Pelo menos 64 bits de entropia (L1) | Pass | `SecureTokenGenerator.cs` (32 bytes) |
| 3.2.3 | Token guardado de forma segura no navegador (L1) | Pass | Cookie HttpOnly; nada em `localStorage`/`sessionStorage` (grep em `frontend/src`) |
| 3.2.4 | Gerado por CSPRNG (L2) | Pass | `RandomNumberGenerator.GetBytes` |
| 3.3.1 | Logout invalida a sessão no servidor (L1) | Pass | `LoginSessionLogoutTests.REQ010_logout_returns_204_expires_the_cookie_and_invalidates_the_session` |
| 3.3.2 | Reautenticação periódica: 30 min de inatividade ou 12 h (L2) | Pass | `Session.cs:9-10,53-66`: sem lembrar-me, 30 min de inatividade; com lembrar-me, 30 dias absolutos (decisão do usuário, REQ-012). Ressalva: lembrar-me de 30 dias é o limite L1, aceito em requisito |
| 3.3.3 | Opção de encerrar as outras sessões após troca de credencial (L2) | Pass | Redefinição revoga todas as sessões (`REQ030_confirm_revokes_every_session_of_the_player`) |
| 3.3.4 | Usuário vê e encerra sessões ativas (L2) | Fail | Não existe. SEC-08 |
| 3.4.1 | Cookie com `Secure` (L1) | Pass | `SessionCookieService.cs:37-43`; `GoogleCookieService.cs:107-114`; antiforgery `ApiHost.cs:68-84` (`PublicOriginCookieBuilder`: `Secure` quando `Auth:PublicOrigin` é https, obrigatório fora de Development). Exceção de Development com origem http: SEC-18 (Low) |
| 3.4.2 | Cookie com `HttpOnly` (L1) | Pass | idem |
| 3.4.3 | Cookie com `SameSite` (L1) | Pass | Sessão/OAuth/pendência `Lax`; CSRF `Strict` |
| 3.4.4 | Prefixo `__Host-` (L1) | Pass | Todos os quatro cookies |
| 3.4.5 | `Path` adequado (L1) | Pass | `Path=/` exigido pelo `__Host-`; origem única |
| 3.5.x | Sessão por token (JWT/OAuth) | N/A | Sessão opaca no servidor (ADR-0002) |
| 3.7.1 | Reautenticação antes de operação sensível (exclusão) (L1) | Pass | `DeleteMyAccount.cs:42-57`; `DeletionEndpointTests.REQ042_*` (4 casos) |
| - | Sessão antiga não revogada ao abrir nova (login sobre sessão válida) | Fail (Low) | Já registrado como R-37 |

### V4 Controle de acesso
| Req. | Descrição | Status | Evidência |
|---|---|---|---|
| 4.1.1 | Regras aplicadas no servidor (L1) | Pass | J10: nenhuma rota recebe `PlayerId` (`MeProfileEndpoints.cs:25`, `AvatarEndpoints.cs:12`, `MeDeletionEndpoints.cs:18`) |
| 4.1.2 | Atributos de autorização não manipuláveis pelo usuário (L1) | Pass | `PlayerId` só da sessão; `PlayerId` do reauth dentro de cookie cifrado (`GoogleCookieService.cs:29-38`) |
| 4.1.3 | Menor privilégio (L1) | Pass | Sem papéis administrativos; banco com `deckloom_app` (ver seção de banco) |
| 4.1.5 | Falha segura (L1) | Pass | Sessão inválida -> `401 unauthenticated` sem dados (`REQ011_session_without_a_valid_cookie_returns_401_unauthenticated`) |
| 4.2.1 | Proteção contra IDOR (L1) | Pass | `AuthorizationByUseCaseTests.REQ013_*`, `GetMyCustomAvatarTests.REQ019_never_returns_the_image_of_another_player`, `GetMyProfileTests.REQ011_the_profile_is_the_one_of_the_caller_player_id_and_not_another_player` |
| 4.2.2 | Proteção CSRF (L1) | Pass | `CsrfProtectionMiddleware.cs`; `CsrfTests` (12 casos, inclusive `T078_mutation_with_foreign_origin_is_rejected_even_with_valid_token`). Confirmação do R-35: aceitar `Origin` ausente com token antiforgery válido não reabre CSRF (o token exige ler o cookie `__Host-dl_csrf` HttpOnly/SameSite=Strict, ou XSS); risco residual aceitável |
| 4.3.1 | MFA em interfaces administrativas | N/A | Sem interface administrativa |
| 4.3.2 | Listagem de diretório desligada | Pass | nginx sem `autoindex`; API sem arquivos estáticos |

### V5 Validação, sanitização e codificação
| Req. | Descrição | Status | Evidência |
|---|---|---|---|
| 5.1.1 | Poluição de parâmetros HTTP (L1) | Pass | `Origin` múltiplo recusado (`CsrfProtectionMiddleware.cs:56`); binding tipado |
| 5.1.2 | Mass assignment (L1) | Pass | DTOs próprios (`UpdateProfileRequest`, `RegisterRequest`...); `REQ045_put_ignores_an_email_sent_in_the_body_and_never_returns_it` |
| 5.1.3 | Validação por lista de permissão (L1) | Pass | VOs do Domain; REQ-040 (caracteres de controle) |
| 5.1.4 | Dados estruturados validados (L1) | Pass | Problem Details `validation_failed` por campo |
| 5.1.5 | Redirecionamentos só para destinos permitidos (L1) | Pass | `SafeReturnTo.cs` (servidor) e `safeReturnTo` (`routes.ts:24-30`); `R34_callback_ok_drops_an_unsafe_returnTo_and_leaves_the_default_to_the_front` |
| 5.2.1 | Sanitização de HTML não confiável | N/A | Sem rich text; React renderiza como texto |
| 5.2.4 | Sem `eval`/execução dinâmica (L1) | Pass | Grep em `frontend/src`: sem `eval`, `new Function`, `dangerouslySetInnerHTML`, `innerHTML` em produção |
| 5.2.6 | SSRF (L1) | Pass | Saída só para o `Auth:Google:Authority` configurado e os endpoints do discovery do Google |
| 5.3.1 | Codificação de saída por contexto (L1) | Pass | React JSX; Problem Details em JSON |
| 5.3.3 | XSS (L1) | Pass | Sem sink perigoso; CSP `script-src 'self'` sem `unsafe-inline` (`SecurityHeadersMiddleware.cs:8-10`, `nginx.conf:14`) |
| 5.3.4 | Consultas parametrizadas (L1) | Pass | Só EF Core; único `CommandText` é constante (`NpgsqlDatabaseRoleInspector.cs:41`); `T066_every_migrationBuilder_Sql_argument_is_a_plain_literal` |
| 5.3.8 / 5.3.9 / 5.3.10 | Injeção de SO, LFI/RFI, XPath | N/A | Sem processos externos, sem caminho de arquivo vindo do usuário, sem XML |
| 5.5.1 / 5.5.3 | Desserialização segura (L1) | Pass | `System.Text.Json` para records tipados, sem polimorfismo; cookie OAuth autenticado antes de desserializar |
| 5.5.2 | XXE | N/A | Sem XML |

### V6 Criptografia
| Req. | Descrição | Status | Evidência |
|---|---|---|---|
| 6.2.1 | Falha segura de módulos criptográficos (L1) | Pass | `GoogleCookieService.cs:67-70` (`CryptographicException` -> nulo); `GoogleIdTokenValidator.cs:31-35` |
| 6.2.2 | Algoritmos aprovados (L2) | Pass | SHA-256, HMAC-SHA256, PBKDF2-SHA256, RS256, Data Protection (AES + HMAC) |
| 6.2.5 | Sem algoritmos inseguros | Pass | Nenhum MD5/SHA-1/DES no código de produção |
| 6.3.1 | Aleatoriedade por CSPRNG (L2) | Pass | `RandomNumberGenerator` em tokens e sal |
| 6.4.1 | Gestão de chaves (L2) | Fail | Chaves do Data Protection em volume sem cifra em repouso e sem nome de aplicação. SEC-07 |
| 6.4.2 | Material de chave isolado | Pass | Segredo HMAC e client secret só por configuração; HMAC exige >= 32 bytes (`HmacKeyedHasher.cs:10-24`, `ModuleOptionsValidators.cs:43-48`) |
| - | Comparações em tempo constante (state, hash de senha, `TokenHash`) | Pass | `HandleGoogleCallback.cs:224-225`; `Pbkdf2PasswordHasher.cs:57`; RS-1 (`TokenHash`) |

### V7 Erros e logs
| Req. | Descrição | Status | Evidência |
|---|---|---|---|
| 7.1.1 | Sem credenciais nem tokens de sessão nos logs (L1) | Pass | `LogRedactionTests.NFR_no_log_entry_contains_any_sensitive_value_used_in_the_flows`; filtros de `Hosting.Diagnostics` e `Http.Result` em Warning (`ApiHost.cs:27-28`); nginx `path_only` e `error_log crit` |
| 7.1.2 | Sem outros dados sensíveis (email, IP em claro) (L1) | Pass | `NFR_catalog_events_carry_only_the_allowed_fields`; `NFR_sensitive_data_logging_is_disabled_on_every_db_context`; exceções logadas só pelo nome do tipo (`UnhandledExceptionMiddleware.cs:38-39`) |
| 7.1.3 | Eventos de segurança registrados (L2) | Pass | Catálogo 1001-1021 (design.md:1636-1658) |
| 7.1.4 | Informação suficiente para investigação (L2) | Pass | `traceId` em todo log (`TraceJsonConsoleFormatter.cs:33-37`) |
| 7.2.1 | Decisões de autenticação registradas (L2) | Pass | 1002/1003/1009/1010/1016/1018 |
| 7.3.1 | Codificação contra injeção em log (L2) | Pass | JSON por `Utf8JsonWriter` (escapa quebras de linha) |
| 7.3.3 / 7.3.4 | Proteção e sincronização dos logs | Não verificado | Hospedagem e retenção de logs na fase 8 |
| 7.4.1 | Mensagem de erro genérica com id de correlação (L1) | Pass | `ProblemDetailsTests.REQ_003_unhandled_exception_body_leaks_no_stack_sql_or_library_message` |
| 7.4.2 | Tratamento de exceção em toda a base (L2) | Pass | `UnhandledExceptionMiddleware` logo após os cabeçalhos (`ApiHost.cs:77-79`) |

### V8 Proteção de dados
| Req. | Descrição | Status | Evidência |
|---|---|---|---|
| 8.2.1 | Cabeçalhos anti-cache em dados sensíveis (L1) | Pass | `Cache-Control: no-store` em todo endpoint autenticado e em respostas com cookie de sessão (`SecurityHeadersMiddleware.cs:30-34`) |
| 8.2.2 | Nada sensível em armazenamento do navegador (L1) | Pass | Grep: sem `localStorage`/`sessionStorage` |
| 8.2.3 | Dados autenticados removidos do cliente ao fim da sessão (L1) | Fail (Low) | Avatar customizado com `Cache-Control: private, max-age=31536000, immutable` (`AvatarEndpoints.cs:108`) fica no cache do navegador após logout/exclusão. SEC-14 |
| 8.3.1 | Dados sensíveis no corpo/cabeçalho, não na URL (L1) | Pass | Token de redefinição no fragmento e removido por `history.replaceState` (`ResetPasswordPage.tsx:63-67`); `Referrer-Policy: no-referrer`. `code`/`state` do OAuth vão na query por exigência do protocolo, com logs sem query |
| 8.3.2 | Titular pode remover e exportar seus dados (L1) | Fail (Low) | Exclusão completa (REQ-024) atendida; acesso/portabilidade não existe e o email não é mostrado ao titular. SEC-13 |
| 8.3.4 | Dados sensíveis identificados e com política | Pass | design.md "Logs", "Retenção", ADR-0005/0006 |
| 8.3.8 | Retenção definida e aplicada (L2) | Pass com pendência | Rate limit 24 h, sessões/pendências por job (`PurgeExpiredData`); logs: fase 8 / D12/D13 (não verificado); backups: decidido em 2026-10-31 pelo usuário (DB12: 30 dias, reaplicação das exclusões após restauração, log sem dado pessoal), requisito documentado, procedimento não implementado, verificação manual H-10 |

### V9 Comunicações
| Req. | Descrição | Status | Evidência |
|---|---|---|---|
| 9.1.1 | TLS para clientes (L1) | Não verificado | Produção indefinida (fase 8); HSTS emitido pela API fora de Development (`SecurityHeadersMiddleware.cs:25-28`), mas o nginx da SPA não emite HSTS |
| 9.2.1 | Conexões de saída com TLS e certificado confiável (L2) | Pass | Re-revisão: fora de Development, `Email:Smtp:UseTls=false`, `Auth:Google:Authority` e `Auth:PublicOrigin` em `http` derrubam o start (`ModuleOptionsValidators.cs:33-41,65-69,100-103`; `CompositionTests.SEC06_outside_development_an_insecure_transport_setting_refuses_startup_naming_only_the_key`, `SEC06_every_environment_other_than_development_refuses_the_three_insecure_settings_together`); `UseTls` usa `StartTls` obrigatório (`MailKitSmtpTransport.cs:21`); banco `VerifyFull` (inalterado). Ressalva: `RequireHttps` recusa só o esquema `http` (não exige `https`). O host E2E remove esses validadores só no ambiente `E2E` (`E2eHostProgram.cs:28-36`, projeto não publicável) |
| 9.2.2 | Conexões autenticadas com sistemas externos (L2) | Pass | Fechamento 2026-10-31: com `Email:Smtp:User` preenchido o transporte autentica depois de conectar e antes de enviar (`MailKitSmtpTransport.cs:31-39`); vazio ou só espaços, envia sem autenticar (Mailpit local). A falha de autenticação é relançada como `InvalidOperationException` só com o nome do tipo, sem a mensagem nem a exceção interna (`MailKitSmtpTransport.cs:43-53`); o `SmtpPasswordResetSender` descarta a exceção e registra só `playerId`/`attempt` (`SmtpPasswordResetSender.cs:51,73-76`); `SmtpOptions.ToString()` não expõe valores. A costura `ISmtpSession` só é substituída nos testes: a produção constrói o transporte com host, porta, usuário e senha e sem fábrica (`AccountsModuleExtensions.cs:152`), e o padrão é a sessão MailKit real (`MailKitSmtpTransport.cs:24`). Testes: `MailKitSmtpTransportTests.With_user_configured_it_authenticates_after_connect_and_before_sending`, `Without_user_it_sends_without_authentication` (3 casos), `Authentication_failure_never_exposes_the_password_in_the_exception`. Ressalva: em Development com `UseTls=false` e usuário definido a credencial iria sem TLS; fora de Development `UseTls=false` derruba o start (9.2.1). SEC-06 Fixed |
| - | Proxy confiável e esquema da requisição | Pass | Re-revisão: SEC-04 corrigido sem confiar em `X-Forwarded-Proto` (ADR-0016 mantida): o antiforgery usa `SameAsRequest` (não lança em HTTP) e o `Secure` vem do esquema de `Auth:PublicOrigin`, nunca da requisição (`PublicOriginCookieBuilder.cs`, `ApiHost.cs:68-84`). Testes `CsrfTests.SEC04_behind_the_proxy_a_plain_http_request_gets_the_csrf_token_and_a_secure_cookie_when_the_public_origin_is_https` (envia `X-Forwarded-Proto: https`, ignorado) e `SEC04_behind_the_proxy_a_plain_http_mutation_with_the_token_pair_is_accepted`. Não verificado no compose em execução. Development com origem http: SEC-18 |

### V10 Código malicioso
| Req. | Descrição | Status | Evidência |
|---|---|---|---|
| 10.2.x | Sem funcionalidade oculta, backdoor ou "phone home" | Pass | Revisão do escopo; host E2E e rotas `/__test` fora do artefato de produção (`Deckloom.Api.E2E.Host.csproj`: `IsPublishable=false`; rotas 404 fora do ambiente `E2E`, `E2eHostProgram.cs:47-51`) |
| 10.3.2 | Sem recursos de terceiros sem integridade | Pass | `index.html` sem CDN; CSP `'self'` |

### V11 Lógica de negócio
| Req. | Descrição | Status | Evidência |
|---|---|---|---|
| 11.1.1 | Fluxo processado em ordem, sem pular etapas (L1) | Pass | Pendência Google consumida por `DELETE ... RETURNING`; reautenticação presa à sessão |
| 11.1.2 | Limite de tempo humano realista (L1) | Pass | Re-revisão: orçamento de 30/60 min por conta contado antes da verificação (SEC-01 Fixed); ver 2.2.1 |
| 11.1.3 | Limites por usuário/ação de negócio (L1) | Pass | Re-revisão: solicitação de redefinição dentro do orçamento por conta; acima dele, o mesmo `202` sem token nem email (`RequestPasswordReset.cs:26-34`). Testes `RequestPasswordResetTests.SEC03_repeated_requests_for_one_account_send_at_most_the_hourly_budget_of_emails`, `PasswordResetEndpointsTests.SEC01_request_over_the_account_budget_is_still_202_but_sends_no_email_and_looks_the_same`. Resíduo aceito: até 30 emails e 30 trocas de token por hora por conta. SEC-03 Fixed |
| 11.1.4 | Anti-automação contra uso excessivo (L1) | Fail (Low) | Re-revisão: SEC-01 e SEC-02 corrigidos; resta SEC-09 (cadastros aceitos e mutações de `/me` sem limite) |
| 11.1.6 | Sem condição de corrida (TOCTOU) em fluxos sensíveis (L2) | Pass | Re-revisão: gravar antes de contar (ADR-0005 E2). A inserção é confirmada fora do `IUnitOfWork` (`EfAccessAttemptStore.cs:14-35`; `EfUnitOfWork` só abre transação em `ExecuteAsync`, e `BeforeAttemptAsync` roda fora dela nos quatro casos de uso) e a contagem vem depois; a corrida na fronteira só recusa a mais. O teste paralelo usa o store em memória; em PostgreSQL (READ COMMITTED) a garantia segue do argumento, não foi executada |

### V12 Arquivos
| Req. | Descrição | Status | Evidência |
|---|---|---|---|
| 12.1.1 | Arquivos grandes recusados (L1) | Pass | 2,5 MB no corpo (`AvatarEndpoints.cs:65-84`), 2 MB no arquivo (design.md:1608); nginx `/api` com `client_max_body_size 2560k`, igual ao limite da API (`nginx.conf:23`; `RepoHygieneTests.T005_nginx_api_location_allows_bodies_up_to_the_api_upload_limit`; SEC-15) |
| 12.1.2 | Bomba de descompressão (L2) | Pass | Dimensões e quadros pelo cabeçalho antes de decodificar; R-38 corrigido |
| 12.1.3 | Cota por usuário (L2) | Pass | Um avatar por Jogador; troca apaga o anterior na mesma transação |
| 12.2.1 | Tipo validado pelo conteúdo (L2) | Pass | Assinatura de bytes (design.md:1608) |
| 12.3.x | Caminho/nome vindo do usuário | N/A | Imagem guardada em `bytea` com chave gerada |
| 12.4.1 | Armazenado fora da raiz web (L1) | Pass | PostgreSQL (ADR-0009) |
| 12.5.2 | Servido com tipo correto e `nosniff` (L1) | Pass | `AvatarEndpoints.cs:107-111` |

Item (R) do modelo de ameaças - código nativo do SkiaSharp: Pass com pendência. `SkiaSharp`/`NativeAssets.Linux.NoDependencies` 3.119.1 fixados em `Directory.Packages.props`, 0 advisory conhecido no `dotnet list package --vulnerable`; só três formatos chegam ao decoder; limites e timeout/semáforo (R-38). O contêiner da API não roda como root (`backend/Dockerfile:11`); sistema de arquivos somente leitura e isolamento de processo ficam para a fase 8 (não verificado).

### V13 API
| Req. | Descrição | Status | Evidência |
|---|---|---|---|
| 13.1.1 | Mesma codificação e parser em todos os componentes | Pass | JSON UTF-8 via `System.Text.Json` |
| 13.1.3 | Sem dado sensível na URL da API (L1) | Pass | Exceto `code`/`state` do OAuth (protocolo), com logs sem query |
| 13.1.4 | Autorização por rota e por recurso (L2) | Pass | Ver V4 |
| 13.2.1 | Só métodos HTTP previstos (L1) | Pass | Rotas mínimas por método |
| 13.2.3 | CSRF em serviços REST com cookie (L1) | Pass | Ver 4.2.2 |
| 13.2.5 | `Content-Type` de entrada verificado (L2) | Fail (Low) | `ReadJsonAsync` desserializa qualquer `Content-Type` (`AuthEndpoints.cs:183-201`). SEC-10 |
| 13.2.6 | Respostas sem campos a mais | Pass | `REQ045_get_exposes_no_email_and_no_hash_anywhere_in_the_json`, `REQ011_session_with_valid_cookie_returns_200_summary_without_email_or_hash` |
| 13.4.x | GraphQL | N/A | - |

### V14 Configuração
| Req. | Descrição | Status | Evidência |
|---|---|---|---|
| 14.1.1 | Build e implantação reprodutíveis e seguros (L2) | Fail (Low) | CI ainda com placeholders, sem auditoria de dependências nem SAST; actions por tag, imagens base por tag móvel. SEC-11 |
| 14.2.1 | Componentes atualizados e sem vulnerabilidade conhecida (L1) | Pass com ressalva | 0 em produção (npm e NuGet); 14 moderate só em dev. SEC-12 |
| 14.2.2 | Recursos desnecessários removidos (L1) | Pass | Sem Swagger, sem página de exceção de desenvolvimento, sem endpoints de teste no host de produção |
| 14.2.4 | Componentes de fonte confiável e fixados | Pass com ressalva | `save-exact=true`, `package-lock.json`, CPM com versões exatas; imagens Docker sem digest (SEC-11) |
| 14.3.2 | Modo debug desligado em produção (L1) | Pass | Nada condicionado a debug; `Development` só no compose de dev (exposição do compose em SEC-17, cookie CSRF de dev em SEC-18) |
| 14.3.3 | Cabeçalhos sem versão de componentes (L1) | Pass | Sem versão exposta |
| 14.4.1 | `Content-Type` com charset (L1) | Pass | ASP.NET Core JSON |
| 14.4.3 | CSP (L1) | Pass | API e nginx, design.md:1619 |
| 14.4.4 | `X-Content-Type-Options: nosniff` (L1) | Pass | API e nginx |
| 14.4.5 | HSTS (L1) | Não verificado | API fora de Development sim; SPA/proxy de produção na fase 8 |
| 14.4.6 | `Referrer-Policy` (L1) | Pass | `no-referrer` |
| 14.4.7 | Proteção contra framing (L1) | Pass | `frame-ancestors 'none'` |
| 14.5.2 | `Origin` não usado como única decisão (L1) | Pass | Só camada extra do CSRF |
| 14.5.3 | CORS por lista de permissão (L1) | Pass | Sem CORS; `REQ_003_cors_preflight_is_not_answered_with_cors_headers` |
| 14.5.4 | Cabeçalhos de proxy aceitos só de proxies conhecidos (L1) | Pass | ADR-0016, `TrustedProxyList`, `ConcurrencyLimiterTests.REQ032_forwarded_for_from_a_non_trusted_origin_is_ignored`. Resíduo: R-36 (SEC-02 e SEC-04 corrigidos na re-revisão) |

### Itens (R) do modelo de ameaças
| Item | Conclusão |
|---|---|
| Força bruta sem bloqueio de conta (D5) | Re-revisão: mitigado. Orçamento de 30/60 min por conta, contado antes da verificação, vale sob concorrência e para qualquer número de IPs (SEC-01 Fixed); chave IPv6 em /64 (SEC-02 Fixed). Risco residual aceito pelo usuário (REQ-032 emendado; ADR-0005, consequências da emenda): quem conhece o email nega o login com senha, o vínculo Google e a exclusão com senha da conta por até 60 min com cerca de 31 requisições por hora, inclusive só com solicitações de redefinição (que também mandam até 30 emails por hora à vítima); o login Google segue disponível quando vinculado |
| Enumeração no cadastro (exceção de REQ-002) | Mitigações implementadas (atraso por IP, `REQ002_existing_email_returns_409_email_unavailable_and_counts_for_the_ip_delay`, resposta sem dado da conta). Re-revisão: chave IPv6 agora em /64 (SEC-02 Fixed). O 429 do orçamento não revela existência (`LoginSessionLogoutTests.SEC01_the_over_budget_answer_is_identical_for_an_existing_and_an_unknown_account`). A exceção continua aceitável como decisão do usuário |
| Código nativo SkiaSharp | Ver V12 |
| Retenção de rate limit, troca do segredo HMAC | Implementado conforme ADR-0005; procedimento de troca do segredo não verificado (operação da fase 8) |
| TLS do banco no compose de dev | Exceção aceita, condicionada a `Development` (validador) |
| API com privilégio de superusuário | Mitigado: papel `deckloom_app`, validador da connection string, verificação de papel no start (DB7) |
| Backups e LGPD (DB12), RLS não adotada (DB11), sem verificação de email (D1), idade autodeclarada (D13) | Riscos aceitos/pendentes de decisão jurídica e da fase 8; não verificáveis aqui. DB12: retenção decidida em 2026-10-31 pelo usuário (30 dias, reaplicação das exclusões após restauração, log sem dado pessoal); requisito documentado, procedimento não implementado, verificação manual H-10; criptografia, papel de backup e aviso ao titular seguem na fase 8 |

## Controle de acesso ao banco (produto) - revisão estática
| Item | Status | Evidência |
|---|---|---|
| `Database/roles/` e `privilege-matrix.md` batem com os `GRANT` das migrações | Pass | `20261025020132_CreateAccountsSchema.cs:245-269`, `20261025015138_CreateFormatCatalog.cs:33-35` x `privilege-matrix.md`; `T066_migrations_grant_exactly_the_privileges_marked_in_the_matrix_to_deckloom_app` |
| API conecta só como papel de runtime de menor privilégio | Pass | `docker-compose.yml:31` (`Username=deckloom_app`); `DatabaseOptionsValidator` recusa outro usuário e chaves `POSTGRES_*`/`DECKLOOM_MIGRATOR_*` (`ModuleOptionsValidators.cs:111-192`) |
| Nenhuma migração/DDL no start | Pass | Grep sem `Migrate`/`EnsureCreated`/`EnsureDeleted`/`GetPendingMigrations` em `backend/src`; `T131_production_code_never_invokes_migration_or_ddl_members` |
| TLS obrigatório fora do dev local | Pass | `ModuleOptionsValidators.cs:179-191` |
| Scripts de papéis com senha do ambiente, nunca literal | Pass | `db/init/01-roles.sh:9-17` (`${VAR:?}`, senhas passadas como variáveis do cliente SQL, heredoc com aspas); `T005_roles_script_contains_no_literal_password` |
| Topologia de dev: superusuário só no bootstrap | Pass | `docker-compose.yml:44-55`; `T005_role_passwords_exist_only_in_the_db_service_environment` |
| Design-time factory sem credencial | Pass | `AccountsDesignTimeDbContextFactory.cs:10` (host `.invalid`, sem senha) |

## Política de não acesso ao banco (diretiva 2) - verificação
| Item | Status | Evidência |
|---|---|---|
| Testes não alcançam banco real | Pass | Adapters EF testados com `SqliteInMemoryFixture` (ADR-0012), inclusive `CleanupJobTests.cs:36`; nenhum `UseNpgsql`/`NpgsqlConnection` em `backend/tests`; hosts/strings de conexão de teste sintéticos (`*.internal`, `*.invalid`) |
| E2E usa o host de teste com fakes | Pass | `frontend/e2e/playwright.config.ts` sobe `Deckloom.Api.E2E.Host` com `ASPNETCORE_ENVIRONMENT=E2E`; `E2eServiceCollectionExtensions.cs:33-58` troca repositórios, unidade de trabalho, catálogos, armazenamento e email por fakes em memória e remove a verificação de papel; connection string placeholder nunca aberta (`E2eEnvironment.cs:10-19`) |
| Migrações só como arquivo | Pass | Arquivos em `Migrations/`; nenhum script/CI aplica migração; `.github/workflows/ci.yml` sem passo de banco |
| Scripts do repositório não tocam banco | Pass | Únicos scripts: `.claude/hooks/*`, `.claude/scripts/*` (sem rede/banco) e `db/init/01-roles.sh` (executado só pelo entrypoint do contêiner `db`, por humano) |
| Fechamento 2026-10-31 | Pass | Nenhum comando de banco executado (só leitura, `dotnet test` filtrado, auditorias e `verify-guard.py`); nenhum `UseNpgsql`/`NpgsqlConnection`/`NpgsqlDataSource` no código-fonte de `backend/tests` e nenhuma referência a pacote Npgsql nos `.csproj` de teste; os testes novos usam fakes em memória (repositórios em memória, sessão SMTP fake); SEC-05/SEC-06 não mudam esquema, migrações nem privilégios |
| Re-revisão 2026-10-31 | Pass | Nenhum comando de banco executado nesta re-revisão (só leitura, `dotnet test` filtrado, auditorias e `verify-guard.py`). Nenhum `UseNpgsql`/`NpgsqlConnection`/`NpgsqlDataSource` no código-fonte de `backend/tests` (só DLLs em `bin/`); o teste novo `CleanupFilterInDatabaseTests` usa `SqliteInMemoryFixture`; nenhuma migração nova (só `AccountsDbContextModelSnapshot.cs` muda o tipo CLR de `ExpiresAt`, sem mudar a coluna); nenhum `Migrate`/`EnsureCreated` em `backend/src`; a emenda SEC-01 não muda esquema nem privilégios (`SELECT`, `INSERT`, `DELETE` já concedidos) |

## Re-revisão 2026-10-31 - verificação das correções
| Item | Resultado | Evidência e ressalvas |
|---|---|---|
| SEC-01 (High) | Fixed | Corrida: cada tentativa grava o próprio evento (inserção confirmada, fora de transação) e só depois conta; a i-ésima inserção confirmada vê pelo menos i eventos, então nenhum número de IPs ou instâncias admite mais de 30 verificações por hora por conta. Evento provisório deixado para trás: cancelamento durante o atraso, timeout e exceção mantêm o evento por desenho (ADR-0005 E5), o que impede o atacante de fugir da contagem; mas as exclusões compensatórias (recusa por orçamento e `ReleaseAsync`) usam o token da requisição, então um cliente que aborta pode deixar evento excedente e estender a recusa (SEC-16, Low). O 429 não revela existência (HMAC do email, decisão antes de consultar o Jogador). O verificador de senha não roda acima do orçamento nos três casos de uso (testes `*_without_verifying_the_password`). Exclusão só Google fora do orçamento (decisão do usuário): aceitável, não há senha a adivinhar e a reautenticação Google recente continua exigida |
| SEC-02 (Medium) | Fixed | `ClientIpKey` é a única leitura de `RemoteIpAddress` em `backend/src/Deckloom.Api` (busca confirma; teste de arquitetura `SEC02_only_ClientIpKey_reads_the_remote_ip_address_in_the_api`, que ignora comentários `//`). Usado no cadastro, login, redefinição, vínculo Google, exclusão e no limitador. O /64 não está descrito no design.md nem em ADR (SEC-19, Low) |
| SEC-03 (Medium) | Fixed | Teto de 30 solicitações por hora por conta, compartilhado com o orçamento; resposta idêntica acima do teto |
| SEC-04 (Medium) | Fixed | `Secure` derivado de `Auth:PublicOrigin`; `X-Forwarded-Proto` continua ignorado. Sem emenda de design/ADR (SEC-19) |
| SEC-06 (Medium) | Open | Partes (a) e (c) verificadas; parte (b), autenticação SMTP, pendente (ver a verificação de fechamento abaixo) |
| nginx (limite de corpo) | Fixed | Registrado como SEC-15 |
| `__Host-dl_csrf` sem `Secure` em Development por http | Risco Low (SEC-18) | O prefixo `__Host-` exige `Secure`; o navegador deve recusar o cookie, então o CSRF falha fechado no compose por http (403 em mutações), sem enfraquecer a proteção. Inferido da regra do prefixo, não verificado em navegador. Correção simples: `Secure` sempre (`http://localhost` é contexto seguro, como o host E2E já assume) |
| Compose com `ASPNETCORE_ENVIRONMENT=Development` | Risco Low (SEC-17) | Aceitável para um compose só local: as exceções de Development (SMTP sem TLS, origens http, banco sem TLS) ficam presas a esse ambiente pelos validadores. O que pesa é a publicação de `8080` e `8025` (Mailpit sem autenticação, com links de redefinição) em todas as interfaces do host; publicar só em `127.0.0.1` |

## Verificação de fechamento 2026-10-31
| Item | Resultado | Evidência e ressalvas |
|---|---|---|
| SEC-05 (Medium) | Fixed, com resíduos | Controle implementado como a emenda de REQ-038 pede (ver 2.1.7): exato, sensível a maiúsculas, só no cadastro e na redefinição, recusa sem consumir o token, mensagem sem a senha, login e vínculo fora da regra. Resíduos: lista escrita à mão, não derivada de vazamentos (SEC-20, Low, Open); sem medidor de força, risco aceito pelo usuário (SEC-21, Low, Open) |
| SEC-06 (Medium) | Fixed | Parte (b) verificada (ver 9.2.2): autentica quando `Email:Smtp:User` está definido; senha fora das mensagens de exceção e dos logs; a costura `ISmtpSession` não é usada para contornar o transporte real em produção. Partes (a) e (c) já verificadas na re-revisão |
| Frontend `too_common` | Sem mudança de segurança | `messages.ts:12,64-65`: texto fixo para `too_common:password` e `too_common:newPassword`, sem eco da senha |
| Dependências | Sem mudança | NuGet 0 vulneráveis (14 projetos), npm produção 0; nenhum arquivo de dependência mudou |

## Fase 7 abreviada 2026-11-04 (rodada pós-retro)
Escopo: `git diff 541bcbf..HEAD` em `feat/conta-jogador` (HEAD `98671da`). Alvo: ASVS 4.0.3 L2, conforme a tarefa da fase. Análise estática, testes sem banco e leitura de `docker logs` da `api` e do `web`. Sem acesso ao banco, sem `docker exec`, sem leitura de `.env` ou `.env.example`.

### Fechamentos
| Item | Resultado | Evidência |
|---|---|---|
| SEC-10 | Fixed | 9 endpoints JSON por `ReadJsonAsync`; `JsonContentTypeTests` |
| SEC-16 | Fixed | `CancellationToken.None` nas exclusões compensatórias; `AccessThrottleCompensationTests`, `AccessThrottleSignatureTests`, `AccountFailuresLateCancellationTests` |
| SEC-17 | Fixed | `127.0.0.1:8081:80` e `127.0.0.1:8025:8025` no compose e no `docker ps -a`; `T153_*`, `QA_compose_*`. `.env.example` não lido |
| SEC-18 | Re-verificado | `SecureCookieBuilder`; `HostPrefixedCookieTests` |
| SEC-19 | Fixed | ADR-0005, ADR-0016 e `design.md` batem com o código |

### Checklist ASVS sobre o diff
| Req. | Status | Evidência |
|---|---|---|
| V2.2.1 / V11.1.6 Anti-automação e corrida | Pass | Evento provisório preservado; exclusões já decididas sem o token da requisição (SEC-16); operações imediatas recusam unidade de trabalho aberta (T-144) |
| V3.4.1, V3.4.4 Cookies `Secure`, `__Host-` | Pass | `HostPrefixedCookieTests` na composição real, os quatro cookies observados |
| V4.2.2 / V13.2.3 CSRF | Pass | Middleware sem mudança; `CsrfTests` verdes; o 415 vem depois do CSRF |
| V5.1.x Validação de entrada | Pass | Leitura JSON em um só ponto; cadastro usa o mesmo limite de corpo |
| V5.3.4 Consultas parametrizadas | Pass | Sem SQL novo em produção; `CommandText` novo só em testes |
| V5.3.3 XSS | Pass | Sem sink novo em produção; `innerHTML` novo só em testes |
| V3 Federada (Google) | Pass | `iss` por comparação ordinal contra dois valores; a assinatura segue validada; a reorganização do callback mantém as recusas e a troca do código só depois delas |
| V7.1.1, V7.1.2 Logs | Pass com ressalvas | Motivos do evento 1010 em constantes (`session_invalid` novo); amostra real sem dado sensível. Ressalvas: SEC-23, R-57 |
| V7.4.1 Erros | Pass | `TrustedProxyList` nomeia chave e índice, sem ecoar valor (R-48) |
| V9.2.1, V9.2.2 Saída autenticada | Pass com ressalva | Sem mudança no código; SEC-22 em Development |
| V12.1.1 / V13.2.5 Limite de corpo e `Content-Type` | Pass | `nginx.conf` e `BodySizeLimitMiddleware` sem diff; V13.2.5 passa de Fail para Pass (SEC-10) |
| V14.1.1 Build | Pendente de execução | Pipeline presente; SAST e gitleaks não executados nesta verificação |
| V14.2.1 Dependências | Pass com ressalva | 0 em produção; 7 moderate só em dev (SEC-12, aceito) |
| V14.3.2, V1.14.1 Exposição do compose | Pass | Loopback verificado em execução (SEC-17) |
| V14.5.4 Proxy confiável | Pass | Leitura adiada em `ApiHost`; entrada inválida ainda derruba o start (`TrustedProxyListTests`); só `X-Forwarded-For`; nginx sobrescreve o cabeçalho (`nginx.conf:26`). Valor vazio em `Forwarding:TrustedProxies` equivale à chave ausente (loopback): efeito de disponibilidade, sem confiar a mais |
| Demais categorias | N/A nesta rodada | V6, V8, V10 e V12.2-12.5 sem mudança de produção no diff; V2.1, V2.4, V2.5, V4.1 e V4.3 sem mudança relevante. Ver checklist completo acima |

### Ferramentas
| Ferramenta | Resultado |
|---|---|
| `npm audit --omit=dev` | 0 |
| `npm audit` completo | 7 moderate, só dev (`minimatch`, SEC-12) |
| NuGet `--vulnerable --include-transitive` + `check_nuget_audit.py` | 0 |
| Varredura de segredos substituta nas linhas acrescentadas | 0 reais em 7.370 linhas; não cobre a raiz fora da lista permitida, `docs/knowledge-graph` nem o histórico completo |
| `dotnet build -c Release` | 0 avisos, 0 erros |
| `npm run lint` | exit 0 |
| `dotnet test` filtrado | Última rodada 289/289; a primeira teve 1 falha intermitente (SEC-24). Suíte completa não executada pelo `security` |
| `verify-guard.py` | 22 PASS, 0 FAIL |
| CodeQL, gitleaks | Não executados: ausentes localmente, HEAD não enviado, sem `gh` para consultar o CI |

### Smoke de 2026-11-04
- **Evento 1008 (R-57):** registrar só o tipo é seguro se a exceção não for passada ao logger e nem `Message` nem `InnerException` forem registradas. Não basta para diagnosticar autenticação, porque o transporte a transforma em `InvalidOperationException`. Exige tipo próprio ou código de lista fechada, emenda do catálogo e teste de redação.
- **`Database.Command`:** log real da `api` com 27 linhas da categoria e 42 de 42 parâmetros como `?`; único literal no SQL é `'google'`; 0 ocorrências dos padrões sensíveis. A amostra cobre só login Google, sessão, perfil e avatar, sem caminho de erro. Proteção em profundidade em SEC-23.

### Guarda do banco
`.claude/` e `.github/` sem diff na rodada e sem alteração na árvore de trabalho; `settings.json`, `block-db-access.py` e `db_patterns.json` sem mudança desde `70cb64e`; `verify-guard.py` sem falhas. Correção à seção "Política de não acesso ao banco": além dos scripts citados, existe `scripts/db/criar-banco-local.ps1`, executado só por humano e fora do CI, que aplica o SQL via `docker compose exec` como `deckloom_migrator`, sem senha literal. Conforme a política; não executado nesta revisão.

### Compliance (LGPD)
Sem novo tratamento de dados pessoais no diff. Na amostra de log aparece só `playerId` interno. As lacunas anteriores seguem (SEC-13, retenção de logs e backups). Não certifico conformidade.

### Não verificado
- CodeQL e gitleaks, nesta branch e neste HEAD.
- Conteúdo e diff de `.env.example`; valor de `Auth__PublicOrigin` no ambiente local.
- Log real de login com senha, redefinição, evento 1008 e erro de comando no PostgreSQL; efeito de `Include Error Detail` (SEC-23c).
- Correspondência entre a imagem em execução e o HEAD.
- Garantia de SEC-16 em PostgreSQL; comportamento do 415 pelo nginx; DNS rebinding contra o Mailpit.

### Resumo
Fixed nesta rodada: SEC-10, SEC-16, SEC-17, SEC-19. Novos `Open`, todos Low: SEC-22, SEC-23, SEC-24. Critical/High `Open` ou `Confirmed`: 0. Medium SEC `Open`: 0. A aprovação do gate é do usuário, com CodeQL e gitleaks ainda por confirmar no CI.

### Verificação pós-fase 7, 2026-11-05 (`98671da..HEAD`, HEAD `1606372`)
| Item | Resultado | Evidência |
|---|---|---|
| R-57 / T-178 | Segue `Open` | Condições de segurança cumpridas e confirmadas por mutação; `tls` não sai numa falha real de handshake (MailKit real resultou em `connect`) |
| SEC-22 / T-179 | Fixed | Recusa no start em qualquer ambiente, mensagem só com chaves; mutações não executadas |
| SEC-24 | Fixed | Mensagens testadas em `Parse`; 12 rodadas sem falha |
| R-57 / T-181 (`8534637`) | Fixed | Transporte real contra servidor falso em loopback, com STARTTLS aceito e resposta inválida: `CAUSA_REAL=Tls INNER=null MSG=Falha no envio SMTP.`. Redação sem mudança desde `1606372`. Não verificados: certificado recusado e log real do compose |
| `InternalsVisibleTo` em `Deckloom.Api` | Sem risco | Só `Deckloom.Api.Tests`; assembly de teste fora da imagem |
| T-175 | Sem impacto | `document.title` constante e CSS |
| ASVS 2.1.12 | Pass | `PasswordField.tsx`: botão "Mostrar senha" alterna o tipo do campo |

Ferramentas: `dotnet test` 2.835/2.835; build Release com 0 avisos; NuGet 0; segredos 0 nas linhas acrescentadas; `verify-guard.py` 0 falhas. Não executados: CodeQL, gitleaks, `npm audit` (dependências sem mudança), frontend, E2E. Não observado: evento 1008 com `cause` em log real.

Critical/High `Open` ou `Confirmed`: 0. Não certifico conformidade com a LGPD; esta verificação não muda o tratamento de dados pessoais.

## Findings
Registrados no ledger compartilhado `docs/specs/conta-jogador/findings-ledger.md` (fase 7, SEC-01..SEC-19), não duplicados aqui.

Resumo após a re-revisão de 2026-10-31: Fixed 5 - SEC-01 (High); SEC-02, SEC-03, SEC-04 (Medium); SEC-15 (Low). Open 14 - Medium 2 (SEC-05; SEC-06 parte b); Low 12 (SEC-07..SEC-14, SEC-16..SEC-19). Critical/High `Open` ou `Confirmed`: 0.

Resumo após a verificação de fechamento de 2026-10-31 (SEC-01..SEC-21): Fixed 7 - SEC-01 (High); SEC-02, SEC-03, SEC-04, SEC-05, SEC-06 (Medium); SEC-15 (Low). Open 14, todos Low - SEC-07..SEC-14, SEC-16..SEC-21 (SEC-21 com risco aceito pelo usuário). Critical/High `Open` ou `Confirmed`: 0. Medium SEC `Open`: 0.

## Compliance (LGPD)
Não certifico conformidade com a LGPD; o que foi possível verificar no código:
| Tema | Situação |
|---|---|
| Minimização e pseudonimização | Pass: IP e email de rate limit só como HMAC; tokens só como SHA-256; logs só com identificadores internos; EXIF removido do avatar |
| Eliminação a pedido do titular (art. 18, VI) | Pass: exclusão atômica (ADR-0006), testes `REQ024_*`/`REQ027_*` |
| Acesso e portabilidade (art. 18, II e V) | Lacuna: sem mecanismo; email não exibido ao titular (SEC-13) |
| Retenção | Parcial: rate limit 24 h e limpeza de sessões/pendências implementadas; logs (proposta 30 dias) pendentes (fase 8, D12/D13); backups decididos em 2026-10-31 pelo usuário (DB12: 30 dias, reaplicação das exclusões após restauração), requisito documentado, procedimento não implementado, verificação manual H-10 |
| Evidência de aceite dos termos | Pass: versão, data/hora e 18+ na linha do Jogador (REQ-048); removida na exclusão por decisão pendente de validação jurídica (D12/D13) |
| Segurança do tratamento (art. 46) | Parcial: SEC-01..SEC-06 corrigidos (SEC-05 com resíduo SEC-20); abertos só os Low |
| Encarregado, base legal, política de privacidade, registro de operações | Não verificável no código |

## DB-guard integrity check
- `.claude/settings.json` deny rules unchanged: Yes. O único commit da feature que tocou o arquivo (`70cb64e`) só acrescentou hooks consultivos do grafo (`SessionStart`, `PreToolUse` em `Grep|Glob`) e removeu `enabledMcpjsonServers`; a lista `permissions.deny` e o registro do `block-db-access.py` não mudaram. Sem alteração não commitada em `.claude/`.
- `.claude/hooks/block-db-access.py` present and blocking: Yes. Arquivo e `db_patterns.json` sem mudança desde o scaffold (`f5306a5`/`eaa42d9`); `verify-guard.py` 22 PASS, 0 falhas; o hook bloqueou quatro chamadas desta revisão (comportamento esperado). Re-revisão 2026-10-31: `.claude/` sem diff em relação ao HEAD; `verify-guard.py` 0 falhas. Fechamento 2026-10-31: idem (sem diff em `.claude/`, 0 falhas).

## Não verificado
- Comportamento em execução de: SEC-04 (antiforgery por HTTP atrás do nginx; coberto por teste de API, não pelo compose), SEC-18 (recusa do cookie `__Host-` sem `Secure` pelo navegador, inferida da regra do prefixo), garantia do SEC-01 em PostgreSQL (teste paralelo só com store em memória), DNS do compose (R-31), cabeçalhos em produção - não subi o compose nem a API. Limite de corpo do nginx verificado só estaticamente.
- Itens da fase 8: TLS/HSTS de produção, proteção e retenção de logs, backups (retenção decidida em 2026-10-31 pelo usuário, DB12; procedimento de restauração com reaplicação das exclusões não implementado nem verificado, conferência manual H-10), contêiner somente leitura.
- Componentes de UI não relidos (2.1.12).
- Nenhuma ferramenta SAST disponível para rodar localmente.

## Sign-off
No `Open` or `Confirmed` Critical/High row in `findings-ledger.md` before this is marked Approved. Estado após a re-revisão de 2026-10-31: nenhuma linha Critical/High `Open` ou `Confirmed` (SEC-01 `Fixed`). O relatório permanece Draft até a decisão do orquestrador; seguem abertos SEC-05 e SEC-06 (b) (Medium) e Low.
Estado após a verificação de fechamento de 2026-10-31: nenhuma linha Critical/High `Open` ou `Confirmed`; nenhuma linha Medium SEC `Open` (SEC-05 e SEC-06 `Fixed`); abertos só Low SEC (SEC-07..SEC-14, SEC-16..SEC-21). Relido nesta verificação: todas as categorias V1..V14 têm linhas preenchidas, sem texto de modelo; os itens `Não verificado` (2.1.12, 7.3.3/7.3.4, 9.1.1, 14.4.5) seguem declarados como tal. O relatório continua Draft: a aprovação e o gate são do orquestrador e do usuário, e o nível ASVS alvo (L2) ainda pede confirmação.
