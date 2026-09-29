"""Extract small, traceable curriculum PGN samples; does not modify Unity lessons.
Requires chess==1.11.2 and zstandard==0.23.0. See Data/Curriculum/README.md.
"""
import argparse, hashlib, io, json, sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'output/training-validation/pgn-deps'))
import chess
import chess.pgn
import zstandard

SOURCE = 'https://database.lichess.org/standard/lichess_db_standard_rated_2013-01.pgn.zst'
NAMES = ['capture', 'exchange', 'mate', 'endgame', 'middlegame', 'fullgame']
VALUES = {chess.PAWN: 1, chess.KNIGHT: 3, chess.BISHOP: 3, chess.ROOK: 5, chess.QUEEN: 9, chess.KING: 0}

def mate_one(board):
    for move in list(board.legal_moves):
        board.push(move)
        mate = board.is_checkmate()
        board.pop()
        if mate:
            return move
    return None

def mate_two(board):
    # Existential attacker move; universal defender replies; mate on the next move.
    for move in list(board.legal_moves):
        board.push(move)
        replies = list(board.legal_moves)
        works = bool(replies) and not board.is_game_over(claim_draw=True)
        if works:
            for reply in replies:
                board.push(reply)
                works = not board.is_game_over(claim_draw=True) and mate_one(board) is not None
                board.pop()
                if not works:
                    break
        board.pop()
        if works:
            return move
    return None

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('source', type=Path)
    ap.add_argument('--out', type=Path, default=Path('Data/Curriculum'))
    args = ap.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    counts = {n: {'train': 0, 'eval': 0} for n in NAMES}
    seen = set()
    records = []
    exports = {n: {'train': [], 'eval': []} for n in NAMES}
    subcounts = {}
    scanned = 0

    def want(name, split):
        return counts[name][split] < (16 if split == 'train' else 4)

    def add(name, split, game, board, ply, criterion, solution=None, category=None):
        key = (name, ' '.join(board.fen().split()[:4]))
        if key in seen or not want(name, split):
            return
        if category:
            ck = (name, split, category)
            if subcounts.get(ck, 0) >= (8 if split == 'train' else 2):
                return
            subcounts[ck] = subcounts.get(ck, 0) + 1
        seen.add(key)
        counts[name][split] += 1
        record = dict(stage=NAMES.index(name), split=split, gameUrl=game.headers['Site'],
                      startPly=ply, fen=board.fen(), criterion=criterion,
                      solutionFirstMove=solution, category=category,
                      movesToStart=[m.uci() for m in list(game.mainline_moves())[:ply]])
        records.append(record)
        # Full original game retained: this FEN is an annotation, not a SetUp header.
        game.headers['CurriculumStage'] = str(NAMES.index(name))
        game.headers['CurriculumStartPly'] = str(ply)
        game.headers['CurriculumStartFEN'] = board.fen()
        game.headers['CurriculumCriterion'] = criterion
        game.headers['DatasetSplit'] = split
        exports[name][split].append(game.accept(chess.pgn.StringExporter(headers=True, variations=False, comments=False)))

    with args.source.open('rb') as raw, zstandard.ZstdDecompressor().stream_reader(raw) as stream:
        text = io.TextIOWrapper(stream, encoding='utf-8')
        while True:
            game = chess.pgn.read_game(text)
            if game is None:
                break
            scanned += 1
            if scanned % 1000 == 0:
                print(scanned, counts, flush=True)
            if game.errors or game.headers.get('Variant', 'Standard') != 'Standard' or game.headers.get('SetUp') == '1':
                continue
            site = game.headers.get('Site', '')
            if not site.startswith('https://lichess.org/'):
                continue
            split = 'eval' if int(hashlib.sha256(site.encode()).hexdigest(), 16) % 5 == 0 else 'train'
            moves = list(game.mainline_moves())
            if len(moves) < 20 or game.headers.get('Termination') != 'Normal':
                continue
            board = game.board()
            # Full games share one initial FEN, so deduplicate by game rather than FEN.
            if want('fullgame', split):
                seen.discard(('fullgame', ' '.join(board.fen().split()[:4])))
                add('fullgame', split, game, board, 0, 'normal completed standard game; initial position')
            used = set()
            for ply, move in enumerate(moves):
                if not board.is_valid() or board.is_game_over(claim_draw=True):
                    break
                pieces = len(board.piece_map())
                captures = None
                if 'capture' not in used and want('capture', split) and pieces <= 12:
                    captures = list(board.generate_legal_captures())
                    for cap in captures:
                        board.push(cap)
                        recaptured = any(r.to_square == cap.to_square for r in board.generate_legal_captures())
                        terminal = board.is_game_over(claim_draw=True)
                        board.pop()
                        if not recaptured and not terminal:
                            add('capture', split, game, board, ply, 'at most 12 pieces; legal capture not immediately legally recapturable; no terminal result', cap.uci())
                            used.add('capture')
                            break
                if 'exchange' not in used and want('exchange', split) and 8 <= pieces <= 20:
                    for cap in list(board.generate_legal_captures()):
                        board.push(cap)
                        recaptured = any(r.to_square == cap.to_square for r in board.generate_legal_captures())
                        terminal = board.is_game_over(claim_draw=True)
                        board.pop()
                        if recaptured and not terminal:
                            add('exchange', split, game, board, ply, '8 to 20 pieces; capture has a legal immediate recapture; not an optimal-move label', cap.uci())
                            used.add('exchange')
                            break
                # Search actual late-game positions; never infer forced mate from the played line.
                if want('mate', split) and ply >= len(moves) - 3:
                    m1 = mate_one(board)
                    category = 'mateIn1' if m1 else 'mateIn2'
                    quota = 8 if split == 'train' else 2
                    if subcounts.get(('mate', split, category), 0) < quota:
                        winning = m1 or mate_two(board)
                        if winning:
                            add('mate', split, game, board, ply, 'forced mate verified over all legal defender replies within 1 or 3 plies', winning.uci(), category)
                if want('endgame', split) and pieces == 3:
                    nonkings = [(sq,p) for sq,p in board.piece_map().items() if p.piece_type != chess.KING]
                    if len(nonkings) == 1 and nonkings[0][1].piece_type in (chess.QUEEN, chess.ROOK) and nonkings[0][1].color == board.turn and not board.is_check():
                        sq, piece = nonkings[0]
                        # Reject immediately hanging major pieces. This is a material filter, not tablebase proof.
                        if not board.is_attacked_by(not board.turn, sq):
                            add('endgame', split, game, board, ply, 'KQK or KRK; stronger side to move; major piece not attacked; no tablebase verification', category='KQK' if piece.piece_type == chess.QUEEN else 'KRK')
                if 'middlegame' not in used and want('middlegame', split) and 20 <= ply <= 50 and 16 <= pieces <= 28 and not board.is_check():
                    material = sum(VALUES[p.piece_type] * (1 if p.color else -1) for p in board.piece_map().values())
                    if abs(material) <= 2:
                        add('middlegame', split, game, board, ply, '20 to 50 plies; 16 to 28 pieces; material difference at most 2; no engine balance claim')
                        used.add('middlegame')
                board.push(move)
            if all(not want(n,s) for n in NAMES for s in ('train','eval')):
                break
    for name in NAMES:
        for split in ('train', 'eval'):
            (args.out / f'{NAMES.index(name):02d}_{name}.{split}.pgn').write_text('\n\n'.join(exports[name][split]) + '\n', encoding='utf-8')
    (args.out/'positions.jsonl').write_text(''.join(json.dumps(r, ensure_ascii=False)+'\n' for r in records), encoding='utf-8')
    manifest = dict(source=SOURCE, sourceLicense='CC0-1.0', sourceSha256=hashlib.sha256(args.source.read_bytes()).hexdigest(),
                    gamesScanned=scanned, counts=counts, note='Small deterministic starter selection; not random, engine-quality, or Unity-integrated. Full games retained; start at annotated ply.')
    (args.out/'manifest.json').write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(manifest, indent=2), flush=True)

if __name__ == '__main__':
    main()
