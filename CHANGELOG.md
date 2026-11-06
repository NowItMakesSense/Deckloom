# Changelog

Formato baseado em [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/). Ainda não há versão publicada.

## [Não lançado]

### Processo e repositório (2026-11-06)

- **Migração para o AISEF (ADR-0017):** a governança dos agentes de IA passa a ser o plugin AISEF (`.aisef/`: contratos, aprovações e log de auditoria assinado). Sai o ferramental anterior em `.claude/` (orquestrador, 14 agentes, grafo de conhecimento, hooks próprios). `docs/specs/` fica como registro histórico.
- **Histórico git reiniciado** num repositório público novo. O histórico anterior existe só na cópia local `Deckloom_old`.
- **CI:** o job `guard-and-traceability` vira `ci-scripts`; o CodeQL envia resultados ao Code Scanning; o gitleaks passa a usar `.gitleaks.toml` (allowlist de valores fictícios de teste) em vez de fingerprints presas a commits antigos.

### Adicionado

- **Conta do jogador (`conta-jogador`):**
  - Cadastro com email e senha, com aceite dos Termos de Uso e declaração de maioridade (18+).
  - Login com email e senha, e login com Google (OpenID Connect com PKCE). Quem já tem conta com senha pode vincular o Google depois, confirmando a senha.
  - Redefinição de senha por email, com link de uso único e validade de 60 minutos. Uma redefinição derruba as sessões abertas da conta.
  - Perfil do jogador: nome de exibição, bio, TCG favorito e formatos preferidos. A edição é tudo ou nada: se um campo for inválido, nada é salvo.
  - Avatar predefinido ou avatar personalizado (imagem enviada e reprocessada no servidor, sem metadados).
  - Exclusão da própria conta, com reautenticação (senha, ou login Google recente), que apaga dados pessoais e avatar (ADR-0006).
  - Catálogos de formatos, avatares predefinidos e TCG favoritos, carregados como dados (ADR-0007).
- Limpeza periódica de dados vencidos: sessões expiradas ou revogadas, tokens de redefinição e pendências de login Google.
- Documentação de operação: README com execução local, testes e configuração de produção; checklist de lançamento em `docs/specs/conta-jogador/release-notes.md`.
- Pipeline de CI: guarda de banco e rastreabilidade, backend (formatação, build, testes com cobertura de 80% nos projetos de produto), frontend (lint, formatação, build, testes com cobertura de 80%), E2E com Playwright, varredura de segredos e auditoria de dependências.

### Alterado

- **Endereço do cliente atrás de proxy:** a API só usa o `X-Forwarded-For` quando a conexão vem de um proxy listado em `Forwarding:TrustedProxies`. Sem essa configuração, todos os clientes atrás do proxy seriam tratados como o mesmo IP nos limites de tentativas. Em produção, a lista é obrigatória (ADR-0016). Um valor sem índice em `Forwarding:TrustedProxies` (por exemplo, `Forwarding__TrustedProxies=...`) também derruba o start, em vez de cair em silêncio no padrão de loopback: use `Forwarding__TrustedProxies__0`.
- **Autenticação por requisição:** a sessão é validada uma única vez por requisição, em vez de duas.
- **Acessibilidade:** o link "Pular para o conteúdo" passou a ser montado no aplicativo; antes existia no código, mas não aparecia nas telas.
  - As telas e os diálogos S1 a S11 refluem a 320 px com texto a 200%, sem rolagem horizontal. As exceções aceitas pelo usuário, registradas em `design.md` (A.8.5), são o controle nativo de arquivo do S10 e a quebra de palavras longas nos diálogos.
  - No campo de senha, o botão "Mostrar senha" desce de linha e a senha digitada continua legível a 320 px com texto a 200%.
  - `<h1>` com 24 px calculados a 100%.
  - Título da aba por tela, inclusive `/perfil`, `/perfil/editar`, o fluxo Google e o link inválido de redefinição de senha.
  - O aviso de reautenticação (S11) e o de sessão expirada (S2) são anunciados depois da montagem da tela. A caixa de confirmação do S11 recebe o foco só quando o texto do aviso já está na região descrita.
