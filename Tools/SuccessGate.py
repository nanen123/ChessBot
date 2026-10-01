"""Pure, persisted success-based promotion decisions."""
import hashlib
import json
from pathlib import Path

class SuccessGate:
    def __init__(self, config, fingerprint, state=None):
        for key in ('evaluation_interval','minimum_stage_steps','consecutive_passes','minimum_episodes','samples_per_stage'):
            if not isinstance(config[key],int) or config[key] <= 0: raise ValueError(f'{key} must be a positive integer')
        for key in ('promotion_thresholds','retention_thresholds'):
            if len(config[key]) != 5 or any(not 0 <= x <= 1 for x in config[key]): raise ValueError(f'Invalid {key}')
        if len(config['maximum_plies']) != 6 or any(x < 1 for x in config['maximum_plies']): raise ValueError('Invalid ply limits')
        if config['samples_per_stage']*2 < config['minimum_episodes']: raise ValueError('Not enough evaluation episodes')
        self.config=config
        self.state=state or dict(version=1,fingerprint=fingerprint,stage=0,stage_started_step=0,last_evaluation_step=0,consecutive=0)
        if self.state['version'] != 1 or self.state['fingerprint'] != fingerprint: raise ValueError('Evaluation data/config changed; use a new run ID')
        if not 0 <= self.state['stage'] <= 5: raise ValueError('Invalid saved stage')
    def due(self,step): return step-self.state['last_evaluation_step'] >= self.config['evaluation_interval']
    def accept(self, report, step):
        stage=self.state['stage']; by_stage={s['stage']:s for s in report['stages']}
        if step <= self.state['last_evaluation_step']: raise ValueError('Repeated or older evaluation cannot count toward promotion')
        passed=True
        for lesson in range(stage+1):
            r=by_stage[lesson]
            if r['episodes'] < self.config['minimum_episodes']: raise ValueError('Insufficient evaluated episodes')
            if lesson < 5:
                threshold=self.config['promotion_thresholds'][lesson] if lesson==stage else self.config['retention_thresholds'][lesson]
                passed &= r['success_rate'] >= threshold
        passed &= step-self.state['stage_started_step'] >= self.config['minimum_stage_steps']
        self.state['consecutive']=self.state['consecutive']+1 if passed else 0
        self.state['last_evaluation_step']=step
        promote=stage<5 and self.state['consecutive'] >= self.config['consecutive_passes']
        if promote:
            self.state.update(stage=stage+1,stage_started_step=step,consecutive=0)
        return bool(promote)
    def save(self,path):
        path=Path(path); temp=path.with_suffix('.tmp')
        temp.write_text(json.dumps(self.state,indent=2)+'\n',encoding='utf-8'); temp.replace(path)
