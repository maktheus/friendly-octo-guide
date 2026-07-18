# Prontidão para produção industrial — especificação de implementação

**Especificação — 2026-07-16. Para o time implementar e validar.** O alvo mudou: a
plataforma vai rodar **direto em fábrica**, adaptada às 3 pesquisas SMT/SMD, e precisa
ser **100% funcional e pronta para indústria** — primeiro **local** (docker-compose),
depois em **cluster**. Este doc especifica o que falta para isso: o endurecimento do
que hoje é "fase 0/dev", a paridade local↔cluster, o desenho de produção por pesquisa
e os critérios de aceite industriais.

> Como ler: cada item tem **estado atual** → **exigência de produção** → **onde**.
> Tudo é acionável; o backlog rastreável está nas issues (ver a última seção).

---

## 1. Endurecimento fase-0 → produção (o que NÃO pode ir pra fábrica como está)

O scaffold foi construído com atalhos de dev deliberados, marcados no código como
"fase 0/fase 1". Para produção industrial, **cada um destes vira requisito**:

### 1.1 Identidade, segredos e autenticação
| Estado atual (dev) | Exigência de produção | Onde |
|---|---|---|
| `InMemoryUserStore` (usuários fixos em memória) | **Keycloak** como fonte da verdade (OIDC + TOTP + RBAC/ABAC); Identity valida, não guarda usuário | `Identity.Api`, `deploy/raspberry/keycloak` |
| ~~Chave JWT literal em 5 serviços~~ **FEITO 17/07**: `PlatformSecrets.JwtSigningKey` centraliza; fallback de dev só em `Development`, fora dele o boot **falha** sem chave do OpenBao | Rotação da chave no OpenBao (a resolução já exige ele fora de dev) | `Platform.ServiceDefaults/PlatformSecrets.cs` |
| ~~Endpoint admin sem auth~~ **FEITO 17/07**: `/v1/admin` exige token com papel `admin` validado no próprio Identity; ator da auditoria = `sub` do token (o fallback `system:dev` virou exceção) | — | `Identity.Api/AdminEndpoints.cs`, `Identity.Api/Program.cs` |
| Segredos com fallback pra env/config | **OpenBao + External Secrets** obrigatório; sem fallback em prod | `Platform.ServiceDefaults/PlatformSecrets.cs` |
| TOTP provisionado pelo Identity (dev) | QR pelo **Account Console do Keycloak** | `Identity.Api/AuthEndpoints.cs` |

### 1.2 Estado durável (em memória → sobrevive a restart e vale entre réplicas)
| Estado atual (dev) | Exigência de produção | Onde |
|---|---|---|
| Cursor de polling do MES em memória | Persistir (Postgres/Valkey) — restart não re-polla | `Mes.Connector` |
| `IdempotencyLedger` em memória (por réplica) | **Valkey `SET NX`** — idempotência entre réplicas | `Ai.Worker.Shared/AiJobLoop`, `Ai.Worker.Llm` |
| `SignalWindow` de correlação em memória | Materializar do **TSDB/Big Data Pool** | `Agents.Api/SignalWindow.cs` |
| `StoreAndForwardBuffer` de estado em memória | Estado em **arquivo/disco** na borda (store-and-forward real) | `Edge.ProtocolGateway/…/Buffering` |
| Rate limiter local por réplica | **Valkey** (token bucket distribuído) obrigatório com >1 réplica | `Gateway.Host/ValkeyRateLimiter.cs` |

### 1.3 Cadastro e configuração da linha (hardcoded → cadastro real)
| Estado atual (dev) | Exigência de produção | Onde |
|---|---|---|
| Limites de sensor fixos no código | **Cadastro de sensores** (Postgres) + cache | `Telemetry.Ingest/IngestConsumer.cs` |
| Envelopes de operação fixos ("PoC do loop") | **Cadastro da linha** por criticidade | `Decision.Engine/Program.cs` |
| Mapa da linha / dispositivos fixo | **Device registry** com **cert X.509 por sensor** + enrollment/revogação | `Edge.ProtocolGateway/Program.cs` |
| Árvore de ativos ISA-95 = seed | **CRUD de ativos** com outbox (já especificado no ADR) | `docs/governanca/modelo-de-ativos-isa95.md` |

