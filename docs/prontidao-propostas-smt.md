# Prontidão da plataforma para as 3 propostas de mestrado (SMT/SMD)

**Documento de planejamento — 2026-07-16.** Avalia o quanto a plataforma de linha
(este repo) está preparada para **hospedar/sustentar** três pesquisas de Mestrado
em Engenharia de Produção (UFAM, Polo Industrial de Manaus, orientador Dércio Luiz
Reis) e o que falta para "recebê-las". Não é análise das pessoas — é análise das
**propostas técnicas** contra a arquitetura.

## As três propostas, em uma linha

| Autor | Proposta | Entrada | Técnica | Saída |
|---|---|---|---|---|
| **Hallyson** | Diagnóstico de defeitos de **impressão de pasta de solda** (SMT) | imagens/dados de máquinas **SPI** (Solder Paste Inspection) | **visão computacional** / deep learning híbrido (features locais + dependências globais) | classifica defeito + causa raiz (ex.: desgaste de estêncil), menos falsa detecção |
| **Victor Rosas** | **iDMSS** — assistente virtual p/ análise de falha e causa raiz em linha **SMD** | **SCADA** + **MES** | **Random Forest** + **Ishikawa** | causa raiz, ação corretiva, menos incerteza na decisão |
| **Jeymerson** | **iDMSS** p/ **gestão do conhecimento** da linha SMD (visibilidade/transparência) | automação + **MES** | base de conhecimento explícito+tácito, **inferência lógica**, Ishikawa | reduzir tempo perda→ação, visibilidade das causas |

**Observação central:** os três projetos descrevem, em vocabulário acadêmico,
exatamente o que a plataforma já é em vocabulário de engenharia. Um **iDMSS**
(intelligent Decision Maker Support System) é, na arquitetura, o par
**Decision.Engine (acatech 5) + Chatbot & Agentes (5b)**. "Visibilidade e
transparência" são os **estágios 3–4 do acatech** que a espinha de observabilidade
já entrega. Diagnóstico de causa raiz é o que os **Agentes de operação** fazem
(correlacionam trace + log + telemetria). Ou seja: **as propostas são aplicações
sobre a plataforma, não uma plataforma nova.**

---

## Como cada proposta mapeia nos blocos existentes

### Hallyson — visão computacional sobre SPI

| Precisa de | Bloco da plataforma | Prontidão |
|---|---|---|
| Ingerir dados/imagens da máquina SPI | `Edge.ProtocolGateway` | 🟡 fala OPC-UA/Modbus/MQTT (grandezas escalares), **não imagem** |
| Rodar inferência de visão | `Ai.Worker.Vision` (pods isolados por tipo) | 🟡 esqueleto existe; falta o **modelo treinado** |
| Guardar imagens + saídas | MinIO (Big Data Pool, WORM) | ✅ serve object storage |
| Escorar/alertar em tempo real | `Predictive` (scoring no stream) | ✅ padrão existe |
| Treinar/versionar o modelo | Feast + MLflow | ❌ fase 2 (ver `carga-operacional.md`) |

**Prontidão geral: 🟡 parcial.** O caminho de IA assíncrona (router → worker de
visão → GPU → DLQ → idempotência) já está desenhado e o `Ai.Worker.Vision` existe.
Os furos são **específicos de imagem**: o contrato `sensor-reading.avsc` carrega um
`double`, não um frame; falta um **caminho de ingestão de imagem** (referência a
objeto no MinIO + metadados) e o modelo em si.

### Victor Rosas — iDMSS com Random Forest sobre SCADA/MES

| Precisa de | Bloco da plataforma | Prontidão |
|---|---|---|
| Ingerir dados de **SCADA/MES** | `Edge.ProtocolGateway` | 🟡 SCADA (nível 2) sim; **MES (nível 3/4) é conector novo** |
| Modelo Random Forest (causa raiz) | `Predictive` (scoring online) | 🟡 hoje é EWMA/z-score; falta pipeline RF |
| Estruturar conhecimento (Ishikawa) | `Knowledge` (JSONB + pgvector) | 🟡 store existe; falta o **modelo de causa** |
| Assistente conversacional | `Chatbot` (RAG + RBAC) | ✅ existe |
| Apoio à decisão com guardrails | `Decision.Engine` (envelope + aprovação humana) | ✅ existe |
| Agir por ferramenta auditada | `Agents` + MCP tool layer | ✅ existe |

**Prontidão geral: 🟡→✅.** É a proposta **mais alinhada**: Decision.Engine +
Chatbot + Agents + Predictive já são a espinha de um iDMSS com guardrails e
auditoria. Os furos: **conector MES**, o **treino do Random Forest** e a
**modelagem do Ishikawa** como base de causa raiz.

### Jeymerson — iDMSS para gestão do conhecimento e transparência

