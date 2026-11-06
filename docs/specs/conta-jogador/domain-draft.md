> Superseded: conteúdo consolidado em `design.md` (aprovado em 2026-10-23). Mantido só como histórico de trabalho - a fonte da verdade é o `design.md`.

## Bounded context and ubiquitous language

### Contexto delimitado desta feature

**Conta/Perfil** (já semeado no glossário e no grafo). Responsabilidade: identidade do Jogador (cadastro, autenticação, sessão, redefinição de senha), consentimento LGPD (aceite de termos e maioridade declarada), Perfil (nome de exibição, bio, avatar, TCG favorito, Formatos preferidos) e exclusão de conta com remoção de dados pessoais.

Organização interna (módulos dentro do mesmo contexto, sem separar em dois contextos nesta fase):
- **Identidade e Acesso**: credenciais (senha, Identidade Externa Google), Sessão, Redefinição de Senha, Tentativas de Acesso (rate limit).
- **Perfil**: dados de apresentação e preferências do Jogador, Avatar.
- Ambos compartilham o agregado `Jogador` (ver "Domain model"). Se o contexto crescer (ex.: outros provedores, 2FA), o módulo Identidade e Acesso é o candidato natural a virar contexto próprio. Isso não é necessário para o MVP.

### Relações com o restante do sistema (context map)

| Contexto / sistema | Relação | Detalhe |
|---|---|---|
| Sugestão de Deck (dono de Deck e do catálogo de Formato) | Conta/Perfil é **upstream** para a identidade; Sugestão de Deck é **upstream** para o catálogo de Formato | Deck referencia o Jogador **somente por `PlayerId`** (sem navegar para o agregado). Conta/Perfil referencia Formato **somente por `FormatId`** e consulta o catálogo por uma porta (camada anticorrupção). Na exclusão da conta, os Decks do Jogador são removidos (REQ-025) por uma porta de saída (`IPlayerDecksPurger`). |
| Comparação de Preço | Sem relação nesta feature | Loja não é ator (discovery, decisão 1). |
| Google (provedor de identidade) | Sistema externo, **anticorrupção** | O resultado do OAuth é convertido em `GoogleVerifiedIdentity` (subject, email, emailVerificado, nome). Nada do Google (foto, tokens) entra no domínio (REQ-022). |
| Provedor de email | Sistema externo, porta de saída | Somente envio do link de redefinição (REQ-028). A escolha do provedor é da arquitetura. |
| Armazenamento de avatar | Sistema externo, porta de saída | Tecnologia decidida pela arquitetura (fora do escopo, requirements). |

### Linguagem ubíqua (termos desta feature)

Termos em pt-BR são os da linguagem ubíqua; a coluna "Identificador" é o nome em código (inglês). Definições completas em `docs/glossary.md`.