### 1.4 Modelos e contratos
| Estado atual (dev) | Exigência de produção | Onde |
|---|---|---|
| `HashingEmbedder` (feature hashing, sem semântica) | **Embeddings reais** (modelo servido no GPU pool) | `Knowledge.Domain/Embeddings` |
| Modelos servidos estáticos / baseline | **MLflow** (versão/rollout) + serving real | Épico #11 |
| Codecs "sem Schema Registry (fase 0)" | **Apicurio Schema Registry** com compat BACKWARD no CI | `Platform.Contracts`, `deploy/infra/register-schemas.sh` |
| `SimulatorMesAdapter` | **`RestMesAdapter`/`SqlMesAdapter`** contra o MES real | `Mes.Connector` |

**Regra de aceite:** um serviço só é "pronto pra fábrica" quando **nenhum** dos seus
itens acima está no estado de dev. O CI deve, idealmente, **falhar** se detectar chave
literal `dev-only-*` ou fallback de segredo com `ASPNETCORE_ENVIRONMENT=Production`.

---

## 2. Paridade local ↔ cluster (preparar os dois desde já)

O mesmo artefato roda nos dois ambientes; muda a **fiação**, não o código. O contrato
já é isso (`OTEL_EXPORTER_OTLP_ENDPOINT`, `ConnectionStrings__*`, `Kafka__Bootstrap`
por env em ambos). O que falta padronizar:

| Aspecto | Local (docker-compose) | Cluster (K8s) | Ação |
|---|---|---|---|
| Orquestração | `docker-compose.yml` | `deploy/` (manifests) + `deploy/helm` | manter os **dois em paridade** — todo serviço novo entra nos dois |
| Segredos | `.env`/OpenBao container | **OpenBao + External Secrets** | um só caminho (`PlatformSecrets`); sem `.env` em prod |
| Escala | réplica única | **HPA/KEDA** | serviços stateful (cursor MES, edge) precisam de estratégia de partição |
| Rede | bridge | **service mesh (mTLS)** zero-trust | Linkerd/Istio no cluster; compose usa rede fechada |
| Stores | contêineres | **PVs + operators** (Postgres, MinIO, Valkey) | backup/DR só no cluster (Velero + PITR) |
| Ingress | portas locais | **NGINX + WAF (Coraza/CRS)** | o WAF é obrigatório em prod |

**Entregável de paridade:** um `docs/deploy/local.md` e `docs/deploy/cluster.md` com o
passo a passo de subir a stack completa em cada um, e um checklist "serviço novo entra
em ambos". Hoje o `Mes.Connector` já tem manifesto K8s mas **falta no docker-compose** —
esse tipo de gap não pode existir.

---

## 3. Adaptação por pesquisa — desenho de produção (não POC)

Para cada pesquisa, o que "pronto pra indústria" exige além do núcleo já entregue.

### 3.1 Victor — iDMSS (Random Forest sobre MES/SCADA)
- **Coleta**: `Mes.Connector` com **adapter real** (não simulador) + **conector SCADA**
  (o edge já fala OPC-UA/Modbus; SCADA nível 2 entra por aí).
- **Processamento**: `DiagnosisRanking` (pronto) + **Random Forest treinado** servido
  (peso do modelo). Treino no MLflow com dataset rotulado do PIM.
- **Interface**: **agente iDMSS** de produção — `Chatbot` + `Decision.Engine`
  (guardrails, aprovação por criticidade, envelope real) + `Agents` (tools MCP).
- **Aceite industrial**: resposta auditada (trilha de auditoria), RBAC por planta/linha,
  latência dentro do SLO, ação corretiva **nunca** escreve direto no PLC (só pelo edge).

### 3.2 Hallyson — visão computacional (SPI)
- **Ingestão de imagem**: `schemas/inspecao-smt.avsc` + caminho de imagem (MinIO +
  `image_ref`) — **novo**, a telemetria escalar não serve.
- **Inferência**: `Ai.Worker.Vision` com modelo real (ViT/CNN/MambaVision/YOLOv5) +
  **XAI** (explicação + confiança). Servido no GPU pool.
