# Convenções de código e testes

Carregue ao escrever ou editar C# (`src/` ou `tests/`). Siga o estilo do arquivo que estiver editando — o código existente é o exemplo canônico.

## Código (src/)

- File-scoped namespaces (`namespace X.Y;`), `Nullable` + `ImplicitUsings` habilitados.
- Classes de domínio `public sealed class`; props com `private init` para imutabilidade de estado externo.
- Serviços com **primary constructor** (`public sealed class AccountService(IAccountRepository repository, IAccountResolver resolver)`).
- Async sempre com sufixo `Async` e `CancellationToken ct = default` nos métodos de serviço/repositório.
- DTOs: **records** em `Application/Dtos.cs` — adicione novos records nesse arquivo, não crie arquivos novos sem motivo.
- Exceptions: herdam de `DomainException` (mensagem em inglês, formato parelha com as existentes).
- Validação de **input** (formato, obrigatoriedade, limites) → métodos estáticos em `Application/Validators/RequestValidators.cs` (sem pacote externo). Regra de **negócio** (saldo etc.) → dentro da entidade de domínio. Não troque os dois de lugar.
- Comentários em inglês; seção headers com `// ── Nome ──...` como nos arquivos existentes.
- Cada endpoint novo precisa de `.WithName()`, `.WithSummary()` e `.Produces<T>()` corretos (viram documentação OpenAPI/Scalar).

## Testes

> **Status:** `tests/FinanceControl.UnitTests` (`Domain/`, `Validators/`, sem banco); `tests/FinanceControl.IntegrationTests` (criação + auth + fluxo + paginação + concorrência + persistência + seed + legado + contrato + cobertura + health + fail-closed). As regras abaixo descrevem o padrão seguido.

- **Padrão de nome:** `Metodo_Condição_Resultado` — ex.: `Withdraw_WithInsufficientFunds_ShouldThrowInsufficientFundsException` (unitários) / `Withdraw_WithInsufficientFunds_Returns422AndKeepsBalance` (integração).
- **Unitários** (`tests/FinanceControl.UnitTests/Domain/`): testam entidades puras, sem mock e sem DI. **`Assert` nativo do xUnit** (sem lib de asserção): `Assert.Throws<T>(act)` para exceções (tipos exatos — as do domínio são `sealed`/lançadas diretamente), `Assert.Single(c)` para coleção com 1 item, `Assert.Equal` com literal `L` em `long` de centavos (ex.: `Assert.Equal(0L, account.Balance)`, senão falha a inferência de tipo) e `Assert.InRange` para janelas de tempo (`BeCloseTo`). Somas usam `checked` — estouro lança `OverflowException` (coberto em `AccountTests`).
- **Integração** (`tests/FinanceControl.IntegrationTests/Endpoints/`): **`FinanceControlWebAppFactory`** (`WebApplicationFactory<Program>` que cria um banco PostgreSQL descartável no `CreateHost` e dropa no `Dispose`; a aplicação não tem repositório em memória para trocar) — classe de teste implementa `IDisposable`, cria **próprio** factory + `HttpClient` no construtor (isolamento — hoje isso custa ~188 ms por teste: `CREATE DATABASE` + `Migrate()` + `DROP DATABASE`; se a suíte crescer, avalie compartilhar o factory por classe via `IClassFixture`). Requests via `PostAsJsonAsync`/`GetAsync`; asserts em `HttpStatusCode` e no DTO desserializado. **Toda rota `/api/*` exige o header `X-Api-Key`** (middleware no `Program.cs`; sem a chave a resposta é `401`): use `CreateAuthenticatedClient()` (já manda `FinanceControlWebAppFactory.TestApiKey`) ou adicione o header `FinanceControlWebAppFactory.ApiKeyHeaderName` ao client — o factory injeta a chave no host, então nenhum teste lê `appsettings`. **Pré-requisito:** Postgres no ar (`docker compose up -d postgres`) — credenciais em `POSTGRES_TEST_ADMIN_CONNECTION` (default: `financecontrol`/`dev-local-pg-password-change-me`, igual ao `.env.example`).
- Nunca compartilhe factory/client entre classes de teste sem motivo; nunca dependa da ordem de execução dos testes.
- **Mesmo banco em duas factories** (ex.: persistência entre restarts): use o overload `FinanceControlWebAppFactory(string databaseName, bool dropDatabaseOnDispose = true)` passando um nome único (ex.: `financecontrol_persistence_{Guid:N}`) e `dropDatabaseOnDispose: false` nas duas — aí o drop do banco é responsabilidade de um `Dispose()` próprio da classe de teste (crie uma "dropper factory" no dispose, mesmo em falha). Sem esse cuidado o banco vaza.
- **Acesso direto ao `DbContext`** nos testes de integração é permitido: `FinanceControl.API.csproj` declara `<InternalsVisibleTo Include="FinanceControl.IntegrationTests" />` para os `*Row`/`DbSet` internos (ex.: injetar linha inválida via `ExecuteSqlRaw` para provar rollback). Use com parcimônia — o caminho padrão continua sendo HTTP.
- Cobertura mínima por mudança nova: regra de domínio → teste unitário; comportamento HTTP novo/alterado → teste de integração (status code esperado).
- `BalanceResponse`/`TransactionResponse`/etc. vêm de `FinanceControl.API.Application` — use `using FinanceControl.API.Application;`.
