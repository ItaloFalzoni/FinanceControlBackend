# FinanceControl API

API RESTful para controle de movimentações de uma conta empresarial, desenvolvida em **C# com .NET 10** utilizando **Minimal APIs**.

---

## Sumário

- [Sobre a Solução](#sobre-a-solução)
- [Decisões Técnicas](#decisões-técnicas)
- [Estrutura do Projeto](#estrutura-do-projeto)
- [Pré-requisitos](#pré-requisitos)
- [Como Executar](#como-executar)
- [Como Testar](#como-testar)
- [Executar com Docker](#executar-com-docker)
- [Endpoints da API](#endpoints-da-api)
- [Melhorias Futuras](#melhorias-futuras)

---

## Sobre a Solução

A aplicação permite que uma empresa controle movimentações de sua conta empresarial, oferecendo:

- ✅ Registro de **entradas** (depósitos/créditos)
- ✅ Registro de **saídas** (saques/débitos/pagamentos)
- ✅ Consulta do **saldo disponível**
- ✅ Consulta do **histórico de movimentações**
- ✅ Regra de negócio: **saldo nunca pode ser negativo**

O sistema trabalha com **conta única**: ela é criada explicitamente via `POST /api/accounts` (tela "Começar" no front) e todas as rotas operam sobre ela. A API também exige **API key** em toda rota `/api/*` (veja [Segurança](#segurança)).

---

## Decisões Técnicas

### Arquitetura: Vertical Slice leve com separação por responsabilidade

O projeto adota uma separação clara em camadas dentro de um único projeto de API:

```
Domain       → Entidades ricas, exceções de domínio, interface de repositório
Infrastructure → Implementação do repositório (PostgreSQL via EF Core) e mapeamento de persistência
Application  → DTOs, validação de entrada, serviço de aplicação (orquestrador)
Endpoints    → Mapeamento das rotas Minimal API (substituindo Controllers)
```

### Entidade Rica (Rich Domain Model)

A entidade `Account` encapsula todas as regras de negócio:
- `Deposit()` e `Withdraw()` são os únicos pontos de entrada para mutar o estado
- `Withdraw()` lança `InsufficientFundsException` antes de adicionar a transação
- O saldo é **calculado** a partir das transações, nunca armazenado separadamente — eliminando a possibilidade de inconsistência

### Persistência: PostgreSQL + EF Core (obrigatória)

A aplicação **depende do banco**: sem PostgreSQL o host não sobe (connection string validada no startup + `Migrate()` antes de servir requisições).

- **Schema** (`accounts` + `transactions`, migrações EF versionadas): o saldo é sempre a soma dos valores assinados das transações (`Σ SignedAmount`), calculada em cada leitura. Valores são **`bigint` em centavos** (ver ADR-004) — nem a API nem o banco lidam com casas decimais.
- **Validação transacional:** escritas rodam dentro de transação no banco. Saques concorrentes são serializados: o segundo enxerga o saldo já debitado e recebe `422`.
- **Stack:** `Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3` (EF Core 10), snake_case, `bigint` (centavos — ver ADR-004), FK `ON DELETE CASCADE` e CHECKs espelhando as invariantes de domínio.
- Reidratação via factories `internal` `Account.Reconstitute`/`Transaction.Reconstitute`; mutação continua **só** via `Deposit()`/`Withdraw()`.

### Conta única com criação explícita

O `Program.cs` roda só as migrações no startup — a conta é criada via `POST /api/accounts` (corpo vazio; `201 {accountId}`, `409` se já existe). Criação concorrente (duplo clique, retry) é serializada com lock consultivo, sem duplicar. Quando um banco legado contém mais de uma conta, a **mais antiga** (`created_at`) vence e é usada por toda a aplicação. Banco vazio responde `404` até a criação (ver ADR-003).

### Validação: manual (sem dependência externa)

Validações de entrada (campos obrigatórios, limites de tamanho, valores positivos) são feitas por métodos estáticos em `Application/Validators/RequestValidators.cs`, na camada de aplicação e separadas das regras de domínio puras. Todos os erros são acumulados e devolvidos em uma única resposta `400` (`ErrorResponse("Validation failed", ...)`).

### Segurança

A API exige **API key** em toda rota `/api/*`: o `ApiKeyMiddleware` compara o header **`X-Api-Key`** com a configuração `Authentication:ApiKey` (env `Authentication__ApiKey`, valor no `.env` → `API_KEY`) e responde **`401`** antes de chegar aos handlers. A chave é validada no startup (fail-closed: sem ela o host não sobe) e comparada em tempo constante. `/health`, `/health/ready` e o documento OpenAPI ficam fora de `/api` e continuam públicos (o healthcheck do container depende disso).

A chave é **fixa e compartilhada**. Detalhes e evolução em [`docs/api-key.md`](docs/api-key.md).

A chave pertence a quem **chama** a API e existe só no servidor do cliente. No front-end ela é injetada pelo proxy `app/api/*` e nunca chega ao browser. Para expor o serviço fora da rede local/privada, além da chave, proteja a camada de transporte (gateway, rede interna, mTLS) ou evolua para autenticação por usuário. Ver [Melhorias Futuras](#melhorias-futuras).

### Documentação: OpenAPI

Em ambiente de desenvolvimento (`ASPNETCORE_ENVIRONMENT=Development`), o contrato machine-readable está disponível em `/openapi/v1.json`.

### Testes

`dotnet test --filter 'Suite!=Slow'` roda o rápido sem banco (<1s). A prova de concorrência (`Suite=Slow`, ~54s) exige `docker compose up -d postgres`:

Projeto `FinanceControl.UnitTests` (sem banco, sem Docker):

- **Domínio** (`Domain/AccountTests`, `Domain/TransactionTests`): regras de saldo, `Deposit`/`Withdraw`, saldo insuficiente, overflow `checked`, `SignedAmount`.
- **Validadores** (`Validators/RequestValidatorsTests`): input (`amount`, `description`) → `400`.
- **Fail-closed** (`StartupFailClosedTests`): `DatabaseOptions`/`ApiKeyOptions` vazios impedem o start via `OptionsValidationException` (puros, sem HTTP e sem banco).

Padrões de nomenclatura e estrutura: `agents/conventions.md`. Concorrência (`Endpoints/ConcurrencyTests`, `Suite=Slow`): 2 saques de 6000 com saldo 7000 ⇒ `201 + 422` e saldo 1000; 2 depósitos concorrentes ⇒ ambos `201`.

---

## Estrutura do Projeto

```
FinanceControl/
├── FinanceControl.sln
├── Dockerfile
├── docker-compose.yml
├── .env.example                    # Modelo de variáveis de ambiente (copie para .env)
├── .env                            # Valores locais (gitignored)
├── .gitignore
├── README.md
├── dotnet-tools.json                          # Tool local dotnet-ef (migrações)
│
├── src/
│   └── FinanceControl.API/
│       ├── Program.cs                         # Composition root, migrações, middleware, DI
│       ├── FinanceControl.API.csproj
│       ├── appsettings.json
│       ├── appsettings.Development.json
│       │
│       ├── Domain/
│       │   ├── Entities/
│       │   │   ├── Account.cs                 # Aggregate root com regras de negócio
│       │   │   └── Transaction.cs             # Imutável; factories CreateCredit/CreateDebit
│       │   ├── Enums/
│       │   │   └── TransactionType.cs         # Unknown | Credit | Debit
│       │   ├── Exceptions/
│       │   │   ├── DomainException.cs         # Base abstrata
│       │   │   ├── InsufficientFundsException.cs
│       │   │   ├── InvalidAmountException.cs
│       │   │   ├── AccountNotFoundException.cs # Conta removida entre leitura e escrita → 404
│       │   │   └── UnknownTransactionTypeException.cs # Type 0/corrompido → 422
│       │   └── Repositories/
│       │       └── IAccountRepository.cs
│       │
│       ├── Infrastructure/
│       │   ├── Authentication/
│       │   │   ├── ApiKeyMiddleware.cs        # X-Api-Key em /api/*, 401, comparação constant-time
│       │   │   ├── ApiKeyOptions.cs           # Authentication:ApiKey (fail-closed)
│       │   │   └── ApiKeySecuritySchemeTransformer.cs # Anuncia ApiKey no OpenAPI
│       │   ├── Persistence/
│       │   │   ├── DatabaseOptions.cs           # Config ConnectionStrings:Postgres
│       │   │   ├── FinanceControlDbContext.cs   # Modelo EF (schema snake_case + CHECKs)
│       │   │   ├── AccountMapper.cs             # Row ↔ entidade de domínio
│       │   │   ├── Entities/                    # AccountRow, TransactionRow
│       │   │   └── Migrations/                  # Migrações EF geradas (dotnet-ef)
│       │   └── Repositories/
│       │       └── PostgresAccountRepository.cs # Transação + SELECT ... FOR UPDATE
│       │
│       ├── Application/
│       │   ├── Dtos.cs                        # Records de request/response
│       │   ├── Services/
│       │   │   └── AccountService.cs          # Orquestrador dos casos de uso
│       │   └── Validators/
│       │       └── RequestValidators.cs       # Validação de entrada (sem lib)
│       │
│       └── Endpoints/
│           └── AccountEndpoints.cs            # Minimal API route mapping
│
└── tests/
    ├── FinanceControl.UnitTests/             # sem banco, sem Docker
    └── FinanceControl.IntegrationTests/Endpoints/ConcurrencyTests.cs  # Suite=Slow, exige Postgres
```

---

## Pré-requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://www.docker.com/) e Docker Compose — a aplicação **exige PostgreSQL 18** (subi-lo via Compose é o caminho padrão). Os testes **não** exigem banco nem Docker.

---

## Como Executar

### 1. Clonar o repositório

```bash
git clone https://github.com/<seu-usuario>/FinanceControl.git
cd FinanceControl
```

### 2. Subir o PostgreSQL

```bash
cp .env.example .env   # se ainda não existir (define POSTGRES_* locais)
docker compose up -d postgres
```

### 3. Rodar a aplicação

A connection string local vem de `appsettings.Development.json` (`ConnectionStrings:Postgres`, aponta para o Postgres do Compose acima); para sobrescrever, use `ConnectionStrings__Postgres`:

```bash
dotnet run --project src/FinanceControl.API
```

No startup a API valida a configuração e **aplica as migrações automaticamente**. A conta é criada via `POST /api/accounts` (tela "Começar" no front); banco vazio responde `404` até lá. Sem banco acessível ela não sobe (fail-closed).

A API estará disponível em:
- HTTP: `http://localhost:5000`
- HTTPS: `https://localhost:5001`

### 4. Acessar o documento OpenAPI

Com `ASPNETCORE_ENVIRONMENT=Development`, abra no navegador: `https://localhost:5001/openapi/v1.json`

---

## Como Testar

> `dotnet test --filter 'Suite!=Slow'` roda o rápido sem banco (<1s). A suíte completa exige `docker compose up -d postgres` (`ConcurrencyTests`, ~54s).

### Executar todos os testes

```bash
dotnet test
```

### Apenas um arquivo/classe

```bash
dotnet test --filter "FullyQualifiedName~AccountTests"
```

---

## Executar com Docker

### Configurar o ambiente

O Docker Compose lê o arquivo `.env` da raiz automaticamente:

```bash
cp .env.example .env   # depois ajuste POSTGRES_PASSWORD (e ASPNETCORE_ENVIRONMENT)
```

Sem `POSTGRES_DB`, `POSTGRES_USER` ou `POSTGRES_PASSWORD` definidos o `docker compose` falha na subida (a API também não iniciaria: ela valida a connection string no startup).

O compose sobe dois serviços: `postgres:18.6` (volume `postgres_data`, dados persistem entre restarts, healthcheck `pg_isready`) e a API, que espera o banco ficar **healthy** antes de subir e recebe `ConnectionStrings__Postgres` apontando para o serviço `postgres`.

### Build e execução com Docker Compose

```bash
docker compose up --build
```

A API ficará disponível em `http://localhost:8080`.

### Ambiente de produção

O compose tem default `Production` (o `.env.example` já vem assim) — dev e prod usam o mesmo arquivo, troque apenas o `.env` para depurar localmente:

```bash
# .env local para depuração (OpenAPI habilitado)
ASPNETCORE_ENVIRONMENT=Development
POSTGRES_PASSWORD=dev-local-pg-password-change-me
```

Com `ASPNETCORE_ENVIRONMENT=Production` (default) a documentação (`/openapi/*`) fica desativada; `/health` continua público para o healthcheck do container. Defina explicitamente `Development` só em ambiente local.

### Documentação no Docker

Disponível apenas com `ASPNETCORE_ENVIRONMENT=Development`:

`http://localhost:8080/openapi/v1.json`

### Parar os containers

```bash
docker compose down        # mantém o volume com os dados
docker compose down -v     # apaga também o volume (reset total do banco)
```

---

## Endpoints da API

Toda rota `/api/*` exige o header **`X-Api-Key`** — sem a chave a resposta é **`401`** (ver [Segurança](#segurança)). `/health` fica fora de `/api` e é público. O contrato machine-readable (`/openapi/v1.json`) só existe em `Development`.

| Método | Rota | Descrição |
|--------|------|-----------|
| `POST` | `/api/accounts` | Cria a conta única — corpo vazio; `201 {accountId}` ou `409` se já existe |
| `GET` | `/api/balance` | Consulta o saldo atual (**centavos**, ex: `70000` = R$ 700,00) |
| `GET` | `/api/transactions?page=1&pageSize=50` | Histórico paginado, valores em **centavos** (mais recente primeiro). Defaults `page=1`, `pageSize=50`, máx `200`; resposta `{accountId, currentBalance, page, pageSize, totalCount, transactions}`; paginação inválida → `400` |
| `POST` | `/api/deposit` | Registra um depósito (`201`) — corpo `{"amount": <centavos int>, "description": "..."}` |
| `POST` | `/api/withdraw` | Registra um saque (`201`) — corpo `{"amount": <centavos int>, "description": "..."}` |
| `GET` | `/health` | Liveness do container/Docker Compose (sem dependências) |
| `GET` | `/health/ready` | Readiness com check do PostgreSQL |

### Idempotência (`Idempotency-Key`) — futura implementação ⚠️

> **Por que importa (leia antes de expor a API):** sem idempotência, todo retry cria uma
> transação nova — timeout de rede, duplo clique ou botão reapertado duplicam depósitos
> e saques silenciosamente (R$ 100 vira R$ 200), quebrando saldo e conciliação. O `409`
> de `POST /api/accounts` não cobre isso: só as rotas que **criam lançamentos**
> (`deposit`/`withdraw`) precisam da proteção. Detalhes e desenho aprovado em
> [`docs/adr/005-idempotencia.md`](docs/adr/005-idempotencia.md) — implementar antes
> de qualquer uso com dinheiro real ou retry automático no cliente.

Desenho reservado: header opcional `Idempotency-Key: <1-64 chars>` em `deposit`/`withdraw`;
mesma chave + mesmo payload → replay `201` da mesma transação; payload diferente → `422`;
chave com TTL 24h. **Hoje o header não é tratado: retries duplicam lançamentos.**

```bash
curl -X POST http://localhost:5000/api/deposit \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: $API_KEY" \
  -d '{"amount": 100000, "description": "Receita de vendas"}'
```

### Exemplos de uso

Os exemplos abaixo assumem a chave exportada no shell (mesmo valor de `API_KEY` no `.env` da raiz): `export API_KEY=dev-local-api-key-change-me`.

**Criar a conta** (first-run, corpo vazio):
```bash
curl -X POST http://localhost:5000/api/accounts \
  -H "X-Api-Key: $API_KEY"
```

**Depositar** (valor em **centavos**: `100000` = R$ 1.000,00):
```bash
curl -X POST http://localhost:5000/api/deposit \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: $API_KEY" \
  -d '{"amount": 100000, "description": "Receita de vendas"}'
```

**Sacar** (valor em **centavos**: `30000` = R$ 300,00):
```bash
curl -X POST http://localhost:5000/api/withdraw \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: $API_KEY" \
  -d '{"amount": 30000, "description": "Pagamento fornecedor"}'
```

**Consultar saldo:**
```bash
curl -H "X-Api-Key: $API_KEY" http://localhost:5000/api/balance
```

**Histórico:**
```bash
curl -H "X-Api-Key: $API_KEY" http://localhost:5000/api/transactions
```

### Respostas de erro

| Código | Situação |
|--------|----------|
| `400` | Dados de entrada inválidos (validação de input ou `?page/?pageSize`). Erros em `Errors[]` (`Detail` mantém o join legado `"; "`) |
| `401` | Header `X-Api-Key` ausente ou diferente da chave configurada |
| `404` | Conta ainda não criada (first-run — crie via `POST /api/accounts`) |
| `409` | Conta já existe (criação duplicada — duplo clique, retry ou corrida) |
| `422` | Regra de negócio violada: saldo insuficiente ou overflow aritmético (`OverflowException`) |
| `500` | Erro interno inesperado |

---

## Melhorias Futuras

### Persistência

- ✅ ~~Substituir o repositório em memória por **Entity Framework Core** + **PostgreSQL**~~ — concluído (Npgsql 10.0.3, transações, `FOR UPDATE`)
- ✅ ~~Adicionar **migrations** versionadas~~ — concluído (`Infrastructure/Persistence/Migrations`, tool `dotnet-ef`)
- Considerar **Event Sourcing** completo para auditoria imutável das transações

### Segurança

- **Autenticação por usuário/escopo** — hoje a proteção é uma **API key fixa e compartilhada** (`X-Api-Key`), que autentica o cliente mas não identifica o usuário nem dá escopo: qualquer detentor da chave saca saldo. Aceitável para desafio técnico/testes, mas não para produção (sem expiração, rotação ou revogação). Detalhes em [`docs/api-key.md`](docs/api-key.md). Evolução (fora da app, sem pacote novo): **JWT Bearer** emitido por provedor gerenciado (usuários, escopos `accounts:read|write`, expiração curta + refresh) validado na borda antes de qualquer exposição fora da rede local
- **Rate limiting / abuse protection** — deliberadamente fora da aplicação (desafio técnico simples, sem necessidade; nenhum middleware in-app por G6). Como evolução, aplicar na borda via **API gateway / infra**: fixed window por IP/chave com `429` + header `Retry-After` (ex.: 100 req/min por chave em escritas, 1000 req/min em leituras — calibrar por métrica), WAF gerenciado, em vez de por IP dentro do serviço. A app já retorna os status que o gateway precisa distinguir (`400/401/404/409/422`)
- HTTPS obrigatório com redirecionamento em produção
- ✅ ~~Restringir a publicação da porta 5432 do Postgres em produção~~ — parcialmente concluído (compose faz bind em `127.0.0.1`; remover a publicação em prod)

### Observabilidade

- ✅ ~~**Health checks** estruturados (`/health` liveness + `/health/ready` com Postgres)~~ — concluído (`AddDbContextCheck`; `/health/live` dedicado segue como evolução trivial)
- Logging com o provedor nativo do ASP.NET (texto no stdout, coletado via Docker; sem sink externo)

### Resiliência

- ✅ ~~**Pessimistic locking** no banco para prevenir race conditions~~ — concluído (`SELECT ... FOR UPDATE` em `PostgresAccountRepository.UpdateAsync` + revalidação do saldo na transação)
- **Optimistic concurrency** (`xmin`/coluna `version`) como alternativa ao lock pesado caso o throughput de escrita cresça
- Policies de retry e circuit breaker com **Polly** caso dependências externas sejam adicionadas

### Validação de Input

Hoje a validação é manual (métodos estáticos em `Application/Validators/RequestValidators.cs`, sem pacotes externos). Caso o número e a complexidade das regras cresçam, considerar a introdução de **FluentValidation**:

**Prós**
- Sintaxe declarativa e fluente (`RuleFor(...).NotEmpty().MaximumLength(...)`), concisa e legível
- Regras compostas e condicionais (`When(...)`, `CascadeMode`), mensagens/localização padronizadas por regra
- `IValidator<T>` injetável via DI torna a validação isolável em testes unitários por request
- Padrão conhecido pela comunidade; regras centralizadas por tipo de request em classes `AbstractValidator<T>`

**Contras**
- 2 pacotes NuGet (`FluentValidation` + `FluentValidation.AspNetCore`) para ~4 regras simples — hoje zero dependências de validação
- Mais indireção: 2 classes de validador + 2 registros de DI + injeção nos handlers
- A auto-validação do pacote `.AspNetCore` está em desuso: a chamada manual do `ValidateAsync` seria necessária de qualquer forma
- Over-engineering para o escopo atual (regras simples, estáveis e sem condicionais)

> Decisão atual: manter a validação manual enquanto as regras forem poucas e estáticas; rever se surgirem regras condicionais, de negócio cruzado ou mensagens por cultura.

### Funcionalidades de Negócio

- ✅ ~~**Paginação** no histórico de transações~~ — concluído (`GET /api/transactions?page=&pageSize=`, defaults `1/50`, máx `200`, com `page/pageSize/totalCount`; front com botão "Ver mais")
- ⚠️ **Idempotência** em escritas — **futura, prioritária antes de dinheiro real**: sem ela, retries/duplo clique duplicam lançamentos (ver seção *Idempotência* acima e ADR-005 com o desenho aprovado: header `Idempotency-Key`, TTL 24h)
- Suporte a **múltiplas moedas** com conversão
- **Categorização** de transações
- **Relatórios** por período (extrato mensal, etc.)
- **Webhooks** ou **mensageria** (RabbitMQ/Kafka) para notificações de eventos

### CI/CD

- Pipeline GitHub Actions com build, test e push da imagem Docker
- Deploy automatizado (Railway, Azure App Service, AWS ECS, etc.)

### Testes (evolução futura)

Decisão atual: rápido sem banco (`--filter 'Suite!=Slow'`, <1s) + prova de concorrência restaurada (`ConcurrencyTests`, `Suite=Slow`, ~54s, exige `docker compose up -d postgres`; cada caso cria `DATABASE + Migrate + DROP`).

Se a API ganhar múltiplas contas ou dinheiro de verdade, ampliar o `FinanceControl.IntegrationTests` (banco por classe + `ResetDatabaseAsync` via `DELETE`, resto com `Trait Suite=Slow`), cobrindo: `401` sem/chave errada, `201/409` de criação (incluindo corrida), fluxo `201/400/422`, paginação (`400` em `?page/?pageSize` inválidos), `10.5` → `400`, `404` first-run, persistência entre restarts e `liveness/readiness` do health. A corrida de saques (`201 + 422`) já está coberta.
