# Requirements: conta-jogador

Status: Approved | Date: 2026-10-23 | Approved by: dono do projeto (NowItMakesSense) (2026-10-23) (emenda fase 3: REQ-038..REQ-049)
## Context and goal

Feature 1 da lista da discovery (`docs/specs/discovery.md`): conta e perfil do **Jogador**. Permite cadastro e login por email+senha ou login social Google, redefinição de senha, gerenciamento de sessão (incluindo logout e "lembrar-me"), perfil do Jogador (nome de exibição, avatar preset OU upload customizado, TCG favorito, formato(s) preferido(s), bio), edição do perfil e exclusão de conta com remoção dos dados pessoais (direito ao esquecimento, LGPD).

Objetivo: dar identidade ao Jogador, base para as demais features (decks, sugestões) que dependem de um usuário autenticado. Nenhum dado sensível (CPF, pagamento) é coletado (discovery).

Termos usados conforme `docs/glossary.md`: Jogador, Formato. "TCG favorito" é um enum cujo único valor hoje é Magic: The Gathering.

## Decisões

Decisões do usuário (2026-10-22) que resolvem as questões levantadas na elaboração destes requisitos:

| # | Tema | Decisão |
|---|---|---|
| D1 | Verificação de email | Não obrigatória no MVP. |
| D2 | Senha | Mínimo de 8 caracteres, sem regras de composição. Redefinição por email (REQ-028 a REQ-031). |
| D3 | Google x conta email+senha | Vínculo somente após o Jogador provar a posse da conta com a senha (REQ-008, REQ-036). |
| D4 | Decks na exclusão | Excluir todos os decks do Jogador. |
| D5 | Força bruta | Rate limit por IP e por conta com atraso progressivo, sem bloqueio de conta (REQ-032). |
| D6 | Sessão | Sessão curta (30 min de inatividade) mais opção "lembrar-me" de 30 dias (REQ-012, REQ-035). |
| D7 | Avatar customizado | JPEG, PNG ou WebP até 2 MB, redimensionado e reencodado no servidor (remove EXIF). |
| D8 | Nome e bio | Nome de exibição de 3 a 30 caracteres, não único; bio de até 280 caracteres. |
| D9 | Formatos | Catálogo configurável de Formatos; inicialmente Standard, Modern, Pioneer, Commander e Pauper. |
| D10 | Foto do Google | Ignorada; o avatar inicial é sempre um preset padrão. |
| D11 | Reautenticação na exclusão | Senha (conta com senha) ou novo login Google (conta sem senha local). |
| D12 | Momento da exclusão | Imediata e irreversível, sem período de carência. |
| D13 | LGPD no cadastro | Registrar aceite de Termos de Uso e Política de Privacidade (data e versão); idade mínima de 18 anos por autodeclaração; sem exportação de dados no MVP. |
| D14 | Throughput | Sem meta de vazão; apenas p95 de 3s. Revisar antes do release. |

Decisões de produto aceitas pelo usuário na fase 3 (design), na medida em que afetam o comportamento (emenda; IDs U referem-se a `design.md`):

| # | Tema | Decisão |
|---|---|---|
| D15 | Senha máxima (U5) | Senha com no máximo 128 caracteres (REQ-038). |
| D16 | Cadastro com Google (U1, U2, U6, U10) | Tela de conclusão com nome editável (3 a 30, após `trim`, em grapheme clusters), aceite de termos e 18+, só para conta nova; pendência de uso único e 10 min (REQ-039). |
| D17 | Caracteres de controle (U10) | Rejeitados em nome de exibição e bio (REQ-040). |
| D18 | Cadastro recusado e atraso (U7) | Cadastro recusado por email indisponível conta para o atraso progressivo por IP (REQ-041). |
| D19 | Exclusão via Google (U3) | Login Google nos últimos 5 min mais confirmação final (REQ-042). |
| D20 | Versão dos termos (U8) | Divergência de versão no envio é rejeitada (REQ-043). |
| D21 | TCG favorito padrão (U9) | Magic na criação da conta (REQ-044). |
| D22 | Email no perfil (U12) | Email não é exibido no perfil (REQ-045). |
| D23 | Formato inativo (U4) | Preferências por Formato inativo são mantidas e ignoradas na leitura (REQ-046). |
| D24 | Dados de rate limit (U7) | Retidos cerca de 24 h e apagados na exclusão da conta (REQ-047). |
| D25 | Maioridade (U8) | A declaração de 18+ é persistida como evidência junto ao aceite (REQ-048). |

## Pendências para o release

Não bloqueiam a aprovação desta spec, mas devem ser resolvidas antes do release:
- Validação jurídica da exclusão imediata e irreversível, incluindo qualquer retenção residual exigida por lei (D12) - pendente de validação jurídica antes do release.
- Validação jurídica do aceite de termos e política de privacidade, da idade mínima de 18 anos por autodeclaração e da ausência de exportação de dados (D13) - pendente de validação jurídica antes do release.
- Texto final dos Termos de Uso e da Política de Privacidade (conteúdo e versionamento).
- Revisar a ausência de meta de throughput (D14) com base no volume esperado.

## Actors

- **Jogador**: único ator com conta/login. Visitante (não autenticado) torna-se Jogador ao se cadastrar.
- **Provedor Google (login social)**: sistema externo de identidade, não é ator de negócio.
- Loja não é ator e não participa desta feature.

## Functional requirements (EARS)