| Termo (pt-BR) | Identificador | Tipo | Resumo |
|---|---|---|---|
| Jogador | `Player` | Agregado raiz | Já existente. Conta + Perfil de uma pessoa. |
| Visitante | `Visitor` | Conceito (não persistido) | Pessoa sem sessão autenticada. Vira Jogador ao se cadastrar. |
| Conta | `Account` | Conceito (aspecto do Jogador, não é agregado separado) | Parte do Jogador que trata de acesso: Email, credenciais, Aceite. |
| Perfil | `Profile` | Value object composto dentro do Jogador | Nome de exibição, bio, avatar, TCG favorito, Formatos preferidos. |
| Email | `Email` | Value object | Normalizado, formato válido, único entre Jogadores. |
| Senha / Hash de Senha | `PasswordHash` | Value object | Só o hash adaptativo é guardado. A senha em claro nunca entra no agregado. |
| Identidade Externa | `ExternalIdentity` | Value object | Provedor (Google) + identificador estável (`subject`) do provedor. |
| Nome de Exibição | `DisplayName` | Value object | 3 a 30 caracteres, não único (REQ-015, REQ-037). |
| Bio | `Bio` | Value object | Até 280 caracteres, opcional (REQ-015, REQ-016). |
| Avatar | `Avatar` | Value object (soma: `PresetAvatar` ou `CustomAvatar`) | Exatamente um por Jogador. |
| TCG Favorito | `FavoriteTcg` | Value object (enumeração extensível) | Hoje só Magic: The Gathering (REQ-017). |
| Formato Preferido | `FormatId` (referência) | Value object (identificador) | Referência ao Formato do catálogo; conjunto sem repetição, pode ser vazio. |
| Aceite de Termos | `TermsAcceptance` | Value object imutável | Versão dos documentos, data/hora e maioridade declarada (REQ-033, REQ-034). |
| Sessão | `Session` | Agregado raiz | Autenticação válida no servidor, invalidável (REQ-010 a REQ-012, REQ-035). |
| Lembrar-me | `RememberMe` | Atributo da Sessão | Sessão de até 30 dias em vez de 30 min de inatividade. |
| Token de Redefinição | `PasswordResetToken` | Entidade dentro do Jogador (0..1) | Uso único, 60 min, só o hash é guardado (REQ-029). |
| Vínculo Google Pendente | `PendingGoogleLink` | Estado transitório da aplicação | Identidade Google verificada aguardando prova de posse da senha (REQ-008, REQ-036). |
| Tentativas de Acesso / Atraso Progressivo | `AccessAttempts`, `ProgressiveDelayPolicy` | Política de domínio + porta | Rate limit por IP e por conta, sem bloqueio (REQ-032). |
| Reautenticação | `Reauthentication` | Value object (prova) | Prova recente de senha ou novo login Google exigida na exclusão (REQ-023). |
| Catálogo de Formatos | `IFormatCatalog` | Porta (dado de outro contexto) | Fonte dos Formatos selecionáveis (REQ-037). |

**Conflitos de nomenclatura sinalizados (não resolvidos silenciosamente):**
1. **Formato**: o glossário o atribui a "Sugestão de Deck" e o grafo o tem como Value Object. Esta feature exige catálogo configurável com identidade (REQ-037), logo Formato é, de fato, **Entidade de catálogo com `FormatId`**. Proposta: Sugestão de Deck continua dona do conceito; Conta/Perfil guarda só `FormatId`. Ver pergunta Q1.
2. **Conta x Jogador**: o glossário define Jogador como "ator com conta/login". Para evitar dois agregados, "Conta" é tratada como aspecto do Jogador e não como entidade própria. Se a equipe preferir `Account` como agregado separado, isso muda as invariantes J1 a J9.

## Domain model

Camada de domínio sem framework, IO ou persistência. Tudo que toca relógio, aleatoriedade, hashing, rede ou armazenamento entra por porta. Identificadores de código em inglês.

### Visão geral dos agregados

| Agregado | Raiz | Contém | Ciclo de vida |
|---|---|---|---|
| `Player` | `Player` | `Email`, `PasswordHash?`, `ExternalIdentity?` (Google, 0..1), `Profile`, `TermsAcceptance`, `PasswordResetToken?` (0..1) | Criado no cadastro, removido fisicamente na exclusão |
| `Session` | `Session` | `SessionId` (hash do token), `PlayerId`, `rememberMe`, datas, `revokedAt?` | Criada no login, revogada ou expirada, apagada na exclusão do Jogador |

Fora de agregado (política, estado operacional ou estado transitório da aplicação): `ProgressiveDelayPolicy` e contadores de tentativas (`AccessAttempts`), `PendingGoogleLink`.

Decisão de modelagem: `PasswordResetToken` fica **dentro** de `Player` (no máximo um ativo) porque assim a invariante "emitir invalida os anteriores" (REQ-029) e a troca atômica "atualizar hash + consumir token" (REQ-030) são garantidas pelo próprio agregado, sem coordenação entre agregados. `Session` é agregado separado por ter alta rotatividade e ciclo de vida independente (muitas por Jogador).

### Agregado `Player`

