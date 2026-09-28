# ADR-002 — Lock pessimista (`SELECT ... FOR UPDATE`) nas escritas

- Status: aceito
- Contexto: dois `Withdraw` concorrentes lendo o mesmo saldo podiam ambos passar e estourar
  a conta (clássico check-then-act). Prova: `tests/.../Endpoints/ConcurrencyTests.cs`.
- Decisão: `PostgresAccountRepository.UpdateAsync` roda em transação com
  `SELECT id FROM accounts WHERE id = ... FOR UPDATE` + releitura do `SUM` + revalidação
  de fundos antes do `INSERT` (`src/.../Infrastructure/Repositories/PostgresAccountRepository.cs`).
- Consequências:
  - (+) Correção simples, sem coluna extra; serializa só escritas da mesma conta.
  - (−) Throughput de escrita limitado pelo lock; espera em fila sob contenção.
- Alternativas rejeitadas: concorrência otimista (`xmin`/coluna `version` + retry) — melhor
  throughput, mas retry + exceção serializada por ora não compensam o volume atual.
- Revisitar quando: contenção de escrita virar gargalo (métrica: espera de lock p95).
