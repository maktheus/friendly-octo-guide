# plataforma-linha

Monorepo da plataforma. Um repositório, um pipeline reutilizável, um serviço por pasta —
com time de 4 pessoas, monorepo elimina o overhead de sincronizar versões entre N repos; se um dia
um serviço precisar de ciclo próprio, ele sai daqui com histórico (`git filter-repo`).

O mapa bloco-da-arquitetura → pasta está em [`docs/arquitetura.md`](docs/arquitetura.md).

## Layout

```
plataforma-linha/
├── .github/workflows/
│   ├── _dotnet-service.yml      # workflow reutilizável: gate TDD → build → testes+cobertura → imagem
│   └── <serviço>.yml            # instância fina por bloco (path filter) — um pipeline por bloco
├── clients/pwa/                 # cliente: core web único (PWA); shells Tauri entram na fase 1
├── src/
│   ├── Platform/                # compartilhados PUROS + bootstrap de observabilidade
│   │   ├── Platform.ServiceDefaults/   # Serilog + OTel — todo host nasce instrumentado
│   │   ├── Platform.AccessControl/     # RBAC/ABAC — Identity emite, Gateway/Chatbot avaliam
│   │   └── Platform.Contracts/         # codec Avro executável de schemas/
│   ├── Gateway/                 # YARP: authN/authZ na borda, rate limit, trace-id raiz
│   ├── Identity/                # TOTP RFC 6238, JWT curto, refresh rotativo c/ detecção de roubo
│   ├── Core.Execution/          # ordens de produção + outbox pattern (evento na mesma transação)
│   ├── Notifications/           # escada de on-call por severidade; push ntfy
│   ├── Edge.ProtocolGateway/    # fábrica → nuvem: Modbus/MQTT → buffer store-and-forward → Kafka
│   ├── Telemetry.Ingest/        # quality gate → Postgres (aceita) / quarentena (rejeitada)
│   ├── Ai/                      # router (DLQ, retry) + worker LLM (vLLM, idempotência por job-id)
│   ├── Predictive/              # scoring online EWMA/z-score + drift (acatech 5)
│   ├── Decision.Engine/         # envelope de operação + aprovação por criticidade (acatech 6)
│   └── Chatbot/                 # RAG com filtro RBAC + guardrails de ferramenta (always_ask)
├── schemas/                     # contratos dos tópicos Kafka — mudança de contrato começa AQUI
├── deploy/                      # manifests K8s por serviço (HPA/KEDA) + espinha de observabilidade
├── docker-compose.yml           # dev local: Kafka, Postgres, Valkey, MinIO, Mosquitto,
│                                #   OTel Collector, Loki, Tempo, VictoriaMetrics, Grafana, ntfy
└── Directory.Build.props        # nullable, warnings-as-errors, cobertura mínima — vale pra todos
```

## Convenções (não são sugestões)

1. **TDD é gate de CI, não cultura.** PR que altera `src/**` sem alterar o `*.Tests` correspondente
   é reprovado pelo job `tdd-gate`. Cobertura de linha mínima: 80% no projeto Domain.
2. **Domain não referencia infra.** `*.Domain.csproj` não pode referenciar Kafka, Npgsql, nada de IO.
   É isso que mantém os testes rápidos (< 5s a suíte) e o TDD viável.
3. **Todo serviço nasce instrumentado.** `Platform.ServiceDefaults` liga Serilog estruturado e
   OTel (traces + métricas) no primeiro deploy — serviço sem telemetria não passa no review.
4. **Schema primeiro.** Mudança de contrato começa em `schemas/`, com compatibilidade BACKWARD
   validada no CI antes de qualquer código.
5. **Nada se perde calado.** Quarentena no ingest, DLQ na IA, buffer da borda que recusa (e grita)
   em vez de descartar, outbox com backoff — todo caminho de falha tem destino e métrica.

## Rodando local

```bash
docker compose up -d          # Kafka, Postgres, Valkey, MinIO, MQTT + espinha de observabilidade
dotnet test                   # suíte completa (rápida — só Domain tem lógica)

# obrigatório pra rodar local: sem isso os hosts que assinam/validam JWT falham no
# boot de propósito (a chave de dev só vale em Development — fora dela, OpenBao).
export ASPNETCORE_ENVIRONMENT=Development

# suba o que for exercitar:
dotnet run --project src/Identity/Identity.Api                          # :5000 — /v1/auth
dotnet run --project src/Gateway/Gateway.Host                           # borda
dotnet run --project src/Core.Execution/Core.Execution.Api              # ordens + outbox
dotnet run --project src/Edge.ProtocolGateway/Edge.ProtocolGateway.Worker
dotnet run --project src/Telemetry.Ingest/Telemetry.Ingest.Worker
dotnet run --project src/Predictive/Predictive.Worker

# cliente (PWA): sirva clients/pwa/ com qualquer servidor estático
python3 -m http.server 8081 -d clients/pwa
```

Grafana em `http://localhost:3000` (Loki + Tempo + VictoriaMetrics provisionados);
push de alerta em `http://localhost:8090` (ntfy).

Usuários dev (seed em `Identity.Api/UserStore.cs`): `msuchoa/w1ntersun` (admin) e
`operador/operador-dev` — provisionamento do authenticator em
`GET /v1/auth/totp/provision/{username}`.

> Versões de pacote nos `.csproj` são as vigentes na criação do scaffold — rode
> `dotnet list package --outdated` na primeira semana e fixe as atuais.