### Cadastro e login
- REQ-001: WHEN um visitante envia email, senha válida, nome de exibição e o aceite dos Termos de Uso e da Política de Privacidade no cadastro THE SYSTEM SHALL criar uma conta de Jogador, armazenar a senha apenas como hash e iniciar uma sessão autenticada, sem exigir verificação de email.
- REQ-002: WHEN um visitante tenta se cadastrar com um email já associado a uma conta existente THE SYSTEM SHALL rejeitar o cadastro, informar que o email não pôde ser usado e não criar nem alterar nenhuma conta.
- REQ-003: IF o email enviado no cadastro tem formato inválido OR a senha tem menos de 8 caracteres OR um campo obrigatório está ausente THEN THE SYSTEM SHALL rejeitar o cadastro com mensagens de erro por campo e não criar conta; THE SYSTEM SHALL NOT impor regras de composição de senha.
- REQ-004: WHEN um Jogador envia email e senha corretos no login THE SYSTEM SHALL iniciar uma sessão autenticada.
- REQ-005: IF o login por email+senha falha (senha incorreta OR email inexistente) THEN THE SYSTEM SHALL recusar o login com uma mensagem única e genérica, que não revele se o email existe, e não iniciar sessão.
- REQ-006: WHEN um visitante conclui com sucesso a autenticação via Google, o email retornado está verificado pelo Google e não está associado a nenhuma conta existente, e o visitante forneceu o aceite dos termos e a autodeclaração de idade (REQ-033, REQ-034) THE SYSTEM SHALL criar uma conta de Jogador sem senha local, preencher o nome de exibição inicial a partir do perfil Google e iniciar uma sessão autenticada.
- REQ-007: WHEN um Jogador com conta criada via Google conclui novamente a autenticação via Google THE SYSTEM SHALL iniciar uma sessão autenticada na conta existente, sem criar conta duplicada.
- REQ-008: WHEN a autenticação via Google retorna um email verificado que já pertence a uma conta de email+senha ainda não vinculada ao Google THE SYSTEM SHALL NOT criar uma segunda conta, SHALL NOT iniciar sessão e SHALL pedir a senha da conta existente para comprovar a posse (REQ-036).
- REQ-009: IF a autenticação via Google é cancelada pelo usuário OR falha OR retorna email não verificado THEN THE SYSTEM SHALL não criar conta nem sessão e informar que o login com Google não foi concluído.
- REQ-038: IF a senha informada no cadastro, na redefinição de senha (REQ-030) ou no vínculo do Google com senha (REQ-036) tem mais de 128 caracteres (code points Unicode) THEN THE SYSTEM SHALL rejeitar a operação com erro no campo senha e não criar conta, não alterar a senha e não vincular o Google; a senha aceita tem de 8 a 128 caracteres. Emenda 2026-10-31 (SEC-05, decisão do usuário): IF a senha informada no cadastro ou na redefinição de senha consta de uma lista local de senhas comuns ou vazadas (sem chamada externa) THEN THE SYSTEM SHALL rejeitá-la com o código `too_common` no campo `password` (cadastro) ou `newPassword` (redefinição), no contrato de erro de validação existente e sem citar a senha na mensagem; a redefinição rejeitada por esse motivo não consome o token, como na senha curta. A regra não se aplica ao login, à exclusão de conta nem ao vínculo do Google (não definem senha).
- REQ-039: WHEN a autenticação via Google retorna um email verificado sem conta associada THE SYSTEM SHALL levar o visitante a uma etapa de conclusão do cadastro, sem criar a conta ainda, exibindo o nome de exibição pré-preenchido com o nome do Google (vazio se o Google não informar) e editável, e SHALL criar a conta somente quando o nome, contado após `trim` em elementos de texto (grapheme clusters), tiver de 3 a 30 caracteres e o aceite dos termos e a autodeclaração de 18+ forem fornecidos na mesma etapa; THE SYSTEM SHALL NOT truncar o nome automaticamente, SHALL exibir essa etapa apenas para contas novas e SHALL tratar o estado pendente do cadastro como de uso único, válido por 10 minutos.

### Sessão
- REQ-010: WHEN um Jogador autenticado solicita logout THE SYSTEM SHALL encerrar a sessão, invalidando-a no servidor, de modo que ela não possa ser reutilizada.
- REQ-011: IF uma requisição a um recurso protegido (perfil, edição, exclusão de conta) chega sem sessão válida OR com sessão expirada ou invalidada THEN THE SYSTEM SHALL recusá-la como não autenticada e não expor dados do Jogador.
- REQ-012: WHILE uma sessão sem "lembrar-me" está ativa THE SYSTEM SHALL mantê-la válida até 30 minutos de inatividade, e depois disso SHALL tratá-la como expirada.
- REQ-035: WHERE o Jogador marca "lembrar-me" no login THE SYSTEM SHALL manter a sessão válida por até 30 dias, invalidável a qualquer momento pelo logout.

### Redefinição de senha
- REQ-028: WHEN um visitante solicita a redefinição de senha informando um email THE SYSTEM SHALL responder sempre com a mesma mensagem genérica, existindo ou não conta com esse email, e SHALL enviar o link de redefinição somente se existir conta de email+senha com esse email.
- REQ-029: WHEN um link de redefinição é gerado THE SYSTEM SHALL emitir um token de uso único, imprevisível, com validade limitada (60 minutos), e SHALL invalidar tokens de redefinição anteriores do mesmo Jogador.
- REQ-030: WHEN um visitante usa um token de redefinição válido e envia uma nova senha com pelo menos 8 caracteres THE SYSTEM SHALL atualizar o hash da senha, consumir o token e encerrar todas as sessões existentes do Jogador.
- REQ-031: IF o token de redefinição é inválido, expirado ou já usado OR a nova senha tem menos de 8 caracteres THEN THE SYSTEM SHALL rejeitar a redefinição, não alterar a senha e informar o erro sem revelar dados da conta.
- REQ-032: WHILE ocorrem tentativas repetidas de login ou de solicitação de redefinição de senha a partir do mesmo IP OR contra a mesma conta THE SYSTEM SHALL aplicar um atraso progressivo crescente às novas tentativas, sem bloquear a conta, de modo que o Jogador legítimo ainda consiga entrar após o atraso enquanto a conta estiver dentro do orçamento de 30 tentativas por hora (janela deslizante de 60 min, contada por conta antes da verificação da senha); IF o orçamento da conta for excedido, por exemplo sob ataque sustentado, THEN THE SYSTEM SHALL recusar o login com senha dessa conta com `429 too_many_attempts` (sem `Retry-After`), por até 60 min, sem bloquear a conta. (Emenda 2026-10-31, SEC-01: trade-off aprovado pelo usuário; ADR-0005.)
- REQ-041: WHEN um cadastro é recusado porque o email não está disponível (REQ-002) THE SYSTEM SHALL contar essa tentativa para o atraso progressivo da chave de IP (REQ-032), sem bloquear o IP.

### Termos e idade (LGPD)
- REQ-033: WHEN uma conta é criada (email+senha ou Google) THE SYSTEM SHALL registrar o aceite dos Termos de Uso e da Política de Privacidade com data/hora e versão dos documentos aceitos, e SHALL recusar o cadastro sem esse aceite.
- REQ-034: WHEN um visitante se cadastra THE SYSTEM SHALL exigir a autodeclaração de ter 18 anos ou mais e SHALL recusar o cadastro sem ela.
- REQ-036: WHEN um visitante, após REQ-008, informa corretamente a senha da conta existente THE SYSTEM SHALL vincular a identidade Google a essa conta, iniciar a sessão nela e passar a aceitar o login via Google para essa conta; IF a senha estiver incorreta THEN THE SYSTEM SHALL não vincular, não iniciar sessão e aplicar REQ-032.
- REQ-043: IF a versão dos Termos de Uso ou da Política de Privacidade enviada com o aceite difere da versão vigente no momento do envio (documentos alterados entre o carregamento da página e o envio) THEN THE SYSTEM SHALL rejeitar o envio com erro no campo de aceite, pedindo nova revisão e aceite, e não criar conta (email+senha ou Google).
- REQ-048: WHEN uma conta é criada THE SYSTEM SHALL persistir a autodeclaração de 18+ como evidência, junto ao registro de aceite (data/hora e versão), e SHALL removê-la na exclusão da conta (REQ-024).

