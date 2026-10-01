import json,sys,unittest,tempfile
from pathlib import Path
sys.dont_write_bytecode=True
sys.path.insert(0,str(Path(__file__).resolve().parent))
from SuccessGate import SuccessGate
from EvaluateChess import ROOT, Model, legal_actions, observations, start_board, forced_mate
import chess,yaml

class GateTests(unittest.TestCase):
    def setUp(self):
        self.config=yaml.safe_load((ROOT/'config/chess_evaluation.yaml').read_text(encoding='utf-8-sig'))
        self.config.update(evaluation_interval=10,minimum_stage_steps=20,consecutive_passes=2)
        self.gate=SuccessGate(self.config,'test')
    def report(self, stage=0, rate=1):
        return {'stages':[dict(stage=s,episodes=32,success_rate=rate) for s in range(stage+1)]}
    def test_time_alone_cannot_promote(self):
        for step in (100,200,300): self.assertFalse(self.gate.accept(self.report(rate=0),step))
        self.assertEqual(self.gate.state['stage'],0)
    def test_minimum_steps_and_consecutive_passes(self):
        self.assertFalse(self.gate.accept(self.report(),10))
        self.assertFalse(self.gate.accept(self.report(),20))
        self.assertTrue(self.gate.accept(self.report(),30))
        self.assertEqual(self.gate.state['stage'],1)
    def test_failed_previous_lesson_blocks_promotion(self):
        self.gate.state.update(stage=1,stage_started_step=0)
        report=self.report(1); report['stages'][0]['success_rate']=0
        self.assertFalse(self.gate.accept(report,100)); self.assertEqual(self.gate.state['consecutive'],0)
    def test_failure_breaks_streak(self):
        self.gate.accept(self.report(),100)
        self.gate.accept(self.report(rate=0),110)
        self.assertFalse(self.gate.accept(self.report(),120))
    def test_resume_and_changed_data_rejection(self):
        self.gate.accept(self.report(),100)
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'state.json';self.gate.save(path)
            gate=SuccessGate(self.config,'test',json.loads(path.read_text()))
            self.assertTrue(gate.accept(self.report(),110))
            with self.assertRaises(ValueError): SuccessGate(self.config,'changed',json.loads(path.read_text()))
    def test_duplicate_and_missing_samples_rejected(self):
        self.gate.accept(self.report(),100)
        with self.assertRaises(ValueError): self.gate.accept(self.report(),100)
        report=self.report();report['stages'][0]['episodes']=1
        with self.assertRaises(ValueError): self.gate.accept(report,110)
    def test_final_stage_never_exceeds_five(self):
        self.gate.state['stage']=5
        self.assertFalse(self.gate.accept(self.report(5),100));self.assertFalse(self.gate.accept(self.report(5),110))
        self.assertEqual(self.gate.state['stage'],5)

class EvaluationTests(unittest.TestCase):
    def test_mate_checks_all_defender_replies(self):
        class Solver:
            def choose(self,b,p,l):
                from PrepareCurriculumPgn import mate_one,mate_two
                from EvaluateChess import encode_move
                m=mate_one(b) or mate_two(b)
                return encode_move(m),m
        self.assertTrue(forced_mate(Solver(),chess.Board('7k/8/5K2/4Q3/8/8/8/8 w - - 0 1'),3))
    def test_draw_claim_and_promotion_masks(self):
        board=chess.Board('7k/P7/8/8/8/8/8/7K w - - 99 1')
        actions=legal_actions(board)
        self.assertEqual(sum(4096<=a<4288 for a in actions),4)
        self.assertTrue(any(4288<=a<8576 for a in actions))
        self.assertNotIn(8576,actions)
    def test_repeated_position_observation(self):
        board=chess.Board()
        for move in ['g1f3','g8f6','f3g1','f6g8']*2: board.push_uci(move)
        obs=observations(board,0,512,legal_actions(board))
        self.assertAlmostEqual(float(obs[0,840]),.6)
        self.assertEqual(float(obs[0,841]),1)
        self.assertEqual(float(obs[0,843]),0)

