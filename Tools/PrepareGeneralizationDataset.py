"""Prepare balanced/augmented train data and independent validation without changing base PGNs."""
import sys,json,hashlib
from pathlib import Path
from collections import Counter,defaultdict
sys.dont_write_bytecode=True
from CurriculumGeneralization import replay,orbit,bucket,horizontal_record
import chess
ROOT=Path('Data/Curriculum');OUT=ROOT/'Generalization'

def main():
    original=[json.loads(l) for l in (ROOT/'positions.jsonl').read_text().splitlines()]
    additional=[json.loads(l) for l in (OUT/'additional.jsonl').read_text().splitlines()]
    held=[r for r in original+additional if r['split']!='train']
    legacy=[]
    for p in (ROOT/'Legacy').glob('*.jsonl'):legacy += [json.loads(l) for l in p.read_text().splitlines()]
    protected={orbit(chess.Board(r['fen'])) for r in held+legacy if not(r['stage']==5 and r['startPly']==0)}
    sites={s:{r['gameUrl'] for r in original+additional if r['split']==s} for s in ['train','eval','validation']}
    assert not sites['train']&sites['eval'] and not sites['train']&sites['validation'] and not sites['eval']&sites['validation']
    output=[];samples=[];removed=0;augmented=Counter();strata=defaultdict(Counter)
    for i,r in enumerate(original+additional):
        board=replay(r);assert board.is_valid() and not board.is_game_over(claim_draw=True)
        standard=r['stage']==5 and r['startPly']==0
        if r['split']=='train' and not standard and orbit(board) in protected:removed+=1;continue
        r=dict(r,stratum=bucket(r,board),horizontal=False,historyOffset=0,initialFen=chess.STARTING_FEN)
        variants=[r]
        if r['split']=='train':
            transformed=horizontal_record(r)
            if transformed is not None:variants.append(transformed);augmented[r['stage']]+=1
        for v in variants:
            output.append(v)
            if v['split']=='train':
                state=replay(v)
                samples.append(dict(stage=v['stage'],difficulty=v.get('difficulty',-1),category=v['category'],stratum=v['stratum'],split='train',sourceGame=v['gameUrl'],startPly=v['startPly'],historyOffset=v['historyOffset'],horizontal=v['horizontal'],initialFen=v['initialFen'],fen=state.fen(en_passant='fen'),moves=v['movesToStart']))
                strata[f"{v['stage']}:{v.get('difficulty',-1)}"][v['stratum']]+=1
        if (i+1)%2000==0:print('prepared',i+1,flush=True)
    payload=json.dumps(samples,sort_keys=True,separators=(',',':'));dataset=dict(version=4,split='train',datasetId=hashlib.sha256(payload.encode()).hexdigest(),samples=samples)
    # Keep variant duplicates in the same source-game partition only.
    train_keys={orbit(chess.Board(r['fen'])) for r in output if r['split']=='train' and not(r['stage']==5 and r['startPly']==0)}
    assert not train_keys & protected
    manifest=dict(version=1,baseSha256=hashlib.sha256((ROOT/'positions.jsonl').read_bytes()).hexdigest(),additionalSha256=hashlib.sha256((OUT/'additional.jsonl').read_bytes()).hexdigest(),datasetId=dataset['datasetId'],removedForSymmetryLeakage=removed,horizontalByStage=dict(augmented),trainStrata=dict(strata),counts={s:dict(Counter(r['stage'] for r in output if r['split']==s)) for s in ['train','eval','validation']})
    (OUT/'positions.jsonl').write_text(''.join(json.dumps(r)+'\n' for r in output));(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    Path('Assets/Resources/ChessCurriculumTraining.json').write_text(json.dumps(dataset,indent=2)+'\n');print(json.dumps(manifest),flush=True)
if __name__=='__main__':main()