### Perfil
- REQ-013: WHEN um Jogador autenticado acessa seu perfil THE SYSTEM SHALL exibir nome de exibição, avatar, TCG favorito, formato(s) preferido(s) e bio, sem nunca exibir o hash da senha; THE SYSTEM SHALL NOT permitir que um Jogador visualize para edição ou altere o perfil de outro.
- REQ-014: WHEN um Jogador autenticado envia alterações válidas de nome de exibição, bio, TCG favorito ou formato(s) preferido(s) THE SYSTEM SHALL persistir as alterações e exibir o perfil atualizado.
- REQ-015: IF o nome de exibição tem menos de 3 ou mais de 30 caracteres OR a bio tem mais de 280 caracteres OR o TCG favorito não pertence ao enum suportado OR algum formato não pertence ao catálogo de Formatos THEN THE SYSTEM SHALL rejeitar a edição com mensagens de erro por campo e manter o perfil inalterado.
- REQ-016: WHERE o Jogador não informa formato(s) preferido(s) ou bio THE SYSTEM SHALL aceitar o perfil válido com esses campos vazios.
- REQ-017: THE SYSTEM SHALL definir Magic: The Gathering como único valor selecionável do enum de TCG favorito, estruturado de modo que novos TCGs possam ser adicionados sem alterar perfis existentes.
- REQ-037: THE SYSTEM SHALL oferecer como formatos preferidos selecionáveis os Formatos de um catálogo configurável, contendo inicialmente Standard, Modern, Pioneer, Commander e Pauper, de modo que adicionar ou remover um Formato do catálogo não exija alteração de código; THE SYSTEM SHALL permitir nomes de exibição repetidos entre Jogadores.
- REQ-040: IF o nome de exibição contém caracteres de controle OR a bio contém caracteres de controle diferentes de quebra de linha THEN THE SYSTEM SHALL rejeitar a operação (cadastro, conclusão do cadastro Google ou edição de perfil) com erro no campo correspondente e não alterar dados; a bio SHALL aceitar quebras de linha.
- REQ-044: WHEN uma conta é criada THE SYSTEM SHALL definir Magic: The Gathering como TCG favorito do Jogador.
- REQ-045: THE SYSTEM SHALL NOT exibir o email do Jogador na tela de perfil nem em respostas de leitura do perfil.
- REQ-046: WHERE o Jogador possui preferência por um Formato que foi inativado no catálogo THE SYSTEM SHALL manter a preferência armazenada e ignorá-la ao exibir ou retornar o perfil, e SHALL permitir salvar o perfil sem exigir a remoção dessa preferência.

### Avatar
- REQ-018: WHEN um Jogador autenticado seleciona um avatar da galeria de presets THE SYSTEM SHALL definir esse preset como avatar do Jogador.
- REQ-019: WHEN um Jogador autenticado envia uma imagem JPEG, PNG ou WebP de até 2 MB THE SYSTEM SHALL redimensioná-la e reencodá-la no servidor, removendo metadados (EXIF), armazenar o resultado, defini-lo como avatar do Jogador e exibi-lo no perfil.
- REQ-020: IF o arquivo enviado como avatar não é JPEG, PNG ou WebP OR excede 2 MB OR não é uma imagem válida (conteúdo não corresponde ao tipo declarado) THEN THE SYSTEM SHALL rejeitar o envio com mensagem de erro e manter o avatar atual inalterado.
- REQ-021: WHEN um Jogador que possui avatar customizado o substitui por outro upload ou seleciona um preset THE SYSTEM SHALL remover a imagem customizada anterior do armazenamento.
- REQ-022: WHEN um Jogador é criado THE SYSTEM SHALL atribuir um avatar preset padrão, ignorando a foto de perfil do Google.
- REQ-049: IF a imagem enviada como avatar é animada OR excede 4096 px em qualquer lado OR excede 16.777.216 pixels (4096 x 4096) THEN THE SYSTEM SHALL rejeitar o envio com a mesma mensagem genérica de erro de avatar do REQ-020 e manter o avatar atual inalterado. A cláusula do total de pixels é defesa em profundidade: com o limite de 4096 px por lado ela nunca é atingida isoladamente.

### Exclusão de conta
- REQ-023: WHEN um Jogador autenticado solicita a exclusão da conta THE SYSTEM SHALL exigir confirmação explícita com reautenticação (senha, para conta com senha local; novo login Google, para conta sem senha local) antes de executar a exclusão.
- REQ-024: WHEN a exclusão da conta é confirmada THE SYSTEM SHALL remover imediata e permanentemente os dados pessoais do Jogador (email, hash de senha, vínculo Google, nome de exibição, bio, preferências, registro de aceite, avatar customizado) e encerrar todas as suas sessões, sem período de carência e sem possibilidade de desfazer.
- REQ-025: WHEN a exclusão da conta é confirmada e o Jogador possui decks THE SYSTEM SHALL excluir todos os decks do Jogador junto com a conta.
- REQ-026: IF a confirmação de exclusão não é fornecida OR a reautenticação falha THEN THE SYSTEM SHALL manter a conta e os dados inalterados.
- REQ-027: IF uma conta foi excluída THEN THE SYSTEM SHALL recusar login por email+senha e por Google nessa conta e, em novo cadastro com o mesmo email, tratá-lo como um novo Jogador sem dados anteriores.
- REQ-042: WHEN um Jogador sem senha local solicita a exclusão da conta THE SYSTEM SHALL exigir um login Google concluído nos últimos 5 minutos e uma confirmação final explícita, e SHALL excluir a conta somente se ambos forem satisfeitos; IF o último login Google tem mais de 5 minutos no momento da confirmação THEN THE SYSTEM SHALL exibir a mensagem "confirmação expirada", não excluir nada e exigir nova reautenticação.
- REQ-047: THE SYSTEM SHALL reter os dados de controle do atraso progressivo (chaves de IP e de conta em forma de hash, e instante da tentativa) por cerca de 24 horas e, WHEN a conta é excluída, SHALL apagar imediatamente os dados de controle do atraso progressivo cuja chave é a conta do Jogador (email normalizado, em forma de hash); registros cuja chave é o IP não podem ser atribuídos a um Jogador, portanto não são apagados na exclusão e saem do sistema somente pela retenção de cerca de 24 horas.

