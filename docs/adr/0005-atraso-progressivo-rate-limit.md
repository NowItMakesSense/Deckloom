# ADR-0005: Atraso progressivo (rate limit) com contadores persistidos no PostgreSQL por chaves HMAC, teto de 10 s

- Status: Accepted (2026-10-23); emendada em 2026-10-31 (ver "Emenda 2026-10-31 (SEC-01)"); notas de 2026-11-01 (ver "Nota 2026-11-01 (SEC-02, SEC-19)" e as notas SEC-16 e T-144 em E5)
- Date: 2026-10-22
- Deciders: architect (proposta), aprovação do usuário no gate da fase 3 (feature `conta-jogador`)

## Context

REQ-032 exige atraso progressivo crescente por IP e por conta em login e em solicitação de redefinição de senha, sem bloquear a conta, de modo que o Jogador legítimo entre após o atraso. O domínio estende a regra ao informe de senha no vínculo Google (REQ-036) e na exclusão de conta. Decisões do usuário na fase 3: dados de rate limit (IP, hash da chave de conta) retidos por cerca de 24 h e apagados na exclusão da conta; aviso "aguarde" na UI após 2 s, sem dica de tempo devolvida pelo servidor. O p95 de 3 s exclui o atraso deliberado. O teto do atraso e o timeout do cliente ficaram para o architect.

## Decision

1. **Registro de eventos** em `accounts.access_attempt`: cada tentativa contável grava `action` (`login`, `password_reset_request`, `google_link`, `account_deletion`, `registration`), `ip_key_hash`, `account_key_hash` e `attempted_at`. Login, vínculo e exclusão contam **falhas**; solicitação de redefinição conta **toda solicitação**; cadastro conta **recusas por email em uso** só pela chave de IP (mitigação da exceção de enumeração de REQ-002).
2. **Chaves**: `ip_key_hash = HMAC-SHA256(segredo, IP)` e `account_key_hash = HMAC-SHA256(segredo, email normalizado)`, calculadas exista ou não a conta (anti-enumeração, T3). O segredo vem de configuração secreta. Nenhum IP nem email em claro é persistido.
3. **Janela**: contam os eventos dos últimos 15 min por chave. **Atraso** = máximo entre o atraso da chave de conta e o da chave de IP:

   | Eventos na janela (conta) | Eventos na janela (IP) | Atraso |
   |---|---|---|
   | 0 a 2 | 0 a 9 | 0 s |
   | 3 | 10 | 1 s |
   | 4 | 11 | 2 s |
   | 5 | 12 | 4 s |
   | 6 | 13 | 8 s |
   | 7 ou mais | 14 ou mais | **10 s (teto)** |

   Na solicitação de redefinição, os limiares de conta e IP são os mesmos. O limiar de IP é mais alto para não punir redes compartilhadas (NAT).
4. O atraso é aplicado **antes** de processar a tentativa (inclusive se ela for bem-sucedida), com espera assíncrona (`Task.Delay` sobre `TimeProvider`, sem ocupar thread). Login bem-sucedido apaga os eventos de falha da chave de conta. Nunca há bloqueio nem resposta diferente de "credenciais inválidas"/mensagem genérica.
5. Um limitador de concorrência por IP (no máximo 4 requisições simultâneas aos endpoints de autenticação, fila de 16) evita que o atraso seja contornado por paralelismo; acima da fila, responde 503 genérico (não é bloqueio de conta).
6. **Retenção** (REQ-047): um job horário apaga eventos com mais de 24 h; a exclusão de conta recalcula o HMAC do email do Jogador e apaga, na mesma transação, todos os eventos com esse `account_key_hash`. Eventos só com chave de IP não são atribuíveis ao Jogador e saem pela retenção. Trocar o segredo HMAC exige apagar todos os eventos existentes, para que nenhum fique impossível de localizar.
7. **Timeout do cliente** para endpoints de autenticação: **20 s** (teto de 10 s + p95 de 3 s + margem). Timeout gera o erro genérico de servidor/rede, nunca "credenciais inválidas".

