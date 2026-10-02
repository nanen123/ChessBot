"""Compile the pure C# generator and cross-check synthetic boards using python-chess."""
import json,subprocess,sys,argparse,statistics
from pathlib import Path
sys.dont_write_bytecode=True
from BuildCaptureExclusions import identity,chess
from ExpandCaptureCurriculum import classify
from ChessTactics import favorable_captures
from EvaluateChess import balanced_samples
ROOT=Path(__file__).resolve().parents[1]
def main():
    ap=argparse.ArgumentParser();ap.add_argument('--unity-data',type=Path,default=Path('C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data'));ap.add_argument('--samples',type=int,default=128);args=ap.parse_args()
    out=ROOT/'output/training-validation/generated-capture';out.mkdir(parents=True,exist_ok=True)
    records=[json.loads(l) for l in (ROOT/'Data/Curriculum/Generalization/positions.jsonl').read_text().splitlines()]
    lines=[]
    for d in range(3):
        samples=balanced_samples([r for r in records if r['stage']==0 and r['difficulty']==d and r['split']=='train'],args.samples,20261002)
        for r in samples:
            for b in (chess.Board(r['fen']),chess.Board(r['fen']).mirror()):lines.append(f"{d}\t{r['stratum']}\t{b.fen()}")
    (out/'sources.tsv').write_text('\n'.join(lines)+'\n')
    doc=json.loads((ROOT/'Assets/Resources/ChessCaptureExclusions.json').read_text());blocked=set(doc['hashes']);(out/'excluded.txt').write_text('\n'.join(sorted(blocked)))
    for r in records:assert identity(chess.Board(r['fen'])) in blocked
    unity=args.unity_data;framework=unity/'MonoBleedingEdge/lib/mono/4.8-api'
    refs=[framework/n for n in ('mscorlib.dll','System.dll','System.Core.dll')]
    sources=[*sorted((ROOT/'Assets/Scripts/Chess/Core').glob('*.cs')),*sorted((ROOT/'Assets/Scripts/Chess/Application').glob('*.cs')),ROOT/'Assets/Scripts/Agents/ChessRewardPolicy.cs',ROOT/'Assets/Scripts/Training/ChessTacticalAssessment.cs',ROOT/'Assets/Scripts/Training/ChessCaptureGenerator.cs',ROOT/'Codex/ExportGeneratedCaptures.cs']
    exe=out/'ExportGeneratedCaptures.exe';rsp=out/'compile.rsp';rsp.write_text('\n'.join(['/nologo','/langversion:9.0','/nostdlib+','/target:exe',f'/out:"{exe}"',*[f'/r:"{r}"' for r in refs],*[f'"{p}"' for p in sources]]))
    subprocess.run([str(unity/'NetCoreRuntime/dotnet.exe'),str(unity/'DotNetSdkRoslyn/csc.dll'),'@'+str(rsp)],check=True)
    with (out/'generated.tsv').open('w') as stream:subprocess.run([str(unity/'MonoBleedingEdge/bin/mono.exe'),str(exe),str(out/'sources.tsv'),str(out/'excluded.txt')],stdout=stream,check=True)
    rows=[]
    for line in (out/'generated.tsv').read_text(encoding='utf-8-sig').splitlines():
        d,p,source,attempts,ms,fen,key,moves=line.split('\t');d=int(d);board=chess.Board(source)
        if fen:
            generated=chess.Board(fen);assert generated.is_valid() and not generated.is_check() and not generated.is_game_over(claim_draw=True)
            assert len(generated.piece_map())==len(board.piece_map()) and sorted(str(x) for x in generated.piece_map().values())==sorted(str(x) for x in board.piece_map().values())
            assert not generated.castling_rights and generated.ep_square is None and generated.halfmove_clock==0
            assert identity(generated)==key and key not in blocked and key!=identity(board)
            result=classify(generated);assert result is not None and result[0]==d,(d,fen,result)
            good=favorable_captures(generated);assert set(moves.split(','))=={m.uci() for m in good}
            assert any(chess.piece_name(generated.piece_type_at(m.from_square))==p and board.piece_at(m.from_square) is None for m in good), 'Attacker coordinates did not change'
        rows.append(dict(difficulty=d,piece=p,generated=bool(fen),attempts=int(attempts),ms=float(ms),identity=key))
    report={}
    for d in range(3):
        selected=[r for r in rows if r['difficulty']==d];times=sorted(r['ms'] for r in selected)
        report[d]=dict(requests=len(selected),generated=sum(r['generated'] for r in selected),unique=len({r['identity'] for r in selected if r['generated']}),mean_ms=statistics.mean(times),p95_ms=times[int(.95*(len(times)-1))],by_piece={p:dict(requests=sum(r['piece']==p for r in selected),generated=sum(r['generated'] and r['piece']==p for r in selected)) for p in sorted({r['piece'] for r in selected})})
    (out/'verification.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2),flush=True)
if __name__=='__main__':main()
