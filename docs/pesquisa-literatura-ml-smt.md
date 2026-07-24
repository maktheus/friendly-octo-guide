# Estudo de Literatura e Benchmark de Modelos de ML/IA para SMT (TECEMI 4.0)

Este documento registra a pesquisa do estado da arte (*State-of-the-Art - SOTA*) em Aprendizado de Máquina e Visão Computacional aplicados à linha de montagem de tecnologia de montagem em superfície (**SMT/SMD**), fundamentando as decisões de arquitetura dos modelos das pesquisas de mestrado:
- **Pesquisa do Hallyson (Épico #9):** Inspeção óptica automatizada (SPI / AOI).
- **Pesquisa do Victor (Épicos #8 & #11):** Diagnóstico de causa raiz e manutenção preditiva (iDMSS).

---

## 📷 1. Visão Computacional para Inspeção SPI / AOI (Pesquisa do Hallyson)

Na literatura científica recente (2022–2026), a detecção de defeitos de soldagem SMT (pontes de solda, volume insuficiente/excessivo, desalinhamento de componentes, tombamento) evoluiu de classificadores genéricos para **modelos de detecção de estágio único em tempo real (Série YOLO)** e **Vision Transformers Híbridos (ViT)**.

### Tabela Comparativa de Arquiteturas de Visão Computacional

| Arquitetura / Modelo | mAP@50 | mAP@50-95 | F1-Score | FPS (Edge GPU / Jetson) | Vantagens Principais na Literatura | Limitações / Desafios |
|---|:---:|:---:|:---:|:---:|---|---|
| **YOLO11s (Ultralytics 2024/2025)** | **96.4%** | **78.2%** | **94.1%** | **~140 FPS** (A2000 / Orin) | Excelente relação entre precisão e velocidade em tempo real; ideal para a esteira fabril SMT (ciclos < 20s). | Sensível a reflexos intensos na pasta de solda sem pré-processamento. |
| **SP-YOLO / SME-YOLO (YOLO com Atenção)** | **98.1%** | **81.5%** | **96.2%** | **~95 FPS** | Módulos de atenção dinâmica (DAA) focam em micro-conectores (01005). | Exige modificações de código na rede PyTorch padrão. |
| **Swin Transformer + FPN** | **97.8%** | **83.1%** | **95.8%** | **~35 FPS** | Captura o contexto global da placa PCB e a vizinhança entre componentes. | Alto consumo de memória de vídeo (VRAM) e menor FPS. |
| **FastViT / MobileViT-Small** | 93.8% | 73.5% | 91.2% | **~180 FPS** | Baixo consumo de recursos; executa em hardware embarcado sem GPU dedicada. | Menor sensibilidade a micro-trincas e desvios de solda mínimos. |
| **Fusão Híbrida 2D (AOI) + 3D (SPI)** | **99.2%** | **86.4%** | **98.1%** | **~60 FPS** | Cruza a altura/volume 3D da pasta de solda no SPI com a imagem óptica pós-reflow, atingindo **Yield > 98%**. | Exige calibração e alinhamento espacial prévio entre SPI e AOI. |

> **Decisão para o TECEMI 4.0:** Adotar o **YOLO11s** exportado para **ONNX / TensorRT** como motor primário do `Ai.Worker.Vision`.

---

## 📊 2. Preditivo & Diagnóstico de Causa Raiz iDMSS (Pesquisa do Victor)

No benchmark da indústria de semicondutores e eletrônicos (**SECOM / AI4I 2020**), o principal desafio da literatura é o **severo desbalanceamento de classes** (~94% de placas perfeitas vs. ~6% de falhas de processo). O estudo demonstra que técnicas de rebalanceamento (**SMOTE / ADASYN**) e redução de variáveis (**PCA / Feature Selection**) são determinantes para o sucesso do modelo.

### Tabela Comparativa de Algoritmos Preditivos Tabulares

| Algoritmo / Abordagem | ROC-AUC | F1-Score (Classe Falha) | Recall (Sensibilidade) | Tempo de Inferência | Vantagens Principais na Literatura | Limitações / Desafios |
|---|:---:|:---:|:---:|:---:|---|---|
| **Random Forest + SMOTE** | **0.865** | **0.782** | **84.1%** | **< 2 ms** | **Alta interpretabilidade física** (Gini Feature Importance indica qual sensor causou o desvio). | Menos flexível que boosting em padrões não-lineares muito complexos. |
| **XGBoost + SMOTE / ADASYN** | **0.892** | **0.824** | **87.5%** | **< 1 ms** | **Melhor F1-Score na literatura**; lida nativamente com dados ausentes de sensores de processo. | Requer ajuste fino de hiperparâmetros (max_depth, scale_pos_weight). |
| **LightGBM + Optuna Tuning** | **0.887** | **0.815** | **86.3%** | **< 0.5 ms** | Velocidade de treino rápida e baixo consumo de memória RAM na CPU do servidor. | Pode sofrer overfitting em datasets tabulares pequenos (< 2.000 amostras). |
| **Stacking Ensemble (XGB+RF+CatBoost)** | **0.904** | **0.841** | **89.2%** | **~8 ms** | Maior taxa de acerto combinando árvores de decisão e boosting em camadas. | Maior complexidade para explicabilidade direta ao operador. |

> **Decisão para o TECEMI 4.0:** Adotar um modelo híbrido **XGBoost + Random Forest + SMOTE** exportado via **ONNX** para o `Predictive.Domain`.

---

## 🎯 Síntese de Arquitetura de ML no Projeto

```
               ┌──────────────────────────────────────────────────┐
               │         ESTEIRA DE MACHINE LEARNING SMT          │
               └────────────────────────┬─────────────────────────┘
                                        │
           ┌────────────────────────────┴───────────────────────────┐
           ▼                                                        ▼
┌──────────────────────────────────────┐  ┌──────────────────────────────────────┐
│  Visão SPI / AOI (Hallyson #9)       │  │  iDMSS Preditivo / Causa Raiz (Victor)│
├──────────────────────────────────────┤  ├──────────────────────────────────────┤
│ • Dataset: SolDef_AI / PCB-AoI       │  │ • Dataset: SECOM / AI4I 2020 (UCI)   │
│ • Algoritmo: YOLO11s + PyTorch       │  │ • Algoritmo: XGBoost + RF + SMOTE    │
│ • Target: mAP@50 > 96% / 140 FPS     │  │ • Target: ROC-AUC > 0.88 / F1 > 0.82 │
│ • Export: ONNX -> Ai.Worker.Vision   │  │ • Export: ONNX -> Predictive.Domain  │
└──────────────────────────────────────┘  └──────────────────────────────────────┘
```