- **Configuração obrigatória fora de desenvolvimento:** a API não sobe com `Auth:PublicOrigin` em `http`, com SMTP sem TLS ou com conexão ao banco sem TLS completo (`SSL Mode=VerifyFull` e certificado raiz). Ver a seção "Configuração de produção" do `README.md`.
- **Papéis do banco (ADR-0015):** a API conecta só com o papel de execução `deckloom_app`, com privilégios por tabela e coluna. Migrações são aplicadas por uma pessoa com o papel `deckloom_migrator`; a API não aplica migrações na inicialização.
- **Compose local:** a interface web passou para `http://localhost:8081` (antes `8080`). O Mailpit segue em `http://localhost:8025`. As duas portas são publicadas só em `127.0.0.1`, não em todas as interfaces do host (SEC-17).
- **Start com SMTP (pode impedir o start):** `Email:Smtp:User` preenchido com `Email:Smtp:UseTls=false` derruba o start em qualquer ambiente, inclusive `Development`. A mensagem nomeia só as chaves. No compose local com Mailpit, `Email__Smtp__User` e `Email__Smtp__Password` ficam vazios (SEC-22).
- **Evento 1008 (falha no envio de redefinição de senha):** ganhou o campo `cause`, de lista fechada: `connect`, `tls`, `auth`, `rejected`, `timeout` ou `other`. O log não recebe senha, email, link nem texto do servidor SMTP. Quem tem alerta ou painel sobre esse evento precisa conhecer o campo.
- **Evento 1010 (falha no login Google):** a reautenticação com sessão ausente, expirada, revogada ou de outro Jogador registra `reason=session_invalid`, em vez de `subject_mismatch`. O desfecho para o usuário não muda.
- **Login Google:** o `iss` do token é aceito com e sem o esquema (`https://accounts.google.com` e `accounts.google.com`), a partir de `Auth:Google:Authority`. Outros emissores seguem recusados.

### Segurança

- CI: gitleaks com a permissão que faltava, e CodeQL sem upload, com falha do job em resultado ativo (repositório privado sem Code Scanning).

- **Limite de tentativas sob concorrência:** o atraso progressivo de login, cadastro, redefinição e vínculo Google não podia ser contornado com requisições paralelas. Cada tentativa agora fica registrada antes de ser contada, e nenhum número de instâncias ou IPs permite mais de 30 verificações por hora por conta (ADR-0005).
- **IPv6:** o limite por IP trata um bloco `/64` como um único cliente, então trocar de endereço dentro do mesmo bloco não contorna o limite.
- **Redefinição de senha:** no máximo 30 solicitações por hora por conta. Acima desse teto, a resposta é idêntica à das demais solicitações.
- **Cookies `Secure`:** o atributo depende de `Auth:PublicOrigin` (https), e não do cabeçalho `X-Forwarded-Proto`, que o cliente controla.
- **Email (SMTP):** TLS obrigatório fora de desenvolvimento, e autenticação SMTP quando `Email:Smtp:User` estiver configurado. A senha do SMTP não aparece em logs nem em mensagens de erro. Com usuário configurado, `UseTls=false` derruba o start em qualquer ambiente (SEC-22).
- **Senhas comuns:** cadastro e redefinição recusam senhas de uma lista de senhas comuns, sem eco da senha na mensagem. A lista é curta e escrita à mão, sem derivação de vazamentos conhecidos (pendência de risco baixo). Não há medidor de força de senha, risco aceito pelo responsável do produto.
- **Corpo de requisição:** limite de tamanho no nginx e na API antes da leitura do corpo.
- **Logs e dados pessoais:** logs usam só identificadores internos. Email e IP aparecem apenas como HMAC nos limites de tentativas. Tokens são guardados como SHA-256.
- **Exceção aceita:** o cadastro com email já usado responde de forma explícita (`email_unavailable`), o que permite descobrir se um email tem conta. Foi aceito por decisão de produto e está registrado como exceção de enumeração.
- **Limite de tentativas e cancelamento:** as exclusões compensatórias do limite (recusa por orçamento, erro de validação, vínculo concluído) não dependem de a requisição continuar aberta. Um cliente que fecha a conexão não consegue deixar evento excedente na contagem (SEC-16).
- **Entrada JSON:** os endpoints que leem JSON respondem `415` (`unsupported_media_type`) quando a requisição tem corpo e o `Content-Type` não é `application/json` (parâmetros como `charset` são aceitos). O caso de uso não é chamado (SEC-10). Clientes que enviam JSON precisam informar o cabeçalho.
