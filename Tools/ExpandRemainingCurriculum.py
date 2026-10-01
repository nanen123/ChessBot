"""Expand curriculum stages 1..5 from real PGNs; keep capture curriculum intact.
Parallel analysis is deterministic: source games are consumed in original order.
"""
import argparse,hashlib,io,json,math,multiprocessing,sys
from collections import Counter,defaultdict
from pathlib import Path
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'output/training-validation/pgn-deps'))
import chess,chess.pgn,zstandard
from ChessTactics import capture_margin,position_key,material
from ExpandCaptureCurriculum import canonical
from PrepareCurriculumPgn import mate_one,mate_two,NAMES

CATEGORIES={1:['safe_gain','favorable_exchange','capture_choice'],2:['mateIn1','mateIn2'],3:['KQK','KRK'],4:['middlegame'],5:['opening3','opening4']}
TRAIN={1:1024,2:1024,3:1024,4:5120,5:512}
EVAL={1:64,2:64,3:64,4:256,5:64}

def exchange_case(board):
    good=[];bad=[]
    for move in list(board.generate_legal_captures()):
        margin=capture_margin(board,move)
        if margin>0:good.append(move)
        elif margin<0:bad.append(move)
    if not good:return None
    if bad:return 'capture_choice',good[0]
    for move in good:
        board.push(move);recapture=any(r.to_square==move.to_square for r in board.generate_legal_captures());board.pop()
        if recapture:return 'favorable_exchange',move
    return 'safe_gain',good[0]

def opening_ok(board,ply):
    return ply in (6,8) and len(board.piece_map())>=30 and not board.is_check() and abs(material(board,chess.WHITE))<=1 and not board.is_game_over(claim_draw=True) and mate_one(board) is None

def endgame_category(board):
    if len(board.piece_map())!=3 or board.is_check():return None
    pieces=[(sq,p) for sq,p in board.piece_map().items() if p.piece_type!=chess.KING]
    if len(pieces)!=1:return None
    sq,piece=pieces[0]
    if piece.color!=board.turn or piece.piece_type not in (chess.QUEEN,chess.ROOK) or board.is_attacked_by(not board.turn,sq):return None
    return 'KQK' if piece.piece_type==chess.QUEEN else 'KRK'

def analyze(job):
    text,needed=job;game=chess.pgn.read_game(io.StringIO(text))
    if game is None or game.errors or game.headers.get('Variant','Standard')!='Standard' or game.headers.get('SetUp')=='1' or game.headers.get('Termination')!='Normal':return None
    site=game.headers.get('Site','')
    if not site.startswith('https://lichess.org/'):return None
    digest=int(hashlib.sha256(site.encode()).hexdigest(),16);split='eval' if digest%5==0 else 'train'
    wanted={tuple(k) for k in needed[split]}
    if not wanted:return None
    moves=list(game.mainline_moves())
    if len(moves)<20:return None
    board=game.board();candidates=[];used=set();end_counts=Counter();last_end=-100;middle=[];mate_positions=[]
    eco=game.headers.get('ECO','Unknown')
    def add(stage,category,ply,solution=None):
        candidates.append(dict(stage=stage,category=category,startPly=ply,fen=board.fen(),solutionFirstMove=solution.uci() if solution else None,eco=eco))
    for ply,move in enumerate(moves):
        # Claimable threefold positions can legally be left; automatic draws cannot.
        if board.halfmove_clock>=150 or board.is_repetition(5):break
        count=len(board.piece_map())
        if (5,'opening3' if ply==6 else 'opening4') in wanted and ply in (6,8) and opening_ok(board,ply):add(5,'opening3' if ply==6 else 'opening4',ply)
        if 20<=ply<=50 and 16<=count<=28 and (4,'middlegame') in wanted and not board.is_check() and abs(material(board,chess.WHITE))<=2 and not board.is_game_over(claim_draw=True):
            middle.append(dict(stage=4,category='middlegame',startPly=ply,fen=board.fen(),solutionFirstMove=None,eco=eco))
        if count==3 and ply-last_end>=2 and any((3,c) in wanted and end_counts[c]<16 for c in CATEGORIES[3]):
            category=endgame_category(board)
            if category and (3,category) in wanted and end_counts[category]<16 and not board.is_game_over(claim_draw=True):
                add(3,category,ply);end_counts[category]+=1;last_end=ply
        if 8<=count<=20 and any((1,c) in wanted and c not in used for c in CATEGORIES[1]) and not board.is_check():
            case=exchange_case(board)
            if case and case[0] not in used and (1,case[0]) in wanted and not board.is_game_over(claim_draw=True):
                add(1,case[0],ply,case[1]);used.add(case[0])
        if ply>=len(moves)-3 and any((2,c) in wanted for c in CATEGORIES[2]):mate_positions.append((ply,board.copy()))
        board.push(move)
    # Only search actual checkmate endings, but verify every defender reply for mate in two.
    if board.is_checkmate():
        for ply,position in reversed(mate_positions):
            if position.is_game_over(claim_draw=True):continue
            m1=mate_one(position);category='mateIn1' if m1 else 'mateIn2'
            if (2,category) not in wanted or category in used:continue
            solution=m1 or mate_two(position)
            if solution:
                candidates.append(dict(stage=2,category=category,startPly=ply,fen=position.fen(),solutionFirstMove=solution.uci(),eco=eco));used.add(category)
    if middle:
        offset=digest%len(middle);candidates.extend((middle[offset:]+middle[:offset])[:8])
    return dict(site=site,split=split,moves=[m.uci() for m in moves],text=text,candidates=candidates)

