# Arquitetura — FinanceControl API

Contexto profundo. Carregue apenas quando a tarefa tocar DI, camadas, fluxo de requisição, persistência ou pacotes.

## Fluxo de uma requisição

```
HTTP
 └─ Serilog request logging
     └─ Middleware global (Program.cs)      → captura DomainException → 422 | Exception → 500
         └─ ApiKeyMiddleware                → sem X-Api-Key válida: 401
             └─ Endpoint (AccountEndpoints)     → valida input com RequestValidators (estático) → inválido: 400
                 └─ AccountService              → orquestração + map para DTOs; conta ausente do banco → null (+ spans/contadores OTel)
                     └─ IAccountResolver + IAccountRepository → PostgresAccountRepository (Scoped, transacional)
                         └─ Account (entidade)  → regras de negócio; lança exceções de domínio
```

Decisões de mapeamento de erro (não reproduzir de memória — conferir `Program.cs` e `AccountEndpoints.cs`):

| Situação | Status | Onde é decidido |
|---|---|---|
| Validação de input inválida | 400 | handler do endpoint (com `Errors[]`; `Detail` = join legado) |
| Corpo malformado / `10.5` em `long` | 400 | middleware global (`BadHttpRequestException`/`JsonException` → `Validation failed`) |
| `?page/?pageSize` inválidos | 400 | handler do endpoint |
| Conta inexistente no banco | 404 | endpoint (retorna `null` do service) — first-run: só some após `POST /api/accounts` |
| Conta removida entre leitura e escrita | 404 | middleware global (`AccountNotFoundException`) + service traduz para `null` → mesmo shape do endpoint |
| Conta já existe na criação | 409 | endpoint de criação (`CreateSingleAsync` retornou `Created: false`) |
| `InsufficientFundsException` | 422 | middleware global (`Insufficient funds`) — vale para withdraw via service e para a revalidação em `UpdateAsync` |
| `OverflowException` (saldo estouraria `long`) | 422 | middleware global |
| `Npgsql.PostgresException` SQLSTATE `22003` (overflow do `SUM` no banco) | 422 | middleware global (mesmo shape do `OverflowException`) |
| Outra `DomainException` | 422 | middleware global |
| Exceção inesperada | 500 | middleware global (mensagem só em Development) |

> Observe: `InvalidAmountException` **não** tem `catch` específico nos endpoints — ela vira 422 pelo middleware. Nunca mude esse comportamento sem pedido explícito.

## Lifetimes de DI (Program.cs)

| Serviço | Lifetime | Motivo |
|---|---|---|
| `IAccountRepository` → `PostgresAccountRepository` | Scoped | concreto registrado uma vez e repassado às duas interfaces (`GetRequiredService`), então cada request compartilha **uma** instância; o `FinanceControlDbContext` do EF não é thread-safe — não pode ser compartilhado entre requests |
| `IAccountResolver` → `PostgresAccountRepository` | Scoped | mesma instância do repositório acima; leituras separadas por necessidade: agregado completo (writes), saldo (SUM) e histórico paginado (COUNT + SUM + Skip/Take no banco) |
| `AccountService` | Scoped | |
| `DatabaseOptions` (`AddOptions().Bind().Validate().ValidateOnStart()`) | Options | connection string obrigatória; valor vem de `ConnectionStrings:Postgres` (env `ConnectionStrings__Postgres`). `Program.cs` roda `Migrate()` antes do `Run()` — sem Postgres, o host **não** sobe (fail-closed, sem fallback in-memory) |

> A validação de input **não** tem serviço de DI: são métodos estáticos em `Application/Validators/RequestValidators.cs`, chamados direto pelos handlers.

## Conta única (criação explícita)