Comportamentos (métodos de domínio, nomes em inglês):
- `Player.RegisterWithPassword(email, password, displayName, acceptance, ...)` e `Player.RegisterWithGoogle(googleIdentity, acceptance, ...)` (fábricas)
- `LinkGoogle(googleIdentity, PasswordProof)`
- `RequestPasswordReset(...)` e `ResetPassword(tokenHash, newPassword, ...)`
- `UpdateProfile(displayName, bio, favoriteTcg, formatIds)`
- `ChooseAvatarPreset(presetId)` e `SetCustomAvatar(storageKey)`
- `Delete(confirmation, Reauthentication)`

Invariantes:

| # | Invariante | REQ |
|---|---|---|
| J1 | O `Email` é normalizado (trim, caixa baixa, formato válido) e **único entre Jogadores existentes**. Após a exclusão, o email fica livre e um novo cadastro gera outro `PlayerId`, sem herdar dados. A unicidade é verificada pelo caso de uso via `IPlayerRepository.ExistsByEmail` e **reforçada pelo armazenamento** (restrição de unicidade, a cargo do `architect`) por causa de corridas. | REQ-002, REQ-003, REQ-027 |
| J2 | Todo Jogador tem **ao menos um método de autenticação**: `PasswordHash` e/ou `ExternalIdentity`. Cadastro por Google nasce sem senha local. | REQ-001, REQ-006 |
| J3 | A senha em claro é validada (mínimo 8 caracteres, sem regras de composição) **antes** de virar hash; o agregado só guarda `PasswordHash`. O hash nunca sai do agregado para leitura de Perfil. | REQ-001, REQ-003, REQ-013, REQ-030, REQ-031 |
| J4 | `TermsAcceptance` é obrigatório na criação (versão dos documentos + data/hora), imutável, e inclui a declaração de maioridade (18+). Sem ambos, o Jogador não é criado, por qualquer via (email ou Google). | REQ-033, REQ-034 |
| J5 | `DisplayName` tem de 3 a 30 caracteres; repetição entre Jogadores é permitida. `Bio` tem no máximo 280 e pode ser vazia. `FavoriteTcg` pertence ao conjunto suportado (hoje só Magic). `FormatIds` é um conjunto sem repetição, pode ser vazio, e cada item deve existir no catálogo (validado por serviço de domínio com `IFormatCatalog`). Edição inválida não altera nada (tudo ou nada). | REQ-014 a REQ-017, REQ-037 |
| J6 | O Jogador tem **exatamente um** `Avatar`: `PresetAvatar` ou `CustomAvatar`. Na criação é sempre o preset padrão; a foto do Google nunca é usada. Ao trocar de avatar customizado para outro ou para preset, o domínio informa a chave do avatar anterior (evento `AvatarChanged`) para remoção do armazenamento. | REQ-018, REQ-021, REQ-022 |
| J7 | `ExternalIdentity`: no máximo uma (Google) por Jogador. Só é vinculada a um Jogador **com senha local**, se o email verificado do Google for igual ao `Email` do Jogador **e** houver `PasswordProof` válida (verificada pelo serviço de aplicação com `IPasswordHasher`). O mesmo `subject` do Google não pode estar vinculado a dois Jogadores (reforçado pelo repositório/armazenamento). Login por Google localiza o Jogador pelo `subject` primeiro. | REQ-007, REQ-008, REQ-036 |
| J8 | `PasswordResetToken`: no máximo um ativo; emitir novo invalida o anterior; validade de 60 min; uso único; só o **hash** do token é guardado; só existe para Jogador com senha local. Usar token inválido, expirado ou já consumido, ou nova senha curta, não altera a senha; senha curta **não consome** o token. Redefinição bem-sucedida troca o hash, consome o token e dispara o encerramento de todas as sessões. | REQ-028 a REQ-031 |
| J9 | Exclusão exige `confirmation` explícita **e** `Reauthentication` válida (senha para quem tem senha local; novo login Google para quem não tem). Sem isso, nada muda. Efetivada, a remoção é **física, imediata e irreversível** (sem soft delete, sem tombstone com email). | REQ-023 a REQ-027 |
| J10 | Autorização: toda operação de leitura/edição de Perfil, avatar e exclusão atua **apenas sobre o `PlayerId` da Sessão autenticada**. Nenhum comando aceita `PlayerId` alvo vindo do cliente. | REQ-011, REQ-013 |

