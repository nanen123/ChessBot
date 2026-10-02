"""Paired horizontal-reflection diagnostic for a completed DiagnoseCaptureGap audit."""
import sys,json,argparse
from pathlib import Path
sys.dont_write_bytecode=True
sys.path.insert(0,'Tools')
from EvaluateChess import Model,start_board
from ChessTactics import favorable_captures
from ExpandCaptureCurriculum import canonical
from DiagnoseCaptureGap import summarize
import chess
ap=argparse.ArgumentParser();ap.add_argument('--audit',default='output/training-validation/capture-gap-audit');args=ap.parse_args()
root=Path(args.audit);summary=json.loads((root/'summary.json').read_text());model=Model(root/f"latest-{summary['latest']['step']}.onnx")
records=[json.loads(l) for l in Path('Data/Curriculum/positions.jsonl').read_text().splitlines()];train=[r for r in records if r['stage']==0 and r.get('difficulty')==0 and r['split']=='train'];keys={canonical(chess.Board(r['fen'])) for r in records if r['split']=='train'}
rows=[]
for r in train:
 for mirrored in [False,True]:
  full=start_board(r,mirrored);base=full.copy(stack=False)
  if base.castling_rights:continue
  flipped=base.transform(chess.flip_horizontal)
  if canonical(flipped) in keys:continue
  assert flipped.is_valid() and not flipped.is_game_over(claim_draw=True)
  good=favorable_captures(base);other=favorable_captures(flipped)
  expected={chess.Move(chess.square_mirror(m.from_square)^63,chess.square_mirror(m.to_square)^63,promotion=m.promotion).uci() for m in good}
  assert expected=={m.uci() for m in other}
  a,move=model.choose(base,0,1);b,m=model.choose(flipped,0,1);c,orig=model.choose(full,0,1)
  rows.append(dict(original=a<4288 and move in good,flipped=b<4288 and m in other,history_changes_action=a!=c))
report=dict(cases=len(rows),original=sum(r['original'] for r in rows),flipped=sum(r['flipped'] for r in rows),history_changes_action=sum(r['history_changes_action'] for r in rows));(root/'symmetry.json').write_text(json.dumps(report,indent=2));print(report)
cases=json.loads((root/'latest-cases.json').read_text());lines=[]
for r in cases:
 if r['split']!='eval':continue
 for move in sorted(set(r['favorable']+[r['selected']])):
  lines.append('|'.join([r['fen'],move,str(move in r['favorable'])]))
(root/'tactics.tsv').write_text('\n'.join(lines))
# Type-standardize evaluation results to the training proportions on shared strata.
t=summary['latest']['groups']['train']['target_types'];e=summary['latest']['groups']['eval']['target_types'];common=set(t)&set(e)
weights=sum(t[k]['episodes'] for k in common)
print('type-standardized eval',sum(t[k]['episodes']*e[k]['success_rate'] for k in common)/weights,'train support',weights)
