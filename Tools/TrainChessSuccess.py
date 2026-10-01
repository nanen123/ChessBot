"""ML-Agents 1.1.0 entry point with held-out success-based curriculum.
Accepts all mlagents-learn arguments plus --evaluation-config.
"""
import argparse
import hashlib
import json
import shutil
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'output/training-validation/pgn-deps'))
import yaml
from EvaluateChess import evaluate
from SuccessGate import SuccessGate

def main():
    own=argparse.ArgumentParser(add_help=False)
    own.add_argument('--evaluation-config',default=str(ROOT/'config/chess_evaluation.yaml'))
    args,remaining=own.parse_known_args()
    config=yaml.safe_load(Path(args.evaluation_config).read_text(encoding='utf-8-sig'))
    fingerprint=hashlib.sha256((json.dumps(config,sort_keys=True)+(ROOT/config['positions']).read_text(encoding='utf-8')).encode()).hexdigest()
    from mlagents import trainers
    if trainers.__version__ != '1.1.0': raise RuntimeError('This adapter is validated for ML-Agents 1.1.0 only')
    from mlagents.trainers import learn
    from mlagents.trainers.trainer_controller import TrainerController
    options=learn.parse_command_line(remaining)
    if options.checkpoint_settings.force: raise ValueError('Use a new run ID or --resume; --force is disabled to protect evaluation history')
    if options.checkpoint_settings.inference: raise ValueError('Use EvaluateChess.py for standalone evaluation')
    if set(options.behaviors) != {'ChessV1'} or options.behaviors['ChessV1'].threaded: raise ValueError('Expected unthreaded ChessV1 trainer')
    setting=options.environment_parameters.get('curriculum_stage')
    if setting is None or len(setting.curriculum) != 1 or setting.curriculum[0].completion_criteria is not None:
        raise ValueError('Set environment_parameters.curriculum_stage: 0; progress-based promotion must be disabled')
    state_path=Path(options.checkpoint_settings.write_path)/'success_curriculum.json'
    state=None
    if options.checkpoint_settings.resume:
        if not state_path.exists(): raise ValueError('Cannot resume without success_curriculum.json; initialize a new run from the old model instead')
        state=json.loads(state_path.read_text())
    gate=SuccessGate(config,fingerprint,state)
    setting.curriculum[0].value.value=float(gate.state['stage'])

    class EvaluatedController(TrainerController):
        def advance(self,env_manager):
            count=super().advance(env_manager)
            trainer=self.trainers.get('ChessV1')
            if trainer is None: return count
            step=int(trainer.get_step)
            if step < gate.state['last_evaluation_step']: raise RuntimeError('Checkpoint is older than persisted evaluation state')
            state_path.parent.mkdir(parents=True,exist_ok=True)
            if not state_path.exists(): gate.save(state_path)
            if not gate.due(step): return count
            stage=gate.state['stage']
            trainer.save_model()
            model=Path(self.output_path)/'ChessV1.onnx'
            print(f'[Evaluation] step={step}, stage={stage}; testing current and previous lessons',flush=True)
            report=evaluate(model,config,stage)
            report.update(step=step,evaluated_stage=stage)
            promoted=gate.accept(report,step)
            report.update(promoted=promoted,next_stage=gate.state['stage'],consecutive_passes=gate.state['consecutive'])
            folder=Path(self.output_path)/'evaluation'; folder.mkdir(exist_ok=True)
            (folder/f'{step:010d}.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
            with (folder/'history.jsonl').open('a',encoding='utf-8') as out:
                out.write(json.dumps({k:v for k,v in report.items() if k!='stages'} | {'scores':[{k:v for k,v in s.items() if k!='cases'} for s in report['stages']]})+'\n')
            for score in report['stages']:
                trainer.stats_reporter.set_stat(f'Evaluation/Lesson{score["stage"]}/Success',score['success_rate'])
                for name,value in score['metrics'].items():
                    if value is not None: trainer.stats_reporter.set_stat(f'Evaluation/Lesson{score["stage"]}/{name}',value)
            trainer.stats_reporter.set_stat('Evaluation/Stage',gate.state['stage'])
            if promoted:
                shutil.copy2(model,folder/f'passed-stage-{stage}.onnx')
                setting.curriculum[0].value.value=float(gate.state['stage'])
                # Standard curriculum reset path: no terminal reward for abandoned episodes.
                self._reset_env(env_manager); self.end_trainer_episodes()
                inner=getattr(trainer,'trainer',trainer)
                inner.update_buffer.reset_agent(); trainer.reward_buffer.clear()
                trainer.save_model()  # Persist the cleared old-task replay buffer as well.
            gate.save(state_path)
            print(f'[Evaluation] scores={[(s["stage"],round(s["success_rate"],3)) for s in report["stages"]]} promoted={promoted}, stage={gate.state["stage"]}',flush=True)
            return count

    learn.TrainerController=EvaluatedController
    learn.run_cli(options)

if __name__=='__main__': main()
