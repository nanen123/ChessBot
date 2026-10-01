"""Persisted success-based promotion including the three capture difficulties."""
import json
from pathlib import Path

class SuccessGate:
    def __init__(self, config, fingerprint, state=None):
        for key in ('evaluation_interval','minimum_stage_steps','consecutive_passes','minimum_episodes','samples_per_stage','capture_samples_per_difficulty','capture_minimum_episodes'):
            if not isinstance(config[key],int) or config[key]<=0:raise ValueError(f'{key} must be a positive integer')
        for key,size in [('promotion_thresholds',5),('retention_thresholds',5),('capture_promotion_thresholds',3),('capture_retention_thresholds',3)]:
            if len(config[key])!=size or any(not 0<=x<=1 for x in config[key]):raise ValueError(f'Invalid {key}')
        if len(config['maximum_plies'])!=6 or any(x<1 for x in config['maximum_plies']):raise ValueError('Invalid ply limits')
        if config['samples_per_stage']*2<config['minimum_episodes'] or config['capture_samples_per_difficulty']*2<config['capture_minimum_episodes']:raise ValueError('Not enough evaluation episodes')
        self.config=config
        self.state=state or dict(version=2,fingerprint=fingerprint,stage=0,capture_difficulty=0,stage_started_step=0,last_evaluation_step=0,consecutive=0)
        if self.state['version']!=2 or self.state['fingerprint']!=fingerprint:raise ValueError('Evaluation data/config changed; use a new run ID')
        if not 0<=self.state['stage']<=5 or not 0<=self.state['capture_difficulty']<=2:raise ValueError('Invalid saved curriculum phase')
        if self.state['stage']>0 and self.state['capture_difficulty']!=2:raise ValueError('Capture curriculum was not completed')
    def due(self,step):return step-self.state['last_evaluation_step']>=self.config['evaluation_interval']
    def accept(self,report,step):
        stage=self.state['stage'];difficulty=self.state['capture_difficulty'];by_stage={s['stage']:s for s in report['stages']}
        if step<=self.state['last_evaluation_step']:raise ValueError('Repeated or older evaluation cannot count toward promotion')
        passed=True
        for lesson in range(stage+1):
            r=by_stage[lesson]
            if lesson==0:
                levels={d['difficulty']:d for d in r['difficulties']}
                for level in range(difficulty+1):
                    result=levels[level]
                    if result['episodes']<self.config['capture_minimum_episodes']:raise ValueError('Insufficient evaluated capture episodes')
                    thresholds=self.config['capture_promotion_thresholds'] if stage==0 and level==difficulty else self.config['capture_retention_thresholds']
                    passed &= result['success_rate']>=thresholds[level]
            else:
                if r['episodes']<self.config['minimum_episodes']:raise ValueError('Insufficient evaluated episodes')
                if lesson<5:
                    threshold=self.config['promotion_thresholds'][lesson] if lesson==stage else self.config['retention_thresholds'][lesson]
                    passed &= r['success_rate']>=threshold
        passed &= step-self.state['stage_started_step']>=self.config['minimum_stage_steps']
        self.state['consecutive']=self.state['consecutive']+1 if passed else 0
        self.state['last_evaluation_step']=step
        promote=stage<5 and self.state['consecutive']>=self.config['consecutive_passes']
        if promote:
            if stage==0 and difficulty<2:self.state['capture_difficulty']+=1
            else:self.state['stage']+=1
            self.state.update(stage_started_step=step,consecutive=0)
        return bool(promote)
    def save(self,path):
        path=Path(path);temp=path.with_suffix('.tmp');temp.write_text(json.dumps(self.state,indent=2)+'\n',encoding='utf-8');temp.replace(path)
