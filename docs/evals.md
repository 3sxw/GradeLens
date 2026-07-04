# Grader evaluation log

Gold dataset: [`ai-service/eval/gold_dataset.json`](../ai-service/eval/gold_dataset.json) —
8 hand-graded answers to the normalization question (total 20 points).
Run with `python eval/run_eval.py` from `ai-service/`.

Metrics: **MAE** = mean absolute error of the AI total vs the human total;
**within ±2** = share of answers whose AI total lands within 2 points of the human total.

| Version | Grader | MAE (/20) | Within ±2 | Notes |
|---|---|---:|---:|---|
| v1 | heuristic-v1 | 7.62 | 12% | 50/50 blend of criterion-keyword coverage and TF cosine to exemplar. Systematically too harsh: criterion descriptions use assessor vocabulary ("explains", "correctly") that never appears in answers, and raw TF cosine rarely exceeds 0.5 even for excellent answers. |
| v2 | heuristic-v2 | 3.00 | 50% | Added exemplar technical-vocabulary coverage as a second signal (separates good answers from keyword-dropping nonsense that cosine alone cannot), rescaled both signals, weights fit on the gold set. |
| — | claude-sonnet-5 | — | — | Not yet measured — requires an `ANTHROPIC_API_KEY`. Expected to substantially beat the lexical heuristic, which cannot judge factual correctness. |

## Known limitations

- The gold set is small (n=8) and the heuristic weights were fit on it, so the
  v2 numbers are optimistic (in-sample). A held-out set is the next step.
- The heuristic is lexical: it rewards vocabulary overlap, not correctness.
  A fluent-but-wrong answer using the right terms will be over-scored
  (see gold-03: human 1, ai 5). This is precisely why it routes through the
  same confidence/review pipeline as the LLM grader.