Value objects de `Player`: `Email`, `PasswordHash`, `ExternalIdentity`, `DisplayName`, `Bio`, `Avatar` (`PresetAvatar(PresetId)` | `CustomAvatar(StorageKey)`), `FavoriteTcg`, `FormatId`, `TermsAcceptance` (`TermsVersion`, `AcceptedAt`, `AdultDeclared`). Entidade interna: `PasswordResetToken` (`TokenHash`, `IssuedAt`, `ExpiresAt`, `ConsumedAt?`), identificada dentro do agregado.

### Agregado `Session`

| # | Invariante | REQ |
|---|---|---|
| S1 | A sessão só nasce de uma autenticação bem-sucedida (cadastro, login por senha, login por Google já vinculado/criado, vínculo concluído com prova de senha). Fluxos que terminam em recusa, pedido de senha (REQ-008) ou cancelamento não criam sessão. | REQ-001, REQ-004 a REQ-009, REQ-036 |
| S2 | O identificador da sessão é opaco e imprevisível; o servidor guarda **apenas seu hash**. | REQ-010 e NFR Security |
| S3 | Sem "lembrar-me": válida enquanto a inatividade for menor que 30 min; cada requisição válida renova a última atividade. Com "lembrar-me": válida até 30 dias após a criação, ou até revogação. | REQ-012, REQ-035 |
| S4 | A revogação é irreversível; sessão revogada ou expirada nunca volta a ser válida. Requisição sem sessão válida é "não autenticada" e não expõe dados. | REQ-010, REQ-011 |
| S5 | Todas as sessões de um Jogador são revogadas na redefinição de senha e removidas na exclusão da conta. | REQ-024, REQ-030 |

### Política de domínio: tentativas de acesso (rate limit)

`ProgressiveDelayPolicy` (função pura: falhas consecutivas -> atraso, com teto) e porta `ILoginAttemptTracker` que conta falhas por **IP** e por **conta**.

| # | Regra | REQ |
|---|---|---|
| T1 | O atraso é monotonicamente crescente com as falhas e **nunca bloqueia** a conta; o Jogador legítimo entra após o atraso. | REQ-032 |
| T2 | Aplica-se a login, solicitação de redefinição e informe de senha no vínculo Google (REQ-036). | REQ-032, REQ-036 |
| T3 | A chave "conta" é derivada do email normalizado (hash), exista ou não a conta, para não permitir enumeração por diferença de comportamento. | REQ-005, REQ-028 |

### Estado transitório: `PendingGoogleLink`

Implícito em REQ-008/REQ-036: após o Google retornar email verificado de conta já existente, o sistema precisa lembrar "esta identidade Google verificada pediu vínculo", sem sessão. Deve ser emitido pelo servidor, não forjável pelo cliente, de curta duração e uso único, e ligado ao `subject` e ao `PlayerId` alvo. Fica na camada de aplicação (não é agregado). Validade: ver Q5.

### Eventos de domínio

Nenhum evento carrega email, senha, hash ou token em claro (NFR Security, Observability); carregam identificadores internos.

| Evento | Origem | Dados | Consumidores previstos |
|---|---|---|---|
| `PlayerRegistered` | `Player` (fábricas) | `PlayerId`, `Method` (Password/Google), `TermsVersion`, `OccurredAt` | Observabilidade/auditoria |
| `GoogleIdentityLinked` | `Player.LinkGoogle` | `PlayerId`, `OccurredAt` | Observabilidade |
| `PasswordResetRequested` | `Player.RequestPasswordReset` | `PlayerId`, `ExpiresAt` | Auditoria. O envio do email é feito pelo caso de uso via `IPasswordResetNotifier`, **não** por evento, porque o token em claro não pode trafegar em evento. |
| `PasswordReset` | `Player.ResetPassword` | `PlayerId` | Caso de uso revoga as sessões (S5); auditoria |
| `ProfileUpdated` | `Player.UpdateProfile` | `PlayerId`, campos alterados (nomes, sem valores) | Observabilidade |
| `AvatarChanged` | `Player.ChooseAvatarPreset` / `Player.SetCustomAvatar` | `PlayerId`, `PreviousStorageKey?` | Remoção do arquivo anterior (REQ-021) |
| `PlayerDeleted` (`ContaExcluida`) | `Player.Delete` | `PlayerId`, `CustomAvatarStorageKey?`, `OccurredAt` | Sessões, Decks, avatar, contadores (ver abaixo) |
| `SessionStarted` / `SessionRevoked` | `Session` | `SessionId` interno, `PlayerId`, motivo (logout, reset, exclusão) | Observabilidade |

