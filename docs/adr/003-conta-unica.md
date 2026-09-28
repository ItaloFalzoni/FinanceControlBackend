# ADR-003 — Conta única com criação explícita (aceito; supersede seed no startup)

- Status: aceito (supersede seed removido; criação via `POST /api/accounts`)
- Contexto: só movimentação, saldo e histórico de "uma conta empresarial". O
  front precisa de um first-run explícito (tela "Começar") em vez de deploy
  populando a tabela.
- Decisão: `Program.cs` roda só `Migrate()` no startup. A conta é criada via
  `POST /api/accounts` (corpo vazio; melhor prática REST: plural na coleção):
  `201 {accountId}` quando cria, `409` quando já existe (duplo clique, retry ou
  corrida — o front trata `409` como sucesso e segue para `GET`).
  Concorrência é serializada com `pg_advisory_xact_lock` dentro de
  `PostgresAccountRepository.CreateSingleAsync` (check+insert atômicos, sem mudar
  o schema). Banco vazio responde `404` em balance/history/deposit/withdraw até
  a criação (first-run real, não mais caminho defensivo).
  Em banco legado com N contas, a mais antiga (`created_at`, desempate `id`) vence em
  todo o app.
- Consequências:
  - (+) Deploy não escreve dados; first-run explícito e auditável.
  - (+) Criação concorrente segura via lock consultivo (desenho sem teste dedicado; a prova de concorrência viva é `ConcurrencyTests` dos saques).
  - (+) Nenhuma rota carrega `{id}` (singleton continua).
  - (−) Cliente precisa chamar a criação antes do fluxo (front mostra "Começar").
  - (−) Multi-tenant exige migração de contrato (`IAccountResolver` dedicado + prefixo `/api/v1`).
- Alternativa considerada (não adotada, não é evolução — é outro desenho):
  tabela `accounts` aceitando somente 1 linha (constraint singleton, ex.:
  coluna `singleton boolean DEFAULT true UNIQUE CHECK (singleton)`).
  Prós: o próprio banco barra a segunda linha mesmo sem lock na aplicação;
  corrida vira erro de constraint em vez de `409` lógico. Contras: regra de
  negócio vazando para DDL; mensagem de erro críptica (`unique violation`)
  exigindo tradução para `409`; migração adicional para um invariante que o
  lock consultivo já garante; dificulta um futuro multi-tenant (teria que
  dropar a constraint). Decisão: manter unicidade lógica via
  `CreateSingleAsync` + lock; revisitar o singleton físico só se surgir
  escrita concorrente fora da API (jobs SQL diretos, múltiplos writers
  bypassando o repositório).
- Outras opções rejeitadas para a criação:
  - `INSERT ... WHERE NOT EXISTS` / `ON CONFLICT DO NOTHING` atômico no SQL
    (equivalente ao lock; lock foi preferido por reaproveitar o repositório).
  - Seed em migration SQL ou job de init (volta a popular no deploy — oposto
    do pedido).
- Revisitar quando: surgir segunda conta real (multi-empresa, multi-moeda por conta).