## Scenarios (Gherkin)

### REQ-001
```gherkin
Scenario: Cadastro com email e senha válidos
  Given que não existe conta com o email "ana@example.com"
  When o visitante envia o cadastro com email "ana@example.com", senha "senhalonga1", nome de exibição "Ana" e aceita os termos
  Then uma conta de Jogador é criada
  And a senha é armazenada apenas como hash
  And o Jogador fica autenticado
  And nenhuma verificação de email é exigida
```

### REQ-002
```gherkin
Scenario: Cadastro com email já existente
  Given que existe uma conta com o email "ana@example.com"
  When um visitante envia o cadastro com o email "ana@example.com"
  Then o cadastro é rejeitado com a informação de que o email não pôde ser usado
  And nenhuma conta nova é criada
  And a conta existente permanece inalterada
```

### REQ-003
```gherkin
Scenario Outline: Cadastro com dados inválidos
  When o visitante envia o cadastro com email "<email>" e senha "<senha>"
  Then o cadastro é rejeitado com erro no campo "<campo>"
  And nenhuma conta é criada

  Examples:
    | email           | senha    | campo |
    | ana-sem-arroba  | abcdefgh | email |
    |                 | abcdefgh | email |
    | ana@example.com |          | senha |
    | ana@example.com | abc1234  | senha |

Scenario: Senha sem regras de composição é aceita
  When o visitante envia o cadastro com senha "aaaaaaaa" (8 letras minúsculas iguais) e demais campos válidos
  Then o cadastro é aceito
```

### REQ-004
```gherkin
Scenario: Login com credenciais corretas
  Given que existe uma conta com email "ana@example.com" e senha "senhalonga1"
  When o Jogador envia o login com essas credenciais
  Then o Jogador fica autenticado
```

### REQ-005
```gherkin
Scenario: Login com senha incorreta
  Given que existe uma conta com o email "ana@example.com"
  When o Jogador envia o login com senha incorreta
  Then o login é recusado com a mensagem genérica "credenciais inválidas"
  And nenhuma sessão é criada

Scenario: Login com email inexistente
  Given que não existe conta com o email "naoexiste@example.com"
  When alguém envia o login com esse email
  Then o login é recusado com a mesma mensagem genérica "credenciais inválidas"
  And nenhuma sessão é criada
```

### REQ-006
```gherkin
Scenario: Primeiro login com Google cria conta
  Given que não existe conta com o email "bia@example.com"
  When o visitante conclui a autenticação via Google com o email verificado "bia@example.com" e aceita os termos e declara ter 18 anos ou mais
  Then uma conta de Jogador é criada sem senha local
  And o nome de exibição inicial vem do perfil Google
  And o Jogador fica autenticado

Scenario: Primeiro login com Google sem aceite dos termos
  Given que não existe conta com o email "bia@example.com"
  When o visitante conclui a autenticação via Google mas não aceita os termos
  Then nenhuma conta é criada
```

### REQ-007
```gherkin
Scenario: Retorno de Jogador Google existente
  Given que existe uma conta criada via Google com o email "bia@example.com"
  When o Jogador conclui novamente a autenticação via Google
  Then o Jogador fica autenticado na conta existente
  And nenhuma conta duplicada é criada
```

### REQ-008
```gherkin
Scenario: Google com email de conta email+senha existente pede a senha
  Given que existe uma conta de email+senha com o email "ana@example.com" sem vínculo Google
  When o visitante conclui a autenticação via Google com o email verificado "ana@example.com"
  Then nenhuma segunda conta é criada
  And nenhuma sessão é iniciada
  And o sistema pede a senha da conta existente para comprovar a posse
```

### REQ-009
```gherkin
Scenario: Usuário cancela o consentimento no Google
  When o visitante cancela a autenticação na tela do Google
  Then nenhuma conta e nenhuma sessão são criadas
  And o visitante vê a informação de que o login com Google não foi concluído

Scenario: Google retorna email não verificado
  When a autenticação via Google retorna um email marcado como não verificado
  Then nenhuma conta e nenhuma sessão são criadas
```

### REQ-010
```gherkin
Scenario: Logout invalida a sessão
  Given um Jogador autenticado
  When o Jogador solicita logout
  Then a sessão é encerrada
  And reutilizar o token anterior em um recurso protegido resulta em "não autenticado"
```

### REQ-011
```gherkin
Scenario: Acesso a perfil sem sessão
  Given um visitante sem sessão
  When ele requisita o perfil
  Then a requisição é recusada como não autenticada
  And nenhum dado de Jogador é retornado

Scenario: Acesso com sessão expirada
  Given um Jogador cuja sessão expirou
  When ele requisita a edição do perfil
  Then a requisição é recusada como não autenticada
```

### REQ-012
```gherkin
Scenario: Sessão sem lembrar-me expira por inatividade
  Given um Jogador autenticado sem "lembrar-me"
  When passam 31 minutos sem nenhuma requisição
  Then a próxima requisição a um recurso protegido é recusada como não autenticada

Scenario: Atividade dentro de 30 minutos mantém a sessão
  Given um Jogador autenticado sem "lembrar-me"
  When ele faz requisições com intervalo menor que 30 minutos
  Then todas são atendidas sem pedir novo login
```

### REQ-013
```gherkin
Scenario: Visualizar o próprio perfil
  Given um Jogador autenticado com perfil preenchido
  When ele acessa o perfil
  Then vê nome de exibição, avatar, TCG favorito, formatos preferidos e bio
  And a resposta não contém o hash da senha

Scenario: Um Jogador não edita o perfil de outro
  Given dois Jogadores autenticados A e B
  When A tenta editar o perfil de B
  Then a operação é recusada
```

### REQ-014
```gherkin
Scenario: Editar perfil com dados válidos
  Given um Jogador autenticado
  When ele altera o nome de exibição para "Ana Mage", a bio para "Jogo Commander" e os formatos preferidos para "Commander"
  Then as alterações são persistidas
  And o perfil exibido reflete os novos valores
```

