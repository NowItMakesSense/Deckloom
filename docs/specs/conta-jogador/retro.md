# Retro: conta-jogador

Date: 2026-11-01 | Facilitated by: orchestrator

Escrita pelo orquestrador a partir de STATUS.md, findings-ledger.md, release-notes.md e dos testes manuais de 2026-11-01; itens "Carried forward" e triagem aprovados pelo usuário na mesma data.

## What went well
- Rastreabilidade e cobertura fecharam sem exceção: `check-traceability.py` com 0 falhas, cobertura de linhas acima de 93% em todos os projetos do backend e de 98% no front (limite 80).
- A revisão de segurança achou problemas reais antes de qualquer lançamento. O principal foi o SEC-01, vazão do atraso progressivo sob concorrência, corrigido com teste de 100 tentativas paralelas.
- O guard de banco (diretiva 2) funcionou o tempo todo, inclusive barrando um comando inofensivo do próprio orquestrador. Nenhum acesso ao banco foi preciso para depurar: logs do container bastaram.
- O teste manual do usuário pegou o que a suíte não pegava. Os dois defeitos foram corrigidos com TDD (teste vermelho antes) no mesmo dia.

## What to adjust next time
- **Fakes divergiram do adaptador real sem nenhum teste notar.** O `InMemoryUnitOfWork` confirma ao fim de `ExecuteAsync`; o `EfUnitOfWork` só gravava com `CommitAsync`, que nenhum caso de uso chama. Os testes do EF sempre chamavam `CommitAsync` dentro do bloco. Resultado: nada que passasse por `ExecuteAsync` era gravado no PostgreSQL (login Google, sessões, cadastro), com 2.400+ testes verdes. Corrigido em `96e0919`.
- **Achado classificado como Low quebrava todas as mutações no ambiente local.** O SEC-18 (`__Host-dl_csrf` sem `Secure` com origem http) estava no ledger como Low e "não verificado em navegador"; na prática, todo POST respondia 403 `csrf_failed` no compose. Corrigido em `37c6df8`.
- **O host E2E escondia os dois problemas.** Ele usa adapters em memória e força `Request.Scheme = https`, então não exercita nem o EF real nem a topologia http do compose.
- **Gates aprovados sem rodar o app.** As fases 6, 7 e 8 foram aprovadas sem subir o compose e fazer um fluxo ponta a ponta no navegador; os dois defeitos só apareceram no teste manual, depois da fase 8.
- **Feature grande.** 139 tarefas numa feature só, com sessões de handoff no meio da fase 5. se vale fatiar a próxima feature em entregas menores.
- **Lows acumulados.** 30 achados Low ficaram `Open` no ledger (R-24..R-43, QA-04/07/08/11, SEC-07..SEC-21). Precisam de destino: backlog, correção ou risco aceito.

## Carried forward
Aprovados pelo usuário em 2026-11-01:

1. **Testes de contrato para cada fake de porta de persistência** (`docs/test-strategy.md`): a mesma suíte roda contra o fake em memória e contra o adaptador EF (SQLite em memória), incluindo `IUnitOfWork.ExecuteAsync` sem `CommitAsync` explícito.
2. **Smoke manual no compose antes do gate da fase 6** (`.claude/skills/orchestrate/SKILL.md`, checklist da fase): subir o compose e percorrer os fluxos principais no navegador por `http://localhost`. Achado não verificado em execução não pode ser rebaixado só por leitura de código.
3. **E2E com pelo menos um caminho contra a composição real** (EF + cookies sem forçar https), ou um teste de API que valide os `Set-Cookie` com prefixo `__Host-` (todos com `Secure`).
4. **Triagem dos 30 Lows abertos**: decidir backlog, correção ou risco aceito antes de abrir a próxima feature.
5. **Tamanho de feature**: limite sugerido de tarefas por feature no `/to-tickets`.
6. **Models & effort** (`docs/specs/STATUS.md`): sem mudança proposta.

### Aplicado em 2026-11-01 (aprovado pelo usuário: itens 1 a 5)
- Itens 1 e 3: `docs/test-strategy.md` ganhou a regra de suíte de contrato por fake de persistência e a seção "Real composition". A implementação dos testes em si é o primeiro bloco da próxima entrega (ver triagem, "Corrigir agora").
- Item 2: `.claude/skills/orchestrate/SKILL.md`: o gate da fase 6 exige smoke no compose registrado no STATUS, e achado não verificado em execução é reclassificado após o smoke.
- Item 5: `.claude/skills/to-tickets/SKILL.md`: teto de cerca de 40 tarefas por feature; acima disso, propor divisão antes do gate.
- Item 4: triagem abaixo.

