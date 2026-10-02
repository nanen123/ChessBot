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
        if config.get('require_independent_validation',False) and (config.get('generalization_samples',0)*2 < config.get('independent_minimum_episodes',64)):
            raise ValueError('Not enough independent validation episodes')
        repair=config.get('capture_b_repair',{})
        if repair.get('enabled',False):
            if not config.get('require_independent_validation',False):raise ValueError('Capture repair requires independent validation')
            if not config['capture_retention_thresholds'][1] <= repair.get('recovery_threshold',0.8) <= 1:raise ValueError('Invalid capture recovery threshold')
            if type(repair.get('recovery_passes',2)) is not int or repair.get('recovery_passes',2)<1:raise ValueError('Invalid capture recovery pass count')
        self.config=config
        self.state=state or dict(version=2,fingerprint=fingerprint,stage=0,capture_difficulty=0,stage_started_step=0,last_evaluation_step=0,consecutive=0)
        if self.state['version']!=2 or self.state['fingerprint']!=fingerprint:raise ValueError('Evaluation data/config changed; use a new run ID')
        if not 0<=self.state['stage']<=5 or not 0<=self.state['capture_difficulty']<=2:raise ValueError('Invalid saved curriculum phase')
        if self.state['stage']>0 and self.state['capture_difficulty']!=2:raise ValueError('Capture curriculum was not completed')
        self.state.setdefault('capture_repair_active',False)
        self.state.setdefault('capture_recovery_passes',0)
        if type(self.state['capture_repair_active']) is not bool or type(self.state['capture_recovery_passes']) is not int or self.state['capture_recovery_passes']<0:raise ValueError('Invalid saved capture repair state')
        if self.state['capture_repair_active'] and (not repair.get('enabled',False) or self.state['stage']!=0 or self.state['capture_difficulty']!=2):raise ValueError('Capture repair is only valid in stage 0-C')
    def start_capture_b_repair(self,source_state):
        if not self.config.get('capture_b_repair',{}).get('enabled',False):raise ValueError('Enable capture_b_repair in evaluation config')
        if source_state.get('version')!=2 or source_state.get('capture_difficulty')!=2 or source_state.get('stage') not in range(6):raise ValueError('Source run must have reached capture difficulty 2')
        self.state.update(stage=0,capture_difficulty=2,capture_repair_active=True,capture_recovery_passes=0)
    def update_capture_repair(self,result):
        settings=self.config.get('capture_b_repair',{})
        if not settings.get('enabled',False) or self.state['stage']!=0 or self.state['capture_difficulty']!=2:return
        if result.get('independent_episodes',0)<self.config.get('independent_minimum_episodes',64):raise ValueError('Missing capture repair validation episodes')
        rate=min(result['success_rate'],result['independent_success_rate'])
        if not self.state['capture_repair_active'] and rate<self.config['capture_retention_thresholds'][1]:
            self.state.update(capture_repair_active=True,capture_recovery_passes=0)
        if self.state['capture_repair_active']:
            count=self.state['capture_recovery_passes']+1 if rate>=settings.get('recovery_threshold',0.8) else 0
            self.state['capture_recovery_passes']=count
            if count>=settings.get('recovery_passes',2):self.state['capture_repair_active']=False
    def due(self,step):return step-self.state['last_evaluation_step']>=self.config['evaluation_interval']
    def accept(self,report,step):
        stage=self.state['stage'];difficulty=self.state['capture_difficulty'];by_stage={s['stage']:s for s in report['stages']}
        if step<=self.state['last_evaluation_step']:raise ValueError('Repeated or older evaluation cannot count toward promotion')
        def independent_pass(result,threshold):
            if not self.config.get('require_independent_validation',False):return True
            if result.get('independent_episodes',0)<self.config.get('independent_minimum_episodes',64):raise ValueError('Missing independent validation episodes')
            return result['independent_success_rate']>=threshold
        passed=True
        for lesson in range(stage+1):
            r=by_stage[lesson]
            if lesson==0:
                levels={d['difficulty']:d for d in r['difficulties']}
                for level in range(difficulty+1):
                    result=levels[level]
                    if result['episodes']<self.config['capture_minimum_episodes']:raise ValueError('Insufficient evaluated capture episodes')
                    thresholds=self.config['capture_promotion_thresholds'] if stage==0 and level==difficulty else self.config['capture_retention_thresholds']
                    passed &= result['success_rate']>=thresholds[level] and independent_pass(result,thresholds[level])
            else:
                if r['episodes']<self.config['minimum_episodes']:raise ValueError('Insufficient evaluated episodes')
                if lesson<5:
                    threshold=self.config['promotion_thresholds'][lesson] if lesson==stage else self.config['retention_thresholds'][lesson]
                    passed &= r['success_rate']>=threshold and independent_pass(r,threshold)
                    if self.config.get('require_category_validation',False) and lesson in (1,2,3):
                        required={1:['safe_gain','favorable_exchange','capture_choice'],2:['mateIn1','mateIn2'],3:['KQK','KRK']}[lesson]
                        for source in [r,r.get('independent',{})]:
                            categories={c['category']:c for c in source.get('categories',[])}
                            for name in required:
                                group=categories.get(name,{})
                                if group.get('episodes',0)<self.config.get('category_minimum_episodes',16):raise ValueError('Missing category validation: '+name)
                                passed &= group['success_rate']>=threshold
        if stage==0 and difficulty==2:self.update_capture_repair(levels[1])
        passed &= step-self.state['stage_started_step']>=self.config['minimum_stage_steps']
        self.state['consecutive']=self.state['consecutive']+1 if passed else 0
        self.state['last_evaluation_step']=step
        promote=stage<5 and not self.state['capture_repair_active'] and self.state['consecutive']>=self.config['consecutive_passes']
        if promote:
            if stage==0 and difficulty<2:self.state['capture_difficulty']+=1
            else:self.state['stage']+=1
            self.state.update(stage_started_step=step,consecutive=0)
        return bool(promote)
    def save(self,path):
        path=Path(path);temp=path.with_suffix('.tmp');temp.write_text(json.dumps(self.state,indent=2)+'\n',encoding='utf-8');temp.replace(path)
