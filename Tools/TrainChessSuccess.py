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

def compact_score(score):
    if isinstance(score,dict):return {k:compact_score(v) for k,v in score.items() if k!='cases'}
    if isinstance(score,list):return [compact_score(v) for v in score]
    return score

def save_evaluation_model(trainer):
    """Export the current policy without serializing SAC replay memory.

    Keep the GhostTrainer save path so self-play metadata is preserved. Restore
    the setting even if export fails; normal checkpoints and shutdown still save
    replay memory according to the configured setting. Requires unthreaded SAC.
    """
    inner=getattr(trainer,'trainer',trainer)
    enabled=inner.checkpoint_replay_buffer
    try:
        inner.checkpoint_replay_buffer=False
        trainer.save_model()
    finally:
        inner.checkpoint_replay_buffer=enabled

def main():
    own=argparse.ArgumentParser(add_help=False)
    own.add_argument('--evaluation-config',default=str(ROOT/'config/chess_evaluation.yaml'))
    own.add_argument('--repair-capture-b',action='store_true',help='Initialize from a run that reached 0-C and start focused 0-B review')
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
    capture_setting=options.environment_parameters.get('capture_difficulty')
    if capture_setting is None or len(capture_setting.curriculum)!=1 or capture_setting.curriculum[0].completion_criteria is not None:
        raise ValueError('Set environment_parameters.capture_difficulty: 0')
    repair_setting=options.environment_parameters.get('capture_repair_active')
    if config.get('capture_b_repair',{}).get('enabled',False) and (repair_setting is None or len(repair_setting.curriculum)!=1 or repair_setting.curriculum[0].completion_criteria is not None):raise ValueError('Set environment_parameters.capture_repair_active: 0')
    state_path=Path(options.checkpoint_settings.write_path)/'success_curriculum.json'
    state=None
    if options.checkpoint_settings.resume:
        if not state_path.exists(): raise ValueError('Cannot resume without success_curriculum.json; initialize a new run from the old model instead')
        state=json.loads(state_path.read_text())
    gate=SuccessGate(config,fingerprint,state)
    if args.repair_capture_b:
        if options.checkpoint_settings.resume or not options.checkpoint_settings.initialize_from:raise ValueError('--repair-capture-b requires a new run with --initialize-from')
        source_path=Path(options.checkpoint_settings.maybe_init_path)/'success_curriculum.json'
        gate.start_capture_b_repair(json.loads(source_path.read_text(encoding='utf-8')))
    if repair_setting is not None:repair_setting.curriculum[0].value.value=float(gate.state['capture_repair_active'])
    setting.curriculum[0].value.value=float(gate.state['stage'])
    capture_setting.curriculum[0].value.value=float(gate.state['capture_difficulty'])

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
            stage=gate.state['stage']; difficulty=gate.state['capture_difficulty']
            save_evaluation_model(trainer)
            model=Path(self.output_path)/'ChessV1.onnx'
            print(f'[Evaluation] step={step}, stage={stage}, capture_difficulty={difficulty}; testing current and previous lessons',flush=True)
            report=evaluate(model,config,stage,difficulty)
            report.update(step=step,evaluated_stage=stage,evaluated_capture_difficulty=difficulty)
            previous_repair=gate.state['capture_repair_active']
            promoted=gate.accept(report,step)
            repair_changed=previous_repair!=gate.state['capture_repair_active']
            report.update(capture_repair_active=gate.state['capture_repair_active'],capture_recovery_passes=gate.state['capture_recovery_passes'])
            report.update(promoted=promoted,next_stage=gate.state['stage'],next_capture_difficulty=gate.state['capture_difficulty'],consecutive_passes=gate.state['consecutive'])
            folder=Path(self.output_path)/'evaluation'; folder.mkdir(exist_ok=True)
            (folder/f'{step:010d}.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
            with (folder/'history.jsonl').open('a',encoding='utf-8') as out:
                out.write(json.dumps({k:v for k,v in report.items() if k!='stages'} | {'scores':[compact_score(s) for s in report['stages']]})+'\n')
            for score in report['stages']:
                trainer.stats_reporter.set_stat(f'Evaluation/Lesson{score["stage"]}/Success',score['success_rate'])
                if max(score.get('generalization_gap',0),score.get('independent_generalization_gap',0))>0.2:
                    print(f'[Generalization] stage={score["stage"]}: train/eval gap={score["generalization_gap"]:.3f}, independent gap={score.get("independent_generalization_gap",0):.3f}; inspect strata before extending training.',flush=True)
                prefix=f'Evaluation/Lesson{score["stage"]}'
                for key,label in [('training_success_rate','TrainingSuccess'),('generalization_gap','GeneralizationGap'),('independent_success_rate','IndependentSuccess'),('independent_generalization_gap','IndependentGap')]:
                    if key in score:trainer.stats_reporter.set_stat(prefix+'/'+label,score[key])
                for group in score.get('strata',[]):
                    trainer.stats_reporter.set_stat(prefix+'/Strata/'+group['stratum'].replace(':','_')+'/Success',group['success_rate'])
                for name,value in score['metrics'].items():
                    if value is not None: trainer.stats_reporter.set_stat(f'Evaluation/Lesson{score["stage"]}/{name}',value)
                for level in score.get('difficulties',[]):
                    prefix=f'Evaluation/Capture{level["difficulty"]}'
                    trainer.stats_reporter.set_stat(prefix+'/Success',level['success_rate'])
                    trainer.stats_reporter.set_stat(prefix+'/TrainingSuccess',level['training_success_rate'])
                    trainer.stats_reporter.set_stat(prefix+'/GeneralizationGap',level['training_success_rate']-level['success_rate'])
                    if 'independent_success_rate' in level:
                        trainer.stats_reporter.set_stat(prefix+'/IndependentSuccess',level['independent_success_rate'])
                        trainer.stats_reporter.set_stat(prefix+'/IndependentGap',level['independent_generalization_gap'])
            trainer.stats_reporter.set_stat('Evaluation/CaptureRepair/Active',float(gate.state['capture_repair_active']))
            trainer.stats_reporter.set_stat('Evaluation/CaptureRepair/RecoveryPasses',gate.state['capture_recovery_passes'])
            for level in report['stages'][0].get('difficulties',[]):
                if level['difficulty']!=1:continue
                for source,label in [(level,'Success'),(level.get('independent',{}),'IndependentSuccess')]:
                    for group in source.get('strata',[]):
                        trainer.stats_reporter.set_stat('Evaluation/Capture1/Strata/'+group['stratum']+'/'+label,group['success_rate'])
            if repair_changed:print(f"[Capture repair] active={gate.state['capture_repair_active']}; recovery passes={gate.state['capture_recovery_passes']}",flush=True)
            trainer.stats_reporter.set_stat('Evaluation/Stage',gate.state['stage'])
            trainer.stats_reporter.set_stat('Evaluation/CaptureDifficulty',gate.state['capture_difficulty'])
            if promoted:
                shutil.copy2(model,folder/(f'passed-stage-0-capture-{difficulty}.onnx' if stage==0 else f'passed-stage-{stage}.onnx'))
            if promoted or repair_changed:
                if repair_setting is not None:repair_setting.curriculum[0].value.value=float(gate.state['capture_repair_active'])
                setting.curriculum[0].value.value=float(gate.state['stage'])
                capture_setting.curriculum[0].value.value=float(gate.state['capture_difficulty'])
                # Standard curriculum reset path: no terminal reward for abandoned episodes.
                self._reset_env(env_manager); self.end_trainer_episodes()
                inner=getattr(trainer,'trainer',trainer)
                inner.update_buffer.reset_agent(); trainer.reward_buffer.clear()
                trainer.save_model()  # Persist the cleared old-task replay buffer as well.
            gate.save(state_path)
            print(f'[Evaluation] scores={[(s["stage"],round(s["success_rate"],3)) for s in report["stages"]]} promoted={promoted}, stage={gate.state["stage"]}, capture_difficulty={gate.state["capture_difficulty"]}, capture_repair={gate.state["capture_repair_active"]}, recovery_passes={gate.state["capture_recovery_passes"]}',flush=True)
            return count

    learn.TrainerController=EvaluatedController
    learn.run_cli(options)

if __name__=='__main__': main()