Eventos de aplicação para observabilidade (não pertencem a agregado): login com sucesso/falha, falha OAuth Google, rejeição de upload, rate limit aplicado.

Efeitos da exclusão (`PlayerDeleted`), todos obrigatórios para cumprir REQ-024/REQ-025: remover o `Player`; remover ou revogar todas as `Session`; excluir Decks do Jogador (porta `IPlayerDecksPurger`, contexto Sugestão de Deck); apagar o avatar customizado do armazenamento; apagar contadores de tentativas ligados à conta. Ver Q2 e Q6 sobre atomicidade e retenção.

### Serviços de domínio e de aplicação

- `PlayerRegistrationService`: unicidade de email, aceite, criação (J1, J2, J4).
- `ProfileValidationService`: verifica `FormatIds` contra `IFormatCatalog` (J5).
- `AvatarPolicy`: tipos (JPEG, PNG, WebP) e tamanho máximo (2 MB) declarados; a verificação por conteúdo e o reencode ficam na porta `IAvatarImageProcessor` (REQ-019, REQ-020). Ordem recomendada: armazenar o novo, atualizar o agregado, remover o antigo (falha não deixa o Jogador sem avatar).
- `ProgressiveDelayPolicy` (T1).

### Portas (somente interfaces; implementações na infraestrutura)

Portas de repositório (pertencem ao domínio):

| Porta | Operações |
|---|---|
| `IPlayerRepository` | `GetById(PlayerId)`, `FindByEmail(Email)`, `FindByGoogleSubject(subject)`, `FindByResetTokenHash(hash)`, `ExistsByEmail(Email)`, `Add(Player)`, `Save(Player)`, `Remove(PlayerId)` (remoção física) |
| `ISessionRepository` | `Add(Session)`, `FindByTokenHash(hash)`, `Save(Session)`, `RevokeAllFor(PlayerId)`, `RemoveAllFor(PlayerId)` |

Portas de serviço (definidas no domínio ou na aplicação, implementadas fora):

| Porta | Finalidade | REQ |
|---|---|---|
| `IPasswordHasher` | Hash adaptativo com salt, verificação, necessidade de rehash | REQ-001, REQ-004 |
| `ISecureTokenGenerator` / `ITokenHasher` | Token imprevisível (sessão e redefinição) e seu hash | REQ-029, S2 |
| `IClock` | Hora atual (testes determinísticos de expiração) | REQ-012, REQ-029, REQ-035 |
| `IGoogleIdentityVerifier` | Conclui OAuth (state/PKCE) e devolve `GoogleVerifiedIdentity` | REQ-006 a REQ-009 |
| `ILoginAttemptTracker` | Registra e consulta falhas por IP e conta | REQ-032 |
| `IPasswordResetNotifier` | Envia o link de redefinição por email | REQ-028 |
| `IFormatCatalog` | Lista/consulta Formatos selecionáveis (ACL do contexto Sugestão de Deck) | REQ-037 |
| `IAvatarPresetCatalog` | Galeria de presets e preset padrão | REQ-018, REQ-022 |
| `IAvatarImageProcessor` | Valida por conteúdo, redimensiona, reencoda, remove EXIF | REQ-019, REQ-020 |
| `IAvatarStorage` | Salva, lê e remove avatar customizado | REQ-019, REQ-021, REQ-024 |
| `ITermsVersionProvider` | Versão vigente dos Termos e da Política | REQ-033 |
| `IPlayerDecksPurger` | Pede ao contexto Sugestão de Deck a exclusão de todos os Decks do Jogador | REQ-025 |
| `IPendingGoogleLinkStore` | Guarda e consome o `PendingGoogleLink` | REQ-008, REQ-036 |

