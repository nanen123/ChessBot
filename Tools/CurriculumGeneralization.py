"""Shared deterministic strata and legal symmetry for curriculum preparation."""
import sys,hashlib
from pathlib import Path
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'output/training-validation/pgn-deps'))
import chess
from ChessTactics import favorable_captures,position_key

def orbit(board):
    keys=[position_key(board),position_key(board.mirror())]
    if not board.castling_rights:
        horizontal=board.transform(chess.flip_horizontal)
        keys += [position_key(horizontal),position_key(horizontal.mirror())]
    return min(keys)

def replay(record):
    board=chess.Board(record.get('initialFen',chess.STARTING_FEN))
    for uci in record['movesToStart']:
        move=chess.Move.from_uci(uci)
        if move not in board.legal_moves:raise ValueError('Illegal curriculum history')
        board.push(move)
    assert board.fen()==record['fen']
    return board

def bucket(record,board):
    stage=record['stage']
    if stage==0:
        choices=favorable_captures(board);assert choices
        primary=chess.Move.from_uci(record['solutionFirstMove'])
        assert primary in choices
        return chess.piece_name(board.piece_type_at(primary.from_square))
    if stage in (1,2,3):return record['category']
    family=record.get('eco','Unknown')[:1]
    if stage==4:return ('16-20' if len(board.piece_map())<=20 else '21-24' if len(board.piece_map())<=24 else '25-28')+':'+family
    return record['category']+(':'+family if record['category']!='standard' else '')

def horizontal_record(record):
    """Replay only since the last irreversible move; earlier repetition cannot recur."""
    if record['stage']==5:return None
    board=chess.Board(record.get('initialFen',chess.STARTING_FEN));moves=record['movesToStart'];offset=0;initial=board.copy(stack=False)
    for i,uci in enumerate(moves):
        move=chess.Move.from_uci(uci);irreversible=board.is_irreversible(move);board.push(move)
        if irreversible:offset=i+1;initial=board.copy(stack=False)
    if board.castling_rights or initial.castling_rights:return None
    transformed=initial.transform(chess.flip_horizontal);history=[]
    for uci in moves[offset:]:
        move=chess.Move.from_uci(uci);flipped=chess.Move(move.from_square^7,move.to_square^7,promotion=move.promotion)
        assert flipped in transformed.legal_moves
        transformed.push(flipped);history.append(flipped.uci())
    assert transformed.is_valid() and not transformed.is_game_over(claim_draw=True)
    assert transformed.fen()==board.transform(chess.flip_horizontal).fen()
    assert board.is_repetition(2)==transformed.is_repetition(2) and board.is_repetition(3)==transformed.is_repetition(3)
    result=dict(record,initialFen=initial.transform(chess.flip_horizontal).fen(en_passant='fen'),movesToStart=history,fen=transformed.fen(),horizontal=True,historyOffset=record.get('historyOffset',0)+offset)
    if record.get('solutionFirstMove'):
        m=chess.Move.from_uci(record['solutionFirstMove']);result['solutionFirstMove']=chess.Move(m.from_square^7,m.to_square^7,promotion=m.promotion).uci()
    if record['stage']==0:
        expected={chess.Move(m.from_square^7,m.to_square^7,promotion=m.promotion) for m in favorable_captures(board)}
        assert expected==set(favorable_captures(transformed))
    return result
