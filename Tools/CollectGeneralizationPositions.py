"""Collect additional training and disjoint validation positions from unused real games."""
import sys,json,hashlib,io,multiprocessing,argparse
from pathlib import Path
from collections import Counter,defaultdict
sys.dont_write_bytecode=True
from CurriculumGeneralization import orbit,bucket
from ExpandRemainingCurriculum import analyze as analyze_remaining,games,CATEGORIES
from ExpandCaptureCurriculum import classify,LABELS
import chess,chess.pgn
ROOT=Path('Data/Curriculum')

USED=set()
def initialize(used):
    global USED
    USED=used

def analyze(job):
    raw,need=job
    used=USED
    game=chess.pgn.read_game(io.StringIO(raw));site=game.headers.get('Site','')
    if site in used or game.errors or game.headers.get('Termination')!='Normal' or game.headers.get('SetUp')=='1' or game.headers.get('Variant','Standard')!='Standard' or not site.startswith('https://lichess.org/'):return None
    split='validation' if int(hashlib.sha256(site.encode()).hexdigest(),16)%5==0 else 'train'
    if not need[split]:return None
    candidates=[]
    if split=='validation':
        needed={'train':[],'eval':[(stage,c) for stage,cats in CATEGORIES.items() for c in cats if (stage,-1,c) in need[split]]}
        result=analyze_remaining((raw,needed))
        if result:candidates+=result['candidates']
    board=game.board();moves=list(game.mainline_moves());seen=set()
    for ply,move in enumerate(moves):
        if board.is_game_over(claim_draw=True):break
        pieces=len(board.piece_map())
        if pieces<=20 and (split=='validation' or pieces<=6):
            possible=0 if pieces<=6 else 1 if pieces<=12 else 2
            if any(key[0]==0 and (key[1]==possible or key[1]==2) for key in need[split]):
                case=classify(board)
                if case:
                    difficulty,solution=case;piece=chess.piece_name(board.piece_type_at(solution.from_square));key=(0,difficulty,piece if split=='train' else LABELS[difficulty])
                    if key in need[split] and key not in seen:
                        candidates.append(dict(stage=0,difficulty=difficulty,category=LABELS[difficulty],startPly=ply,fen=board.fen(),solutionFirstMove=solution.uci(),eco=game.headers.get('ECO','Unknown')));seen.add(key)
        board.push(move)
    return dict(site=site,split=split,moves=[m.uci() for m in moves],candidates=candidates)

def main():
    ap=argparse.ArgumentParser();ap.add_argument('source',type=Path);args=ap.parse_args()
    old=[json.loads(l) for l in (ROOT/'positions.jsonl').read_text().splitlines()]
    for p in (ROOT/'Legacy').glob('*.jsonl'):old += [json.loads(l) for l in p.read_text().splitlines()]
    used={r['gameUrl'] for r in old};protected={orbit(chess.Board(r['fen'])) for r in old};counts=Counter();pergame=Counter();records=[]
    targets={'train':{(0,0,p):32 for p in ['pawn','knight','bishop','rook','queen','king']},'validation':{(0,d,LABELS[d]):64 for d in range(3)}}
    targets['validation'].update({(stage,-1,c):32 if stage!=4 else 64 for stage,cats in CATEGORIES.items() for c in cats})
    def jobs():
        for raw in games(args.source):yield raw,{s:{k for k,v in ts.items() if counts[s,k]<v} for s,ts in targets.items()}
    scanned=0
    with multiprocessing.Pool(4,initializer=initialize,initargs=(used,)) as pool:
        for item in pool.imap(analyze,jobs(),chunksize=4):
            scanned+=1
            if item:
                split=item['split']
                for r in item['candidates']:
                    board=chess.Board(r['fen']);key=(r['stage'],r.get('difficulty',-1),bucket(r,board) if split=='train' else r['category'])
                    if key not in targets[split] or counts[split,key]>=targets[split][key]:continue
                    canonical=orbit(board);gamekey=(item['site'],key)
                    if canonical in protected or pergame[gamekey]>=(4 if r['stage']==3 else 1):continue
                    records.append(dict(r,split=split,gameUrl=item['site'],movesToStart=item['moves'][:r['startPly']],criterion='additional unused-game generalization dataset'))
                    protected.add(canonical);pergame[gamekey]+=1;counts[split,key]+=1
            if scanned%5000==0:print(scanned,dict(counts),flush=True)
            if all(counts[s,k]>=v for s,ts in targets.items() for k,v in ts.items()):break
    missing={str((s,k)):counts[s,k] for s,ts in targets.items() for k,v in ts.items() if counts[s,k]<v}
    if missing:raise RuntimeError(str(missing))
    out=ROOT/'Generalization';out.mkdir(exist_ok=True)
    (out/'additional.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in records))
    (out/'collection.json').write_text(json.dumps(dict(sourceSha256=hashlib.sha256(args.source.read_bytes()).hexdigest(),gamesScanned=scanned,counts={str(k):v for k,v in counts.items()}),indent=2));print('DONE',len(records),flush=True)
if __name__=='__main__':multiprocessing.freeze_support();main()
