"""Expand only capture lesson from the original PGN; preserve lessons 1..5.
Run from repository root. Source-game hash partition is shared with PrepareCurriculumPgn.
"""
import argparse, hashlib, io, json, sys
from pathlib import Path
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'output/training-validation/pgn-deps'))
import chess, chess.pgn, zstandard
from ChessTactics import capture_margin, position_key

LABELS=['free_capture','safe_capture','favorable_exchange']
def canonical(board): return min(position_key(board),position_key(board.mirror()))
def classify(board):
    pieces=len(board.piece_map())
    if pieces>20 or pieces<4 or board.is_check(): return None
    safe=[]; trades=[]
    for move in list(board.generate_legal_captures()):
        # Cheap material filter before examining all replies.
        captured=board.piece_type_at(move.to_square) or chess.PAWN
        board.push(move)
        replies=list(board.generate_legal_captures())
        recaptured=any(r.to_square==move.to_square for r in replies)
        terminal=board.is_game_over(claim_draw=True)
        board.pop()
        if terminal or capture_margin(board,move)<=0: continue
        (trades if recaptured else safe).append(move)
    if pieces<=6 and safe: return 0,safe[0]
    if 7<=pieces<=12 and safe: return 1,safe[0]
    # No easier positive-gain capture in the hard bucket.
    if trades and not safe: return 2,trades[0]
    return None

def main():
    ap=argparse.ArgumentParser();ap.add_argument('source',type=Path);ap.add_argument('--train-count',type=int,default=1024);ap.add_argument('--eval-count',type=int,default=64);ap.add_argument('--out',type=Path,default=Path('Data/Curriculum'));args=ap.parse_args()
    if min(args.train_count,args.eval_count)<1:ap.error('Counts must be positive')
    root=args.out;old=[json.loads(x) for x in (root/'positions.jsonl').read_text().splitlines()]
    # Never recycle an old held-out position into training, even from another game.
    reserved={canonical(chess.Board(r['fen'])) for r in old if r['split']=='eval'}
    legacy=root/'Legacy';legacy.mkdir(exist_ok=True)
    if not (legacy/'00_capture.eval.pgn').exists():
        (legacy/'00_capture.eval.pgn').write_bytes((root/'00_capture.eval.pgn').read_bytes())
        (legacy/'capture_eval.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in old if r['stage']==0 and r['split']=='eval'))
    reserved.update(canonical(chess.Board(json.loads(x)['fen'])) for x in (legacy/'capture_eval.jsonl').read_text().splitlines())
    counts={s:[0,0,0] for s in ['train','eval']};records=[];exports={s:[] for s in counts};seen=set();used_games={s:[set(),set(),set()] for s in counts}
    def need(split,d):return counts[split][d]<(args.train_count if split=='train' else args.eval_count)
    scanned=0
    with args.source.open('rb') as raw,zstandard.ZstdDecompressor().stream_reader(raw) as stream:
        text=io.TextIOWrapper(stream,encoding='utf-8')
        while (game:=chess.pgn.read_game(text)) is not None:
            scanned+=1
            if scanned%1000==0:print(scanned,counts,flush=True)
            site=game.headers.get('Site','')
            if game.errors or not site.startswith('https://lichess.org/') or game.headers.get('SetUp')=='1' or game.headers.get('Variant','Standard')!='Standard' or game.headers.get('Termination')!='Normal':continue
            split='eval' if int(hashlib.sha256(site.encode()).hexdigest(),16)%5==0 else 'train'
            if not any(need(split,d) for d in range(3)):continue
            moves=list(game.mainline_moves());board=game.board();used=set()
            for ply,move in enumerate(moves):
                if board.is_game_over(claim_draw=True):break
                pieces=len(board.piece_map())
                possible=([0] if pieces<=6 else [1,2] if pieces<=12 else [2]) if pieces<=20 else []
                if any(d not in used and need(split,d) and site not in used_games[split][d] for d in possible):
                    result=classify(board)
                    if result is not None:
                        difficulty,solution=result;key=canonical(board)
                        if difficulty not in used and need(split,difficulty) and key not in seen and site not in used_games[split][difficulty] and not (split=='train' and key in reserved):
                            seen.add(key);used.add(difficulty);used_games[split][difficulty].add(site);counts[split][difficulty]+=1
                            criterion=f'{LABELS[difficulty]}; positive two-ply material margin; no immediate mate conceded; no terminal capture'
                            records.append(dict(stage=0,difficulty=difficulty,split=split,gameUrl=site,startPly=ply,fen=board.fen(),criterion=criterion,solutionFirstMove=solution.uci(),category=LABELS[difficulty],movesToStart=[m.uci() for m in moves[:ply]]))
                            game.headers.update(CurriculumStage='0',CaptureDifficulty=str(difficulty),CurriculumStartPly=str(ply),CurriculumStartFEN=board.fen(),CurriculumCriterion=criterion,DatasetSplit=split)
                            exports[split].append(game.accept(chess.pgn.StringExporter(headers=True,variations=False,comments=False)))
                board.push(move)
                if all(not need(split,d) or d in used for d in range(3)):break
            if all(not need(s,d) for s in counts for d in range(3)):break
    if any(need(s,d) for s in counts for d in range(3)):raise RuntimeError(f'Not enough distinct games/positions: {counts}; no active dataset replaced')
    for split in counts:(root/f'00_capture.{split}.pgn').write_text('\n\n'.join(exports[split])+'\n',encoding='utf-8')
    records+= [r for r in old if r['stage']!=0]
    (root/'positions.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in records),encoding='utf-8')
    manifest=json.loads((root/'manifest.json').read_text());manifest.update(captureExpansion=dict(gamesScanned=scanned,sourceSha256=hashlib.sha256(args.source.read_bytes()).hexdigest(),counts=counts,labels=LABELS,perGamePerDifficulty=1,mirrorDeduplicated=True));manifest['counts']['capture']={s:sum(c) for s,c in counts.items()}
    (root/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n');print('Completed',counts,flush=True)
if __name__=='__main__':main()
