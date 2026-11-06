# Glossary (ubiquitous language)

| Term | Definition | Bounded context | Example |
|---|---|---|---|
| Jogador | Único ator com conta/login no sistema; monta decks e recebe sugestões | Conta/Perfil | Jogador cadastra-se via Google e define Magic como TCG favorito |
| Loja | Entidade de dados representando uma loja externa de TCG, sem login nem dashboard próprio | Comparação de Preço | LigaMagic, MYP Cards, XPlace, Meruru, Epic Game |
| Fonte de Preço | Papel que uma Loja assume ao ser raspada periodicamente como origem de preço de cartas; não é uma entidade separada da Loja | Comparação de Preço | As 5 lojas do MVP são Fontes de Preço |
| Fonte de Meta | Site de referência de metagame/torneio usado para derivar arquétipos e popularidade | Sugestão de Deck | MTGMelee, EDHREC, MTGGoldfish, MTGTop8 |
| Snapshot de Preço | Registro histórico e imutável do preço de uma carta em uma Loja, com timestamp de coleta | Comparação de Preço | Nunca sobrescreve um snapshot anterior |
| Decklist | Lista de cartas que compõem um Deck, informada em texto ou montada via busca manual | Sugestão de Deck | `4 Lightning Bolt` colado pelo Jogador |
| Arquétipo | Estratégia/identidade de um Deck, derivada dos dados de meta (a "mecânica" sugerida) | Sugestão de Deck | "Aggro Vermelho/Branco", "Sinergia de Sacrifício" |
| Comandante | Carta que define o Deck no formato Commander; usada para consultar dados do EDHREC | Sugestão de Deck | Dados de cartas mais jogadas por Comandante |
| Formato | Regra de construção de Deck escolhida pelo Jogador | Sugestão de Deck | Standard, Modern, Pioneer, Commander, Pauper |
| Sugestão de Compra | Recomendação de onde comprar as cartas faltantes de um Deck, ordenada por preço entre as Fontes de Preço | Comparação de Preço | Link de saída para a Loja com melhor preço |
| Visitante | Pessoa sem sessão autenticada; torna-se Jogador ao se cadastrar (código: `Visitor`) | Conta/Perfil | Quem abre a tela de cadastro ou de login |
| Conta | Aspecto de acesso do Jogador: Email, credenciais (senha e/ou Identidade Externa) e Aceite de Termos. Não é agregado separado do Jogador (código: `Account`) | Conta/Perfil | "Conta criada via Google, sem senha local" |
| Perfil | Dados de apresentação e preferências do Jogador: Nome de Exibição, Bio, Avatar, TCG Favorito e Formatos Preferidos (código: `Profile`) | Conta/Perfil | Tela de edição de perfil |
| Email | Endereço de contato e identificador de login do Jogador; normalizado e único entre Jogadores (código: `Email`) | Conta/Perfil | `ana@example.com` |
| Hash de Senha | Resultado do hash adaptativo com salt da senha; é a única forma em que a senha é guardada e nunca é exibido (código: `PasswordHash`) | Conta/Perfil | Jogador por Google não tem Hash de Senha |
| Identidade Externa | Vínculo do Jogador com um provedor de login social (hoje só Google), identificado pelo `subject` estável do provedor (código: `ExternalIdentity`) | Conta/Perfil | Identidade Google vinculada após provar a senha |
| Nome de Exibição | Nome mostrado do Jogador, de 3 a 30 caracteres, não único (código: `DisplayName`) | Conta/Perfil | "Ana Mage" |
| Bio | Texto livre opcional do Jogador, até 280 caracteres (código: `Bio`) | Conta/Perfil | "Jogo Commander" |
| Avatar | Imagem do Jogador: preset da galeria OU imagem customizada enviada, sempre exatamente um (código: `Avatar`) | Conta/Perfil | Preset "mago-azul" |
| Avatar Preset | Avatar escolhido da galeria fixa da plataforma; o padrão é atribuído ao criar o Jogador (código: `PresetAvatar`) | Conta/Perfil | "mago-azul" |
| Avatar Customizado | Avatar enviado pelo Jogador (JPEG, PNG ou WebP, até 2 MB), redimensionado e reencodado no servidor sem metadados (código: `CustomAvatar`) | Conta/Perfil | Foto enviada e reencodada |
| TCG Favorito | TCG preferido do Jogador; enumeração extensível cujo único valor hoje é Magic: The Gathering (código: `FavoriteTcg`) | Conta/Perfil | Magic: The Gathering |
| Formato Preferido | Formato do catálogo marcado pelo Jogador como de seu interesse; zero ou mais por Jogador. Usa o termo Formato (existente), sem redefini-lo (código: referência `FormatId`) | Conta/Perfil | Modern e Commander |
| Catálogo de Formatos | Lista configurável de Formatos selecionáveis; alterá-la não exige mudança de código. Cada Formato tem a marca "ativo": só Formatos ativos podem ser escolhidos; preferências já guardadas para um Formato inativo são mantidas e ignoradas na leitura (código: porta `IFormatCatalog`) | Sugestão de Deck (consumido por Conta/Perfil) | Inicialmente Standard, Modern, Pioneer, Commander, Pauper |
| Aceite de Termos | Registro imutável do aceite dos Termos de Uso e da Política de Privacidade (versão e data/hora) feito na criação da conta, junto com a declaração de maioridade (código: `TermsAcceptance`) | Conta/Perfil | Versão "2026-10-v1" aceita em 22/10/2026 |
| Maioridade Declarada | Autodeclaração do visitante de ter 18 anos ou mais, exigida no cadastro; sem verificação documental | Conta/Perfil | Caixa "Declaro ter 18 anos ou mais" |
| Sessão | Autenticação válida e revogável no servidor após login ou cadastro (código: `Session`) | Conta/Perfil | Expira em 30 min de inatividade, ou 30 dias com Lembrar-me |
| Lembrar-me | Opção de login que estende a Sessão para até 30 dias, invalidável por logout (código: `RememberMe`) | Conta/Perfil | Caixa marcada no login |
| Token de Redefinição | Credencial de uso único e validade de 60 min enviada por email para trocar a senha; só o hash é guardado (código: `PasswordResetToken`) | Conta/Perfil | Link "redefinir senha" no email |
| Vínculo Google Pendente | Estado transitório em que uma identidade Google verificada, cujo email já pertence a uma conta com senha, aguarda a prova de posse da senha (código: `PendingGoogleLink`) | Conta/Perfil | Tela "informe a senha para vincular o Google" |
| Cadastro Google Pendente | Estado transitório em que uma identidade Google verificada, sem conta no sistema, aguarda nome de exibição, aceite de termos e declaração de 18+ para criar o Jogador; vale 10 min e é de uso único (código: `PendingGoogleSignup`) | Conta/Perfil | Tela "concluir cadastro" após o retorno do Google |
| Atraso Progressivo | Rate limit que aumenta o atraso das tentativas repetidas de login ou de redefinição por IP e por conta, sem bloquear a conta (código: `ProgressiveDelayPolicy`) | Conta/Perfil | 5ª falha espera mais que a 4ª |
| Reautenticação | Prova recente de identidade exigida para ações destrutivas: senha (conta com senha) ou novo login Google (conta sem senha) (código: `Reauthentication`) | Conta/Perfil | Pedir a senha ao excluir a conta |
| Exclusão de Conta | Remoção física, imediata e irreversível dos dados pessoais do Jogador e de seus Decks, sem período de carência (código: evento `PlayerDeleted`) | Conta/Perfil | Jogador confirma com a senha e a conta some |
