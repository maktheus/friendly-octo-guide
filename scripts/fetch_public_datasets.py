#!/usr/bin/env python3
"""
Script de Download e Preparação de Datasets Públicos SMT/SMD (TECEMI 4.0)
Baixa e estrutura os datasets para treino dos modelos de visão (Hallyson #9)
e preditivo Random Forest (Victor #8 & #11).
"""

import os
import sys
import urllib.request
import zipfile
import io

# Configura codificação utf-8 no stdout
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA_DIR = os.path.join(BASE_DIR, "data", "datasets")

SECOM_URL = "https://archive.ics.uci.edu/static/public/179/secom.zip"
AI4I_URL = "https://archive.ics.uci.edu/static/public/601/ai4i+2020+predictive+maintenance+dataset.zip"

def ensure_dirs():
    os.makedirs(os.path.join(DATA_DIR, "secom"), exist_ok=True)
    os.makedirs(os.path.join(DATA_DIR, "ai4i_2020"), exist_ok=True)
    os.makedirs(os.path.join(DATA_DIR, "soldef_ai"), exist_ok=True)
    os.makedirs(os.path.join(DATA_DIR, "pcb_aoi"), exist_ok=True)
    print(f"[+] Diretorios de datasets criados em: {DATA_DIR}")

def download_secom():
    target_dir = os.path.join(DATA_DIR, "secom")
    print("[*] Baixando SECOM Semiconductor Manufacturing Dataset (UCI)...")
    try:
        req = urllib.request.Request(SECOM_URL, headers={'User-Agent': 'Mozilla/5.0'})
        with urllib.request.urlopen(req) as resp:
            content = resp.read()
            with zipfile.ZipFile(io.BytesIO(content)) as z:
                z.extractall(target_dir)
        print(f"[OK] SECOM baixado e extraido com sucesso em: {target_dir}")
    except Exception as e:
        print(f"[ERR] Erro ao baixar SECOM: {e}")

def download_ai4i():
    target_dir = os.path.join(DATA_DIR, "ai4i_2020")
    print("[*] Baixando AI4I 2020 Predictive Maintenance Dataset (UCI)...")
    try:
        req = urllib.request.Request(AI4I_URL, headers={'User-Agent': 'Mozilla/5.0'})
        with urllib.request.urlopen(req) as resp:
            content = resp.read()
            with zipfile.ZipFile(io.BytesIO(content)) as z:
                z.extractall(target_dir)
        print(f"[OK] AI4I 2020 baixado e extraido com sucesso em: {target_dir}")
    except Exception as e:
        print(f"[ERR] Erro ao baixar AI4I 2020: {e}")

def print_instructions():
    print("\n" + "="*70)
    print(" INSTRUCOES PARA DATASETS DE VISAO SPI / AOI (Kaggle / Roboflow)")
    print("="*70)
    print("""
1. SolDef_AI Dataset (1.150 imagens SMT multi-angulo):
   kaggle datasets download -d defectdetection/soldef-ai-pcb-dataset-for-mask-r-cnn -p data/datasets/soldef_ai --unzip

2. PCB-AoI Dataset (1.200 imagens de inspecao SPI/AOI - KubeEdge):
   kaggle datasets download -d gpiosenka/pcb-aoi-dataset -p data/datasets/pcb_aoi --unzip

3. PCBA-DET Dataset (GitHub):
   git clone https://github.com/ismh16/PCBA-Dataset.git data/datasets/pcb_det
""")
    print("="*70)

def main():
    ensure_dirs()
    download_secom()
    download_ai4i()
    print_instructions()

if __name__ == "__main__":
    main()