### REQ-015
```gherkin
Scenario Outline: Nome de exibição fora dos limites
  Given um Jogador autenticado com perfil válido
  When ele envia nome de exibição com <tamanho> caracteres
  Then a edição é <resultado>

  Examples:
    | tamanho | resultado                                        |
    | 2       | rejeitada com erro no campo nome de exibição     |
    | 3       | aceita                                           |
    | 30      | aceita                                           |
    | 31      | rejeitada com erro no campo nome de exibição     |

Scenario: Bio acima do limite
  Given um Jogador autenticado
  When ele envia uma bio com 281 caracteres
  Then a edição é rejeitada com erro no campo bio
  And o perfil permanece inalterado

Scenario: TCG favorito fora do enum
  Given um Jogador autenticado
  When ele envia TCG favorito "Pokemon"
  Then a edição é rejeitada com erro no campo TCG favorito
  And o perfil permanece inalterado

Scenario: Formato fora do catálogo
  Given um Jogador autenticado
  When ele envia o formato preferido "FormatoInventado"
  Then a edição é rejeitada com erro no campo formatos preferidos
  And o perfil permanece inalterado
```

### REQ-016
```gherkin
Scenario: Perfil sem formatos e sem bio
  Given um Jogador autenticado
  When ele salva o perfil sem formatos preferidos e sem bio
  Then o perfil é aceito com esses campos vazios

Scenario: Múltiplos formatos preferidos
  Given um Jogador autenticado
  When ele seleciona os formatos "Modern" e "Commander"
  Then ambos são persistidos como formatos preferidos
```

### REQ-017
```gherkin
Scenario: Enum de TCG favorito oferece somente Magic
  Given um Jogador autenticado editando o perfil
  When ele consulta as opções de TCG favorito
  Then a única opção é "Magic: The Gathering"
```

### REQ-018
```gherkin
Scenario: Escolher avatar preset
  Given um Jogador autenticado e uma galeria de avatares preset
  When ele seleciona o preset "mago-azul"
  Then o avatar do Jogador passa a ser "mago-azul"
```

### REQ-019
```gherkin
Scenario: Upload de avatar válido é redimensionado e reencodado
  Given um Jogador autenticado
  When ele envia uma imagem JPEG de 1,5 MB com metadados EXIF
  Then a imagem é redimensionada e reencodada no servidor
  And a imagem armazenada não contém metadados EXIF
  And passa a ser o avatar exibido no perfil

Scenario Outline: Tipos permitidos
  Given um Jogador autenticado
  When ele envia uma imagem <tipo> de até 2 MB
  Then o envio é aceito

  Examples:
    | tipo |
    | JPEG |
    | PNG  |
    | WebP |
```

### REQ-020
```gherkin
Scenario: Upload com tipo não permitido
  Given um Jogador autenticado com avatar atual
  When ele envia um arquivo ".exe" ou uma imagem GIF como avatar
  Then o envio é rejeitado com mensagem de tipo inválido
  And o avatar atual é mantido

Scenario: Upload acima de 2 MB
  Given um Jogador autenticado com avatar atual
  When ele envia uma imagem PNG de 2,1 MB
  Then o envio é rejeitado com mensagem de tamanho excedido
  And o avatar atual é mantido

Scenario: Arquivo com extensão de imagem mas conteúdo inválido
  Given um Jogador autenticado com avatar atual
  When ele envia um arquivo "foto.png" cujo conteúdo não é uma imagem
  Then o envio é rejeitado
  And o avatar atual é mantido
```

### REQ-021
```gherkin
Scenario: Substituir avatar customizado remove o anterior
  Given um Jogador com avatar customizado armazenado
  When ele envia um novo avatar válido
  Then o novo avatar é definido
  And a imagem anterior é removida do armazenamento

Scenario: Trocar avatar customizado por preset remove a imagem
  Given um Jogador com avatar customizado armazenado
  When ele seleciona um preset
  Then o preset é definido
  And a imagem customizada é removida do armazenamento
```

### REQ-022
```gherkin
Scenario: Novo Jogador por email recebe avatar padrão
  Given um visitante que se cadastra por email e senha
  When a conta é criada
  Then o Jogador possui um avatar preset padrão

Scenario: Novo Jogador por Google ignora a foto do Google
  Given um visitante cujo perfil Google possui foto
  When a conta é criada via Google
  Then o Jogador possui o avatar preset padrão
  And a foto do Google não é copiada nem armazenada
```

### REQ-023
```gherkin
Scenario: Exclusão de conta com senha exige senha
  Given um Jogador autenticado com senha local
  When ele solicita a exclusão da conta
  Then o sistema pede confirmação explícita e a senha
  And a conta continua existente até a confirmação

Scenario: Exclusão de conta Google exige novo login Google
  Given um Jogador autenticado cuja conta não tem senha local
  When ele solicita a exclusão da conta
  Then o sistema exige uma nova autenticação via Google antes de excluir
```

### REQ-024
```gherkin
Scenario: Exclusão remove dados pessoais imediatamente
  Given um Jogador autenticado com perfil e avatar customizado
  When ele confirma a exclusão da conta com a reautenticação correta
  Then email, hash de senha, vínculo Google, nome de exibição, bio, preferências, registro de aceite e avatar customizado são removidos permanentemente
  And todas as sessões do Jogador são encerradas
  And não existe período de carência nem como desfazer
```

### REQ-025
```gherkin
Scenario: Exclusão de conta de Jogador com decks
  Given um Jogador autenticado que possui decks salvos
  When ele confirma a exclusão da conta
  Then todos os decks do Jogador são excluídos
```

### REQ-026
```gherkin
Scenario: Confirmação não fornecida
  Given um Jogador autenticado que solicitou exclusão
  When ele cancela ou deixa de confirmar
  Then a conta e os dados permanecem inalterados

Scenario: Reautenticação falha
  Given um Jogador autenticado que solicitou exclusão
  When ele informa uma senha incorreta
  Then a conta e os dados permanecem inalterados
```

### REQ-027
```gherkin
Scenario: Login em conta excluída
  Given uma conta que foi excluída
  When alguém tenta login por email+senha com as credenciais antigas
  Then o login é recusado com a mensagem genérica de credenciais inválidas

Scenario: Novo cadastro com email de conta excluída
  Given uma conta que foi excluída
  When o mesmo email se cadastra novamente
  Then é criado um novo Jogador sem nenhum dado anterior
```

### REQ-028
```gherkin
Scenario: Solicitação para email existente
  Given que existe uma conta de email+senha com o email "ana@example.com"
  When um visitante solicita redefinição de senha para "ana@example.com"
  Then recebe a mensagem genérica "se o email existir, enviaremos instruções"
  And um email com o link de redefinição é enviado

Scenario: Solicitação para email inexistente
  Given que não existe conta com o email "naoexiste@example.com"
  When um visitante solicita redefinição de senha para esse email
  Then recebe exatamente a mesma mensagem genérica
  And nenhum email é enviado
```

### REQ-029
```gherkin
Scenario: Nova solicitação invalida o token anterior
  Given que o Jogador solicitou redefinição e recebeu o token T1
  When ele solicita novamente e recebe o token T2
  Then T1 deixa de ser aceito
  And T2 é aceito até 60 minutos após a emissão
```

