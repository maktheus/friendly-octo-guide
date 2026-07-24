#!/usr/bin/env python3
import os
import sys
import numpy as np
import pandas as pd
from sklearn.model_selection import StratifiedKFold
from sklearn.ensemble import RandomForestClassifier, GradientBoostingClassifier
from sklearn.metrics import roc_auc_score
from sklearn.impute import SimpleImputer
from sklearn.preprocessing import StandardScaler

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

BASE_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 1. AUDITORIA SECOM
secom_data = os.path.join(BASE_DIR, "data", "datasets", "secom", "secom.data")
secom_labels = os.path.join(BASE_DIR, "data", "datasets", "secom", "secom_labels.data")

X_sec = np.loadtxt(secom_data)
y_sec_raw = []
with open(secom_labels, "r") as f:
    for line in f:
        p = line.strip().split()
        if p: y_sec_raw.append(int(p[0]))
y_sec = np.where(np.array(y_sec_raw) == 1, 1, 0)

# Cleaning NAs & Variance
valid_cols = np.isnan(X_sec).mean(axis=0) < 0.45
X_sec = X_sec[:, valid_cols]

imp = SimpleImputer(strategy='median')
X_sec_imp = imp.fit_transform(X_sec)

cv = StratifiedKFold(n_splits=5, shuffle=True, random_state=42)
scores_sec = []
for tr, te in cv.split(X_sec_imp, y_sec):
    rf = RandomForestClassifier(n_estimators=50, max_depth=8, class_weight='balanced', random_state=42)
    rf.fit(X_sec_imp[tr], y_sec[tr])
    probs = rf.predict_proba(X_sec_imp[te])[:, 1]
    scores_sec.append(roc_auc_score(y_sec[te], probs))

print(f"SECOM Real ROC-AUC: {np.mean(scores_sec):.4f}")

# 2. AUDITORIA AI4I 2020
ai4i_path = os.path.join(BASE_DIR, "data", "datasets", "ai4i_2020", "ai4i2020.csv")
df = pd.read_csv(ai4i_path)
X_ai = df[["Air temperature [K]", "Process temperature [K]", "Rotational speed [rpm]", "Torque [Nm]", "Tool wear [min]"]].values
y_ai = df["Machine failure"].values

scaler = StandardScaler()
X_ai_sc = scaler.fit_transform(X_ai)

scores_ai = []
for tr, te in cv.split(X_ai_sc, y_ai):
    gb = GradientBoostingClassifier(n_estimators=100, max_depth=5, random_state=42)
    gb.fit(X_ai_sc[tr], y_ai[tr])
    probs = gb.predict_proba(X_ai_sc[te])[:, 1]
    scores_ai.append(roc_auc_score(y_ai[te], probs))

print(f"AI4I 2020 Real ROC-AUC: {np.mean(scores_ai):.4f}")
