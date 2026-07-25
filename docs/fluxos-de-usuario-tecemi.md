# Mapeamento Completo de Fluxos de Usuário (User Flows) — TECEMI 4.0

Este documento apresenta o mapeamento detalhado dos **fluxos de trabalho e experiência do usuário (User Flows)** para todas as personas que interagem com a plataforma **TECEMI 4.0** na linha de montagem industrial SMT (*Surface Mount Technology*).

---

## 👤 Mapeamento de Personas da Plataforma

```mermaid
graph TD
    A["Plataforma TECEMI 4.0"] --> B["1. Operador da Linha SMT"]
    A --> C["2. Engenheiro de Processo / Qualidade"]
    A --> D["3. Engenheiro de Manutenção Preditiva"]
    A --> E["4. Gerente de Fábrica / Operações"]
    A --> F["5. Administrador de TI / DevOps"]
```

---

## 🛠️ 1. Operador da Linha SMT (Operador de Produção & Inspeção)

### **Perfil & Objetivos:**
Atua diretamente no chão de fábrica acompanhando as máquinas SPI (*Solder Paste Inspection*), Pick-and-Place e AOI (*Automated Optical Inspection*). Necessita de alertas visuais rápidos, sem atrito, com tomadas de decisão imediatas sobre placas defeituosas.

### **Fluxo de Trabalho Passo a Passo:**

```mermaid
sequenceDiagram
    autonumber
    actor OP as Operador SMT
    participant GW as YARP Gateway
    participant VIS as Módulo Visão (YOLO11s)
    participant MES as Conector MES
    participant UI as IHM / Painel Industrial

    OP->>GW: 1. Autenticação rápida (Matrícula + TOTP 6 dígitos)
    GW->>UI: 2. Exibe Painel de Monitoramento da Linha Ativa
    VIS->>UI: 3. Dispara Alerta em Tempo Real: Placa #PCB-8891 com defeito SPI (Falta de Solda no C12)
    OP->>UI: 4. Clica no alerta e visualiza a imagem anotada (Bounding Box + mAP 96.4%)
    OP->>UI: 5. Valida se é defeito real ou falso alarme
    alt Defeito Confirmado
        OP->>UI: 6a. Clica em "Confirmar Refugo / Retrabalho"
        UI->>MES: 7a. Envia evento de descarte para o MES (Dapper/REST)
    else Falso Positivo
        OP->>UI: 6b. Clica em "Descartar Alerta (Falso Positivo)"
        UI->>VIS: 7b. Registra feedback para ajuste fino do modelo
    end
    UI->>OP: 8. Alerta resolvido, linha continua em operação normal
```

---

## 🔬 2. Engenheiro de Processo e Qualidade

### **Perfil & Objetivos:**
Responsável por investigar as causas raízes dos defeitos recorrentes de soldagem e desvios de processo, aplicando a metodologia Ishikawa (6M) para evitar parada de linha e aplicar ações corretivas.

### **Fluxo de Trabalho Passo a Passo:**

```mermaid
sequenceDiagram
    autonumber
    actor ENG as Eng. de Processo
    participant UI as Portal de Diagnóstico
    participant KNOW as Knowledge.Api (pgvector)
    participant LOGIC as Motor Ishikawa (6M)
    participant CHAT as Assistente MCP / Chatbot

    ENG->>UI: 1. Acessa o Portal e identifica pico de defeito de "Ponte de Solda" (Bridging)
    ENG->>CHAT: 2. Pergunta ao Assistente: "Quais as causas raízes para ponte de solda na insersora 02?"
    CHAT->>KNOW: 3. Consulta vetorial HNSW em pgvector por histórico de RNCs similares
    CHAT->>LOGIC: 4. Executa consulta GraphQL 'avaliarRegrasIshikawa'
    LOGIC-->>CHAT: 5. Retorna Grafo 6M (Máquina: Pressão do Squeegee alta; Material: Viscosidade da pasta baixa)
    CHAT->>UI: 6. Exibe Diagrama Ishikawa com Árvore de Explicação (XAI) e ranking de probabilidades
    ENG->>UI: 7. Seleciona a Ação Corretiva: "Ajustar viscosidade da pasta no lote B-402"
    ENG->>UI: 8. Registra o Relatório de Não Conformidade (RNC) e envia para homologação
```

---

## ⚡ 3. Engenheiro de Manutenção Preditiva

### **Perfil & Objetivos:**
Monitora a saúde mecânica e elétrica dos equipamentos SMT (forno de reflow, motores de Pick-and-Place) para atuar antes que ocorra uma quebra não planejada.

### **Fluxo de Trabalho Passo a Passo:**

```mermaid
sequenceDiagram
    autonumber
    actor MAN as Eng. de Manutenção
    participant IDMSS as Sistema Preditivo iDMSS
    participant VALK as Valkey Lock (SET NX)
    participant NOTIF as Serviço de Notificação
    participant UI as Dashboard Preditivo

    IDMSS->>VALK: 1. Verifica trava atômica de idempotência para o job de análise preditiva
    IDMSS->>IDMSS: 2. Processa telemetria dos sensores (Temperatura, Vibração, Torque, Tool Wear)
    IDMSS->>NOTIF: 3. Detecta anomalia no Motor 3 da Insersora (85% risco de falha em 4 horas)
    NOTIF->>MAN: 4. Envia notificação de alta prioridade (Push / E-mail / Painel)
    MAN->>UI: 5. Acessa a curva de tendência da telemetria e o tempo estimado para falha (RTL)
    MAN->>UI: 6. Clica em "Agendar Manutenção Preventiva na Troca de Turno"
    UI->>MAN: 7. Ordem de Serviço (OS) gerada automaticamente e peça reservada no estoque
```

