"""Read-only capture generalization audit; copies checkpoint bytes before inference."""
import sys,json,hashlib,argparse
from pathlib import Path
from collections import Counter,defaultdict
sys.dont_write_bytecode=True
from EvaluateChess import Model,start_board,legal_actions,balanced_samples
from ChessTactics import favorable_captures,capture_margin
import chess

def summarize(rows):
 return dict(episodes=len(rows),successes=sum(r['success'] for r in rows),success_rate=sum(r['success'] for r in rows)/len(rows) if rows else None)
def audit(model,records):
 rows=[]
 for index,r in enumerate(records):
  for mirrored in (False,True):
   board=start_board(r,mirrored);good=favorable_captures(board);assert good
   action,move=model.choose(board,0,1);success=action<4288 and move in good
   types='+'.join(sorted({chess.piece_name(board.piece_type_at(m.from_square)) for m in good}))
   selected=chess.piece_name(board.piece_type_at(move.from_square)) if move else 'claim'
   rows.append(dict(game=r['gameUrl'],startPly=r['startPly'],split=r['split'],mirrored=mirrored,fen=board.fen(),color='white' if board.turn else 'black',target_types=types,legal_count=board.legal_moves.count(),choices='1' if len(good)==1 else '2+',success=success,selected_piece=selected,selected=move.uci() if move else None,favorable=[m.uci() for m in good],failure='none' if success else 'draw_claim' if action>=4288 else 'bad_capture' if board.is_capture(move) else 'quiet'))
  if (index+1)%256==0:print('positions',index+1,flush=True)
 return rows

def main():
 ap=argparse.ArgumentParser();ap.add_argument('--run',default='results/curriculum_3');ap.add_argument('--out',default='output/training-validation/capture-gap-audit');args=ap.parse_args()
 out=Path(args.out);out.mkdir(parents=True,exist_ok=True);run=Path(args.run)
 history=[json.loads(l) for l in (run/'evaluation/history.jsonl').read_text().splitlines()]
 eligible=[h for h in history if h['evaluated_stage']==0 and h['evaluated_capture_difficulty']==0 and (run/'ChessV1'/f"ChessV1-{h['step']}.onnx").exists()]
 best=max(eligible,key=lambda h:h['scores'][0]['success_rate']);latest=max(eligible,key=lambda h:h['step'])
 records=[json.loads(l) for l in Path('Data/Curriculum/positions.jsonl').read_text().splitlines()];records=[r for r in records if r['stage']==0 and r['difficulty']==0]
 dataset_hash=hashlib.sha256(Path('Data/Curriculum/positions.jsonl').read_bytes()).hexdigest()
 assert all(h['dataset_sha256']==dataset_hash for h in [best,latest]), 'Dataset differs from recorded evaluation'
 results={}
 for label,h in [('best',best),('latest',latest)]:
  data=(run/'ChessV1'/f"ChessV1-{h['step']}.onnx").read_bytes();digest=hashlib.sha256(data).hexdigest();assert digest==h['model_sha256']
  model_path=out/f'{label}-{h["step"]}.onnx';model_path.write_bytes(data)
  rows=audit(Model(model_path),records);(out/f'{label}-cases.json').write_text(json.dumps(rows,indent=2))
  report=dict(step=h['step'],sha256=digest,groups={})
  for split in ['train','eval']:
   subset=[r for r in rows if r['split']==split];groups={}
   for field in ['color','target_types','choices','failure']:
    groups[field]={value:summarize([r for r in subset if r[field]==value]) for value in sorted({r[field] for r in subset})}
   report['groups'][split]=dict(total=summarize(subset),**groups)
  results[label]=report;print(label,json.dumps(report),flush=True)
 a=json.loads((out/'best-cases.json').read_text());b=json.loads((out/'latest-cases.json').read_text());assert len(a)==len(b)
 results['changes']={split:dict(Counter(('kept_correct' if x['success'] and y['success'] else 'regressed' if x['success'] else 'improved' if y['success'] else 'both_failed') for x,y in zip(a,b) if x['split']==split)) for split in ['train','eval']}
 results['dataset_sha256']=hashlib.sha256(Path('Data/Curriculum/positions.jsonl').read_bytes()).hexdigest()
 (out/'summary.json').write_text(json.dumps(results,indent=2));print('CHANGES',results['changes'],flush=True)
if __name__=='__main__':main()