- **Realidade de hardware**: 1× A2000 Ada (16 GB) — visão + LLM **não** cabem quentes
  juntos; **time-sharing** + scale-to-zero (ver `carga-operacional.md`). Treino de visão
  provavelmente **fora** da A2000 (mais GPU) ou batch pequeno.
- **Aceite industrial**: integração real com a máquina SPI, taxa de falsa detecção
  medida, ligação defeito→Ishikawa, throughput compatível com a linha.

### 3.3 Jeymerson — gestão do conhecimento + inferência lógica
- **Base**: `Knowledge` com persistência **dupla** (pgvector p/ RAG + estruturada p/
  regras) — falta implementar o store.
- **Inferência**: **sistema especialista** (regras interpretáveis, XAI) sobre a base
  Ishikawa — a semente é o `IshikawaClassifier`; produção precisa de encadeamento de
  regras e **elicitação com a operação**.
- **Visibilidade/transparência**: reuso da observabilidade (acatech 3–4) — pronto.
- **Aceite industrial**: base versionada, regras auditáveis, relatório diário confiável.

---

## 4. Critérios de aceite industriais (transversais)

Além do endurecimento, "pronto pra fábrica" exige o cross-cutting **ligado e testado**
(muito já está especificado nos ADRs de governança):

- **Segurança**: mTLS zero-trust, WAF, supply chain (Trivy/cosign/SBOM, admission),
  runtime (Falco), OpenBao. → `seguranca-runtime-siem.md`, plataforma de engenharia.
- **Confiabilidade**: SLO por serviço (Pyrra), HPA/KEDA, DR multi-região (RPO/RTO
  testado por chaos drill). → `continuidade-rpo-rto.md`.
- **Dado**: LGPD (PII, retenção, esquecimento), auditoria WORM, linhagem. →
  `dados-pii-lgpd.md`, `seguranca-runtime-siem.md`.
- **OT**: segmentação Purdue, sincronização de tempo (chrony/PTP), quality gate,
  comando de volta só pelo edge auditado. → `sincronizacao-tempo-ot.md`.
- **Qualidade**: TDD gate (já), eval set de IA, teste de carga (k6), teste contra
  simulador de linha. → `avaliacao-ia.md`.
- **Manufatura**: OEE, modelo de ativos ISA-95, motivo de parada. → docs respectivos.

**Definição de "pronto pra indústria" por serviço:** endurecimento (§1) feito +
paridade local/cluster (§2) + os aceites acima que se aplicam ao serviço.

---

## 5. Ordem de implementação sugerida (para o time)

```
Fase A — Endurecimento base (destrava produção de tudo)
  Keycloak + OpenBao + chaves reais · cadastro de linha (sensores/envelopes/ativos)
  · Schema Registry · estado durável (cursor/ledger/buffer)

Fase B — Fechar as 3 pesquisas (por cima da base endurecida)
  #7 persistência Ishikawa + MCP · #8 agente iDMSS · #9 imagem+visão · #10 inferência

Fase C — Escala e continuidade (cluster)
  service mesh · HPA/KEDA · DR multi-região · MLflow/Feast · SLO/Pyrra
```

Racional: **não adianta** fechar as pesquisas (Fase B) sobre uma base de dev (chave
literal, usuário em memória, cadastro fixo) se o alvo é fábrica. A **Fase A** é o que
transforma o scaffold em plataforma industrial; as pesquisas assentam por cima.

---

## 6. Backlog rastreável

- **Pesquisas**: issues **#6–#11** (conector MES, Ishikawa, iDMSS, visão, inferência,
  treino) — já abertas, com o núcleo de #6/#7/#8 entregue e verde.
- **Endurecimento**: issues a abrir por tema desta spec — identidade/segredos,
  estado durável, cadastro de linha, Schema Registry, paridade local↔cluster.
- **Este doc** é o contrato de "pronto pra indústria"; cada item vira aceite de PR.

Mapa geral do estado atual em [`estado-execucao-smt.md`](./estado-execucao-smt.md) e
[`mapa-implementacao.md`](./mapa-implementacao.md).