## Alternatives considered

- **Contadores em memória do processo**: zero IO, mas perdidos a cada reinício e incoerentes com mais de uma instância.
- **Redis**: adequado para escala, mas é mais um componente na infraestrutura sem nuvem e sem meta de throughput (D14).
- **Middleware de rate limiting do ASP.NET Core sozinho**: rejeita com 429 (bloqueio temporário), não atrasa, e não conhece a chave de conta.
- **Teto maior (30-60 s)**: mais proteção contra força bruta, mas pior para o Jogador legítimo e exige timeout de cliente muito longo; com 10 s, ataque a uma conta fica limitado a cerca de 6 tentativas por minuto por conta.

## Consequences

- Uma escrita por falha e duas contagens indexadas por tentativa; aceitável no volume do MVP.
- Os limiares e o teto ficam em configuração (`ProgressiveDelayOptions`), testados com relógio falso.
- Requisições em espera seguram conexões HTTP; o limitador por IP e o teto de 10 s limitam o impacto.

## Emenda 2026-10-31 (SEC-01)

- Status: Accepted (2026-10-31)
- Deciders: usuário (decisão sobre o achado SEC-01, High, fase 7: limitar por conta antes da verificação da senha, sem bloqueio de conta); architect (números e mecanismo)
- Origem: `docs/specs/conta-jogador/findings-ledger.md` (SEC-01) e `docs/specs/conta-jogador/security-report.md` (ASVS 2.2.1, 11.1.2, 11.1.4, 11.1.6)

### Contexto da emenda

O item 4 da decisão original contava os eventos, esperava o atraso e só depois o caso de uso verificava a senha e registrava a falha. N requisições simultâneas para a mesma conta (de vários IPs, contornando o limitador por IP do item 5) liam a mesma contagem, esperavam no máximo 10 s em paralelo e todas verificavam a senha: a vazão por conta não tinha teto (SEC-01 estima ~36.000 tentativas/hora). Mesmo um atacante sequencial passa do limite da ASVS 2.2.1 (cerca de 100 falhas por hora por conta): pela tabela do item 3, as 7 primeiras tentativas custam 0+0+0+1+2+4+8 = 15 s de atraso e cada uma das seguintes 10 s, o que dá cerca de 7 + (3600 - 15) / 10 ≈ 365 tentativas/hora (cerca de 350 com o custo do PBKDF2). O "cerca de 6 tentativas por minuto por conta" de "Alternatives considered" vale só para o atacante sequencial e já estava acima do limite. O teto de 10 s não pode subir sem estourar o timeout de 20 s do cliente (item 7). Logo, o atraso sozinho não limita vazão; é preciso um orçamento por conta, contado antes da verificação e de forma atômica.

### Decisão da emenda

E1. **Registro antes da verificação.** Para as ações com chave de conta (`login`, `password_reset_request`, `google_link`, `account_deletion`), a tentativa é gravada em `accounts.access_attempt` **antes** de qualquer verificação de senha (e antes do atraso), como evento provisório que já conta como falha. A gravação é imediata e durável, fora do `IUnitOfWork` (como o `RecordAsync` original). `registration` não muda (só chave de IP, contada na recusa, item 1 e REQ-041).

E2. **Contagem atômica, sem serialização em memória.** A ordem é: (1) gravar o evento provisório e obter o seu id; (2) só depois contar, por conta e por IP, os eventos das janelas, **incluindo** o próprio. Como cada contagem é feita depois que o próprio evento já está gravado e visível, a i-ésima tentativa gravada (na ordem de confirmação das gravações, cada uma confirmada antes da contagem, em `READ COMMITTED`) vê pelo menos i eventos da conta, por mais que as requisições cheguem em paralelo, de qualquer número de IPs e de qualquer número de instâncias da API. Não há janela de leitura antes da escrita (fim do TOCTOU, ASVS 11.1.6). Não se usa lock nem limitador por conta em memória.

