---
id: arquitetura
title: Arquitetura implementada
sidebar_position: 1
hide_title: true
description: Arquitetura executável da plataforma de linha, ligada diretamente aos projetos, rotas, tópicos e manifests do monorepo.
---

<header className="architecture-masthead">
  <div className="architecture-eyebrow">Arquitetura viva · validada contra o código em 17/07/2026</div>
  <h1>Observabilidade de ponta a ponta, do clique do usuário ao registro no banco</h1>
  <div className="architecture-lede">
    O fluxo implementado começa na <strong>PWA com login em duas etapas</strong>, atravessa o
    <strong> Gateway .NET com JWT, RBAC/ABAC e rate limit</strong>, chega às APIs e aos workers de domínio,
    e persiste em <strong>PostgreSQL/pgvector, Valkey, VictoriaMetrics e MinIO</strong>. Em paralelo, a borda
    industrial traduz MQTT para Avro e publica no mesmo Kafka usado pelos pipelines preditivo, de decisão,
    notificações, arquivamento, linhagem e IA. Logs, traces e métricas convergem no mesmo OpenTelemetry Collector.
  </div>

  <div className="architecture-kpis">
    <div><strong>17</strong><span>processos da aplicação ativos</span></div>
    <div><strong>24</strong><span>serviços Compose ativos</span></div>
    <div><strong>16</strong><span>workloads no Helm chart</span></div>
    <div><strong>32</strong><span>monitores no Uptime Kuma</span></div>
  </div>

  <div className="architecture-legend">
    <span><i className="legend-dot client-dot"></i>Cliente</span>
    <span><i className="legend-dot access-dot"></i>Acesso / Gateway</span>
    <span><i className="legend-dot edge-dot"></i>Borda industrial</span>
    <span><i className="legend-dot service-dot"></i>Serviços .NET</span>
    <span><i className="legend-dot ai-dot"></i>IA assíncrona</span>
    <span><i className="legend-dot data-dot"></i>Dados</span>
    <span><i className="legend-dot obs-dot"></i>Observabilidade</span>
  </div>

  <div className="runtime-band">
    <span className="status status-local">OK LOCAL</span>
    <strong>Execução nesta estação:</strong> PWA, Gateway, cinco APIs, dez workers, Kafka, PostgreSQL HA,
    Valkey, MinIO, Mosquitto e o stack Grafana estão ativos. Docker Compose cuida da infraestrutura;
    os hosts .NET rodam como processos locais instrumentados.
  </div>
  <div className="runtime-band">
    <span className="status status-code">OK CÓDIGO</span>
    <strong>Interfaces:</strong> o core Web/PWA e os shells Tauri Desktop estão versionados.
    <span className="status status-wait">AGUARDA SDK</span> O empacotamento Mobile continua dependente do Android SDK/NDK.
  </div>
  <div className="runtime-band">
    <span className="status status-config">OK CONFIG</span>
    <strong>Cluster:</strong> Helm, ArgoCD, namespaces, HPA, KEDA, Linkerd, External Secrets/OpenBao,
    Velero e MirrorMaker 2 estão declarados. A execução distribuída aguarda os nós k3s/Jetson.
  </div>
  <div className="runtime-band">
    <span className="status status-local">SEM SAAS PAGO</span>
    <strong>Operação self-hosted:</strong> Flipt substitui o Unleash, MLflow registra modelos,
    Marquez apresenta linhagem, MinIO mantém o lake e Grafana OSS concentra a operação.
  </div>
</header>

## Como ler os selos

| Selo | Significado objetivo |
|---|---|
| <span className="status status-local">OK LOCAL</span> | Serviço ou fluxo verificado em execução nesta máquina. |
| <span className="status status-code">OK CÓDIGO</span> | Implementação e testes existem no monorepo; pode depender de outro processo para produzir carga. |
| <span className="status status-config">OK CONFIG</span> | Manifest, chart ou runbook está pronto, mas não está ativo nesta estação. |
| <span className="status status-wait">AGUARDA HARDWARE</span> | Depende de GPU, Android SDK/NDK ou dos outros nós do cluster. |
| <span className="status status-gap">LACUNA</span> | O próprio código mostra um elo ainda não fechado; está exposto aqui para não virar promessa implícita. |