### REQ-030
```gherkin
Scenario: Redefinição com token válido
  Given um token de redefinição válido e não usado
  And o Jogador possui sessões ativas
  When o visitante envia uma nova senha de 8 ou mais caracteres com esse token
  Then o hash da senha é atualizado
  And o token é consumido
  And todas as sessões anteriores do Jogador são encerradas
```

### REQ-031
```gherkin
Scenario: Token expirado
  Given um token de redefinição emitido há mais de 60 minutos
  When o visitante tenta usá-lo
  Then a redefinição é rejeitada e a senha não muda

Scenario: Token já utilizado
  Given um token de redefinição já consumido
  When o visitante tenta usá-lo novamente
  Then a redefinição é rejeitada e a senha não muda

Scenario: Token inválido
  When o visitante usa um token inexistente
  Then a redefinição é rejeitada sem revelar dados de conta

Scenario: Nova senha curta
  Given um token de redefinição válido
  When o visitante envia uma nova senha de 7 caracteres
  Then a redefinição é rejeitada com erro no campo senha
  And o token continua válido até expirar ou ser usado com sucesso
```

### REQ-032
```gherkin
Scenario: Atraso progressivo após falhas repetidas
  Given que um IP acumulou várias tentativas de login falhas contra a conta "ana@example.com"
  When ele tenta novo login
  Then a resposta é atrasada por um tempo maior que o da tentativa anterior
  And a conta não fica bloqueada

Scenario: Jogador legítimo entra após o atraso
  Given que a conta "ana@example.com" sofreu tentativas falhas de outro IP
  When o Jogador legítimo envia a senha correta de um IP diferente e aguarda o atraso aplicado
  Then o login é concluído com sucesso

Scenario: Orçamento por conta excedido recusa o login com senha (emenda 2026-10-31, SEC-01)
  Given que a conta "ana@example.com" recebeu 30 tentativas de login na última hora, de vários IPs
  When qualquer pessoa, inclusive o Jogador legítimo com a senha correta, tenta login com senha nessa conta
  Then a resposta é "429 too_many_attempts" sem cabeçalho Retry-After e a senha não é verificada
  And a conta não fica bloqueada
  And o login com senha volta a ser aceito quando as tentativas saem da janela de 60 min

Scenario: Limite também na solicitação de redefinição
  Given um IP que fez muitas solicitações de redefinição de senha
  When ele faz nova solicitação
  Then a resposta é atrasada progressivamente
```

### REQ-033
```gherkin
Scenario: Cadastro registra o aceite
  When o visitante se cadastra aceitando os termos na versão "2026-10-v1"
  Then o registro de aceite guarda data/hora e a versão "2026-10-v1" dos Termos de Uso e da Política de Privacidade

Scenario: Cadastro sem aceite
  When o visitante envia o cadastro sem aceitar os termos
  Then o cadastro é recusado e nenhuma conta é criada
```

### REQ-034
```gherkin
Scenario: Autodeclaração de maioridade ausente
  When o visitante envia o cadastro sem declarar ter 18 anos ou mais
  Then o cadastro é recusado com erro no campo de maioridade
  And nenhuma conta é criada
```

### REQ-035
```gherkin
Scenario: Lembrar-me mantém a sessão por até 30 dias
  Given um Jogador que fez login marcando "lembrar-me"
  When ele retorna 29 dias depois sem logout
  Then a sessão continua válida

Scenario: Lembrar-me expira após 30 dias
  Given um Jogador que fez login marcando "lembrar-me"
  When ele retorna 31 dias depois
  Then a sessão está expirada e ele precisa fazer login

Scenario: Logout encerra sessão lembrada
  Given um Jogador com sessão "lembrar-me" ativa
  When ele solicita logout
  Then a sessão deixa de ser aceita
```

### REQ-036
```gherkin
Scenario: Vincular Google após provar a senha
  Given que o visitante concluiu o Google com o email de uma conta email+senha existente
  When ele informa a senha correta da conta
  Then a identidade Google é vinculada à conta
  And ele fica autenticado na conta existente
  And logins futuros via Google entram nessa conta

Scenario: Senha incorreta no vínculo
  Given que o visitante concluiu o Google com o email de uma conta email+senha existente
  When ele informa a senha incorreta
  Then nenhum vínculo é criado
  And nenhuma sessão é iniciada
```

### REQ-037
```gherkin
Scenario: Catálogo inicial de Formatos
  When o Jogador consulta os formatos selecionáveis
  Then vê Standard, Modern, Pioneer, Commander e Pauper

Scenario: Novo Formato adicionado ao catálogo
  Given que o catálogo recebeu o Formato "Legacy" por configuração
  When o Jogador consulta os formatos selecionáveis
  Then "Legacy" está disponível sem mudança de código

Scenario: Nome de exibição repetido é permitido
  Given um Jogador com nome de exibição "Ana"
  When outro Jogador define o nome de exibição "Ana"
  Then a edição é aceita
```

### REQ-038
```gherkin
Scenario Outline: Senha acima de 128 caracteres é rejeitada
  When o visitante informa uma senha de <tamanho> caracteres em <fluxo>
  Then a operação é <resultado>

  Examples:
    | fluxo                         | tamanho | resultado                              |
    | cadastro                      | 128     | aceita                                 |
    | cadastro                      | 129     | rejeitada com erro no campo senha      |
    | redefinição de senha          | 129     | rejeitada com erro no campo senha      |
    | vínculo do Google com senha   | 129     | rejeitada, sem vínculo e sem sessão    |

Scenario: Redefinição com senha longa não consome o token
  Given um token de redefinição válido
  When o visitante envia uma nova senha de 129 caracteres
  Then a redefinição é rejeitada e a senha não muda
  And o token continua válido até expirar ou ser usado com sucesso

# Emenda 2026-10-31 (SEC-05, decisão do usuário)
Scenario Outline: Senha comum é rejeitada ao definir a senha
  When o visitante informa uma senha da lista local de senhas comuns em <fluxo>
  Then a operação é rejeitada com o erro "too_common" no campo <campo>
  And nenhuma conta é criada e nenhuma senha é alterada

  Examples:
    | fluxo                | campo       |
    | cadastro             | password    |
    | redefinição de senha | newPassword |

Scenario: Redefinição com senha comum não consome o token
  Given um token de redefinição válido
  When o visitante envia uma nova senha que consta da lista de senhas comuns
  Then a redefinição é rejeitada com "too_common" e a senha não muda
  And o token continua válido até expirar ou ser usado com sucesso

Scenario: Login com senha da lista não é afetado
  Given uma conta cuja senha consta da lista de senhas comuns
  When o titular entra com essa senha
  Then o login segue as regras normais de credenciais
```