E3. **Atraso.** O atraso continua o da tabela do item 3 (janela de 15 min, teto de 10 s), calculado com as contagens de 15 min **menos o próprio evento** (conta - 1, IP - 1), para que a tabela mantenha o significado de "eventos anteriores". Sob concorrência, cada requisição paralela vê uma contagem maior que a anterior, então a concorrência eleva o atraso em vez de compartilhá-lo.

E4. **Orçamento por conta: 30 tentativas contadas por chave de conta numa janela deslizante de 60 min** (`Auth:ProgressiveDelay:AccountHourlyBudget`, padrão 30; a validação das opções recusa valores menores que 1 ou maiores que 100, o limite da ASVS 2.2.1). Se a contagem de 60 min da conta, incluindo o próprio evento, passar de 30, a tentativa é **recusada sem verificar a senha** e o evento provisório é apagado (a recusa não consome orçamento, para que tentativas excedentes não estendam a recusa). Garantia: nunca há mais que 30 verificações de senha por conta em 60 min, porque, se houvesse 31 eventos admitidos na janela, o último deles teria contado pelo menos 31 e sido recusado. A corrida na fronteira só pode recusar a mais (duas requisições veem 31 e ambas desistem), nunca admitir a mais.
   Justificativa do número 30: (a) 3,3 vezes abaixo do limite de cerca de 100 por hora da ASVS 2.2.1 (no máximo 720 tentativas por dia por conta); (b) sem o orçamento, a tabela atual admite ~350 por hora mesmo para um atacante sequencial; (c) com a tabela atual, um atacante sequencial chega a 30 em cerca de 15 s + 23 x 10 s ≈ 4 min e fica sem orçamento pelo restante da hora; (d) um Jogador legítimo, a partir da 8ª falha em 15 min, já espera 10 s por tentativa, então chegar a 30 tentativas contadas em uma hora não é uso humano plausível; o login bem-sucedido continua apagando os eventos da conta (item 4), o que zera o orçamento; (e) o contador é único por chave (sem filtro por ação, como no item 3), então as solicitações de redefinição também consomem o orçamento, e 30 deixa folga para um Jogador que erra a senha algumas vezes e pede redefinição.

E5. **Desfecho do evento provisório.**
   - Falha de credencial (login, vínculo, exclusão): o evento provisório já é a falha; o caso de uso **não** grava um segundo evento.
   - Login bem-sucedido: apaga os eventos da conta, como no item 4 (inclui o provisório).
   - Vínculo Google bem-sucedido: apaga só o próprio evento provisório (pelo id).
   - Exclusão efetivada: os eventos da conta já são apagados na transação da exclusão (item 6).
   - Solicitação de redefinição com email válido: o evento fica (a ação conta toda solicitação, item 1).
   - Erro de validação que hoje não conta (campo obrigatório ausente, e nos casos de uso que já tratam assim também senha acima de 128 caracteres ou email malformado): apaga só o próprio evento provisório.
   - Cancelamento (cliente desconectou durante o atraso), timeout ou exceção: o evento **fica** e conta como falha. Isso impede que um atacante cancele a requisição depois de verificar a senha para não ser contado.
   - Nota 2026-11-01 (SEC-16, decisão do usuário): uma exclusão compensatória **já decidida** (apagar o próprio evento na recusa por orçamento de E4, no erro de validação ou no vínculo Google bem-sucedido) roda sem o token de cancelamento da requisição, para que o cliente fechar a conexão entre a gravação e a exclusão não deixe o evento contando (o que quebraria a garantia de E4 de que a recusa não consome orçamento). O cancelamento **antes** do desfecho continua como no item anterior: o evento fica e conta.
   - Nota 2026-11-01 (T-144, decisão do usuário): a gravação do evento provisório (E1) e a sua exclusão pelo id (E4/E5) são imediatas e fora do unit of work, e passam a ser **proibidas** com uma unidade de trabalho (`ExecuteAsync`) aberta no escopo: `RecordAsync` e `DeleteByIdAsync` do `IAccessAttemptStore` lançam `InvalidOperationException` nesse caso, no fake e no adapter EF. A mesma regra vale para `DeleteOlderThanAsync` (retenção do item 6, imediata, em transação própria no EF), por decisão do usuário em 2026-11-01. Motivo: dentro de um `ExecuteAsync` que falha, o EF desfazia essas operações (mesma transação) e o fake não; o evento provisório não pode depender de commit nem de rollback. Detalhe em `docs/specs/conta-jogador/design.md`, "Forma do AccessThrottle" (emenda SEC-01).

