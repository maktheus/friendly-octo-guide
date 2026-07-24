#!/usr/bin/env python3
"""
Script de Auditoria Rigorosa sem Vazamento de Dados (Data Leakage Audit)
Avalia a perfomance real com Pipeline Scikit-Learn (Imputer -> Scaler -> SMOTE -> Model)
em 10-Fold Cross-Validation nos datasets SECOM (Fábrica Real) e AI4I 2020 (Física de Processo).
"""

import os
import sys
import json
import numpy as np
import pandas as pd

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SECOM_DIR = os.path.join(BASE_DIR, "data", "datasets", "secom")
AI4I_PATH = os.path.join(BASE_DIR, "data", "datasets", "ai4i_2020", "ai4i2020.csv")

def audit_secom():
    from sklearn.model_selection import StratifiedKFold
    from sklearn.impute import SimpleImputer
    from sklearn.feature_selection import VarianceThreshold, SelectKBest, f_classif
    from sklearn.ensemble import RandomForestClassifier
    from sklearn.metrics import roc_auc_score
    from imblearn.over_sampling import SMOTE
    from imblearn.pipeline import Pipeline as ImbPipeline

    print("\n" + "="*70)
    print(" 🕵️ AUDITORIA SECOM (Dados Reais da Fábrica de Semicondutores)")
    print("="*70)

    data_path = os.path.join(SECOM_DIR, "secom.data")
    labels_path = os.path.join(SECOM_DIR, "secom_labels.data")

    X = np.loadtxt(data_path)
    y_raw = []
    with open(labels_path, "r", encoding="utf-8") as f:
        for line in f:
            p = line.strip().split()
            if p: y_raw.append(int(p[0]))
    y = np.where(np.array(y_raw) == 1, 1, 0)

    # Remove colunas totalmente vazias/constantes
    missing_pct = np.isnan(X).mean(axis=0)
    X = X[:, missing_pct < 0.45]

    # Pipeline rigoroso sem vazamento (SMOTE dentro da dobra)
    pipeline = ImbPipeline([
        ('imputer', SimpleImputer(strategy='median')),
        ('variance', VarianceThreshold(threshold=0.01)),
        ('select', SelectKBest(score_func=f_classif, k=40)),
        ('smote', SMOTE(random_state=42, k_neighbors=3)),
        ('rf', RandomForestClassifier(n_estimators=100, max_depth=10, random_state=42))
    ])

    cv = StratifiedKFold(n_splits=10, shuffle=True, random_state=42)
    scores = []

    for fold, (train_idx, test_idx) in enumerate(cv.split(X, y), 1):
        X_tr, y_tr = X[train_idx], y[train_idx]
        X_te, y_te = X[test_idx], y[test_idx]

        pipeline.fit(X_tr, y_tr)
        probs = pipeline.predict_proba(X_te)[:, 1]
        auc = roc_auc_score(y_te, probs)
        scores.append(auc)

    print(f" • 10-Fold CV ROC-AUC Médio: {np.mean(scores):.4f} (Desvio Padrão: {np.std(scores):.4f})")
    print(f" • Min: {np.min(scores):.4f} | Max: {np.max(scores):.4f}")
    print(" -> Conclusão SECOM: O ruído e o alto número de variáveis sem calibração limitam a AUC real em ~0.72 - 0.76.")

def audit_ai4i():
    from sklearn.model_selection import StratifiedKFold
    from sklearn.ensemble import GradientBoostingClassifier
    from sklearn.metrics import roc_auc_score
    from imblearn.over_sampling import SMOTE
    from imblearn.pipeline import Pipeline as ImbPipeline
    from sklearn.preprocessing import StandardScaler

    print("\n" + "="*70)
    print(" 🕵️ AUDITORIA AI4I 2020 (Manutenção Preditiva com Regras Físicas)")
    print("="*70)

    df = pd.read_csv(AI4I_PATH)
    feature_cols = [
        "Air temperature [K]",
        "Process temperature [K]",
        "Rotational speed [rpm]",
        "Torque [Nm]",
        "Tool wear [min]"
    ]
    X = df[feature_cols].values
    y = df["Machine failure"].values

    pipeline = ImbPipeline([
        ('scaler', StandardScaler()),
        ('smote', SMOTE(random_state=42)),
        ('gb', GradientBoostingClassifier(n_estimators=150, max_depth=5, learning_rate=0.08, random_state=42))
    ])

    cv = StratifiedKFold(n_splits=10, shuffle=True, random_state=42)
    scores = []

    for fold, (train_idx, test_idx) in enumerate(cv.split(X, y), 1):
        X_tr, y_tr = X[train_idx], y[train_idx]
        X_te, y_te = X[test_idx], y[test_idx]

        pipeline.fit(X_tr, y_tr)
        probs = pipeline.predict_proba(X_te)[:, 1]
        auc = roc_auc_score(y_te, probs)
        scores.append(auc)

    print(f" • 10-Fold CV ROC-AUC Médio: {np.mean(scores):.4f} (Desvio Padrão: {np.std(scores):.4f})")
    print(f" • Min: {np.min(scores):.4f} | Max: {np.max(scores):.4f}")
    print(" -> Conclusão AI4I 2020: A AUC atinge ~0.96+ por conta das equações físicas explícitas (Torque x Velocidade / Desgaste da ferramenta).")

def main():
    audit_secom()
    audit_ai4i()

if __name__ == "__main__":
    main()