### REQ-039
```gherkin
Scenario: Conclusão do cadastro Google com nome editado
  Given que o Google retornou o email verificado "bia@example.com" sem conta associada e o nome "Beatriz Souza"
  When o visitante chega à etapa de conclusão
  Then o nome de exibição vem pré-preenchido com "Beatriz Souza" e é editável
  And a conta ainda não existe
  When ele altera o nome para "Bia", aceita os termos e declara 18+
  Then a conta é criada e o Jogador fica autenticado

Scenario: Nome do Google fora do limite não é truncado
  Given que o nome retornado pelo Google tem 40 caracteres
  When o visitante chega à etapa de conclusão
  Then o nome vem pré-preenchido sem truncamento
  And enviar sem editá-lo é rejeitado com erro no campo nome de exibição

Scenario: Contagem após trim e em grapheme clusters
  When o visitante envia "  Bi  " como nome de exibição na conclusão
  Then a contagem considera 2 caracteres e o envio é rejeitado

Scenario: Emoji composto conta como um caractere
  When o visitante envia um nome de 2 letras seguidas de um emoji composto por vários code points
  Then a contagem é 3 e o nome é aceito

Scenario: Pendência expira em 10 minutos
  Given que a etapa de conclusão foi aberta há mais de 10 minutos
  When o visitante envia o formulário
  Then nenhuma conta é criada
  And ele é orientado a refazer o login com Google

Scenario: Pendência é de uso único
  Given uma conclusão de cadastro Google concluída com sucesso
  When o mesmo estado pendente é reenviado
  Then nenhuma segunda conta é criada

Scenario: Etapa só para contas novas
  Given que existe uma conta criada via Google com o email "bia@example.com"
  When o Jogador conclui a autenticação via Google com esse email
  Then ele entra na conta existente sem passar pela etapa de conclusão

Scenario: Conclusão sem aceite ou sem 18+
  When o visitante envia a conclusão sem aceitar os termos ou sem declarar 18+
  Then nenhuma conta é criada
```

### REQ-040
```gherkin
Scenario: Caractere de controle no nome de exibição
  Given um Jogador autenticado
  When ele envia um nome de exibição contendo uma quebra de linha ou um caractere de controle
  Then a edição é rejeitada com erro no campo nome de exibição
  And o perfil permanece inalterado

Scenario: Caractere de controle na bio
  Given um Jogador autenticado
  When ele envia uma bio contendo o caractere de controle NUL
  Then a edição é rejeitada com erro no campo bio

Scenario: Quebra de linha na bio é permitida
  Given um Jogador autenticado
  When ele envia uma bio de duas linhas
  Then a edição é aceita
```

### REQ-041
```gherkin
Scenario: Cadastros recusados por email indisponível aumentam o atraso do IP
  Given um IP que teve vários cadastros recusados por email indisponível
  When ele envia novo cadastro
  Then a resposta é atrasada por um tempo maior que o da tentativa anterior
  And o IP não fica bloqueado

Scenario: Cadastros recusados não afetam a chave de conta
  Given vários cadastros recusados por email indisponível vindos de um IP
  When o Jogador dono do email faz login de outro IP com a senha correta
  Then o login não sofre atraso causado por esses cadastros
```

### REQ-042
```gherkin
Scenario: Exclusão via Google dentro de 5 minutos
  Given um Jogador sem senha local que concluiu um login Google há 2 minutos
  When ele dá a confirmação final de exclusão
  Then a conta é excluída

Scenario: Confirmação expirada após 5 minutos
  Given um Jogador sem senha local cujo último login Google foi há 6 minutos
  When ele dá a confirmação final de exclusão
  Then vê a mensagem "confirmação expirada"
  And nada é excluído
  And é exigida nova reautenticação

Scenario: Login Google sem confirmação final
  Given um Jogador sem senha local que acabou de reautenticar com Google
  When ele não dá a confirmação final
  Then a conta permanece inalterada
```

### REQ-043
```gherkin
Scenario: Termos alterados entre carregar a página e enviar
  Given que o visitante carregou o cadastro com os termos na versão "v1"
  And a versão vigente passou a ser "v2"
  When ele envia o cadastro aceitando a versão "v1"
  Then o envio é rejeitado com erro no campo de aceite pedindo nova revisão
  And nenhuma conta é criada

Scenario: Versão divergente na conclusão Google
  Given que o visitante abriu a conclusão do cadastro Google com a versão "v1" e a vigente é "v2"
  When ele envia a conclusão
  Then o envio é rejeitado com erro no campo de aceite
  And nenhuma conta é criada
```

### REQ-044
```gherkin
Scenario: TCG favorito padrão
  When uma conta é criada por email+senha ou por Google
  Then o TCG favorito do Jogador é "Magic: The Gathering"
```

### REQ-045
```gherkin
Scenario: Email não aparece no perfil
  Given um Jogador autenticado
  When ele acessa o perfil
  Then a tela e a resposta de leitura do perfil não contêm o email
```

### REQ-046
```gherkin
Scenario: Formato inativado é ignorado na leitura
  Given um Jogador com os formatos preferidos "Modern" e "Pauper"
  And o Formato "Pauper" foi inativado no catálogo
  When ele acessa o perfil
  Then vê apenas "Modern" como formato preferido

Scenario: Salvar perfil mantém a preferência inativa
  Given o cenário acima
  When o Jogador salva o perfil sem alterar os formatos
  Then o salvamento é aceito
  And a preferência por "Pauper" continua armazenada

Scenario: Formato reativado volta a aparecer
  Given que "Pauper" foi reativado no catálogo
  When o Jogador acessa o perfil
  Then "Pauper" volta a aparecer como formato preferido
```

### REQ-047
```gherkin
Scenario: Dados de rate limit são retidos por cerca de 24 horas
  Given tentativas de login registradas para o controle do atraso progressivo
  When passam mais de 24 horas
  Then esses registros são descartados

Scenario: Exclusão apaga os registros de rate limit com chave de conta
  Given um Jogador com registros de rate limit cuja chave é a sua conta (email normalizado)
  When ele confirma a exclusão da conta
  Then os registros de rate limit com a chave da sua conta são apagados na exclusão

Scenario: Registros com chave de IP não são apagados na exclusão
  Given registros de rate limit cuja chave é o IP usado pelo Jogador
  When o Jogador confirma a exclusão da conta
  Then os registros com chave de IP permanecem, pois não são atribuíveis a um Jogador
  And são descartados apenas quando passam cerca de 24 horas de retenção
```

