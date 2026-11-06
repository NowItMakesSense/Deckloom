# Notas de lançamento: conta-jogador

Status: fase 8 confirmada pelo usuário em 2026-11-05. O código está na `main` (PR #2 e PR #3). **Nada foi implantado em produção, publicado nem marcado (tag).** A decisão de lançamento e a execução do checklist abaixo são de pessoas da equipe.

## Rodada pós-retro (2026-11-04 e 2026-11-05)

Escopo: tarefas T-140 a T-184 de `tasks.md`, todas `done`, mergeadas na `main` pelos PRs #3 e #4. Nada foi implantado em produção, e nenhuma versão ou data de release foi definida.

### Mudanças para quem instala ou opera

Antes de subir a API ou o compose local, conferir os itens 1 e 2: eles podem impedir o start.

1. **SMTP (pode impedir o start, SEC-22, T-179).** `Email:Smtp:User` preenchido com `Email:Smtp:UseTls=false` derruba o start em qualquer ambiente, inclusive `Development`. A mensagem nomeia só as chaves. No compose local com Mailpit, `Email__Smtp__User` e `Email__Smtp__Password` ficam vazios (README, seção "Executar localmente com Docker Compose").
2. **Proxies confiáveis sem índice (pode impedir o start, R-36, T-149).** `Forwarding:TrustedProxies` com valor escalar, sem `__0`, derruba o start. Antes caía em silêncio no padrão de loopback. Use a forma indexada, `Forwarding__TrustedProxies__0`, `__1`, ... A mensagem nomeia a chave e o índice, nunca o valor.
3. **Evento 1008 com `cause` (R-57, T-178 e T-181).** Campo de lista fechada: `connect`, `tls`, `auth`, `rejected`, `timeout`, `other`. O log não recebe senha SMTP, email, link nem texto do servidor. Quem tem alerta ou painel sobre o 1008 precisa conhecer o campo (item 21 do checklist).
4. **Evento 1010 com `session_invalid` (R-41, T-151).** A reautenticação Google com sessão ausente, expirada, revogada ou de outro Jogador registra `reason=session_invalid`. Antes registrava `subject_mismatch`. Quem filtra por `subject_mismatch` vê menos esses casos. O desfecho para o usuário não muda.
5. **Compose local (SEC-17, T-153).** Web em `http://localhost:8081` (antes `8080`) e Mailpit em `http://localhost:8025`, ambos publicados só em `127.0.0.1`. Não vale para produção.
6. **JSON com `Content-Type` diferente (SEC-10, T-148).** Os endpoints que leem JSON respondem `415` (`unsupported_media_type`) quando há corpo sem `application/json`. Clientes precisam enviar o cabeçalho.
7. **Login Google (R-39, T-150).** O `iss` é aceito com e sem esquema, derivado de `Auth:Google:Authority`. Outros emissores seguem recusados.

### Correções visíveis

- Telas e diálogos S1 a S11 refluem a 320 px com texto a 200%, sem rolagem horizontal, exceto as duas exceções abaixo.
- Campo de senha: o botão "Mostrar senha" desce de linha e a senha segue legível.
- `<h1>` com 24 px calculados a 100%.
- Título da aba por tela: `/perfil`, `/perfil/editar`, fluxo Google e link inválido de redefinição.
- Avisos de reautenticação (S11) e de sessão expirada (S2) são anunciados após a montagem. A caixa do S11 recebe o foco só com o texto já na região descrita.

### Exceções aceitas pelo usuário (2026-11-04)

- **R-63:** o controle nativo `input type="file"` do S10, a 320 px com texto a 200%, pode mostrar o texto interno cortado com reticências. O nome do arquivo escolhido segue em `.file-upload__name`. Registrado em `design.md`, A.8.5.
- **R-64:** no `.dialog__panel` dos diálogos S10 e S11, palavras longas podem quebrar no meio, para o painel não rolar na horizontal. Registrado em `design.md`, A.8.5.

### Evidências da rodada

Números registrados nos documentos da feature. Não foram reexecutados para esta nota.

- Backend: `dotnet test` 2.842/2.842 (orquestrador, HEAD `8534637`, 2026-11-05).
- Frontend: 1.021/1.021 e cobertura de linhas de 98,99%. Backend: cobertura de linhas de 93,67% (`Accounts.Infrastructure`, o menor). Ambas medidas em `8534637`; limite de 80% atendido.
- E2E: 215/215 no CI do PR #3 (run #16, `8c486d8`), em Ubuntu, e localmente; antes, 4 rodadas completas com 212/212 (`qa`, T-180).
- CI: todos os jobs verdes no run #16 do PR #3, a primeira execução verde do repositório. Na `main`, o run #17 do merge do PR #3 falhou no CodeQL completo (SEC-28, SEC-29), tratado no PR #4; o CI da `main` depois do merge do PR #4 ainda precisa ser conferido.
- `check-traceability.py`: 0 falhas. `dotnet format`, lint, `format:check` e builds sem falhas (2026-11-04).
- Segurança (2026-11-05): NuGet com 0 pacotes vulneráveis; `verify-guard.py` com 0 falhas; CodeQL com análise completa (`csharp` e `javascript-typescript`) com 0 resultado ativo no run #18 do PR #4, depois de 2 falsos positivos tratados sem exceção no gate (SEC-28, SEC-29); gitleaks sem vazamento, com 2 falsos positivos ignorados por fingerprint (SEC-27).
- Ledger (2026-11-05): Critical/High `Open` ou `Confirmed`: 0.

### Não verificado e riscos abertos

- **QA-21 (Low, `Open`):** falha intermitente no E2E. A ocorrência do S8 teve a causa achada e corrigida; seguem sem causa `ERR_CONNECTION_REFUSED` no Vite, timeout e URL antiga no S4. Pode deixar o CI vermelho sem defeito de produto.
- **CI lento por `apt`:** a instalação do Chromium chegou a 13 min no run #16, por lentidão do espelho do Ubuntu. Vale cache, se virar rotina.
- **SEC-23 (Low, backlog):** o log do EF Core não tem filtro próprio, e `Include Error Detail` não é recusado pelo validador. Nenhum vazamento na amostra de log real.
- **CodeQL sem a aba de alertas:** repositório privado de conta pessoal, sem Code Scanning. O SARIF fica como artefato por 14 dias e o job falha com resultado ativo (SEC-26). A varredura do gitleaks no PR cobre os commits do PR, não todo o histórico.
- **Leitor de tela real** (NVDA e VoiceOver): não verificado. U-02 e U-04 seguem só com a árvore de acessibilidade do Chromium.
- **Login Google real:** não verificado. Os testes usam stub.
- **`cause=tls` (R-57, R-67):** provado em `8534637` pelo `security` com o transporte real contra servidor falso em loopback (STARTTLS aceito e resposta inválida): resulta em `Tls`. Certificado recusado e evento 1008 em log real do compose não foram verificados.
- **Evento 1008 com `cause` em log real:** não observado.
- **Demais Lows:** seguem como estão no `findings-ledger.md` e no `security-report.md`.

## Escopo da documentação

- **Incluído:** `README.md` (execução local, testes, configuração de produção, papéis do banco, referência à validação manual H-1..H-10), `CHANGELOG.md` (uma entrada, em Adicionado, Alterado e Segurança) e este checklist operacional.
- **Não incluído agora, e por quê:**
  - Runbook de incidentes e guia de rollback detalhado: não há tráfego de produção ainda. Ficam para o primeiro lançamento, quando os alertas já estiverem ativos (ver checklist, item 6 e 13).
  - Documentação pública da API (OpenAPI): não está especificada na feature.
  - Textos legais: dependem de validação jurídica (D12/D13); a documentação técnica não pode antecipar essa decisão. A retenção de backups (30 dias, com reaplicação de exclusões na restauração) está registrada no checklist, item 7.

## Evidências desta fase (medidas nesta sessão)

Comandos executados na raiz do repositório, na branch `feat/conta-jogador` com as alterações ainda não commitadas.

| Verificação | Resultado |
|---|---|
| `dotnet format backend/Deckloom.sln --verify-no-changes` | exit 0 |
| `dotnet build backend/Deckloom.sln -c Release` | exit 0 |
| `dotnet test` com coverlet (Release, `backend/coverlet.runsettings`) | 2408 aprovados de 2408; Application 679, Domain 606, Infrastructure 446, Api 513, Arquitetura 150, DeckSuggestion.Infrastructure 14 |
| Portão de cobertura (`scripts/ci/check_coverage.py`, limite 80) | OK. Linhas: Application 98,04%; Domain 99,60%; Accounts.Infrastructure 93,46%; Api 95,55%; DeckSuggestion.Infrastructure 100%; SharedKernel 100% |
| `npm --prefix frontend run lint` e `format:check` | exit 0 |
| `npm --prefix frontend run build` | exit 0 |
| `npm --prefix frontend run test -- --run --coverage` | 924 aprovados de 924; linhas 98,98%, instruções 98,00%, funções 98,83%, ramos 91,54%; limite 80 em `frontend/vite.config.ts` respeitado |
| `npm --prefix frontend audit --omit=dev` | 0 vulnerabilidades |
| `dotnet list ... package --vulnerable --include-transitive` | nenhum pacote vulnerável (saída JSON) |
| `python .claude/hooks/verify-guard.py` | 0 falhas |
| `python .claude/scripts/check-traceability.py` | 0 falhas |

**Não verificado nesta fase:**

- Suíte E2E Playwright (`npm --prefix frontend run e2e`): não executada aqui. Último número registrado pelo QA: 164/164 em 3 execuções (ledger).
- Workflow `.github/workflows/ci.yml` executado no GitHub: só foi validado como YAML e por conferência dos SHAs das ações com as tags do repositório remoto. Não houve execução real.
- gitleaks: à época, não executado; depois, no CI do PR #3, sem vazamento (ver "Rodada pós-retro").
- SAST (CodeQL): à época, não executado; depois, no CI do PR #3, com 0 resultado ativo (ver "Rodada pós-retro").

## Achados abertos (ledger e relatório de segurança)

Não há achado Critical ou High `Open` ou `Confirmed` no `docs/specs/conta-jogador/findings-ledger.md` nem no `docs/specs/conta-jogador/security-report.md`. Abertos, para decisão do usuário antes do lançamento:

- **Medium (ledger):** R-18 (a limpeza de retenção carrega a tabela inteira em memória; o STATUS da feature diz que foi corrigida com mapeamento UTC aprovado, então confirmar o estado da linha no ledger) e R-21 (`asyncUtilTimeout` de 4000 ms sem causa raiz fechada).
- **Low:** 17 itens do ledger de revisão (conforme o resumo do reviewer) e 14 do relatório de segurança (SEC-07..SEC-14, SEC-16..SEC-21). Listas completas no `docs/specs/conta-jogador/findings-ledger.md`.
- **Nível ASVS alvo L2:** ainda pede confirmação do usuário (`docs/specs/conta-jogador/security-report.md`, sign-off).

## Checklist operacional antes de produção

Cada item deve ser confirmado por uma pessoa e registrado com data e responsável. Nenhum deles foi feito por agente.

1. **TLS e HSTS na borda.**
   - Certificado válido e TLS restrito no reverse proxy.
   - A API envia `Strict-Transport-Security: max-age=31536000` fora de `Development`, mas o nginx da SPA não envia HSTS. Confirmar que a borda envia HSTS também nas respostas estáticas e definir `includeSubDomains` e `preload` (decisão humana).
   - Responsável: infraestrutura.
2. **Lista de proxies confiáveis.**
   - `Forwarding:TrustedProxies` com o IP ou CIDR exato do reverse proxy, sem faixas públicas.
   - O proxy deve sobrescrever `X-Forwarded-For` com o IP real do cliente, e não repassar o valor enviado pelo cliente.
   - Teste manual: uma requisição vinda de fora da lista não pode influenciar o IP usado nos limites (ADR-0016).
   - Responsável: infraestrutura.
3. **Configuração SMTP.**
   - `Email:Smtp:UseTls=true` (obrigatório fora de `Development`), host e porta do provedor, usuário e senha de autenticação quando o provedor exigir.
   - Domínio de `Email:From` verificado no provedor (SPF, DKIM e DMARC).
   - Os valores ficam no gerenciador de segredos da plataforma, nunca no repositório.
   - Com `Email:Smtp:User` preenchido, `UseTls=true` é obrigatório em qualquer ambiente; a API não sobe de outro modo (SEC-22).
   - Responsável: operação e responsável pelo email.
4. **Papéis e privilégios do banco (ADR-0015).**
   - Papéis criados por uma pessoa: `postgres` só para bootstrap; `deckloom_owner` NOLOGIN; `deckloom_migrator` LOGIN só para humanos; `deckloom_app` LOGIN para a API.
   - Senhas definidas fora do repositório e com rotação planejada.
   - `ConnectionStrings:Deckloom` da API usa `deckloom_app` com `SSL Mode=VerifyFull` e certificado raiz. A API recusa outro papel e outro `SSL Mode` fora de `Development`.
   - A configuração da API não contém credencial do migrador nem do superusuário.
   - Executar H-1 a H-10 (`docs/specs/conta-jogador/tasks.md`, seção "Validação manual por humano") e registrar o resultado de cada um.
   - Responsável: banco (pessoa com acesso ao servidor).
5. **Revisão humana das migrações.**
   - Migrações geradas como arquivos e revisadas (incluindo os `GRANT` de cada tabela).
   - `dotnet ef migrations add` não pode gerar diff em relação ao snapshot editado. Se gerar, o snapshot está desatualizado e o release para. Antes de rodar, ler a pendência registrada no STATUS (incidente de 2026-10-25: a fábrica de tempo de design tentou resolver um host fictício; nenhuma conexão foi feita, mas a fábrica precisa de revisão).
   - Aplicação feita por uma pessoa com `deckloom_migrator`, nunca pela API na inicialização, e nunca por agente ou pelo CI.
   - Responsável: dev revisor e pessoa que aplica.
6. **Alertas de banco (observabilidade).**
   - `db.command_failures{sqlstate=57014}`, `{sqlstate=55P03}` e `{sqlstate=53300}`: alerta quando passarem de 5 em 15 minutos (design.md, linha 1683), ligados ao evento 1021 `DatabaseCommandFailed`.
   - `db.command_failures{sqlstate=42501}`: alerta imediato, porque indica privilégio faltando após uma migração.
   - O repositório só expõe as métricas; a regra de alerta e a rota para plantão são configuradas fora do código. Confirmar que existem.
   - Responsável: operação.
7. **Backups e LGPD (DB12).**
   - Retenção dos backups do banco: **30 dias** (decisão do usuário, em 2026-10-31). Uma conta excluída pode continuar em backups por até 30 dias após a exclusão.
   - Restauração de backup: antes de liberar o acesso à API, as exclusões de conta feitas depois da data do backup são reaplicadas. Sem essa etapa concluída, a restauração não é liberada. Assim, o restore não traz de volta uma conta já excluída.
   - A reaplicação é registrada em log sem dado pessoal: somente a data de corte do backup e a quantidade de exclusões reaplicadas. Não registrar e-mail, nome, identificador de conta nem IP.
   - Este é um requisito documentado. Nenhum procedimento ou script foi escrito nem executado, e nenhum banco foi acessado. O procedimento de restauração deve ser criado e revisado por uma pessoa antes do primeiro uso.
   - Ainda pendentes (fase 8, com D12/D13): criptografia dos backups, papel ou mecanismo de backup (nenhum papel da ADR-0015 tem privilégio de backup) e forma de informar o titular.
   - Retenção de logs (proposta de 30 dias no design) confirmada.
   - Validação jurídica dos Termos de Uso e da Política de Privacidade (D12/D13) e texto final publicado. Pendente.
   - Canal e responsável pelo atendimento de titulares (acesso e portabilidade, art. 18, II e V): hoje não há mecanismo de acesso e portabilidade (ver `docs/specs/conta-jogador/security-report.md`, LGPD).
   - Responsável: produto e jurídico.
8. **Contêiner somente leitura.**
   - O compose de desenvolvimento não define `read_only`. A API roda com usuário `app` (Dockerfile), mas o sistema de arquivos não é somente leitura.
   - Em produção, configurar `read_only: true`, com `tmpfs` só para os diretórios que precisarem de escrita, e confirmar que o volume das chaves do Data Protection é o único ponto de escrita persistente.
   - Responsável: infraestrutura.
9. **Chaves do Data Protection.**
   - Volume persistente das chaves, usado pelo cookie de correlação do login Google.
   - Com mais de uma instância da API, é preciso um armazenamento compartilhado de chaves. Pendente (T-085).
   - Responsável: infraestrutura.
10. **Imagens base fixadas por digest.**
    - `dotnet/sdk:10.0`, `dotnet/aspnet:10.0`, `node:24.11-alpine` e `nginx:1.29-alpine` ainda estão por tag móvel. Fixar por `@sha256` (pendência do STATUS) e confirmar as tags no registro, que não foram verificadas.
    - Responsável: infraestrutura.
11. **Cliente OAuth do Google por ambiente.**
    - Cliente próprio para produção, com a origem pública e o endereço de retorno do callback cadastrados.
    - Os valores ficam no gerenciador de segredos da plataforma.
    - Responsável: produto e infraestrutura.
12. **Composição de Infrastructure.**
    - Confirmar no código de produção que estão ligados: store EF de pendências Google, serviço de limpeza de dados expirados, adaptador SMTP, processador de imagem e armazenamento de avatar. Os registros de STATUS mencionam essas ligações como pendentes em fases anteriores.
    - Responsável: dev.
13. **Plano de reversão.**
    - Reverter o commit não desfaz uma migração já aplicada nem os dados gravados. O plano deve dizer o que acontece com as contas criadas e os avatares gravados.
    - O checklist padrão está em `.claude/skills/orchestrate/references/revert-checklist.md`. Preencher para este release.
    - Responsável: orquestrador e usuário.
14. **Achados abertos (decisão do usuário).** Decidir se R-18 e R-21, e os Low listados, bloqueiam o lançamento.
15. **Nível ASVS e relatório de segurança.** Confirmar o nível alvo L2 e a aprovação do `docs/specs/conta-jogador/security-report.md`.
16. **Documentação de decisões.** Confirmar que as ADRs 0001 a 0016 estão com o status correto (ADR-0002 aparecia como `Proposed` em uma lista do design.md, pendente de correção).
17. **Compose de desenvolvimento.** O compose publica `127.0.0.1:8081` e `127.0.0.1:8025`, só no loopback (SEC-17, T-153). Não vale para produção, mas deve ficar ressalvado no README (já está).
18. **Pins de dependências.** SEC-11 segue aberto. As ações do GitHub foram fixadas em SHA de commit, conferidos com as tags do repositório remoto no dia da verificação. Não houve verificação de assinatura nem de conteúdo das ações. As imagens Docker ainda não estão fixadas (item 10).
19. **SAST.** Job `codeql` no CI para C# (build manual com `dotnet build backend/Deckloom.sln -c Release`) e JavaScript/TypeScript (sem build), sem upload e com gate por `scripts/ci/check_sarif.py`. Primeira execução confirmada no PR #3, com 0 resultado ativo.
    - Responsável: dev revisor.
20. **Varredura de segredos.** O gitleaks roda no CI, com primeira execução confirmada no PR #3. Se o repositório passar a pertencer a uma organização, o gitleaks pode exigir licença; isso não foi verificado.

21. **Eventos de log alterados nesta rodada.**
   - O evento 1008 (`PasswordResetEmailFailed`) traz `cause`, de lista fechada: `connect`, `tls`, `auth`, `rejected`, `timeout`, `other`.
   - O evento 1010 registra `reason=session_invalid` na reautenticação com sessão inválida, antes registrado como `subject_mismatch`.
   - Revisar alertas e painéis que usam esses eventos.
   - Responsável: operação.

## Perguntas abertas para o usuário

1. Como as migrações são aplicadas no ambiente local de desenvolvimento? O README diz que o procedimento ainda não está documentado.
2. A retenção de logs (proposta de 30 dias) e os pontos de backup ainda pendentes do DB12 (criptografia, papel de backup, aviso ao titular).
3. R-18 e R-21 bloqueiam o lançamento, ou são aceitos com prazo?
4. Confirmar o nível ASVS L2 como alvo.
