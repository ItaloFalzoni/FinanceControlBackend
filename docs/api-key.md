# API key fixa — por que existe e por que não vai para produção

- Status: aceito para o escopo atual (desafio técnico / ambiente de testes)
- Data: 2026-09-27
- Onde: `src/FinanceControl.API/Infrastructure/Authentication/ApiKeyMiddleware.cs`
  (`X-Api-Key` em toda rota `/api/*`), config `Authentication:ApiKey`
  (env `Authentication__ApiKey`, valor em `.env` → `API_KEY`).

## Como funciona hoje

- Chave **única e compartilhada**: todo cliente usa o mesmo valor.
- Comparação em tempo constante (hash SHA-512 + `FixedTimeEquals`) para não
  vazar prefixo por timing.
- Fail-closed no startup (`ValidateOnStart`): sem chave o host nem sobe —
  nunca existe modo "aberto por esquecimento".
- A chave existe **só no servidor**: no front ela é injetada pelo proxy
  `app/api/*` e nunca chega ao browser (`NEXT_PUBLIC_*` proibido).
- `/health`, `/health/ready` e o documento OpenAPI ficam fora de `/api` e
  continuam públicos (healthcheck do container depende disso).

## Por que é aceitável aqui

- Um único consumidor first-party (o front Next) com deploy em lockstep.
- Sem usuários, sem escopos, sem multi-tenant — não há "quem" para identificar,
  só "qual cliente".
- Escopo de desafio técnico: trocar por JWT/OIDC agora seria over-engineering
  (não há versionamento de API — `/api/*` sem prefixo `/v1` — por decisão).

## Por que não serve em produção

- **Sem identidade**: qualquer detentor da chave faz qualquer operação
  (depósito, saque, leitura) — não dá para auditar "quem" nem limitar "o quê".
- **Sem expiração/escopo**: vazou, vale tudo até troca manual; não há
  refresh, revogação por usuário ou permissão por rota.
- **Rotação manual**: trocar exige redeploy coordenado do backend + proxy do
  front; sem secret manager há janela de indisponibilidade ou de chave velha
  válida.
- **Compartilhamento**: a mesma chave em todos os ambientes/clientes aumenta
  o blast radius de um vazamento (log, `.env` commitado por engano, etc.).

## Evolução (quando expor fora da rede local/privada)

1. **JWT Bearer com escopos** (usuários, `accounts:read|write`, expiração curta
   + refresh) — identifica o usuário e limita o impacto do vazamento.
2. **OIDC / provedor gerenciado** (Entra ID, Keycloak, Auth0) em vez de
   emitir token na mão.
3. **Segredos em cofre** (Vault, AWS Secrets Manager, Key Vault) com rotação
   automática; nunca `.env` em imagem ou log.
4. **Transporte**: gateway + TLS obrigatório, mTLS entre serviços, rede
   interna para o Postgres (sem publicação de `5432`).
5. **Abuse protection na borda**: rate limit / WAF no API gateway (ver
   `README.md` → *Melhorias Futuras*), não por IP na aplicação.

Ver também: `README.md` → *Segurança* e *Melhorias Futuras*.