### REQ-049
```gherkin
Scenario: Imagem animada é rejeitada
  Given um Jogador autenticado com avatar atual
  When ele envia um WebP animado de até 2 MB
  Then o envio é rejeitado com a mesma mensagem genérica de erro de avatar do REQ-020
  And o avatar atual é mantido

Scenario Outline: Dimensões acima do limite
  Given um Jogador autenticado com avatar atual
  When ele envia uma imagem estática de <largura> x <altura> px de até 2 MB
  Then o envio é <resultado>

  Examples:
    | largura | altura | resultado                                                        |
    | 4096    | 4096   | aceito (limite exato: 16.777.216 pixels)                        |
    | 4097    | 4096   | rejeitado com a mesma mensagem genérica de erro de avatar       |
    | 4097    | 100    | rejeitado com a mesma mensagem genérica de erro de avatar       |
    | 100     | 4097   | rejeitado com a mesma mensagem genérica de erro de avatar       |

Scenario: Rejeição mantém o avatar atual
  Given um Jogador autenticado com avatar customizado armazenado
  When ele envia uma imagem de 4097 x 4096 px
  Then o envio é rejeitado
  And o avatar customizado atual permanece armazenado e exibido
```

### REQ-048
```gherkin
Scenario: Maioridade persistida como evidência
  When o visitante se cadastra declarando 18+ e aceitando os termos na versão "v1"
  Then o registro de aceite guarda a declaração de 18+ junto com data/hora e versão "v1"

Scenario: Evidência removida na exclusão
  Given um Jogador com registro de aceite e declaração de 18+
  When ele confirma a exclusão da conta
  Then o registro de aceite e a declaração de 18+ são removidos
```

## Non-functional requirements

- **Performance**: cadastro, login, logout, redefinição de senha, leitura e edição de perfil e upload de avatar respondem em até 3s p95 (alvo da discovery), medido na resposta do servidor após o recebimento completo da requisição; o atraso deliberado do rate limit (REQ-032) é excluído da medição. Throughput: sem meta definida no MVP (D14), a ser revisada antes do release.
- **Availability**: meta informal de 99% de uptime da discovery, sem SLA formal e sem multi-região. Indisponibilidade do Google não deve afetar login por email+senha (degradação graciosa). A redefinição de senha depende de envio de email, e a falha do envio não deve revelar a existência da conta.
- **Security**: seguir `docs/security-policy.md` e o checklist do agente `security`. Da política geral, aplicam-se diretamente: validação de entrada nas fronteiras (cadastro, login, perfil, upload), consultas parametrizadas, codificação de saída, autenticação/autorização explícitas por caso de uso, logs sem PII nem segredos (eventos de login, redefinição e exclusão registram identificador interno e resultado, nunca email em claro, senha, token ou hash), retenção de dados documentada conforme LGPD, ação destrutiva (exclusão de conta) somente com confirmação explícita (REQ-023), e dependências de autenticação/imagem auditadas e com versão fixada. Específico desta feature: senha sempre armazenada com hash adaptativo e salt (algoritmo definido no design), nunca registrada em log; mensagens de login e de redefinição sem enumeração de contas (REQ-005, REQ-028); tokens de redefinição imprevisíveis, de uso único e armazenados de forma que o vazamento do banco não os revele; cookies/tokens de sessão protegidos e invalidáveis no servidor (REQ-010); validação de upload por conteúdo e não só por extensão, com reencode que remove metadados (REQ-019, REQ-020); proteção contra CSRF/XSS nos formulários; autorização garantindo que o Jogador só acessa e altera a própria conta (REQ-013); fluxo OAuth com `state`/PKCE; vínculo Google somente com prova de posse (REQ-036); rate limit com atraso progressivo (REQ-032).
- **Privacy/compliance**: LGPD. Dados pessoais tratados: email, hash de senha, identificador Google, nome de exibição, avatar, bio, preferências de jogo, registro de aceite (data e versão). Nenhum dado sensível (CPF, pagamento) é coletado. Direito ao esquecimento atendido por REQ-024, REQ-025 e REQ-027; coleta mínima (a foto do Google não é coletada, REQ-022). Aceite de termos e política (REQ-033), idade mínima de 18 anos por autodeclaração (REQ-034); sem exportação de dados no MVP. Exclusão imediata e aceite/idade/ausência de exportação estão pendentes de validação jurídica antes do release (ver "Pendências para o release"). Logs não devem conter senha, token ou hash, e dados pessoais em logs devem ser mínimos.
- **Accessibility**: WCAG 2.1 nível AA em todas as telas (cadastro, login, redefinição de senha, perfil, edição, upload de avatar, exclusão de conta): formulários com rótulos associados e erros por campo identificados de forma programática, navegação por teclado, contraste mínimo AA, alternativa textual para avatares, foco gerenciado em diálogos de confirmação, e mensagens de atraso do rate limit anunciadas a leitores de tela.
- **Observability**: logs estruturados (sem senha, token, hash nem email em claro - apenas identificador interno do Jogador) de eventos de cadastro, login (sucesso/falha), logout, solicitação e conclusão de redefinição de senha, vínculo Google, falha de OAuth Google, edição de perfil, rejeição de upload, aplicação de rate limit e exclusão de conta, com identificador de correlação; métricas de taxa de falha de login e de falha do fluxo Google; alerta em pico anômalo de falhas de login, de acionamentos do rate limit ou de falha do provedor Google. Alertas de scraper não pertencem a esta feature.

## Out of scope

- Outros provedores de login social além do Google (Apple, Facebook, etc.).
- Autenticação multifator (2FA).
- Verificação de email no cadastro (D1).
- Bloqueio de conta após falhas de login (D5).
- Exportação/portabilidade de dados pessoais no MVP (D13).
- Período de carência ou desfazer a exclusão de conta (D12).
- Contas ou login para Loja (a Loja não é ator).
- Perfil público/visível a outros Jogadores e recursos sociais (seguir, mensagens).
- Papéis e permissões além de Jogador (sem administrador nesta feature).
- Qualquer TCG além de Magic como valor do enum de TCG favorito.
- Pagamento, checkout e coleta de dados sensíveis (CPF, cartão).
- CRUD de decks (feature 9); esta feature só define que a exclusão de conta exclui os decks do Jogador (REQ-025).
- Escolha da tecnologia de armazenamento do avatar e do provedor de envio de email (decisões da fase de arquitetura).
- Hospedagem/infraestrutura de produção.

## Open questions (must be empty before approval)

Nenhuma. Pendências de validação jurídica e de conteúdo ficam em "Pendências para o release".
