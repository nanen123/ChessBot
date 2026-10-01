"""Compare Python evaluator observations/masks with canonical Unity-exported fixtures."""
import json,sys
from pathlib import Path
sys.dont_write_bytecode=True
from EvaluateChess import legal_actions,observations
import chess,numpy as np
path=Path(sys.argv[1])
rows=json.loads(path.read_text())['cases']
for row in rows:
    board=chess.Board(row['initial'])
    for move in row['history']+row['moves']: board.push_uci(move)
    actions=legal_actions(board)
    assert sorted(actions)==row['actions'],board.fen()
    np.testing.assert_allclose(observations(board,len(row['moves']),512,actions).reshape(-1),row['observations'],atol=1e-6)
print(f'PASS: {len(rows)} Unity/Python observation and legal-action parity fixtures')