| Precisa de | Bloco da plataforma | Prontidão |
|---|---|---|
| Integrar dados de automação + **MES** | `Edge.ProtocolGateway` | 🟡 MES é conector novo |
| Base de conhecimento explícito+tácito | `Knowledge` (RAG, pgvector) | 🟡 store existe; falta a base |
| Inferência lógica / Ishikawa | `Agents` + `Knowledge` | 🟡 RAG cobre parte; regras lógicas explícitas seriam adicionais |
| **Visibilidade e transparência** | Espinha de observabilidade (acatech 3–4) | ✅ **é o coração da plataforma** |
| Reduzir tempo perda→ação | `Agents` (diagnóstico) + alerta/on-call | ✅ existe |

**Prontidão geral: ✅→🟡.** O objetivo dele — "visibilidade e transparência das
causas raízes" — é literalmente o que a plataforma entrega nos estágios 3–4 do
acatech (trace-id ponta a ponta, linhagem, painel ao vivo). O que falta é a
**camada de conhecimento estruturada** e a **ingestão de MES**.

---

## O que os três compartilham (os gaps transversais)

Um padrão claro: **os furos se repetem**. Resolver os transversais destrava os três.

1. **Conector MES/SCADA (FEITO 23/07).** Implementado o `Mes.Connector` com suporte a `RestMesAdapter` (APIs REST) e `SqlMesAdapter` (visões SQL parametrizadas via Dapper/SqlClient/Npgsql) + `PostgresCursorStore`. Atende ordens, apontamentos e rastreabilidade dos três mestrandos.

2. **Base de causa raiz / Ishikawa (2 de 3).** Cabe no `Knowledge` (pgvector),
   e **conecta com o que já foi construído**: o schema `parada-linha.avsc`
   (motivo de parada classificado) e o `modelo-de-ativos-isa95.md` (onde o defeito/
   parada se pendura). A árvore de ativos + o catálogo de motivos são o esqueleto
   do Ishikawa — já meio caminho andado.

3. **Ingestão de imagem (só Hallyson).** Caminho novo além da telemetria escalar:
   referência a objeto no MinIO + metadados no contrato.

4. **Treino e registro de modelo (os três, em graus diferentes).** Feast (features)
   + MLflow (versionamento) estão no diagrama mas são **fase 2**. Sem eles, dá pra
   treinar offline e servir o modelo estático no worker; com eles, vira contínuo.

5. **Dados reais rotulados do PIM.** Nenhuma das três roda sem dataset real da
   linha — é pré-requisito de fora do software.

---

## Resumo de prontidão

| Bloco necessário | Hallyson | Victor | Jeymerson |
|---|:---:|:---:|:---:|
| Ingestão chão de fábrica (edge) | 🟡 imagem | ✅ SCADA | ✅ automação |
| Conector MES | — | ✅ | ✅ |
| IA (visão / ML / agentes) | 🟡 visão | 🟡 RF | ✅ agentes/RAG |
| Decisão com guardrails | ✅ | ✅ | ✅ |
| Conhecimento / Ishikawa | — | 🟡 | 🟡 |
| Observabilidade (visibilidade) | ✅ | ✅ | ✅ |
| Treino/registro de modelo | ❌ f2 | ❌ f2 | 🟡 |
| Dados rotulados do PIM | ❌ | ❌ | ❌ |

Legenda: ✅ pronto · 🟡 parcial/adaptar · ❌ falta · f2 = fase 2 · — não se aplica

---

## Plano para "receber" as propostas

### Fase A — destravar o comum (serve os três)
- **Conector MES** ao lado do `Edge.ProtocolGateway` (ordens/apontamentos → Kafka,
  mesmo padrão de quality gate + quarentena).
- **Base de causa raiz (Ishikawa)** no `Knowledge`, ligada ao `parada-linha.avsc`
  e à árvore ISA-95 — reusa o que já existe.
- Confirmar que a **linhagem + clock_source** (já implementados) dão a
  correlação temporal confiável que os três diagnósticos exigem.

### Fase B — por proposta
- **Hallyson:** caminho de **ingestão de imagem** (contrato + refs MinIO) →
  `Ai.Worker.Vision` com o modelo de visão → scoring/alerta no `Predictive`.
- **Victor:** pipeline de **Random Forest** (treino offline → servir no worker) +
  o **agente iDMSS** (Chatbot + Decision.Engine + Agents) consumindo MES/SCADA.
- **Jeymerson:** **gestão do conhecimento** (Knowledge + RAG) + motor de
  inferência sobre a base Ishikawa; a transparência já vem da observabilidade.

### Fase C — continuidade (fase 2 da plataforma)
- **Feast + MLflow** para treino/registro contínuo (hoje: modelo estático serve).
- **Dataset rotulado** do PIM e validação em campo (fora do software).

---

## Veredito

A plataforma é uma **base surpreendentemente aderente** às três propostas — ela já
é um "iDMSS + observabilidade + IA + decisão com guardrails" para linha SMT/SMD, e
o trabalho recente (parada-linha/OEE, ISA-95, clock_source, auditoria) empurra
justamente o que esses diagnósticos de causa raiz precisam. O que separa "receber a
proposta" de "não receber" **não é a arquitetura** — é um punhado de integrações
pontuais, sendo o **conector MES** o gargalo comum, mais **ingestão de imagem** (só
Hallyson) e **treino de modelo** (fase 2). Nenhum deles pede repensar a plataforma;
todos pedem plugar dados reais e modelos nela.