- Criação explícita: `POST /api/accounts` (corpo vazio) cria **a** conta do sistema (`201 {accountId}`); nenhuma outra rota cria, lista ou busca contas e nenhuma rota carrega `{id}` — balance, history, deposit e withdraw operam sobre ela.
- **Concorrência:** `PostgresAccountRepository.CreateSingleAsync` abre transação + `pg_advisory_xact_lock`, re-lê só o header (helper privado `GetOldestHeaderAsync`, sem join de transações) e só insere se vazio — duplo clique/retry/duas réplicas viram `1x201 + 1x409`, nunca 2 linhas (detalhes e alternativa singleton físico em `docs/adr/003-conta-unica.md`).
- **Desempate:** em banco legado com mais de uma linha, a conta de menor `created_at` (e menor `id`) vence — o helper privado `OldestAccountsFirst()` é a única fonte dessa regra; `GetOldestHeaderAsync` (header/saldo/histórico/criação) e `GetSingleAccountAsync` (agregado dos writes) só acrescentam `Select`/`Include` em cima dele.
- **Histórico paginado no banco:** `GetHistoryPageAsync` resolve COUNT + SUM + Skip/Take em SQL (mais recente primeiro, desempate por `id`; `Skip` em `checked`); nada de `Skip/Take` em memória. `AccountService.GetHistoryAsync` valida `page >= 1, 1 <= pageSize <= 200` (defesa em profundidade além do endpoint). `GetCurrentBalanceAsync` é header + SUM; só Deposit/Withdraw carregam o agregado completo.
- **Sem conta ainda** (first-run) ⇒ service devolve `null` ⇒ `404` (coberto em `tests/.../Endpoints/PersistenceTests.cs` + `AccountCreationTests.cs`); o front mostra a tela "Começar".

**Ao adicionar qualquer serviço:** registre no `Program.cs` na seção `// ── Services ──`, senão o handler lança exceção de runtime em tempo de request (a suíte não cobre o registro de serviços, então essa falha só aparece na verificação manual).

## Invariantes de domínio (nunca quebrar)

1. `Account.Balance` é **propriedade calculada em centavos** (soma verificada `checked` de `SignedAmount: long`) — não existe campo de saldo a "sincronizar". Ver ADR-004.
2. Únicos pontos de mutação: `Deposit()` e `Withdraw()`. Não exponha `_transactions` mutável (o getter devolve `AsReadOnly()`). `Deposit()` falha rápido com `OverflowException` (via `checked Balance + amount`) antes de adicionar; `Withdraw()` lê `Balance` uma vez e reutiliza na projeção e na exceção.
3. `Withdraw()` valida **antes** de criar/adicionar a transação (nada é registrado quando lança exceção).
4. `Transaction` é criado só pelas factories `CreateCredit`/`CreateDebit`; `amount <= 0` → `InvalidAmountException`. `Amount` é `long` em **centavos** — fração de centavo nunca entra no sistema (ver ADR-004).
5. `Transaction.SignedAmount` define o sinal pelo `Type` — qualquer novo tipo em `TransactionType` precisa de decisão explícita nesse método.

## Persistência (PostgreSQL + EF Core 10)

- **O banco é obrigatório** — não existe fallback in-memory. `Program.cs` valida `DatabaseOptions` e roda `Migrate()` antes de `app.Run()`; Postgres indisponível ⇒ o host não sobe (fail-closed).
- Esquema (snake_case, migrações em `Infrastructure/Persistence/Migrations/`): `accounts` (id, created_at com default `now()` — **sem nome**, produto refatorado) + `transactions` (id, account_id, amount **`bigint` (centavos, ver ADR-004)**, type, description, created_at com default `now()`; FK `ON DELETE CASCADE`; CHECKs `amount > 0` e `type IN (1, 2)` derivados do enum; índice composto `IX_transactions_account_history (account_id, created_at, id)` cobrindo o `ORDER BY + Skip/Take` do histórico e as buscas por conta). **Não existe coluna de saldo** — `Σ SignedAmount` é sempre calculado (leitura via `SUM`/reidratação, nunca armazenado). O caminho feliz continua mandando `CreatedAt` da aplicação; o default só cobre inserts crus/SQL. Idempotência de escritas é **futura** (desenho em `docs/adr/005-idempotencia.md`; migrações de ida/volta preservadas no histórico).
- Escrita acontece só no `PostgresAccountRepository.UpdateAsync`, dentro de transação: `BeginTransaction` → `SELECT … FOR UPDATE` na linha de `accounts` (SQL bruto — EF não expõe `FOR UPDATE` fluent) → `SUM` fresco → valida fundos → `INSERT` das transações pendentes → `Commit`. Rejeição ⇒ `Rollback` + `InsufficientFundsException` (422 com a mesma mensagem de sempre) — saques concorrentes não conseguem estourar o saldo.
- Reidratação: linhas (`Infrastructure/Persistence/Entities/*Row`) viram entidades pelas factories `internal` `Account.Reconstitute`/`Transaction.Reconstitute` — mutação continua só via `Deposit()`/`Withdraw()`; as transações novas geradas no agregado é o que o repositório persiste (diff por `Id`).
- **Trocar de implementação:** siga o playbook D; troque o `AddScoped<IAccountRepository, ...>` no `Program.cs`. Não altere a interface de domínio para acomodar o banco. (A `FinanceControlWebAppFactory` de teste — banco descartável via `POSTGRES_TEST_ADMIN_CONNECTION`, drop no `Dispose` — **existe** e precisa acompanhar a troca de provedor.)

