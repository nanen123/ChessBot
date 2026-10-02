"""Audit all capture-0 positions against a saved evaluated checkpoint, read-only."""
import argparse, hashlib, json
from pathlib import Path
from collections import defaultdict
from DiagnoseCaptureGap import audit, summarize
from EvaluateChess import Model, terminal
import chess

def normalize_outcome(row):
    # Stage-0 episodes also succeed on immediate mate, even without a capture.
    row.setdefault('favorable_capture_selected',row['success'])
    if row['selected'] and row['failure']!='draw_claim':
        board=chess.Board(row['fen']);focus=board.turn;board.push_uci(row['selected'])
        ending=terminal(board,focus)
        if ending is not None:
            row['success']=ending[0];row['failure']='none' if ending[0] else ending[1]
            row['terminal_result']=ending[1]
    return row

def geometry(row):
    board=chess.Board(row['fen']); features=defaultdict(set)
    for uci in row['favorable']:
        move=chess.Move.from_uci(uci);dx=chess.square_file(move.to_square)-chess.square_file(move.from_square);dy=chess.square_rank(move.to_square)-chess.square_rank(move.from_square)
        piece=chess.piece_name(board.piece_type_at(move.from_square))
        distance=max(abs(dx),abs(dy))
        direction=('N' if dy>0 else 'S' if dy<0 else '')+('E' if dx>0 else 'W' if dx<0 else '')
        features['piece'].add(piece);features['distance'].add(str(distance));features['direction'].add(direction)
        features['piece_distance'].add(f'{piece}:{distance}')
        # Number of the same piece's pseudo-legal capture rays obstructed by friendly pieces.
        if piece in ('rook','bishop','queen'):
            rays=([(1,0),(-1,0),(0,1),(0,-1)] if piece!='bishop' else [])+([(1,1),(1,-1),(-1,1),(-1,-1)] if piece!='rook' else [])
            blocked=0
            for sx,sy in rays:
                x,y=chess.square_file(move.from_square)+sx,chess.square_rank(move.from_square)+sy
                while 0<=x<8 and 0<=y<8:
                    p=board.piece_at(chess.square(x,y))
                    if p:
                        blocked+=int(p.color==board.turn);break
                    x+=sx;y+=sy
            features['friendly_blocked_rays'].add(str(blocked))
    return {k:sorted(v) for k,v in features.items()}

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--run',default='results/curriculum_4');ap.add_argument('--out',default='output/training-validation/capture4-geometry');args=ap.parse_args()
    run=Path(args.run);out=Path(args.out);out.mkdir(parents=True,exist_ok=True)
    path=Path('Data/Curriculum/Generalization/positions.jsonl');digest=hashlib.sha256(path.read_bytes()).hexdigest()
    history=[json.loads(l) for l in (run/'evaluation/history.jsonl').read_text().splitlines()]
    eligible=[h for h in history if h['evaluated_stage']==0 and h['evaluated_capture_difficulty']==0 and h['dataset_sha256']==digest and (run/'ChessV1'/f"ChessV1-{h['step']}.onnx").exists()]
    h=max(eligible,key=lambda h:h['step']);data=(run/'ChessV1'/f"ChessV1-{h['step']}.onnx").read_bytes();assert hashlib.sha256(data).hexdigest()==h['model_sha256']
    snapshot=out/'snapshot.onnx';snapshot.write_bytes(data)
    records=[r for l in path.read_text().splitlines() if (r:=json.loads(l))['stage']==0 and r['difficulty']==0]
    rows=audit(Model(snapshot),records)
    for row in rows:
        normalize_outcome(row);row['geometry']=geometry(row)
    report=dict(step=h['step'],model_sha256=h['model_sha256'],dataset_sha256=digest,splits={})
    for split in ('train','eval','validation'):
        subset=[r for r in rows if r['split']==split];groups={}
        for field in ('piece','distance','direction','piece_distance','friendly_blocked_rays'):
            vals=sorted({v for r in subset for v in r['geometry'].get(field,[])})
            groups[field]={v:summarize([r for r in subset if v in r['geometry'].get(field,[])]) for v in vals}
        groups['failure']={v:summarize([r for r in subset if r['failure']==v]) for v in sorted({r['failure'] for r in subset})}
        report['splits'][split]=dict(total=summarize(subset),groups=groups)
    (out/'cases.json').write_text(json.dumps(rows,indent=2));(out/'summary.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report),flush=True)
if __name__=='__main__':main()