E6. **Sem bloqueio de conta (D5 mantido).** Não existe estado de bloqueio na conta, nem desbloqueio manual, nem email de desbloqueio. O orçamento é uma janela deslizante por chave: libera sozinho, à medida que os eventos admitidos saem da janela de 60 min, e vale igual para todas as chaves de conta. A tabela de atraso e o teto de 10 s não mudam.

E7. **Sem enumeração.** A chave de conta continua sendo o HMAC do email normalizado, exista ou não a conta (item 2); no login e na solicitação de redefinição, o orçamento, a gravação antecipada e a recusa acontecem antes de qualquer consulta ao Jogador, então a resposta (corpo, código e tempo) é a mesma para conta existente e inexistente. No vínculo Google (email da pendência) e na exclusão (Jogador da sessão) a conta já é conhecida pelo chamador; a regra é a mesma. Respostas de quem passa do orçamento:
   - `login`, `google_link`, `account_deletion`: `429` com código `too_many_attempts` e mensagem genérica, sem `Retry-After` nem outra dica de tempo (decisão U14 mantida), aplicada depois do atraso calculado em E3, como qualquer outro desfecho.
   - `password_reset_request`: o mesmo `202` de sempre, sem emitir token nem enviar email (a resposta de uma solicitação já não revela se a conta existe).
   - Ajuste 2026-10-31 (SEC-01, decisão do usuário): em `account_deletion`, o orçamento vale só para a exclusão com senha. A exclusão de conta só Google (sem senha local) não reserva evento nem conta no orçamento e nunca devolve `429 too_many_attempts`; segue protegida pela confirmação e pela reautenticação Google recente (REQ-042).

E8. **Limitador por IP (item 5) mantido** como proteção de conexões; deixa de ser a defesa da vazão por conta, que passa a ser o orçamento de E4.

E9. **Observabilidade.** Novo evento de log `AccessBudgetExceeded` (action, sem chave nem IP) e atributo `outcome=budget_exceeded` nas métricas de autenticação; o evento `ProgressiveDelayApplied` continua igual.

### Alternativas consideradas na emenda

- **Limitador de concorrência particionado por conta (1 em andamento, fila curta), em memória do processo**: serializa só dentro de uma instância, exige ler o email do corpo num middleware e, sozinho, ainda admite ~350 tentativas por hora por conta (o atraso de 10 s não chega a 36 s por tentativa). Rejeitado como mecanismo principal.
- **Lock consultivo do PostgreSQL (`pg_advisory_xact_lock`) por chave de conta em volta de contar e gravar**: serializa entre instâncias, mas exige transação curta extra em cada tentativa, sujeita ao `lock_timeout` de 2 s (DB9), e acrescenta ao papel de runtime uma dependência que a matriz de privilégios de `Database/roles/` não cobre hoje. A ordem "gravar e depois contar" (E2) dá a mesma garantia de teto sem lock e só com os privilégios atuais (`SELECT`, `INSERT`, `DELETE`).
- **Subir o teto do atraso até ~36 s por tentativa (100 por hora só com atraso)**: estoura o timeout de 20 s do cliente e o p95 percebido; e, sem contagem atômica, a concorrência continuaria contornando o atraso.
- **Responder `401 invalid_credentials` acima do orçamento**: esconderia o limite, mas diria "credenciais inválidas" a um Jogador legítimo com a senha certa durante um ataque, levando-o a pedir redefinição sem motivo. Rejeitado em favor de `429 too_many_attempts` genérico.
- **Bloqueio temporário de conta com desbloqueio por email**: contraria D5.