def games(source):
    with source.open('rb') as raw,zstandard.ZstdDecompressor().stream_reader(raw) as stream:
        text=io.TextIOWrapper(stream,encoding='utf-8');lines=[]
        for line in text:
            if line.startswith('[Event ') and lines:
                yield ''.join(lines);lines=[]
            lines.append(line)
        if lines:yield ''.join(lines)

def main():
    ap=argparse.ArgumentParser();ap.add_argument('source',type=Path);ap.add_argument('--out',type=Path,default=Path('Data/Curriculum'));ap.add_argument('--workers',type=int,default=4);args=ap.parse_args()
    if not 1<=args.workers<=8:ap.error('workers must be 1..8')
    root=args.out;old=[json.loads(x) for x in (root/'positions.jsonl').read_text().splitlines()]
    legacy=root/'Legacy';legacy.mkdir(exist_ok=True)
    if not (legacy/'stages_1_5_eval.jsonl').exists():
        (legacy/'stages_1_5_eval.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in old if r['stage']>0 and r['split']=='eval'))
        for p in root.glob('*.eval.pgn'):
            if not p.name.startswith('00_'):(legacy/p.name).write_bytes(p.read_bytes())
    global_keys={'train':set(),'eval':set()}
    for r in old:
        if r['stage']==0 or r['split']=='eval':global_keys[r['split']].add(canonical(chess.Board(r['fen'])))
    for path in legacy.glob('*.jsonl'):
        for line in path.read_text().splitlines():global_keys['eval'].add(canonical(chess.Board(json.loads(line)['fen'])))
    counts=Counter();seen=defaultdict(set);pergame=Counter();families=Counter();records=[];exports=defaultdict(list)
    def target(split,stage):return TRAIN[stage] if split=='train' else EVAL[stage]
    def need(split,stage,category):return counts[split,stage,category]<target(split,stage)
    def jobs():
        for text in games(args.source):
            yield text,{s:[(stage,c) for stage,cats in CATEGORIES.items() for c in cats if need(s,stage,c)] for s in ('train','eval')}
    scanned=0
    with multiprocessing.Pool(args.workers) as pool:
        for item in pool.imap(analyze,jobs(),chunksize=4):
            scanned+=1
            if scanned%1000==0:print(scanned,dict(counts),flush=True)
            if item:
                split=item['split'];opposite='eval' if split=='train' else 'train';site=item['site'];game=None
                for candidate in item['candidates']:
                    stage=candidate['stage'];category=candidate['category']
                    if not need(split,stage,category):continue
                    key=canonical(chess.Board(candidate['fen']))
                    if key in seen[stage] or key in global_keys[opposite]:continue
                    cap=16 if stage==3 else 1
                    if pergame[split,stage,category,site]>=cap:continue
                    # Limit concentration by source ECO and early move prefix, not just PGN count.
                    if stage in (4,5):
                        family=candidate['eco'];limit=math.ceil(target(split,stage)*0.05)
                        if families[split,stage,category,'eco',family]>=limit:continue
                        if stage==5:
                            prefix=' '.join(item['moves'][:4]);prefix_limit=math.ceil(target(split,stage)*0.0625)
                            if families[split,stage,category,'prefix',prefix]>=prefix_limit:continue
                    if game is None:game=chess.pgn.read_game(io.StringIO(item['text']))
                    criterion=f'{category}; expanded real-game curriculum; see ExpandRemainingCurriculum.py'
                    record=dict(candidate,split=split,gameUrl=site,criterion=criterion,movesToStart=item['moves'][:candidate['startPly']])
                    records.append(record);counts[split,stage,category]+=1;seen[stage].add(key);global_keys[split].add(key);pergame[split,stage,category,site]+=1
                    if stage in (4,5):
                        families[split,stage,category,'eco',candidate['eco']]+=1
                        if stage==5:families[split,stage,category,'prefix',' '.join(item['moves'][:4])]+=1
                    game.headers.update(CurriculumStage=str(stage),CurriculumCategory=category,CurriculumStartPly=str(candidate['startPly']),CurriculumStartFEN=candidate['fen'],CurriculumCriterion=criterion,DatasetSplit=split)
                    exports[stage,split].append(game.accept(chess.pgn.StringExporter(headers=True,variations=False,comments=False)))
            if all(not need(s,stage,c) for s in ('train','eval') for stage,cats in CATEGORIES.items() for c in cats):break
    missing={(s,stage,c):counts[s,stage,c] for s in ('train','eval') for stage,cats in CATEGORIES.items() for c in cats if need(s,stage,c)}
    if missing:raise RuntimeError(f'Insufficient distinct samples; active dataset not replaced: {missing}')
    # One baseline initial-position record per split, sampled explicitly rather than by its list frequency.
    for split in ('train','eval'):
        record=next(r for r in old if r['stage']==5 and r['split']==split and r['startPly']==0)
        with (root/f'05_fullgame.{split}.pgn').open(encoding='utf-8') as stream:
            while (game:=chess.pgn.read_game(stream)) is not None:
                if game.headers['Site']==record['gameUrl']:break
        record=dict(record,category='standard');records.append(record)
        game.headers['CurriculumCategory']='standard';exports[5,split].append(game.accept(chess.pgn.StringExporter(headers=True,variations=False,comments=False)))
        counts[split,5,'standard']=1
    for (stage,split),texts in exports.items():(root/f'{stage:02d}_{NAMES[stage]}.{split}.pgn').write_text('\n\n'.join(texts)+'\n',encoding='utf-8')
    records=[r for r in old if r['stage']==0]+records
    (root/'positions.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in records),encoding='utf-8')
    manifest=json.loads((root/'manifest.json').read_text());manifest['remainingExpansion']=dict(gamesScanned=scanned,sourceSha256=hashlib.sha256(args.source.read_bytes()).hexdigest(),categories={s:{str(stage):{c:counts[s,stage,c] for c in cats+(['standard'] if stage==5 else [])} for stage,cats in CATEGORIES.items()} for s in ('train','eval')},openingPlies=[6,8],standardStartProbability=0.2,openingEcoCapFraction=0.05,openingPrefixCapFraction=0.0625)
    for stage in CATEGORIES:manifest['counts'][NAMES[stage]]={s:sum(1 for r in records if r['stage']==stage and r['split']==s) for s in ('train','eval')}
    (root/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n');print('Completed',manifest['remainingExpansion'],flush=True)
if __name__=='__main__':multiprocessing.freeze_support();main()

