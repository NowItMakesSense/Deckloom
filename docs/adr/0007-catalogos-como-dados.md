# ADR-0007: Catálogo de Formatos (dono: Sugestão de Deck) e catálogo de avatares preset como dados, com flag de ativo

- Status: Accepted (2026-10-23)
- Date: 2026-10-22
- Deciders: architect (proposta, aplicando decisões do usuário de 2026-10-22), aprovação do usuário no gate da fase 3

## Context

REQ-037 exige um catálogo configurável de Formatos (inicialmente Standard, Modern, Pioneer, Commander, Pauper) em que adicionar ou remover um Formato não exija alteração de código. Decisões do usuário na fase 3: o catálogo pertence ao contexto Sugestão de Deck; Formato tem flag "ativo"; preferências que apontam para Formato inativo são mantidas mas ignoradas na leitura; avatares preset são 8 a 12 placeholders neutros e o catálogo de presets é dado, não código.

## Decision

1. Tabela `deck_suggestion.format` (`id` textual estável, ex. `modern`; `name`; `is_active`; `sort_order`), criada agora por ser a primeira feature que a consome, mas de propriedade do contexto Sugestão de Deck. "Remover do catálogo" significa `is_active = false`; Formatos nunca são apagados fisicamente enquanto houver referências (FK `RESTRICT`).
2. Conta/Perfil acessa o catálogo apenas pela porta `IFormatCatalog` (consulta somente leitura, camada anticorrupção). Seleção nova só aceita Formatos ativos (REQ-015). Leitura do Perfil omite os inativos. Ao salvar o Perfil, o serviço de aplicação **preserva** as referências inativas já existentes (o cliente não as conhece e não pode removê-las sem querer).
3. Tabela `accounts.avatar_preset` (`id` textual, rótulo acessível, caminho da imagem, `is_default`, `sort_order`, `is_active`), com exatamente um preset padrão ativo (índice único parcial). As imagens dos presets são arquivos estáticos (SVG neutros) servidos pelo frontend em `/avatars/presets/<id>.svg`; adicionar um preset é adicionar um arquivo de imagem e uma linha, sem mudar código.
4. Carga inicial dos dois catálogos por migração gerada como arquivo e aplicada por humano; alterações posteriores são operações de dados (script SQL revisado e executado por humano, ou uma futura funcionalidade administrativa), sem deploy de código.

## Alternatives considered

- **Lista em arquivo de configuração (appsettings/JSON)**: sem banco, mas exige redeploy/reinício para mudar e não permite FK de integridade.
- **Remoção física do Formato com limpeza das preferências**: rejeitada pelo usuário (perda silenciosa de dado).
- **Catálogo de Formatos dentro de Conta/Perfil**: rejeitada pelo usuário; Formato é conceito de Sugestão de Deck (glossário).

## Consequences

- Existe uma FK entre schemas (`accounts.player_preferred_format.format_id -> deck_suggestion.format.id`), aceita pela ADR-0001.
- Sem perfil administrativo nesta feature, mudar catálogos depende de operação manual de um humano com acesso ao banco.
