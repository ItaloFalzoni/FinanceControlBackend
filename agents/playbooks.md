# Playbooks — passos de implementação

Carregue o playbook da tarefa antes de começar. Execute a [Validação obrigatória](../AGENTS.md#validação-obrigatória-definition-of-done) ao final de todos.

## A) Novo endpoint

1. **DTO**: adicione request/response records em `Application/Dtos.cs` (novo arquivo só se o assunto for totalmente novo).
2. **Validação** (se houver input): método `Validate(TReq)` estático em `Application/Validators/RequestValidators.cs` acumulando os erros em inglês (retorna `IReadOnlyList<string>`; sem pacote externo e sem registro de DI — o handler chama direto).
3. **Serviço**: método em `Application/Services/AccountService.cs` retornando DTO (ou `null` quando conta não existe). Nenhuma regra de domínio aqui.
4. **Endpoint**: em `Endpoints/AccountEndpoints.cs`, adicione `group.MapPost/MapGet(...)` com `.WithName`, `.WithSummary`, `.Produces` dos status codes reais; handler valida (`RequestValidators.Validate` → `400`) → chama service → mapeia resultado (copie o padrão dos handlers vizinhos).
5. **Testes**: integração em `tests/FinanceControl.IntegrationTests/Endpoints/` (201 + caminhos de 400/404/422 aplicáveis). **Toda rota `/api/*` exige o header `X-Api-Key`** (middleware no `Program.cs` → `401` sem a chave) — use `CreateAuthenticatedClient()` do factory, senão o teste falha antes de chegar ao handler; o caso 400 é obrigatório quando há input. A suíte cobre auth, criação, fluxo, paginação, concorrência, persistência, health e fail-closed — estenda o arquivo do assunto (ver `agents/conventions.md` → *Testes*).
6. Valide → atualize a tabela de rotas no `README.md` se a rota for nova.

## B) Nova regra de domínio

1. Leia a entidade envolvida (`Domain/Entities/`) e seus testes (`tests/FinanceControl.UnitTests/Domain/`) antes de mexer.
2. Implemente a regra **dentro da entidade** — único ponto de mutação continua sendo `Deposit`/`Withdraw` (ou método público equivalente da entidade). Nunca no service nem no endpoint.
3. Exceção nova herda de `DomainException` em `Domain/Exceptions/` (mensagem em inglês); o middleware global já converte em 422.
4. **Não** adicione estado derivado (saldo continua calculado — invariante central).
5. Teste unitário em `tests/FinanceControl.UnitTests/Domain/` cobrindo o caso feliz e o caso de exceção — **`Assert.Throws<T>` nativo** (não há lib de asserção; ver `agents/conventions.md`).
6. Se a regra afetar resposta HTTP, reflete no endpoint + teste de integração.
7. Valide.

## C) Novo DTO / alteração de contrato

1. Altere o record em `Application/Dtos.cs` e o mapper correspondente em `AccountService` (mappers são `private static` na própria classe).
2. Grep por todos os usos: `grep -rn "NomeDoDtos" src tests` — ajuste os testes que desserializam o DTO (integração + `Dashboard.test.tsx` no front quando o shape chega ao browser).
3. Alteração de contrato é mudança pública (G5): confirme com o usuário se não foi pedido explicitamente.
4. Valide.

## D) Trocar a persistência (ex.: SQL Server)

1. Leia `agents/architecture.md` → seção *Persistência (PostgreSQL + EF Core 10)*.
2. Nova implementação de `IAccountRepository` em `Infrastructure/Repositories/` — **não altere a interface** do domínio. A escrita continua transacional: valide fundos contra o banco sob lock dentro da transação (copie o padrão do `PostgresAccountRepository`).
3. Troque o registro em `Program.cs` (o lifetime precisa ser compatível com o provedor — hoje `Scoped`, porque o `DbContext` não é thread-safe).
4. Ajuste a validação/migração de startup no `Program.cs` — a aplicação não pode ganhar fallback in-memory silencioso. (A `FinanceControlWebAppFactory` — que cria um banco descartável com drop no `Dispose` — precisa acompanhar a troca de provedor.)
5. Valide — a suíte de integração (fluxo, paginação, idempotência, concorrência, persistência) é a prova do pipeline: exercite as rotas nela antes do manual.

## E) Adicionar pacote NuGet

1. Justifique antes (G6) — verifique se já existe algo equivalente em `FinanceControl.API.csproj`.
2. Instale com `dotnet add package` no projeto certo (`src/FinanceControl.API` ou o projeto de teste).
3. Pacote novo que exige registro de serviço → seção `// ── Services ──` do `Program.cs`.
4. Valide.