### Consequências da emenda

- Vazão máxima de verificações de senha por conta: 30 por 60 min, independentemente de quantos IPs ou instâncias; o atraso progressivo continua valendo dentro do orçamento.
- Risco residual (aceito por D5, a confirmar no texto de REQ-032): sob ataque contínuo a uma conta, o atacante consome o orçamento e o Jogador legítimo recebe `too_many_attempts` até a janela de 60 min liberar; não há estado de bloqueio, mas há negação temporária do login com senha para aquela conta. O login com Google (quando vinculado) não passa pelo orçamento. Como o contador é único por chave, basta ao atacante conhecer o email e fazer cerca de 30 requisições por hora (inclusive solicitações de redefinição, que não exigem senha) para manter essa negação; separar o orçamento por ação mudaria o item 3 e fica como decisão do usuário se o risco não for aceito. REQ-032 diz "de modo que o Jogador legítimo ainda consiga entrar após o atraso"; o texto precisa de emenda aprovada pelo usuário para refletir o orçamento.
- Uma escrita por tentativa (antes, uma por falha) e três contagens indexadas (conta 15 min, IP 15 min, conta 60 min), servidas pelos índices existentes `ix_access_attempt_account_key` (`account_key_hash, attempted_at`) e `ix_access_attempt_ip_key`. Sem mudança de esquema nem de privilégios. A retenção de 24 h cobre a janela de 60 min.
- Contrato novo na API e na UI (`429 too_many_attempts` em login, vínculo Google e exclusão), com mensagem genérica a aprovar.

## Nota 2026-11-01 (SEC-02, SEC-19): chave de IP normalizada

- Origem: achados SEC-02 (corrigido no código em 2026-10-31) e SEC-19 (falta de emenda de spec) em `docs/specs/conta-jogador/findings-ledger.md`; emenda aprovada pelo usuário na triagem do retro (`docs/specs/conta-jogador/retro.md`, 2026-11-01). Esta nota não reescreve a decisão original; esclarece o que é o "IP" dos itens 2 e 5.
- No item 2, `ip_key_hash = HMAC-SHA256(segredo, IP)` passa a ler-se `HMAC-SHA256(segredo, chave de IP)`, e a partição do limitador do item 5 usa a mesma chave. A chave é produzida num único ponto da API (`ClientIpKey`, depois do `UseForwardedHeaders` da ADR-0016): IPv4 inteiro; IPv4 mapeado em IPv6 tratado como o IPv4; IPv6 truncado no prefixo `/64` (texto `<prefixo>/64`); sem endereço de conexão, a constante `unknown`.
- Motivo: com o endereço IPv6 completo, um cliente que controla um `/64` (caso comum) usa um endereço novo por requisição e anula o limiar de IP do item 3, o limitador do item 5 e a mitigação de enumeração no cadastro (REQ-041).
- Consequência aceita: clientes distintos no mesmo `/64` compartilham a chave de IP, como clientes atrás do mesmo NAT no IPv4 (o limiar de IP mais alto do item 3 já existe para isso). Nenhuma mudança de esquema, de privilégios ou de limiares; trocar a regra de truncamento muda as chaves gravadas, que expiram pela retenção de 24 h.
- Detalhe em `docs/specs/conta-jogador/design.md`, "Política de atraso progressivo > Chave de IP normalizada".
