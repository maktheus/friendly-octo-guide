# Catalogação e Preparação de Datasets Públicos SMT/SMD

Este documento detalha os datasets públicos integrados e catalogados no repositório **TECEMI 4.0** para habilitar o desenvolvimento, treino e validação dos modelos de inteligência artificial das pesquisas de mestrado:
- **Pesquisa do Victor (Épicos #8 & #11):** Previsão de falhas e diagnósticos iDMSS (Random Forest).
- **Pesquisa do Hallyson (Épico #9):** Inspeção óptica automatizada (SPI / AOI) para detecção de defeitos de solda SMT.

---

## 📁 Estrutura Local de Datasets no Repositório

Os dados são mantidos e organizados sob o diretório `data/datasets/`:

```
data/datasets/
├── secom/                       # SECOM Semiconductor Manufacturing (UCI)
│   ├── secom.data               # 590 parâmetros de sensores de processo (5.3 MB)
│   ├── secom_labels.data        # Rótulos de falha/sucesso
│   └── secom.names              # Metadados e variáveis
├── ai4i_2020/                   # AI4I 2020 Predictive Maintenance (UCI)
│   └── ai4i2020.csv             # 10.000 amostras de séries temporais de sensores (522 KB)
├── soldef_ai/                   # SolDef_AI SMT Defect Dataset (Kaggle/Roboflow)
└── pcb_aoi/                     # PCB-AoI Dataset (KubeEdge Ianvs Project)
```

---

## 🛠️ Script de Download Automatizado

O script [`scripts/fetch_public_datasets.py`](file:///e:/UFAM/tecemi/scripts/fetch_public_datasets.py) realiza a criação das pastas e o download automático dos datasets abertos:

```bash
# Baixar e preparar datasets abertos (SECOM e AI4I 2020)
python scripts/fetch_public_datasets.py
```

### Comandos para Datasets de Visão SPI / AOI (Kaggle / GitHub)

Para baixar os datasets de visão computacional via Kaggle CLI ou GitHub:

```bash
# 1. SolDef_AI Dataset (1.150 imagens SMT multi-ângulo)
kaggle datasets download -d defectdetection/soldef-ai-pcb-dataset-for-mask-r-cnn -p data/datasets/soldef_ai --unzip

# 2. PCB-AoI Dataset (1.200 imagens de inspeção SPI/AOI - KubeEdge)
kaggle datasets download -d gpiosenka/pcb-aoi-dataset -p data/datasets/pcb_aoi --unzip

# 3. PCBA-DET Dataset (GitHub)
git clone https://github.com/ismh16/PCBA-Dataset.git data/datasets/pcb_det
```

---

## 📊 Detalhamento dos Datasets Catalogados

### 1. SECOM Semiconductor Manufacturing (Victor — Épico #8 & #11)
* **Origem:** UCI Machine Learning Repository / Kaggle
* **Tamanho:** 1.567 amostras, 590 variáveis contínuas de sensores.
* **Aplicação:** Treino do modelo **Random Forest** do iDMSS para substituição do peso estático (1.0), associando desvios de processo a falhas de placas.
* **Local:** `data/datasets/secom/`

### 2. AI4I 2020 Predictive Maintenance (Victor — Épico #8 & #11)
* **Origem:** UCI Machine Learning Repository / Kaggle
* **Tamanho:** 10.000 amostras temporais com medições de temperatura de ar/processo, rotação, torque e desgaste.
* **Aplicação:** Classificação multi-classe de tipos de falhas de máquina.
* **Local:** `data/datasets/ai4i_2020/`

### 3. SolDef_AI SMT Defect Dataset (Hallyson — Épico #9)
* **Origem:** Kaggle / Roboflow Universe
* **Tamanho:** 1.150 imagens de alta resolução em visões superior e lateral.
* **Defeitos:** Componentes desalinhados, falta de solda, excesso de solda, pontes de solda (SPI/AOI).
* **Aplicação:** Treino de modelos YOLOv8 / ViT em `Ai.Worker.Vision`.
* **Local:** `data/datasets/soldef_ai/`

### 4. PCB-AoI Dataset (Hallyson — Épico #9)
* **Origem:** KubeEdge Ianvs Project / Kaggle
* **Tamanho:** 1.200 imagens provenientes de 230+ placas PCB industriais.
* **Defeitos:** Falhas de pasta de solda em SPI e montagem de componentes SMT em AOI.
* **Local:** `data/datasets/pcb_aoi/`
