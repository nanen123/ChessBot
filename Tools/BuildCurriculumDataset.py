"""Compile train PGNs into the Unity resource. Run from the project root."""
import hashlib, io, json, sys
from pathlib import Path
sys.path.insert(0, 'output/training-validation/pgn-deps')
import chess.pgn

source = Path('Data/Curriculum')
samples = []
train_sites, eval_sites = set(), set()
for path in sorted(source.glob('*.eval.pgn')):
    with path.open(encoding='utf-8') as stream:
        while (game := chess.pgn.read_game(stream)) is not None:
            assert not game.errors
            eval_sites.add(game.headers['Site'])
for stage in range(6):
    files = list(source.glob(f'{stage:02d}_*.train.pgn'))
    assert len(files) == 1, f'Expected one training file for stage {stage}'
    with files[0].open(encoding='utf-8') as stream:
        while (game := chess.pgn.read_game(stream)) is not None:
            assert not game.errors and game.headers['DatasetSplit'] == 'train'
            assert int(game.headers['CurriculumStage']) == stage
            board = game.board()
            initial = board.fen(en_passant='fen')
            moves = list(game.mainline_moves())
            ply = int(game.headers['CurriculumStartPly'])
            assert 0 <= ply <= len(moves)
            for move in moves[:ply]:
                assert move in board.legal_moves
                board.push(move)
            assert board.fen() == game.headers['CurriculumStartFEN']
            assert board.is_valid() and not board.is_game_over(claim_draw=True)
            samples.append(dict(stage=stage, split='train', sourceGame=game.headers['Site'],
                                startPly=ply, initialFen=initial, fen=board.fen(en_passant='fen'),
                                moves=[m.uci() for m in moves[:ply]]))
            train_sites.add(game.headers['Site'])
assert not train_sites & eval_sites, 'Source game leakage between train and eval'
assert all(any(s['stage'] == n for s in samples) for n in range(6))
payload = json.dumps(samples, sort_keys=True, separators=(',', ':'))
dataset = dict(version=1, split='train', datasetId=hashlib.sha256(payload.encode()).hexdigest(), samples=samples)
target = Path('Assets/Resources/ChessCurriculumTraining.json')
target.write_text(json.dumps(dataset, indent=2)+'\n', encoding='utf-8')
print(f'Wrote {len(samples)} training samples; evaluation PGNs excluded; dataset {dataset["datasetId"]}')
