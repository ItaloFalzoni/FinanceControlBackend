# ADR-004 — Valores em centavos (`bigint`/`long`), sem casas decimais

- Status: aceito
- Contexto: valores trafegavam como `decimal` em reais na API e `numeric(19,2)`
  no Postgres. Casas decimais no fio/banco/memória abrem três riscos: (a)
  `float`/`double` (binário IEEE-754) não representam `0.1` — `0.1+0.2 ≠ 0.3`
  — e qualquer ponto do pipeline em ponto flutuante corrompe centavos;
  (b) `numeric` tem precisão arbitrária mas é lento e o Npgsql o mapeia para
  `decimal` (128 bits), mantendo escala/arredondamento como preocupação em
  toda operação; (c) JSON com `1000.00` vs `1000` vs `"1000.00"` fragiliza o
  contrato entre back e front.
- Decisão: unidade menor em todo o sistema. `transactions.amount` é `bigint`;
  domínio/DTOs usam `long` em **centavos** (`100000` = R$ 1.000,00); o front
  trabalha internamente só com inteiros e converte para R$ apenas na exibição
  (`formatCentsBRL`, único ponto com `/100`). Fração de centavo é rejeitada na
  borda (front: `parseAmountInputToCents` devolve `null` com >2 casas;
  back: `System.Text.Json` recusa `10.5` no bind para `long` com `400`).
- Consequências:
  - (+) Aritmética inteira exata de 64 bits; `SUM` nativo e barato no Postgres;
    contrato JSON não ambíguo (`integer/int64`).
  - (+) Margem: `long.Max ≈ 9,2×10¹⁸` centavos ≈ R$ 92 quatrilhões — ordens de
    magnitude acima do teto do `numeric(19,2)` anterior (≈ R$ 10¹⁷).
  - (−) Breaking change: clientes antigos em reais quebram; deploy BE+FE juntos.
  - (−) Sem migração de dados: `AmountAsCents` altera o tipo sem converter
    linhas — banco existente deve ser recriado (`docker compose down -v`).
- Operações com `long` em memória: somas de saldo (`Account.Balance`,
  `PostgresAccountRepository.UpdateAsync`) rodam em bloco `checked` — estouro
  lança `OverflowException` (vira `500` no handler global) em vez de wrap
  silencioso. Sem teto artificial de domínio (decisão registrada); teste em
  `AccountTests.Balance_Overflow_ThrowsOverflowException`.
- Alternativas rejeitadas: manter `numeric(19,2)`/`decimal` (precisão ok, mas
  mantém escala como preocupação e `SUM` mais caro); `double` em qualquer
  camada (erro binário inaceitável para dinheiro).
- Revisitar quando: multi-moeda com expoente ≠ 2 (ex: 0 ou 3 casas) — aí a
  "unidade menor" deixa de ser universalmente "centavos".
