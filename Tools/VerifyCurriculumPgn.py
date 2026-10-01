import sys, json
sys.dont_write_bytecode = True
from pathlib import Path
sys.path.insert(0, 'output/training-validation/pgn-deps')
sys.path.insert(0, 'Tools')
import chess, chess.pgn
from PrepareCurriculumPgn import mate_one, mate_two
from ChessTactics import capture_margin
root = Path('Data/Curriculum')
expected = sum(sum(v.values()) for v in json.loads((root/'manifest.json').read_text())['counts'].values())
records = [json.loads(x) for x in (root/'positions.jsonl').read_text(encoding='utf-8').splitlines()]
by_key = {(r['stage'], r['split'], r['gameUrl'], r['startPly']): r for r in records}
assert len(by_key) == len(records) == expected
sites = {'train': set(), 'eval': set()}
count = 0
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
            if stage == 0:
                assert capture_margin(board, chess.Move.from_uci(record['solutionFirstMove'])) > 0
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
assert count == expected
assert not sites['train'] & sites['eval']
print(f'PASS: {expected} PGN records, complete legal replay, exact start FEN, forced mates, disjoint source-game splits')
print('Unique source games:', {k: len(v) for k,v in sites.items()})