## Documentação / infraestrutura existente

- OpenAPI: `AddOpenApi()` + `ApiKeySecuritySchemeTransformer` (anuncia o security scheme `ApiKey` → header **`X-Api-Key`**, exigido em toda rota `/api/*`); `MapOpenApi()` e referência **Scalar** em `/scalar/v1` **apenas em Development**.
- Health: `/health` liveness-only (healthcheck do compose) + `/health/ready` com `AddDbContextCheck<FinanceControlDbContext>` (pacote `HealthChecks.EntityFrameworkCore 10.0.12`).
- Sem rate limiting na aplicação (decisão deliberada, G6 — evolução via API gateway na infra com `429` + `Retry-After`; ver `README.md` → *Melhorias Futuras*).
- Logs: Serilog (`Serilog.AspNetCore 10.0.0`, `CompactJsonFormatter` no stdout) + `UseSerilogRequestLogging`.
- Traces/métricas: `Application/Telemetry/AccountTelemetry.cs` (`ActivitySource` + `Meter` `FinanceControl.Account`, spans `account.deposit|withdraw|balance|history`, contadores `deposit_total/withdraw_total/balance_read_total/history_read_total`); exportador console opt-in (`OTEL_CONSOLE_EXPORTER=true`, default off no compose; pacotes `OpenTelemetry.* 1.19.x`).
- `public partial class Program { }` no fim de `Program.cs` é **obrigatório** para o `WebApplicationFactory` dos testes de integração — não remova, os testes existentes dependem dele.
- Docker: multi-stage build sem etapa de teste (testes rodam em CI/local via `dotnet test` com Postgres; a integração exige banco e não há banco na fase de build). O compose sobe `postgres:18.6` (volume `postgres_data` montado em `/var/lib/postgresql` — layout do PG 18+; healthcheck `pg_isready`) e a API com `depends_on: service_healthy`. O compose interpola `${POSTGRES_DB}`, `${POSTGRES_USER}` e `${POSTGRES_PASSWORD}` do `.env` (raiz; `.env.example` é o modelo) para `ConnectionStrings__Postgres`; `ASPNETCORE_ENVIRONMENT` também vem do `.env` (default `Production`, mesmo compose para dev/prod).
- **Reset total do banco** (produto novo / esquema recriado): `docker compose down -v` apaga o volume; as migrações são uma única `InitialCreate` (`bigint`, sem histórico — squash pré-produção, ver ADR-004). Para recriar migrações do zero: apague a pasta `Migrations` e rode o playbook abaixo com a connection string no ambiente (o `Program.cs` roda `Migrate()` no design-time):
  `ConnectionStrings__Postgres=... dotnet dotnet-ef migrations add <Nome> --project src/FinanceControl.API --output-dir Infrastructure/Persistence/Migrations`

## Pacotes já referenciados

`Microsoft.AspNetCore.OpenApi 10.0.12`, `Scalar.AspNetCore 2.0.14`, `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3` (provider EF Core 10 para Postgres — único pacote novo da migração, G6) e `Microsoft.EntityFrameworkCore.Design 10.0.4` (`PrivateAssets=all`, só para gerar migrações; tool `dotnet-ef` 10.0.4 no manifest local `dotnet-tools.json`). Observabilidade (G6 justificado na trilha de arquiteto): `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore 10.0.12`, `Serilog.AspNetCore 10.0.0`, `OpenTelemetry.Extensions.Hosting` + `OpenTelemetry.Instrumentation.AspNetCore` + `OpenTelemetry.Exporter.Console 1.19.x`. Nos testes: xunit 2.9.3, Microsoft.NET.Test.Sdk 17.14.0 e Microsoft.AspNetCore.Mvc.Testing 10.0.0 (usado pela `FinanceControlWebAppFactory` dos testes de integração; asserções via `Assert` nativo do xUnit — nenhuma lib de asserção). Cobertura: `coverlet.collector 10.0.1` nos dois projetos de teste (`PrivateAssets=all`, só o coletor VSTest — o relatório é gerado fora do build pelo tool `dotnet-reportgenerator-globaltool`; comandos no `README.md` → *Como Testar*). A validação de input é manual, sem pacote externo. Qualquer outro pacote exige justificativa (G6).
