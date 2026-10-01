"""Conservative two-ply material diagnostic, not an engine best-move oracle."""
import chess
VALUES = {1: 1, 2: 3, 3: 3, 4: 5, 5: 9, 6: 0}
def gain(board, move):
    captured = board.piece_type_at(move.to_square) or (1 if board.is_en_passant(move) else 0)
    return VALUES.get(captured, 0) + (VALUES[move.promotion]-1 if move.promotion else 0)
def capture_margin(board, move):
    if not board.is_capture(move): return 0
    value=gain(board,move); child=board.copy(stack=False); child.push(move)
    worst=0
    for reply in list(child.legal_moves):
        reply_gain=gain(child,reply)
        child.push(reply); mate=child.is_checkmate(); child.pop()
        if mate: return -10000
        worst=max(worst,reply_gain)
    return value-worst
def favorable_captures(board):
    return [m for m in board.generate_legal_captures() if capture_margin(board,m)>0]
def position_key(board):
    return ' '.join(board.fen(en_passant='legal').split()[:4])
def material(board, color):
    return sum(VALUES[p.piece_type]*(1 if p.color==color else -1) for p in board.piece_map().values())