---

## 📈 4. Gerente de Fábrica / Diretor de Operações

### **Perfil & Objetivos:**
Necessita de visão macro da eficiência global dos equipamentos (OEE - *Overall Equipment Effectiveness*), taxa de refugo (*scrap rate*), rendimento de primeira passagem (*First Pass Yield - FPY*) e retorno sobre investimento das soluções de IA.

### **Fluxo de Trabalho Passo a Passo:**

```mermaid
sequenceDiagram
    autonumber
    actor GER as Gerente de Fábrica
    participant UI as Dashboard Executivo (Portal Web)
    participant GATEWAY as API Gateway YARP
    participant ARCHIVER as Data Archiver (Parquet)

    GER->>GATEWAY: 1. Autenticação corporativa SSO (RBAC: Perfil Executivo)
    GATEWAY->>UI: 2. Renderiza Dashboard Executivo de OEE e Métricas FPY
    GER->>UI: 3. Aplica filtros por Linha de Produção (Linha SMT 01 vs 02) e Turno
    UI->>ARCHIVER: 4. Consulta métricas consolidadas em frio Parquet/S3
    ARCHIVER-->>UI: 5. Retorna indicadores acumulados do mês
    GER->>UI: 6. Visualiza redução de 30% nos falsos alarmes de AOI e ROI da IA
    GER->>UI: 7. Exporta Relatório Gerencial consolidado em PDF / HTML
```

---

## 🛡️ 5. Administrador de TI / Engenheiro DevOps & SecOps

### **Perfil & Objetivos:**
Garante a estabilidade, segurança, monitoramento de saúde dos microsserviços, segredos no OpenBao, conectividade do Conector MES e performance do barramento Kafka.

### **Fluxo de Trabalho Passo a Passo:**

```mermaid
sequenceDiagram
    autonumber
    actor ADM as Administrador de TI
    participant ASPIRE as Dashboard Aspire / OpenTelemetry
    participant GW as YARP Gateway
    participant VAULT as OpenBao / PlatformSecrets
    participant MES as Conector MES Legado

    ADM->>ASPIRE: 1. Acessa o Painel de Telemetria e Saúde dos Serviços (OTel)
    ASPIRE->>ADM: 2. Exibe status dos 16 módulos C#, latências de inferência (<15ms) e uso de CPU/RAM
    ADM->>GW: 3. Revisa políticas de segurança (TOTP RFC 6238, validade JWT de 5 min)
    ADM->>VAULT: 4. Rotaciona credenciais de banco e chaves de APIs com zero downtime
    ADM->>MES: 5. Testa conectividade do Conector MES Dual (REST & SQL via Dapper em PostgreSQL)
    MES-->>ADM: 6. Confirma sincronização do cursor durável de eventos 'PostgresCursorStore'
```

---

## 📊 Matriz Comparativa de Acesso por Persona

| Funcionalidade / Módulo | Operador SMT | Eng. Processo | Eng. Manutenção | Gerente Fábrica | Admin TI / DevOps |
|---|:---:|:---:|:---:|:---:|:---:|
| **Alertas de Visão SPI/AOI em Tempo Real** | 🟢 Total | 🟡 Leitura | ⚪ N/A | ⚪ N/A | 🟡 Monitor |
| **Diagrama & Regras Ishikawa 6M (XAI)** | ⚪ N/A | 🟢 Total | 🟡 Leitura | 🟡 Resumo | ⚪ N/A |
| **Assistente de IA & Chatbot MCP** | 🟡 Consulta | 🟢 Total | 🟢 Total | 🟡 Consulta | 🟡 Monitor |
| **Dashboards Preditivos iDMSS** | ⚪ N/A | 🟡 Leitura | 🟢 Total | 🟡 Resumo | ⚪ N/A |
| **Dashboards Executivos de OEE / FPY** | ⚪ N/A | 🟡 Leitura | 🟡 Leitura | 🟢 Total | 🟡 Monitor |
| **Gestão de Segredos & Gateway YARP** | ⚪ N/A | ⚪ N/A | ⚪ N/A | ⚪ N/A | 🟢 Total |
| **Configuração do Conector MES Dual** | ⚪ N/A | ⚪ N/A | ⚪ N/A | ⚪ N/A | 🟢 Total |

---

## 🎯 Conclusão

Esta arquitetura de fluxos garante que **cada perfil de usuário possui uma experiência otimizada e sem ruídos**:
1. O **Operador** foca em **agilidade operacional sem cliques desnecessários**.
2. Os **Engenheiros** focam em **análise técnica de causa raiz e manutenção preditiva**.
3. O **Gerente** foca em **indicadores estratégicos de OEE e ROI**.
4. O **Administrador de TI** foca em **segurança, auditabilidade e resiliência da infraestrutura**.
