import sys, json, math
sys.dont_write_bytecode = True
from pathlib import Path
sys.path.insert(0, 'output/training-validation/pgn-deps')
sys.path.insert(0, 'Tools')
import chess, chess.pgn
from PrepareCurriculumPgn import mate_one, mate_two
from ChessTactics import capture_margin, material
from ExpandRemainingCurriculum import exchange_case, endgame_category, opening_ok
from ExpandCaptureCurriculum import classify, canonical
from collections import Counter
root = Path('Data/Curriculum')
expected = sum(sum(v.values()) for v in json.loads((root/'manifest.json').read_text())['counts'].values())
records = [json.loads(x) for x in (root/'positions.jsonl').read_text(encoding='utf-8').splitlines()]
by_key = {(r['stage'], r['split'], r['gameUrl'], r['startPly']): r for r in records}
assert len(by_key) == len(records) == expected
sites = {'train': set(), 'eval': set()}
all_keys={"train":set(),"eval":set()}; category_counts=Counter()
count = 0
capture_counts=Counter(); capture_keys={'train':set(),'eval':set()}
for path in root.glob('*.pgn'):
    with path.open(encoding='utf-8') as f:
        while (game := chess.pgn.read_game(f)) is not None:
            assert not game.errors
            stage, split, ply = int(game.headers['CurriculumStage']), game.headers['DatasetSplit'], int(game.headers['CurriculumStartPly'])
            record = by_key[(stage, split, game.headers['Site'], ply)]
            board = game.board()
            moves = list(game.mainline_moves())
            for move in moves[:ply]:
                assert move in board.legal_moves
                board.push(move)
            assert board.fen() == game.headers['CurriculumStartFEN'] == record['fen']
            assert [m.uci() for m in moves[:ply]] == record['movesToStart']
            assert board.is_valid() and not board.is_game_over(claim_draw=True)
            if stage > 0:
                assert game.headers['CurriculumCategory']==record['category']
                category_counts[split,stage,record['category']]+=1
            if not (stage==5 and ply==0):all_keys[split].add(canonical(board))
            if stage == 1:assert exchange_case(board)[0]==record['category']
            if stage == 3:assert endgame_category(board)==record['category']
            if stage == 4:assert 20<=ply<=50 and 16<=len(board.piece_map())<=28 and abs(material(board,chess.WHITE))<=2 and not board.is_check()
            if stage == 5:
                if record['category']=='standard':assert ply==0 and board.fen()==chess.STARTING_FEN
                else:assert opening_ok(board,ply) and record['category']==('opening3' if ply==6 else 'opening4')
            if stage == 0:
                assert capture_margin(board, chess.Move.from_uci(record['solutionFirstMove'])) > 0
                difficulty=int(game.headers['CaptureDifficulty'])
                assert difficulty==record['difficulty']==classify(board)[0]
                capture_counts[(split,difficulty)]+=1
                key=canonical(board);assert key not in capture_keys[split]
                capture_keys[split].add(key)
            if stage == 2:
                m1 = mate_one(board)
                if record['category'] == 'mateIn1':
                    assert m1 and m1.uci() == record['solutionFirstMove']
                else:
                    assert not m1
                    assert mate_two(board).uci() == record['solutionFirstMove']
            for move in moves[ply:]:
                assert move in board.legal_moves
                board.push(move)
            sites[split].add(game.headers['Site'])
            count += 1
assert not capture_keys['train'] & capture_keys['eval']
manifest=json.loads((root/'manifest.json').read_text())
for split,counts in manifest['captureExpansion']['counts'].items():
    for difficulty,total in enumerate(counts):assert capture_counts[(split,difficulty)]==total
legacy=[json.loads(x) for x in (root/'Legacy/capture_eval.jsonl').read_text().splitlines()]
assert not capture_keys['train'] & {canonical(chess.Board(r['fen'])) for r in legacy}
assert not sites['train'] & {r['gameUrl'] for r in legacy}
assert not all_keys['train'] & all_keys['eval']
for split,stages in manifest['remainingExpansion']['categories'].items():
    for stage,categories in stages.items():
        for category,total in categories.items():assert category_counts[split,int(stage),category]==total
for path in (root/'Legacy').glob('*.jsonl'):
    previous=[json.loads(line) for line in path.read_text().splitlines()]
    assert not sites['train'] & {r['gameUrl'] for r in previous}
    assert not all_keys['train'] & {canonical(chess.Board(r['fen'])) for r in previous}
for split in ('train','eval'):
    for stage in (4,5):
        for category in {r['category'] for r in records if r['stage']==stage and r['split']==split and r['category']!='standard'}:
            pool=[r for r in records if r['stage']==stage and r['split']==split and r['category']==category]
            assert max(Counter(r['eco'] for r in pool).values())<=math.ceil(len(pool)*0.05)
            if stage==5:assert max(Counter(tuple(r['movesToStart'][:4]) for r in pool).values())<=math.ceil(len(pool)*0.0625)
assert count == expected
assert not sites['train'] & sites['eval']
print(f'PASS: {expected} PGN records, complete legal replay, exact start FEN, forced mates, disjoint source-game splits')
print('Unique source games:', {k: len(v) for k,v in sites.items()})
