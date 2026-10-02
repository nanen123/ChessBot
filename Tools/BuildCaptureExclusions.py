"""Reserve every fixed curriculum board (including old validation) from online generation.
Only SHA-256 board identities are bundled; no validation moves or solutions are exported.
"""
import hashlib,json,sys
from pathlib import Path
sys.dont_write_bytecode=True
from CurriculumGeneralization import chess
ROOT=Path(__file__).resolve().parents[1]

def identity(board):
    # Ignore rights/counters conservatively, including all color/file symmetries.
    variants=[board,board.mirror(),board.transform(chess.flip_horizontal),board.mirror().transform(chess.flip_horizontal)]
    key=min(b.board_fen()+(' w' if b.turn else ' b') for b in variants)
    return hashlib.sha256(key.encode('ascii')).hexdigest()

def build():
    positions=ROOT/'Data/Curriculum/Generalization/positions.jsonl'
    paths=[positions,*sorted((ROOT/'Data/Curriculum/Legacy').glob('*.jsonl'))]
    blocked=set()
    for path in paths:
        for line in path.read_text(encoding='utf-8').splitlines():
            r=json.loads(line)
            if 'fen' in r:blocked.add(identity(chess.Board(r['fen'])))
    bundle=json.loads((ROOT/'Assets/Resources/ChessCurriculumTraining.json').read_text())
    doc=dict(version=1,datasetId=bundle['datasetId'],positionsSha256=hashlib.sha256(positions.read_bytes()).hexdigest(),hashes=sorted(blocked))
    (ROOT/'Assets/Resources/ChessCaptureExclusions.json').write_text(json.dumps(doc,separators=(',',':'))+'\n',encoding='utf-8')
    print('Reserved identities:',len(blocked))
if __name__=='__main__':build()
