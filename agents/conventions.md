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
- Cada endpoint novo precisa de `.WithName()`, `.WithSummary()` e `.Produces<T>()` corretos (viram documentação OpenAPI).

## Testes

> **Status:** `tests/FinanceControl.UnitTests` (`Domain/`, `Validators/`, `StartupFailClosedTests` — sem banco, sem Docker). Testes HTTP contra banco foram removidos (ver `README.md` → *Testes (evolução futura)*).

- **Padrão de nome:** `Metodo_Condição_Resultado` — ex.: `Withdraw_WithInsufficientFunds_ThrowsInsufficientFundsException`.
- **Unitários** (`tests/FinanceControl.UnitTests/`): entidades puras sem mock e sem DI (`Domain/`), validadores (`Validators/`) e fail-closed de `DatabaseOptions`/`ApiKeyOptions` (`StartupFailClosedTests`, puro via `ServiceCollection`, sem HTTP e sem banco). **`Assert` nativo do xUnit** (sem lib de asserção): `Assert.Throws<T>(act)` para exceções (tipos exatos — as do domínio são `sealed`/lançadas diretamente), `Assert.Single(c)` para coleção com 1 item, `Assert.Equal` com literal `L` em `long` de centavos (ex.: `Assert.Equal(0L, account.Balance)`, senão falha a inferência de tipo) e `Assert.InRange` para janelas de tempo (`BeCloseTo`). Somas usam `checked` — estouro lança `OverflowException` (coberto em `AccountTests`).
- Nunca dependa da ordem de execução dos testes.
- Cobertura mínima por mudança nova: regra de domínio/validador/fail-closed → teste unitário.
