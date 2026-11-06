# ADR-0010: Email de redefinição por SMTP genérico (MailKit), Mailpit no desenvolvimento local

- Status: Accepted (2026-10-23)
- Date: 2026-10-23
- Deciders: usuário (escolha da opção a, 2026-10-23), a partir da proposta do architect (feature `conta-jogador`)

## Context

REQ-028 exige enviar o link de redefinição por email somente se existir conta com senha local, sem revelar a existência da conta (inclusive em falhas de envio). Não há provedor nem nuvem escolhidos (restrição de custo). Logs não podem conter email, token nem link.

## Decision

- `IPasswordResetNotifier` enfileira o envio numa fila em memória (`Channel`); um `BackgroundService` envia por `SmtpPasswordResetSender`, implementado com **MailKit** (SMTP com STARTTLS/TLS obrigatório fora do dev), 3 tentativas com backoff (2 s, 8 s, 30 s), timeout de 10 s por tentativa.
- Desenvolvimento local: serviço `mail` (**Mailpit**) no Docker Compose; a API aponta para ele por configuração e os emails ficam visíveis só na interface local, sem sair da máquina.
- Produção: provedor com SMTP escolhido na fase 8 (custo), trocado apenas por configuração (`Email:Smtp:Host`, `Port`, `User`, `Password` como segredo, `From`).
- Testes: `RecordingPasswordResetNotifier`; nenhum teste fala com SMTP real.

## Alternatives considered

- **(b) API HTTP de um provedor específico desde já**: melhor telemetria de entrega, mas antecipa uma decisão de custo e cria adapter específico de fornecedor.
- **(c) Só log/arquivo no dev, provedor depois**: o link com token acabaria em log/arquivo, contrariando a regra de logs, e o caminho real não seria exercitado.

## Consequences

- Um único adapter serve dev e produção; trocar de provedor não exige código.
- SMTP oferece menos rastreio de entrega; falhas finais são só registradas (`PasswordResetEmailFailed`, sem dados pessoais) e alertadas.
- Emails pendentes na fila se perdem em reinício do processo (o Jogador pode pedir de novo); o token em claro nunca é persistido.
