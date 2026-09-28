# ADR-005 — Idempotência via `Idempotency-Key` em deposit/withdraw (FUTURA)

- Status: **proposto, não implementado** (removido do código nesta fase; este ADR é a referência de desenho para a implementação futura)

## Por que isso é crítico num sistema financeiro

Dinheiro não tolera "talvez duplicou":

- **Retry é inevitável.** Timeouts de rede, duplo clique, botão apertado duas vezes,
  `fetch` repetido após tela de erro — qualquer um deles reenvia a mesma intenção.
  Sem idempotência, cada reenvio cria uma **transação nova**: um depósito de R$ 100
  vira R$ 200; um saque de R$ 60 debita R$ 120. O saldo deixa de refletir a realidade
  e a correção é manual (estorno), com custo operacional e risco jurídico.
- **O `409` da criação de conta não cobre isso.** `POST /api/accounts` é idempotente
  por natureza (uma conta só), mas `deposit`/`withdraw` criam um recurso novo a cada
  chamada — são eles que precisam da proteção.
- **Auditoria exige unicidade de intenção.** Cada linha do extrato deveria corresponder
  a exatamente uma intenção do operador. Duplicatas silenciosas quebram conciliação.
- **Concorrência agrava.** Dois envios simultâneos da mesma intenção (abas duplicadas,
  retry automático + clique manual) passam por qualquer trava de UI — só o banco,
  com constraint única, decide quem vence.

Em resumo: sem `Idempotency-Key`, todo retry é uma aposta. Com ela, retry é seguro
por construção — a propriedade mais importante de uma API que movimenta valores.

## Desenho aprovado (quando implementar)

- Header opcional `Idempotency-Key` (trim, 1-64 chars) só em
  `POST /api/deposit|withdraw`. Tabela `idempotency_keys` (`key` PK varchar(64),
  `account_id`, `transaction_id` único, `amount`, `type`, `description`,
  `created_at`, `expires_at` TTL 24h com índice; FKs em cascata).
- `AccountService` faz pre-check (replay não revalida fundos nem reconta telemetria);
  `PostgresAccountRepository.UpdateAsync` revalida na transação (`FOR UPDATE`) e trata
  `UniqueViolation` da corrida (vencedor insere, perdedor faz replay).
- Mesma chave + payload diferente → `422 Idempotency conflict`; chave inválida → `400`;
  sem chave → comportamento atual (duplica). Chave expirada vira operação nova
  (remoção preguiçosa + `DELETE FROM idempotency_keys WHERE expires_at < now();`
  de rotina).
- Histórico de referência: a implementação completa existiu neste repositório
  (migrações `AddIdempotencyKeys`, `AddIdempotencyExpiry`, removidas por
  `RemoveIdempotencyKeys`; testes `Endpoints/IdempotencyTests.cs` com replay,
  conflito, expiração e concorrência) — `git log` recupera cada peça.

## Alternativas rejeitadas

- TTL 24h + job dedicado de limpeza desde o dia 1 — mais código sem necessidade
  inicial; a remoção preguiçosa + DELETE de rotina bastam.
- Idempotência em `POST /api/accounts` — já coberta por `409`.
- Revisitar quando: o front ganhar retry automático, ou a primeira duplicata real
  aparecer em produção.
