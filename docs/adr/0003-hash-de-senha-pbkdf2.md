# ADR-0003: Hash de senha com PBKDF2-HMAC-SHA256 (600.000 iterações) da BCL, formato versionado

- Status: Accepted (2026-10-23)
- Date: 2026-10-22
- Deciders: architect (proposta), aprovação do usuário no gate da fase 3 (feature `conta-jogador`)

## Context

`conta-jogador` exige senha armazenada com hash adaptativo e salt, nunca registrada em log (NFR Security), com mínimo de 8 caracteres sem regras de composição (D2) e máximo de 128 caracteres (decisão do usuário na fase 3, limita o custo de hashing de entradas abusivas). A política de segurança pede dependências de autenticação auditadas e com versão fixada. O .NET não traz Argon2 nativo.

## Decision

- Algoritmo: **PBKDF2-HMAC-SHA256 com 600.000 iterações** (parâmetro da OWASP para PBKDF2-SHA256), salt aleatório de 16 bytes, saída de 32 bytes, via `System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2` (biblioteca padrão, sem dependência de terceiros).
- Formato armazenado autodescritivo e versionado: `v1$pbkdf2-sha256$<iterações>$<salt base64>$<hash base64>`. A porta `IPasswordHasher` expõe `Hash`, `Verify` (comparação em tempo constante) e `NeedsRehash`; após login bem-sucedido com parâmetros antigos, o hash é regravado (migração transparente para parâmetros/algoritmos futuros).
- Senha com mais de 128 caracteres: rejeitada antes do hash (erro de campo no cadastro e na redefinição; "credenciais inválidas" no login, sem calcular hash).
- Login com email inexistente ou conta sem senha local executa `Verify` contra um hash fictício fixo, para igualar o tempo de resposta (anti-enumeração, REQ-005).
- O número de hashes simultâneos é limitado (semáforo do tamanho do número de núcleos) para evitar esgotamento de CPU por rajadas.

## Alternatives considered

- **Argon2id** (recomendação primária da OWASP, resistente a GPU por uso de memória): exige biblioteca de terceiros (ex.: Konscious.Security.Cryptography) com manutenção e auditoria menos garantidas; custo de memória por hash pesa num servidor pequeno. Fica como evolução possível via `NeedsRehash` (formato `v2`).
- **bcrypt** (BCrypt.Net-Next): terceiros, limite de 72 bytes que conflita com o máximo de 128 caracteres.
- **PasswordHasher do ASP.NET Core Identity** (PBKDF2-HMAC-SHA512, 100.000 iterações): abaixo do parâmetro atual da OWASP para PBKDF2-SHA512 (210.000) e acopla ao pacote Identity, que não será usado (ADR-0002).

## Consequences

- Cada login/cadastro/redefinição custa da ordem de centenas de milissegundos de CPU, dentro do p95 de 3 s; os testes unitários usam um `IPasswordHasher` falso rápido, e um teste do adapter real usa parâmetros reduzidos por configuração.
- O `security` deve revisar o número de iterações antes do release e a cada atualização da recomendação da OWASP.
