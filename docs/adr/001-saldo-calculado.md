# ADR-001 — Saldo sempre calculado, nunca armazenado

- Status: aceito
- Contexto: conta empresarial com entradas/saídas; requisito "nunca permitir saldo negativo".
  Alternativa natural seria coluna `balance` em `accounts` atualizada a cada escrita.
- Decisão: `Account.Balance` é propriedade calculada (`_transactions.Sum(t => t.SignedAmount)`,
  `src/FinanceControl.API/Domain/Entities/Account.cs`); o banco não tem coluna de saldo
  (`bigint` de centavos só em `transactions.amount`, ver ADR-004). Escrita = append de transação; leitura = `SUM`.
- Consequências:
  - (+) Impossível divergir saldo x extrato (sem dual-write); auditoria natural.
  - (−) Leitura O(n) por conta; extrato full agrava (paginação implementada em
    `GET /api/transactions?page=&pageSize=`, ver `PostgresAccountRepository.GetHistoryPageAsync`).
- Alternativas rejeitadas: coluna materializada + trigger/`CHECK(balance >= 0)` (acopla banco à
  regra, reintroduz drift); snapshot mensal (complexidade sem volume que justifique).
- Revisitar quando: extrato > 100k linhas/conta ou leitura p95 estourar SLO.
