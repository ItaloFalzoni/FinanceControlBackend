# AGENTS.md — FinanceControl API

Contexto para agentes de IA. **Leia este arquivo inteiro antes de qualquer tarefa.** Carregue os demais arquivos apenas quando a tabela [Carregamento por tarefa](#carregamento-por-tarefa) indicar (economia de tokens).

## Visão em 30 segundos

- C# / **.NET 10**, Minimal API, **um único projeto** `src/FinanceControl.API` com 4 camadas separadas por pasta.
- Persistência **obrigatória em PostgreSQL 18** (EF Core 10 + Npgsql; `docker compose up -d postgres`): sem banco acessível a API **não sobe** (fail-closed, migrações aplicam no startup) e não há fallback in-memory. Sem mensageria — não crie nenhuma sem pedido explícito.
- Testes: `tests/FinanceControl.UnitTests` (sem banco, <1s) + `tests/FinanceControl.IntegrationTests/Endpoints/ConcurrencyTests` (`Suite=Slow`, ~54s, exige `docker compose up -d postgres`). Padrões: `agents/conventions.md`. `dotnet test --filter 'Suite!=Slow'` roda só o rápido.
- Regra central: **saldo é sempre calculado** (`Σ SignedAmount`), nunca armazenado; mutação só via `Account.Deposit()` / `Account.Withdraw()`.
- **Conta única**: criada explicitamente via `POST /api/accounts` (`201`, `409` se já existe; em banco legado a conta **mais antiga** vence). Rotas: `/api/accounts`, `/api/balance`, `/api/transactions`, `/api/deposit`, `/api/withdraw`.
- **API Key obrigatória**: toda rota `/api/*` exige o header `X-Api-Key` e responde `401` sem ela (`ApiKeyMiddleware`; config `Authentication:ApiKey`, env `Authentication__ApiKey`, valor em `.env` → `API_KEY`; fail-closed no startup). `/health` e o documento OpenAPI ficam fora de `/api` e continuam públicos.

## Golden Rules (anti-alucinação)

| # | Regra |
|---|-------|
| G1 | **O código-fonte é a única verdade.** README e estes docs podem estar desatualizados — confirme lendo o arquivo antes de afirmar comportamento. |
| G2 | **Nunca invente caminhos, símbolos, pacotes ou endpoints.** Confirme com `glob`/`grep`/leitura antes de editar ou citar. |
| G3 | **Não suponha contexto não lido.** Se a tarefa toca um arquivo que você não leu, leia-o antes de alterar. |
| G4 | **Escopo mínimo.** Não refatore, renomeie ou "melhore" código fora do pedido. Sem mudanças colaterais. |
| G5 | **Não quebre contratos públicos** (rotas, status codes, shape dos DTOs) sem pedido explícito do usuário. |
| G6 | **Não adicione pacote NuGet, projeto ou camada** sem necessidade real; se for necessário, justifique antes de implementar. |
| G7 | **Nada de código quebrado entregue.** Se um passo da validação falhar, corrija ou reporte — nunca declare "pronto" com falha. |
| G8 | **Idioma:** identificadores e comentários em **inglês**; comunicação com o usuário em português. |
| G9 | Se faltar requisito ambíguo, **pergunte** em vez de escolher por conta própria. |

## Carregamento por tarefa

| Tarefa | Leia antes (além deste arquivo) |
|--------|--------------------------------|
| Alterar DI, camadas, fluxo de requisição, adicionar pacote, trocar persistência | `agents/architecture.md` |
| Escrever/editar código C# ou testes | `agents/conventions.md` |
| Novo endpoint / nova regra de domínio / novo DTO / trocar implementação de repositório | `agents/playbooks.md` |
| Só consultar rotas, status codes ou exemplos curl | `README.md` → *Endpoints da API* |

Fora desses casos, **não leia mais nada** — use `grep` para localizar trechos específicos.

## Mapa do repositório

```
FinanceControl.sln
src/FinanceControl.API/
  Program.cs                  # Composition root: DI, migrações, middleware global, mapeamento
  Domain/                     # Nada referencia esta camada. NÃO referencia ninguém.
    Entities/Account.cs       # Aggregate root — regras de saldo
    Entities/Transaction.cs   # Imutável; factories CreateCredit/CreateDebit
    Enums/TransactionType.cs  # Credit | Debit
    Exceptions/               # DomainException (base) → InvalidAmount / InsufficientFunds / AccountNotFound / UnknownType
    Repositories/IAccountRepository.cs
  Infrastructure/
    Persistence/                         # EF Core (→ Domain apenas)
      DatabaseOptions.cs                 # ConnectionStrings:Postgres (fail-closed)
      FinanceControlDbContext.cs         # Schema snake_case + CHECKs; SEM coluna de saldo
      AccountMapper.cs                   # Row ↔ entidade de domínio
      Entities/                          # AccountRow, TransactionRow
      Migrations/                        # dotnet-ef (tool em dotnet-tools.json)
    Repositories/PostgresAccountRepository.cs  # Transação + SELECT ... FOR UPDATE (helpers privados)
    Authentication/                      # ApiKeyMiddleware (X-Api-Key em /api/*, fail-closed) + transformer OpenAPI
  Application/
    Dtos.cs                   # Todos os records de request/response
    Services/AccountService.cs      # Orquestrador dos casos de uso
    Validators/RequestValidators.cs # Validação de input manual (sem lib) → 400
  Endpoints/AccountEndpoints.cs     # Minimal API → Application
tests/                              # Suíte sem banco (Domain/ + Validators/ + fail-closed)
  FinanceControl.UnitTests/         # Sem banco, sem DI, sem Docker
```

**Dependências permitidas:** `Endpoints → Application → Domain ← Infrastructure`. Nunca inverta.

## Comandos

```bash
docker compose up -d postgres           # Postgres 18 (exigido para rodar a API; testes não usam banco)
dotnet build FinanceControl.sln         # build rápido (0 erros esperados)
dotnet test                             # suíte completa, sem banco (<1s)
dotnet test --filter "FullyQualifiedName~AccountTests"  # um arquivo/classe
dotnet run --project src/FinanceControl.API       # sobe a API (localhost:5000; aplica migrações no startup)
dotnet dotnet-ef migrations add <Nome> --project src/FinanceControl.API --output-dir Infrastructure/Persistence/Migrations  # nova migração
```

## Validação obrigatória (Definition of Done)

Execute **nesta ordem** antes de declarar a tarefa concluída:

1. [ ] `dotnet build FinanceControl.sln` → **0 erros, 0 warnings novos**.
2. [ ] `dotnet test` → **0 falhas** (sem banco, <1s).
3. [ ] Testes **adicionados/atualizados** para todo comportamento novo ou alterado (regra de domínio/validador/fail-closed → unitário; HTTP contra banco só na evolução futura descrita no `README.md`). Exceção permitida: mudança puramente cosmética — declare isso no relato.
4. [ ] Nenhum teste existente foi deletado ou enfraquecido para "passar" — remoção deliberada só com pedido explícito do usuário.
5. [ ] Contrato HTTP inalterado (rotas, status codes, DTOs) — ou alteração pedida e comunicada.
6. [ ] Conferido o checklist de dependências (G6): nada novo foi referenciado sem necessidade.

**Relato final padrão:** arquivos alterados (caminho + por quê), comandos de validação executados e resultado resumido (ex.: `dotnet test` → 84 passed, 0 failed). Se algum passo falhou e não pôde ser corrigido, diga **claramente** o que está quebrado.

## Manutenção destes arquivos

- Mudou arquitetura, comandos ou convenções? **Atualize `AGENTS.md` e o subarquivo afetado na mesma tarefa.**
- Mantenha `AGENTS.md` enxuto (< 150 linhas) e subarquivos < 150 linhas. Detalhe demais aqui vira ruído de contexto.