## Mapa completo da implementação

<div className="map-page-callout">
  <div>
    <span className="status status-code">PÁGINA SEPARADA</span>
    <h3>Explore a plataforma inteira em uma visão expandida</h3>
    <p>Clientes, APIs, edge, Kafka, IA, dados, observabilidade e GitOps agora vivem em uma página própria, com espaço para navegar horizontalmente sem comprimir a documentação.</p>
  </div>
  <a className="map-page-link" href="./mapa-implementacao">Abrir mapa completo →</a>
</div>

## Status por bloco e evidência no código

| Bloco | Status | Como foi implementado | Fonte da verdade |
|---|---|---|---|
| Cliente principal | <span className="status status-local">OK LOCAL</span> | PWA estática, login senha→TOTP, token em memória, service worker sem cache em `/v1/**`, RUM por `sendBeacon`. | `clients/pwa/` |
| Shell Desktop | <span className="status status-code">OK CÓDIGO</span> | Tauri carrega o mesmo core Web com CSP liberando apenas HTTP(S)/WS(S) necessários. | `clients/desktop/src-tauri/` |
| Shell Mobile | <span className="status status-wait">AGUARDA SDK</span> | Alvo e fluxo de empacotamento documentados; falta gerar/assinar o binário com Android SDK/NDK. | `clients/mobile/README.md` |
| Cliente de observabilidade | <span className="status status-code">OK CÓDIGO</span> | PWA/Tauri com desbloqueio TOTP local e Grafana embutido; shell independente do cliente operacional. | `clients/observability/` |
| WAF | <span className="status status-local">OK LOCAL</span> | NGINX + ModSecurity + OWASP CRS em modo de bloqueio, proxy para o Gateway. | `docker-compose.yml` perfil `waf` |
| Gateway/BFF | <span className="status status-local">OK LOCAL</span> | YARP, JWT simétrico ou JWKS Keycloak, RBAC/ABAC, multi-tenant opcional, rota negada por padrão, rate limit Valkey/local e RUM. | `src/Gateway/` |
| Identity | <span className="status status-local">OK LOCAL</span> | Desafio de 2 min, TOTP RFC 6238 com janela ±1 e proteção de replay, access token de 5 min, refresh rotativo com revogação da família. | `src/Identity/` |
| Core.Execution | <span className="status status-local">OK LOCAL</span> | Máquina de estados de ordem; agregado e outbox gravados na mesma transação; relay idempotente para Kafka. | `src/Core.Execution/` |
| Edge.ProtocolGateway | <span className="status status-local">OK LOCAL</span> | Assina `linha/+/sensor/+`, traduz payload, valida identidade do dispositivo, usa buffer store-and-forward e produz Avro. | `src/Edge.ProtocolGateway/` |
| Quality gate | <span className="status status-local">OK LOCAL</span> | Valida faixa física, drift de relógio e staleness; aceita em Postgres ou publica em quarentena com motivo. | `src/Telemetry.Ingest/` |
| Predictive | <span className="status status-local">OK LOCAL</span> | EWMA/z-score online, warmup, baseline protegido contra anomalia, monitor de drift, publicação de alerta e ranking de diagnóstico explicável do iDMSS (épico #8, peso do RF pendente). | `src/Predictive/` |
| Notifications | <span className="status status-local">OK LOCAL</span> | Consome alertas com commit manual, decide escala on-call e envia push ntfy. E-mail ainda é log estruturado. | `src/Notifications/` |
| Decision Engine | <span className="status status-local">OK LOCAL</span> | Envelope operacional, degrau máximo, criticidade e aprovação humana; todo desfecho gera auditoria com trace-id. | `src/Decision.Engine/` |
| Chatbot/MCP | <span className="status status-local">OK LOCAL</span> | RAG com visibilidade RBAC, REST e servidor MCP na mesma API, ferramentas destrutivas com `always_ask`. | `src/Chatbot/` |
| Knowledge | <span className="status status-local">OK LOCAL</span> | HotChocolate GraphQL autenticado, chunking, embeddings, JSONB, pgvector/HNSW, filtro de visibilidade na query e base de causa raiz Ishikawa 6M (classificador + seed, épico #7). | `src/Knowledge/` |
| Conector MES | <span className="status status-code">OK CÓDIGO</span> | Poll genérico com cursor idempotente, normalização e quarentena → `mes.eventos.v1`; hoje só o `SimulatorMesAdapter` (adapter REST/SQL real e cursor persistente pendentes, épico #6). | `src/Mes.Connector/` |
| Agents | <span className="status status-local">OK LOCAL</span> | Janela de sinais, diagnóstico de incidente, relatório diário no Kafka e proposta de ação protegida. | `src/Agents/` |
| IA assíncrona | <span className="status status-code">OK PROCESSOS</span> | Router por tipo, três workers, ledger idempotente, retry via tópico raiz e DLQ após limite de tentativas. | `src/Ai/` |
| Serving de IA | <span className="status status-wait">AGUARDA GPU</span> | Contratos HTTP OpenAI-compatible prontos; `llama.cpp` para Nano e vLLM para Orin declarados no deploy Jetson. | `deploy/jetson/` |
| Data lake | <span className="status status-local">OK LOCAL</span> | Lê Kafka por partição, compacta JSONL.gz, grava no MinIO com chave Hive-style e emite OpenLineage. | `src/Data.Archiver/` |
| Contratos | <span className="status status-local">OK LOCAL</span> | Onze schemas Avro versionados; codec binário executável; Apicurio registra os artefatos de forma idempotente. | `schemas/`, `src/Platform/Platform.Contracts/` |
| Observabilidade | <span className="status status-local">OK LOCAL</span> | Serilog + OTel em todos os hosts; Collector separa logs→Loki, traces→Tempo e métricas→VictoriaMetrics; Grafana correlaciona por `trace_id`. | `src/Platform/Platform.ServiceDefaults/`, `deploy/observability/` |
| SLO/FinOps/status | <span className="status status-local">OK LOCAL</span> | Pyrra lê SLOs versionados, OpenCost usa a API Prometheus e Uptime Kuma executa 32 verificações. | `deploy/observability/slo/`, `docker-compose.yml` |
| GitOps e autoscale | <span className="status status-config">OK CONFIG</span> | Chart com namespaces, resources, HPA e KEDA por lag; ArgoCD com sync, prune e self-heal. | `deploy/helm/`, `deploy/argocd/` |
| mTLS e segredos | <span className="status status-config">OK CONFIG</span> | Linkerd por injeção de proxy; External Secrets lê OpenBao; `PlatformSecrets` dá fallback explícito para dev. | `deploy/cluster/`, `src/Platform/Platform.ServiceDefaults/` |
| DR | <span className="status status-config">OK CONFIG</span> | Replica PostgreSQL, WAL archive e dump já rodam localmente; Velero, MirrorMaker 2 e failover cross-region estão versionados. | `deploy/dr/`, `docs/governanca/` |

## Rotas HTTP implementadas

O Gateway só encaminha prefixos presentes simultaneamente na configuração YARP e na tabela de autorização.

| Entrada | Destino | Política | Implementação relevante |
|---|---|---|---|
| `/v1/auth/**` | Identity | Pública, protegida pelo rate limit | login, TOTP, refresh e provisionamento TOTP |
| `/v1/core/**` | Core.Execution | `operador` ou `admin`; `/admin` exige `admin` | ordens e transições de produção |
| `/v1/chat/**` | Chatbot | `operador` ou `admin` | chat, ferramentas e MCP |
| `/v1/knowledge/**` | Knowledge | `operador` ou `admin` | GraphQL com filtro RBAC |
| `/v1/agents/**` | Agents | `operador` ou `admin` | diagnóstico, relatório e proposta de ação |
| `/rum` | Gateway | Pública, payload limitado | histograma `client.rum.duration` |
| `/healthz` | Cada host HTTP | Pública | liveness sintético |
| `/v1/linha/ws` | Telemetry.Ingest | Pública na borda (browser não envia header no upgrade); o hub valida o JWT do **primeiro frame** + papel antes de transmitir | painel ao vivo: feed espelha `linha.telemetria.v1` |
| `/v1/linha/**` (demais) | Telemetry.Ingest | `operador` ou `admin` | reservado; hoje só o WS existe |

## Tópicos Kafka e garantias

```mermaid
flowchart LR
  MQTT["MQTT / Edge"] -->|"Avro · producer idempotente"| TELEMETRY[("linha.telemetria.v1")]
  TELEMETRY --> INGEST["telemetry-ingest<br/>commit manual"]
  INGEST -->|"aceita · upsert"| PG[("PostgreSQL")]
  INGEST -->|"rejeita + reason"| QUAR[("linha.telemetria.quarentena.v1")]
  TELEMETRY --> PRED["predictive"] --> ALERTS[("linha.alertas.v1")]
  ALERTS --> NOTIFY["notifications → ntfy"]
  ALERTS --> AGENT["agents"] --> REPORT[("relatorios.diarios.v1")]
  TELEMETRY --> ARCH["data-archiver"] --> MINIO[("MinIO / JSONL.gz")]
  ARCH --> LINEAGE[("linhagem.openlineage.v1")]

  MES["MES real (pendente)<br/>· SimulatorMesAdapter"] --> MESCON["mes-connector<br/>cursor idempotente"]
  MESCON -->|"normaliza · Avro"| MESEVT[("mes.eventos.v1")]
  MESCON -->|"rejeita + reason"| MESQUAR[("mes.eventos.quarentena.v1")]

  CORE["Core.Execution"] -->|"outbox transacional"| COREEVT[("core.eventos.v1")]
  ALERTS --> KSQL["ksqlDB · janela 10 min"] --> STORM[("linha.alertas.tempestade.v1")]
  COREEVT --> KSQL --> FLOW[("linha.throughput.v1")]

  COMMAND[("linha.comandos.propostos.v1")] --> DECISION["Decision.Engine"]
  DECISION --> APPROVED[("linha.comandos.aprovados.v1")]
  DECISION --> PENDING[("linha.comandos.pendentes.v1")]
  DECISION --> AUDIT[("auditoria.decisoes.v1<br/>sempre + trace-id")]

  AIJOB[("ai.jobs.v1")] --> ROUTER["Ai.Router"]
  ROUTER --> LLM[("ai.jobs.llm.v1")]
  ROUTER --> VISION[("ai.jobs.vision.v1")]
  ROUTER --> EMBED[("ai.jobs.embedding.v1")]
  ROUTER --> DLQ[("ai.jobs.dlq.v1")]
  LLM --> WORKERS["workers · idempotência por job-id"]
  VISION --> WORKERS
  EMBED --> WORKERS
  WORKERS --> RESULT[("ai.resultados.v1")]
  WORKERS -->|"falha · attempts + 1"| AIJOB
```

| Garantia | Onde aparece |
|---|---|
| **At-least-once controlado** | Consumers desativam auto-commit e confirmam só após persistir, publicar ou reenfileirar. |
| **Outbox transacional** | `WorkOrderStore` grava estado e evento na mesma transação PostgreSQL; `OutboxRelay` publica depois. |
| **Idempotência de telemetria** | Chave `(sensor_id, measured_at)` e `ON CONFLICT DO NOTHING`. |
| **Idempotência de IA** | `IdempotencyLedger` impede resultado duplicado por `job-id`; em cluster o próximo passo é `SET NX` no Valkey. |
| **Falha explícita** | Quarentena para telemetria, DLQ para IA, pending para decisão humana e métricas para outbox/ingest. |
| **Contrato versionado** | `.avsc` no repositório, codec em `Platform.Contracts` e publicação no Apicurio. |

## Sequência: clique até o banco e o trace

```mermaid
sequenceDiagram
  autonumber
  actor U as Admin/operador
  participant P as PWA
  participant W as WAF
  participant G as Gateway/YARP
  participant I as Identity
  participant C as Core.Execution
  participant D as PostgreSQL
  participant O as OutboxRelay
  participant K as Kafka
  participant T as OTel Collector

  U->>P: usuário + senha
  P->>W: POST /v1/auth/login
  W->>G: request filtrado pelo CRS
  G->>I: proxy público + rate limit
  I-->>P: challengeId (2 min)
  U->>P: TOTP de 6 dígitos
  P->>G: POST /v1/auth/totp
  G->>I: completa desafio
  I-->>P: access JWT (5 min) + refresh rotativo
  P->>G: POST /v1/core/ordens + Bearer
  G->>G: valida JWT, papel, atributo e quota
  G->>C: YARP + traceparent W3C
  C->>D: BEGIN + INSERT/UPDATE agregado + INSERT outbox + COMMIT
  C-->>P: 201/200
  O->>D: busca outbox pendente
  O->>K: publish core.eventos.v1
  K-->>O: ack
  O->>D: marca published_at
  P-->>G: sendBeacon /rum
  G-->>T: métrica RUM
  G-->>T: logs, spans e métricas
  C-->>T: logs, spans e métricas com o mesmo trace-id
```

## Sequência: fábrica, qualidade, predição e alerta

```mermaid
sequenceDiagram
  autonumber
  participant S as Sensor/PLC
  participant M as Mosquitto
  participant E as Edge.ProtocolGateway
  participant K as Kafka
  participant Q as Telemetry.Ingest
  participant P as PostgreSQL
  participant A as Predictive
  participant N as Notifications
  participant F as ntfy
  participant L as Data.Archiver
  participant O as MinIO

  S->>M: linha/{linha}/sensor/{id}
  M->>E: MQTT payload
  E->>E: identidade + tradução + buffer
  E->>K: linha.telemetria.v1 (Avro)
  par Qualidade e persistência
    K->>Q: leitura
    Q->>Q: range + drift + staleness
    alt leitura válida
      Q->>P: upsert idempotente
    else leitura inválida
      Q->>K: linha.telemetria.quarentena.v1 + motivo
    end
  and Scoring online
    K->>A: leitura
    A->>A: warmup + EWMA/z-score + drift
    opt anomalia
      A->>K: linha.alertas.v1
      K->>N: alerta
      N->>F: push por severidade/on-call
    end
  and Data lake
    K->>L: lote por partição
    L->>O: JSONL.gz / Hive-style key
    L->>K: linhagem.openlineage.v1
  end
```

## Observabilidade e operação local

```mermaid
flowchart LR
  RUM["PWA RUM"] -->|"POST /rum"| GW["Gateway"]
  DOTNET["APIs + workers .NET<br/>Serilog · ActivitySource · Meter"] -->|"OTLP gRPC/HTTP"| COL["OTel Collector :4317/:4318"]
  GW -->|"histograma + spans + logs"| COL
  COL -->|"logs"| LOKI["Loki :3100"]
  COL -->|"traces"| TEMPO["Tempo :3200"]
  COL -->|"remote_write"| VM["VictoriaMetrics :8428"]
  PYRRA["Pyrra · SLOs"] --> VM
  COST["OpenCost · custos"] <--> VM
  LOKI --> GRAFANA["Grafana OSS :3000"]
  TEMPO --> GRAFANA
  VM --> GRAFANA
  GRAFANA -->|"derived field trace_id"| TEMPO
  KUMA["Uptime Kuma · 32 monitores"] -. "health checks" .-> DOTNET
  KUMA -. "UI checks" .-> GRAFANA
  KUMA -. "UI checks" .-> TOOLING["Flipt · MLflow · MinIO · Marquez · Docs"]
```

O `Platform.ServiceDefaults` padroniza `service.name`, logs estruturados, `ActivitySource`, `Meter` e exportação
OTLP. O mascaramento de PII cobre nomes conhecidos como senha, CPF, telefone e seed TOTP antes do sink.
No Grafana, o datasource Loki extrai `trace_id` e abre o trace correspondente no Tempo.

## Interfaces locais

Credencial administrativa de desenvolvimento: **`msuchoa` / `w1ntersun`**. Ela não deve ser reutilizada fora deste ambiente.

| Sistema | URL | Status | Para que serve | Acesso local |
|---|---|---|---|---|
| PWA operacional | [127.0.0.1:8081](http://127.0.0.1:8081/) | <span className="status status-local">OK LOCAL</span> | Operação da linha, autenticação e RUM | admin + TOTP |
| Uptime Kuma | [127.0.0.1:3001](http://127.0.0.1:3001/dashboard) | <span className="status status-local">OK LOCAL</span> | Saúde sintética dos 32 endpoints | admin |
| Grafana OSS | [127.0.0.1:3000](http://127.0.0.1:3000/) | <span className="status status-local">OK LOCAL</span> | Logs, traces, métricas e FinOps | anônimo admin em dev; admin disponível |
| Flipt | [127.0.0.1:4242](http://127.0.0.1:4242/) | <span className="status status-local">OK LOCAL</span> | Feature flags self-hosted | sem login no perfil dev |
| Marquez Web | [127.0.0.1:3012](http://127.0.0.1:3012/) | <span className="status status-local">OK LOCAL</span> | Grafo de linhagem OpenLineage | sem login no perfil dev |
| MLflow | [127.0.0.1:5500](http://127.0.0.1:5500/) | <span className="status status-local">OK LOCAL</span> | Experimentos, artefatos e registro de modelos | sem login no perfil dev |
| MinIO Console | [127.0.0.1:9001](http://127.0.0.1:9001/login) | <span className="status status-local">OK LOCAL</span> | Lake S3 e inspeção do bucket `linha-lake` | admin |
| Docs | [127.0.0.1:3003](http://127.0.0.1:3003/docs/arquitetura) | <span className="status status-local">OK LOCAL</span> | Arquitetura e governança versionadas | sem login no perfil dev |
| OpenCost | [127.0.0.1:9003](http://127.0.0.1:9003/) | <span className="status status-local">OK LOCAL</span> | Custos e eficiência de recursos | sem login no perfil dev |
| Pyrra | [127.0.0.1:9099](http://127.0.0.1:9099/) | <span className="status status-local">OK LOCAL</span> | SLOs e error budgets | sem login no perfil dev |
| Apicurio | [127.0.0.1:8085](http://127.0.0.1:8085/) | <span className="status status-local">OK LOCAL</span> | Catálogo e versões dos contratos Avro | sem login no perfil dev |
| ksqlDB | [127.0.0.1:8088](http://127.0.0.1:8088/info) | <span className="status status-local">OK LOCAL</span> | Stream SQL e janelas contínuas | API local |
| ntfy | [127.0.0.1:8090](http://127.0.0.1:8090/) | <span className="status status-local">OK LOCAL</span> | Push self-hosted para alertas | sem login no perfil dev |

## Deploy: do desenvolvimento ao cluster

```mermaid
flowchart TB
  DEV["Desenvolvimento<br/>processos .NET + Docker Compose"] --> TEST["GitHub Actions<br/>TDD gate · unit/integration/contract · k6"]
  TEST --> SCAN["Trivy<br/>bloqueia CRITICAL"]
  SCAN --> SIGN["cosign keyless<br/>OIDC do job"]
  SIGN --> REG["imagem por commit<br/>GHCR / Harbor quando disponível"]
  REG --> ARGO["ArgoCD<br/>sync · prune · selfHeal"]
  GIT["deploy/helm/plataforma-linha"] --> ARGO
  ARGO --> K3S["k3s multi-node"]

  subgraph NS["Namespaces"]
    ACCESSNS["acesso<br/>gateway · identity"]
    CORENS["nucleo<br/>core · notifications"]
    LINENS["linha<br/>edge · ingest · predictive<br/>decision · archiver"]
    AINS["ia<br/>router · 3 workers · chatbot<br/>knowledge · agents"]
  end

  K3S --> NS
  HPA["HPA<br/>gateway 2→10 por CPU"] --> ACCESSNS
  KEDA["KEDA por lag<br/>ingest 1→12 · notifications 1→5<br/>AI 0→N scale-to-zero"] --> NS
  LINKERD["Linkerd<br/>mTLS por identidade de workload"] -.-> NS
  SECRETS["OpenBao + External Secrets<br/>sem segredo no values.yaml"] -.-> NS
  VELERO["Velero → MinIO<br/>backup de objetos do cluster"] -.-> K3S
  MM2["MirrorMaker 2<br/>Kafka primário → standby"] -.-> K3S
  PI["Raspberry Pi<br/>agent ARM64"] -.-> K3S
  JETSON["Jetson<br/>GPU pool / serving"] -.-> AINS
```

O chart declara requests/limits e escaladores por workload. O Gateway usa HPA por CPU; consumers usam KEDA por
lag do tópico; LLM, visão e embeddings começam em zero réplicas. `mesh.enabled` injeta Linkerd sem alterar as APIs,
e os segredos chegam por External Secrets, não pelo `values.yaml`.

## Lacunas reais, agora explícitas

| Item | Estado atual | Para fechar |
|---|---|---|
| WebSocket da linha | <span className="status status-code">FEITO 18/07</span> Hub no Telemetry.Ingest (`LineFeedHub` + broadcaster com grupo efêmero), rota/cluster `linha` no YARP; credencial no primeiro frame, validada no hub. | Validar em cluster (Service criado; falta exercitar no k3s). |
| Marquez ingest | <span className="status status-code">FEITO 18/07</span> `LineageBridge` no Data.Archiver consome o tópico e entrega no `POST /api/v1/lineage` (liga com `Marquez:BaseUrl`); teto anti-veneno pra evento que o Marquez nunca aceita. Facets corrigidos pro spec (`_producer`/`_schemaURL`, `dataSource.uri`) — sem eles o Marquez recusa (422) ou quebra (500/NPE). | Validado local: dataset do lake visível no grafo. |
| E-mail real | <span className="status status-code">FEITO 18/07</span> `EmailSender` envia via SMTP quando `Smtp:Host` está configurado (Mailpit no compose; relay corporativo em prod); sem config, segue o log da fase 0. | Validado local: alerta Critical entregue no Mailpit. |
| Idempotência IA distribuída | <span className="status status-gap">FASE ATUAL</span> Ledger é em memória por processo. | Trocar por `Valkey SET NX` com TTL para múltiplas réplicas. |
| LLM/visão/embeddings reais | <span className="status status-wait">AGUARDA GPU</span> Workers estão vivos, mas o serving precisa da Jetson. | Subir um dos perfis em `deploy/jetson/` e apontar as três URLs. |
| Mobile | <span className="status status-wait">AGUARDA SDK</span> Arquitetura e shell estão documentados, sem APK/IPA gerado aqui. | Instalar SDK/NDK, inicializar o target Tauri Mobile e assinar o artefato. |
| Cluster/DR cross-region | <span className="status status-config">OK CONFIG</span> Scripts, manifests e runbook estão prontos; não há segunda região nesta estação. | Executar bootstrap dos nós, game day e medir RPO/RTO real. |
| Eval automatizada de IA | <span className="status status-gap">BACKLOG</span> Critérios estão documentados, sem gate executável no CI. | Transformar `docs/governanca/avaliacao-ia.md` em suíte de regressão. |

## Decisões que o código já tomou

- **Single-tenant por instância é o padrão.** Multi-planta usa atributo ABAC; multi-tenant no mesmo cluster só entra com `MultiTenant:Enabled` e `X-Tenant` obrigatório.
- **A borda autoriza antes de encaminhar.** Prefixo mais específico vence e rota não listada retorna 404; autenticação, autorização e rate limit acontecem antes do YARP.
- **Nenhum caminho de falha importante é silencioso.** Há quarentena, DLQ, pending humano, backpressure, retry contado, outbox com métrica e SLO versionado.
- **O contrato do evento é código.** Os `.avsc` documentam; `Platform.Contracts` executa o wire format; Apicurio cataloga e versiona.
- **Observabilidade não é sidecar opcional da regra de negócio.** O `ServiceInstrumentation` entra em cada host e o `traceparent` atravessa Gateway, serviços e mensagens.
- **Dados quentes e frios têm destinos diferentes.** PostgreSQL/Valkey atendem o operacional; MinIO recebe lotes compactados e particionados; VictoriaMetrics atende série temporal e métricas.
- **Autoscale segue a causa da carga.** HTTP escala por CPU; consumidor escala por lag; GPU escala de zero quando chega job.
- **O ambiente local é deliberadamente simples.** Processos .NET falam com a infraestrutura Compose; o mesmo contrato de configuração alimenta os manifests do cluster.
