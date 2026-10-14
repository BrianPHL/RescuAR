# PREP Validation Acceptance Criteria

## Primary acceptance criterion

The project-defined acceptable flood-extent accuracy rate is **85%**, operationalized as:

```text
F1 >= 0.85
```

F1 is used as the primary acceptance measure rather than raw overall pixel accuracy because flood/non-flood maps are class-imbalanced.

## Supporting diagnostics

These metrics remain reported for interpretation and calibration, but they do not override the primary F1 PASS/FAIL decision:

- Precision >= 0.80
- Recall >= 0.80
- IoU / CSI >= 0.70
- Population WAPE <= 20%
- Population Spearman >= 0.80

These are **project-defined research/engineering thresholds**, not universal PAGASA, NAMRIA, NDRRMC, legal, or regulatory standards.
