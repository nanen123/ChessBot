"""Verify prepared curriculum provenance, symmetry, replay and bundled training-only data."""
import sys,json,hashlib
from pathlib import Path
from collections import Counter
sys.dont_write_bytecode=True
from CurriculumGeneralization import replay,orbit,horizontal_record
from PrepareCurriculumPgn import mate_one,mate_two
from ChessTactics import favorable_captures
import chess
root=Path('Data/Curriculum/Generalization');rows=[json.loads(l) for l in (root/'positions.jsonl').read_text().splitlines()]
resource=json.loads(Path('Assets/Resources/ChessCurriculumTraining.json').read_text());assert resource['version']==4
assert resource['datasetId']==hashlib.sha256(json.dumps(resource['samples'],sort_keys=True,separators=(',',':')).encode()).hexdigest()
keys={s:set() for s in ['train','eval','validation']};sites={s:set() for s in keys};by_id={}
for i,r in enumerate(rows):
    board=replay(r);assert board.is_valid() and not board.is_game_over(claim_draw=True)
    assert r['startPly']==len(r['movesToStart'])+r['historyOffset']
    identity=(r['stage'],r['gameUrl'],r['startPly'],r['horizontal']);assert identity not in by_id;by_id[identity]=r
    if r['stage']==0:assert chess.Move.from_uci(r['solutionFirstMove']) in favorable_captures(board)
    if r['split']=='validation' and r['stage']==2:assert (mate_one(board) if r['category']=='mateIn1' else mate_two(board)) is not None
    if not(r['stage']==5 and r['startPly']==0):keys[r['split']].add(orbit(board))
    sites[r['split']].add(r['gameUrl'])
    if r['horizontal']:
        assert r['split']=='train' and r['stage']<5 and not board.castling_rights
        expected=horizontal_record(by_id[(r['stage'],r['gameUrl'],r['startPly'],False)])
        assert r['fen']==expected['fen'] and r['movesToStart']==expected['movesToStart'] and r['initialFen']==expected['initialFen']
    if (i+1)%5000==0:print('verified',i+1,flush=True)
for a,b in [('train','eval'),('train','validation'),('eval','validation')]:
    assert not keys[a]&keys[b] and not sites[a]&sites[b]
for p in Path('Data/Curriculum/Legacy').glob('*.jsonl'):
    for line in p.read_text().splitlines():
        r=json.loads(line)
        if not(r['stage']==5 and r['startPly']==0):assert orbit(chess.Board(r['fen'])) not in keys['train']
for sample in resource['samples']:
    r=by_id[(sample['stage'],sample['sourceGame'],sample['startPly'],sample['horizontal'])]
    assert r['split']=='train' and sample['moves']==r['movesToStart'] and sample['initialFen']==r['initialFen'] and sample['stratum']==r['stratum']
    assert sample['fen']==replay(r).fen(en_passant='fen')
assert len(resource['samples'])==sum(r['split']=='train' for r in rows)
print('PASS',len(rows),'positions; replay, symmetry, source-game/orbit separation, train-only bundle',flush=True)