### Rastreabilidade REQ -> modelo

REQ-001/003: J1-J4, S1. REQ-002/027: J1, J9. REQ-004/005: S1, T3. REQ-006 a 009: J2, J4, J7, S1. REQ-010 a 012/035: S2-S4. REQ-013: J3, J10. REQ-014 a 017/037: J5. REQ-018 a 022: J6. REQ-023 a 026: J9, S5. REQ-028 a 031: J8, S5, T1-T3. REQ-032: T1-T3. REQ-033/034: J4. REQ-036: J7, S1, T2.

### Questões abertas para o orquestrador (precisam de decisão antes de fechar o design)

| # | Questão | Opções | Recomendação |
|---|---|---|---|
| Q1 | Quem é o dono do catálogo de Formatos e o que acontece com preferências quando um Formato sai do catálogo (REQ-037 permite remover sem código, mas não diz o efeito)? | (a) Sugestão de Deck é dona; remoção física e limpeza das preferências. (b) Sugestão de Deck é dona; Formato ganha flag "ativo" e preferências de Formato inativo são ignoradas na leitura e mantidas. (c) Catálogo em Conta/Perfil. | (b). Evita perda silenciosa de dado e mantém Deck e Perfil consistentes. |
| Q2 | Exclusão de decks (REQ-025) deve ser atômica com a exclusão da conta? | (a) Mesma unidade de trabalho (monólito modular, mesmo banco). (b) Assíncrona por evento com outbox e retentativa. | (a) se houver um único banco; (b) apenas se os contextos forem separados fisicamente. Decisão do `architect`. |
| Q3 | Nome de exibição inicial vindo do Google (REQ-006) pode vir com menos de 3 ou mais de 30 caracteres ou vazio. | (a) Truncar em 30 e, se ficar com menos de 3, usar o nome padrão "Jogador". (b) Exigir que o Jogador informe um nome logo após o primeiro login. (c) Derivar do email. | (a). Não bloqueia o fluxo e não usa o email como nome. |
| Q4 | Reautenticação Google para exclusão (REQ-023): quão recente deve ser o login "novo"? E o limite máximo de tamanho da senha (proteção contra abuso de hashing)? | Janela de 5 min; senha com máximo de 128 caracteres. | Adotar ambos. Observação: o mínimo de 8 do requisito não muda. |
| Q5 | TTL do `PendingGoogleLink` (REQ-008/036). | 5, 10 ou 15 min. | 10 min, uso único. |
| Q6 | O IP guardado pelo rate limit é dado pessoal (LGPD) e as chaves de conta também. Qual retenção? Contadores devem ser apagados na exclusão? | Retenção curta (janela do atraso, ex. 24h) e apagar na exclusão. | Adotar; registrar na política de retenção do `security`. |
| Q7 | A autodeclaração de 18+ (REQ-034) deve ser persistida como parte do registro de aceite, ou apenas exigida e descartada? | (a) Persistir em `TermsAcceptance` (evidência). (b) Só validar. | (a). O registro já é apagado na exclusão (REQ-024). Depende da validação jurídica pendente (D13). |
| Q8 | `FavoriteTcg` é obrigatório no Perfil, mas o cadastro (REQ-001) não o recebe. | (a) Valor padrão Magic na criação. (b) Campo opcional no Perfil. | (a), pois Magic é o único valor (REQ-017) e REQ-013 sempre o exibe. |
| Q9 | Como contar os 3 a 30 caracteres do nome (unidades Unicode, espaços nas bordas)? | Contar após `trim`, em elementos de texto Unicode, sem controle/invisíveis. | Adotar; detalhar no design e nos testes de limite (2, 3, 30, 31). |