class DiagnosticTests(unittest.TestCase):
    def test_tactics(self):
        from ChessTactics import capture_margin
        for fen,uci,expected in [
            ('7k/8/8/8/8/8/p7/R6K w - - 0 1','a1a2',True),
            ('7k/8/8/8/8/r7/p7/R6K w - - 0 1','a1a2',False),
            ('7k/8/8/8/8/1p4qb/8/N6K w - - 0 1','a1b3',False),
            ('7k/8/8/8/8/r7/q7/R6K w - - 0 1','a1a2',True),
            ('4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1','e5d6',True),
            ('r6k/1P6/8/8/8/8/8/7K w - - 0 1','b7a8q',True)]:
            b=chess.Board(fen);m=chess.Move.from_uci(uci)
            self.assertEqual(capture_margin(b,m)>0,expected)
            self.assertEqual(b.fen(en_passant='fen'),fen)
    def test_diagnostics_misses_safe_capture_not_poison(self):
        from EvaluateChess import Diagnostics,encode_move
        class Quiet:
            def choose(self,b,p,l):
                m=chess.Move.from_uci('h1g1'); return encode_move(m),m
        tracker=Diagnostics(Quiet())
        tracker.choose(chess.Board('7k/8/8/8/8/8/p7/R6K w - - 0 1'),0,1)
        self.assertEqual(tracker.counts['missed_favorable_captures'],1)
        tracker=Diagnostics(Quiet())
        tracker.choose(chess.Board('7k/8/8/8/8/r7/p7/R6K w - - 0 1'),0,1)
        self.assertEqual(tracker.counts['missed_favorable_captures'],0)
    def test_repetition_and_material_advantage_draw(self):
        from unittest.mock import patch
        from EvaluateChess import play,encode_move
        class Repeat:
            def choose(self,b,p,l):
                if p>=8:return 8576,None
                m=chess.Move.from_uci('g1f3' if b.piece_at(chess.G1) else 'f3g1')
                return encode_move(m),m
        def black(b,r):
            m=chess.Move.from_uci('g8f6' if b.piece_at(chess.G8) else 'f6g8')
            return encode_move(m),m
        board=chess.Board();board.remove_piece_at(chess.D8)
        with patch('EvaluateChess.start_board',return_value=board),patch('EvaluateChess.opponent',side_effect=black):
            result=play(Repeat(),{'stage':1},False,12,1)
        self.assertEqual(result['result'],'draw_claim')
        self.assertEqual(result['metrics']['repeated_positions'],5)
        self.assertEqual(result['metrics']['model_repetitions'],2)
        self.assertEqual(result['metrics']['advantageous_repetition_draws'],1)
    def test_first_move_task_and_actual_loss_metrics(self):
        from unittest.mock import patch
        from EvaluateChess import play,encode_move
        class Fixed:
            def __init__(self,uci):self.uci=uci
            def choose(self,b,p,l):
                m=chess.Move.from_uci(self.uci);return encode_move(m),m
        record={'stage':0}
        with patch('EvaluateChess.start_board',return_value=chess.Board('7k/8/8/8/8/8/p7/R6K w - - 0 1')):
            result=play(Fixed('h1g1'),record,False,10,1)
            self.assertFalse(result['success']);self.assertEqual(result['plies'],1)
        with patch('EvaluateChess.start_board',return_value=chess.Board('7k/8/8/8/8/8/p7/R6K w - - 0 1')):
            self.assertTrue(play(Fixed('a1a2'),record,False,1,1)['success'])
        with patch('EvaluateChess.start_board',return_value=chess.Board('7k/8/8/8/8/8/r7/R6K w - - 0 1')):
            result=play(Fixed('h1g1'),{'stage':1},False,2,1)
            self.assertEqual(result['metrics']['free_piece_losses'],1)
            self.assertEqual(result['metrics']['free_material_lost'],5)

if __name__=='__main__': unittest.main()
