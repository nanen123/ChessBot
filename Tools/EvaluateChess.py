"""Deterministic held-out ChessV1 evaluation. No training or Unity process required."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import random
import sys
sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT/'output/training-validation/pgn-deps'))
import chess
import numpy as np
import onnxruntime as ort
import yaml
from ChessTactics import capture_margin, favorable_captures, position_key, material

VALUES = {1: 1, 2: 3, 3: 3, 4: 5, 5: 9, 6: 0}

def encode_move(move):
    if not move.promotion:
        return move.from_square * 64 + move.to_square
    side = 0 if chess.square_rank(move.from_square) == 6 else 1
    return 4096 + ((side*8 + chess.square_file(move.from_square))*3 + chess.square_file(move.to_square)-chess.square_file(move.from_square)+1)*4 + [5,4,3,2].index(move.promotion)

def claim(board):
    return board.halfmove_clock >= 100 or board.is_repetition(3)

def legal_actions(board):
    actions = {}
    for move in list(board.legal_moves):
        action = encode_move(move)
        actions[action] = move
        board.push(move)
        if claim(board): actions[action + 4288] = move
        board.pop()
    if claim(board): actions[8576] = None
    return actions

def observations(board, plies, limit, actions):
    obs = np.zeros((1,844), dtype=np.float32)
    for square,piece in board.piece_map().items():
        obs[0,square*12 + (0 if piece.color == board.turn else 6) + piece.piece_type-1] = 1
    obs[0,768:774] = [float(board.turn),1,board.has_kingside_castling_rights(chess.WHITE),board.has_queenside_castling_rights(chess.WHITE),board.has_kingside_castling_rights(chess.BLACK),board.has_queenside_castling_rights(chess.BLACK)]
    obs[0,774+(64 if board.ep_square is None else board.ep_square)] = 1
    count = next(n for n in range(5,0,-1) if board.is_repetition(n))
    obs[0,839:] = [min(board.halfmove_clock,150)/150, count/5, float(claim(board)), float(any(4288 <= a < 8576 for a in actions)), min(plies/max(1,limit),1)]
    return obs

class Model:
    def __init__(self, path):
        opts=ort.SessionOptions(); opts.intra_op_num_threads=1; opts.inter_op_num_threads=1
        self.session=ort.InferenceSession(str(path), sess_options=opts, providers=['CPUExecutionProvider'])
        assert {i.name for i in self.session.get_inputs()} == {'obs_0','action_masks'}, 'Unsupported observation schema'
        assert 'deterministic_discrete_actions' in {o.name for o in self.session.get_outputs()}, 'Model needs deterministic output'
    def choose(self, board, plies, limit):
        actions=legal_actions(board)
        mask=np.zeros((1,8577), dtype=np.float32); mask[0,list(actions)]=1
        result=self.session.run(['deterministic_discrete_actions'], {'obs_0':observations(board,plies,limit,actions),'action_masks':mask})
        action=int(result[0].reshape(-1)[0])
        if action not in actions: raise ValueError(f'Model chose illegal action {action}')
        return action, actions[action]

def opponent(board, rng):
    # Fixed one-ply material heuristic; not Stockfish or an Elo-calibrated opponent.
    if claim(board): return 8576, None
    side=board.turn; candidates=[]
    for move in list(board.legal_moves):
        captured=board.piece_type_at(move.to_square) or (1 if board.is_en_passant(move) else 0)
        board.push(move)
        score=VALUES.get(captured,0)
        if move.promotion: score += VALUES[move.promotion]-1
        if board.is_checkmate(): score=10000
        elif board.is_stalemate(): score=0
        elif any(r.to_square == move.to_square for r in board.generate_legal_captures()):
            score -= VALUES[board.piece_type_at(move.to_square)]
        board.pop(); candidates.append((score,move))
    best=max(s for s,m in candidates)
    move=rng.choice(sorted((m for s,m in candidates if s == best), key=lambda m:m.uci()))
    return encode_move(move),move

def start_board(record, mirrored):
    board=chess.Board()
    if mirrored:
        board.turn=chess.BLACK
    for uci in record['movesToStart']:
        if mirrored: uci=uci[0]+str(9-int(uci[1]))+uci[2]+str(9-int(uci[3]))+uci[4:]
        move=chess.Move.from_uci(uci)
        if move not in board.legal_moves: raise ValueError('Invalid evaluation history')
        board.push(move)
    return board

def terminal(board, focus):
    if board.is_checkmate(): return (board.turn != focus, 'win' if board.turn != focus else 'loss')
    if board.is_stalemate() or board.is_insufficient_material() or board.is_fivefold_repetition() or board.is_seventyfive_moves(): return False,'draw'
    return None

def forced_mate(model, board, limit):
    focus=board.turn
    action,move=model.choose(board,0,limit)
    if action >= 4288: return False
    board=board.copy(); board.push(move)
    result=terminal(board,focus)
    if result is not None: return result[0]
    if limit < 3 or claim(board): return False
    for reply in list(board.legal_moves):
        child=board.copy(); child.push(reply)
        if terminal(child,focus) is not None or claim(child): return False
        action,move=model.choose(child,2,limit)
        if action >= 4288: return False
        child.push(move)
        if not child.is_checkmate(): return False
    return True

class Diagnostics:
    def __init__(self, model):
        self.model=model
        self.counts=Counter(model_decisions=0, favorable_capture_opportunities=0, missed_favorable_captures=0,
                            free_piece_losses=0, free_material_lost=0, repeated_positions=0,
                            model_repetitions=0, advantageous_repetition_draws=0)
    def choose(self, board, plies, limit):
        action,move=self.model.choose(board,plies,limit)
        self.counts['model_decisions']+=1
        favorable=favorable_captures(board)
        if favorable:
            self.counts['favorable_capture_opportunities']+=1
            mate=False
            if action<4288:
                board.push(move); mate=board.is_checkmate(); board.pop()
            if not mate and (action>=4288 or move not in favorable): self.counts['missed_favorable_captures']+=1
        return action,move

def play(model, record, mirrored, limit, seed):
    stage=record['stage']; board=start_board(record, mirrored if stage != 5 else False)
    focus=(chess.BLACK if mirrored else chess.WHITE) if stage == 5 else board.turn
    tracker=Diagnostics(model); seen=Counter({position_key(board):1}); gain=0
    def finish(success,result,plies):
        return dict(success=success,result=result,plies=plies,materialGain=gain,metrics=dict(tracker.counts),
                    metric_scope='searched_model_decisions' if stage==2 else 'played_episode')
    if stage == 2:
        success=forced_mate(tracker,board,limit)
        return finish(success,'mate_solved' if success else 'mate_failed',None)
    rng=random.Random(seed)
    for ply in range(1 if stage==0 else limit):
        own=board.turn==focus
        action,move = tracker.choose(board,ply,limit) if own else opponent(board,rng)
        if action >= 4288:
            claim_board=board.copy()
            if move is not None: claim_board.push(move)
            if claim_board.is_repetition(3) and material(claim_board,focus)>0:
                tracker.counts['advantageous_repetition_draws']+=1
            return finish(False,'draw_claim',ply)
        favorable=stage==0 and capture_margin(board,move)>0
        captured=board.piece_type_at(move.to_square) or (1 if board.is_en_passant(move) else 0)
        # Actual loss with no legal immediate recapture of the capturer; a diagnostic, not a blunder oracle.
        lost_value=VALUES.get(captured,0) if not own else 0
        gain += VALUES.get(captured,0)*(1 if own else -1)
        board.push(move)
        if lost_value and not any(r.to_square==move.to_square for r in board.generate_legal_captures()):
            tracker.counts['free_piece_losses']+=1; tracker.counts['free_material_lost']+=lost_value
        key=position_key(board);seen[key]+=1
        if seen[key]>1:
            tracker.counts['repeated_positions']+=1
            if own: tracker.counts['model_repetitions']+=1
        result=terminal(board,focus)
        if result is not None:
            if result[1]=='draw' and board.is_fivefold_repetition() and material(board,focus)>0:
                tracker.counts['advantageous_repetition_draws']+=1
            return finish(result[0],result[1],ply+1)
        if stage==0: return finish(favorable,'favorable_capture' if favorable else 'first_move_failed',ply+1)
    return finish(stage==1 and gain>0,'horizon',limit)

def evaluate(model_path, config, through_stage=5):
    if not 0 <= through_stage <= 5: raise ValueError('through_stage must be 0..5')
    if config['samples_per_stage'] <= 0 or len(config['maximum_plies']) != 6 or any(n < 1 for n in config['maximum_plies']): raise ValueError('Invalid evaluation sample count or horizons')
    if config['maximum_plies'][0] != 1: raise ValueError('Stage 0 requires a one-ply evaluation horizon')
    data_path=ROOT/config['positions']
    records=[json.loads(line) for line in data_path.read_text(encoding='utf-8').splitlines()]
    train_sites={r['gameUrl'] for r in records if r['split']=='train'}
    eval_sites={r['gameUrl'] for r in records if r['split']=='eval'}
    if train_sites & eval_sites: raise ValueError('Training/evaluation source game leakage')
    model=Model(model_path); stages=[]
    for stage in range(through_stage+1):
        chosen=[r for r in records if r['split']=='eval' and r['stage']==stage][:config['samples_per_stage']]
        if len(chosen) < config['samples_per_stage']: raise ValueError(f'Insufficient evaluation samples for stage {stage}')
        cases=[]
        for index,record in enumerate(chosen):
            for mirrored in (False,True):
                result=play(model,record,mirrored,config['maximum_plies'][stage],config['seed']+stage*1000+index*2+int(mirrored))
                cases.append(dict(game=record['gameUrl'],startPly=record['startPly'],mirrored=mirrored,**result))
        metrics=Counter()
        for case in cases: metrics.update(case['metrics'])
        metrics=dict(metrics)
        opportunities=metrics['favorable_capture_opportunities']
        metrics['missed_favorable_capture_rate']=metrics['missed_favorable_captures']/opportunities if opportunities else None
        metrics['repetitions_per_episode']=metrics['repeated_positions']/len(cases)
        win_plies=[c['plies'] for c in cases if c['result']=='win' and c['plies'] is not None]
        stages.append(dict(stage=stage,metrics=metrics,episodes=len(cases),successes=sum(c['success'] for c in cases),success_rate=sum(c['success'] for c in cases)/len(cases),mean_win_plies=sum(win_plies)/len(win_plies) if win_plies else None,results=dict(Counter(c['result'] for c in cases)),cases=cases))
    return dict(evaluation_version=2, tactical_criterion='two-ply-material-v1', model=str(model_path),model_sha256=hashlib.sha256(Path(model_path).read_bytes()).hexdigest(),dataset_sha256=hashlib.sha256(data_path.read_bytes()).hexdigest(),opponent='material-one-ply-v1',stages=stages)

if __name__=='__main__':
    ap=argparse.ArgumentParser(); ap.add_argument('model'); ap.add_argument('--config',default=str(ROOT/'config/chess_evaluation.yaml')); ap.add_argument('--through-stage',type=int,choices=range(6),default=5); ap.add_argument('--out',default='evaluation.json')
    args=ap.parse_args(); config=yaml.safe_load(Path(args.config).read_text()); report=evaluate(args.model,config,args.through_stage)
    Path(args.out).write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print([(s['stage'],s['success_rate']) for s in report['stages']])