### Triagem dos Lows abertos (aprovada em bloco pelo usuário em 2026-11-01; ledger atualizado)
| Destino | Achados | Motivo |
|---|---|---|
| Fechar (já resolvido) | QA-07 | `backend/coverlet.runsettings` exclui Migrations, `*.g.cs` e `obj/`; o CI usa o arquivo. |
| Fechar com resíduo no backlog | SEC-11 | Actions fixadas por SHA, auditoria de dependências e CodeQL estão no CI. Resta: imagens base por digest (`backend/Dockerfile`, `frontend/Dockerfile`). |
| Corrigir agora (pequenos, antes da próxima feature) | R-24, R-27, R-28, R-29, R-36, R-39, R-41, R-43, QA-04, QA-11, SEC-10, SEC-16, SEC-17, SEC-19 | Código morto, tipagem, teste faltando, validação de config, `iss` sem esquema do Google, motivo de log errado, a11y, flakes por timeout, `415` em JSON, token de cancelamento nas compensações, compose ligado só em `127.0.0.1`, emenda do design/ADR (SEC-02, SEC-04 e agora SEC-18). Junto: os testes de contrato fake x EF e o teste de `__Host-` (itens 1 e 3). |
| Registrar e fechar | R-30 | T-124 é guarda de regressão (sem RED por não ter código de produção), mesmo tratamento do T-052. Registrado aqui. |
| Emendar o design (decisão já sugerida) | R-26 | "Solicitar novo link" fica link (semântica de navegação); ordem do texto do S11 Google e foco após senha curta seguem o código. |
| Backlog (decisão de produto/operação, próxima feature ou pré-lançamento) | R-25, R-37, R-40, SEC-07, SEC-08, SEC-09, SEC-13, SEC-14, QA-08 | Relógio E2E por reflexão, revogar sessão anterior no novo login, cache do JWKS, chaves do Data Protection cifradas (produção), troca de senha/notificações/sessões ativas, anti-automação fora de `/auth`, acesso e exportação de dados (LGPD, depende do jurídico D12/D13), cache do avatar, validação manual H-1..H-10 com PostgreSQL/SMTP reais. |
| Risco aceito | SEC-12, SEC-20, SEC-21 | Moderates só em dependências de dev (fora do bundle); lista de senhas própria; sem medidor de força (já aceito em 2026-10-31). |

Itens 1 e 2 mudam padrões do projeto. Se forem para o `CLAUDE.md`, entram via `/amend-constitution`; `docs/test-strategy.md` e o skill podem ser editados direto, com aprovação.

## Rodada pós-retro (fechamento em 2026-11-05)
Escrita pelo orquestrador.

### O que funcionou
- O smoke no navegador real, exigido pelo item 2 acima, achou o título errado e a falha de email por configuração, antes de qualquer gate.
- A revisão de UI com a tela renderizada achou o que a medida automática não via: o campo de senha com área de cerca de um caractere (U-12, High).
- A revisão independente recusou um fechamento: o R-57 passava com fake, e o `security` provou com o MailKit real que estava errado.

### O que ajustar
- **O CI nunca tinha ficado verde, e ninguém olhou.** Os 15 primeiros runs falharam, inclusive na `main`, e as fases 6 a 8 foram aprovadas em 2026-10-31 e 2026-11-01 sem esse dado. O gitleaks e o CodeQL não entregavam resultado desde o início.
- **Gate de PR via só as linhas alteradas.** O CodeQL do PR deu 0 e a `main` achou 2 falsos positivos depois do merge (SEC-30). Código morto da T-101 estava no alvo: nada que só um teste usa deveria sobreviver à fase 5.
- **Verde no Windows não prova o CI.** A primeira execução em Ubuntu achou um teste dependente de fonte (QA-22) e dois testes com corrida (QA-23 e o S8 de QA-21).
- **Orquestrador registrou resultado sem ler o código de saída:** "E2E 212/212" foi commitado com a suíte falhando, e corrigido em seguida (`11db82f`).
- **Gate aprovado com tarefas ainda por vir:** T-178..T-182 entraram depois da aprovação das fases 6 e 7. Passaram por revisão curta, mas o gate não cobriu esse código.
- **Agentes deixaram processos no ar:** o host E2E e o Vite travaram DLLs do backend e serviram código antigo à E2E.

### Carried forward (para o próximo `/plan-grill`; ainda não aplicados)
1. Gate da fase 6 e da fase 8 exigem o CI verde no commit aprovado, com o link do run no `STATUS.md`. Muda o skill `orchestrate`.
2. Agentes encerram os processos que sobem, e a E2E não reutiliza servidor antigo sem aviso.
3. QA-21: guardar o log completo na próxima falha, com as linhas `[WebServer]`.
4. Bloqueios de go-live, em `release-notes.md`: validação jurídica (D12/D13), validação manual H-1..H-10, imagens por digest, cliente Google OAuth e provedor de email por ambiente, retenção de logs e backups, regras de alerta, meta de throughput (D14), medição do p95 de 3 s, chaves compartilhadas do Data Protection.
5. Não verificados: leitor de tela real, login Google real, certificado SMTP recusado e evento 1008 em log real.
