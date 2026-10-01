# 실제 경기 기반 커리큘럼 PGN 샘플

출처: [Lichess Open Database](https://database.lichess.org/). 2013년 1월 Standard Rated 경기 아카이브를 사용했다. 이 묶음은 소규모 초기 자료를 빠르게 재현하기 위해 선택했으며, 최신 경기나 마스터급 경기만으로 구성된 자료는 아니다.

원본: https://database.lichess.org/standard/lichess_db_standard_rated_2013-01.pgn.zst

원본 라이선스: **CC0 1.0**. Lichess 공개 데이터베이스 안내에 따라 이용·수정·재배포할 수 있다. 다운로드한 압축 원본은 `output/training-validation/pgn-source/lichess-2013-01.pgn.zst`에 있으며 Git에서는 제외된다.

## 파일 사용법
각 단계에는 `*.train.pgn`과 `*.eval.pgn`이 있다. 학습 16,385개 / 평가 1,025개다. 단계별 수량은 [커리큘럼 표](../../Codex/005.Curriculum.md)를 따른다. 원본 URL의 SHA256으로 경기 단위 분할을 고정하므로 같은 경기가 학습과 평가에 걸쳐 등장하지 않는다. 같은 분할 안에서는 한 경기가 여러 단계에 쓰일 수 있다. 단계 내 동일 배치와 색 반전 배치를 제거하고 학습/평가 간 동일 배치도 제외한다. 표준 초기 배치만 공통 기준점으로 허용한다.

PGN에는 실제 경기 전체 수순과 선수·날짜·결과·원본 Site를 보존하고 아래 태그를 추가했다.

- `CurriculumStage`: 단계 번호 0~5
- `CurriculumCategory`: 1~5단계의 유형(교환, 메이트, 엔드게임, 오프닝 등)
- `CaptureDifficulty`: 0단계의 세부 난이도 0~2
- `CurriculumStartPly`: 첫 수부터 이 수만큼 재생한 뒤 학습을 시작
- `CurriculumStartFEN`: 시작 배치 검증용 FEN
- `CurriculumCriterion`: 선택한 이유
- `DatasetSplit`: train 또는 eval

`positions.jsonl`에는 같은 시작 FEN, 해당 위치까지의 UCI 수순, 원본 경기 URL, 선택 기준과 후보 수가 있다. `solutionFirstMove`는 메이트 단계에서만 증명된 해답의 첫 수이며 포획·교환 단계에서는 조건을 충족하는 **예시 수**다. 교환 예시는 최선의 수라는 의미가 아니다. 원본 PGN의 이어지는 사람 수순도 정답 라벨로 취급하지 않는다.

## 단계별 선정 기준

| 단계 | 기준 | 검증 범위 |
| --- | --- | --- |
| 0-A 공짜 포획 | 4~6기물. 즉시 되잡히지 않는 유리한 포획 | 모든 즉시 응수 뒤 순기물 이득 양수, 상대 즉시 메이트 없음 |
| 0-B 안전한 포획 | 7~12기물. 즉시 되잡히지 않는 유리한 포획 | 같은 두 반수 기준 |
| 0-C 유리한 교환 | 최대 20기물. 유리한 포획은 모두 즉시 되잡힘 가능 | 같은 두 반수 기준. 쉬운 비재포획 대안 제외 |
| 1 포획과 방어 | 8~20기물, 체크 아님. 안전한 이득 / 유리한 교환 / 유리·불리한 포획이 함께 존재하는 선택 문제 | 두 반수 기준의 유리한 포획을 최소 하나 포함. 장기 최선 수를 증명하지 않음 |
| 2 메이트 인 1·2 | 실제 경기 마지막 3반수 구간에서 강제 메이트가 존재 | 모든 합법적 방어 응수에 대해 최대 3반수 안의 메이트를 직접 탐색. mateIn2는 mateIn1이 없는 위치만 포함 |
| 3 간단한 엔드게임 | 실제 경기에서 KQK 또는 KRK, 강한 쪽 차례, 큰 기물이 공격받지 않음 | 합법성·비종료 상태 확인. 테이블베이스 승리/50수 규칙까지 증명한 데이터는 아님 |
| 4 중반 | 20~50반수 진행, 16~28기물, 기물 가치 차이 2 이하, 체크 아님 | 기물 수·가치에 따른 필터. 엔진 평가상 균형을 보장하지 않음 |
| 5 전체 대국 | 6/8반수 진행, 30기물 이상, 체크 아님, 기물 차이 1 이하, 현재 차례 즉시 메이트 없음 | 실제 오프닝 이후 Self-Play. 표준 초기 배치도 20% 혼합 |

변형 체스, 사용자 초기 배치, 파싱 오류 및 시간패 경기는 제외한다. 1~5단계의 원본 경기는 20반수 이상 조건도 적용한다. 시작점까지 무승부 청구가 가능한 종료 상황은 제외한다. 입력 순서대로 조건을 충족하는 표본을 선택하므로 무작위 대표 표본은 아니다. 선수 실력 필터는 적용하지 않았다. 큰 기력 비교에는 별도 엔진 평가와 미사용 검증 자료가 필요하다.

## Unity 연결 상태

**Training 환경에 연결되어 있다.** 기본 Position Source = Pgn Training은 train PGN에서 변환한 리소스를 사용한다. eval 파일은 포함하지 않는다. 원본 기보의 시작 수순을 재생해 반복 이력을 보존하고 그 위치부터 양측 Agent가 Self-Play한다. 수집 데이터가 이미 리소스로 생성되어 있으므로 Training 실행 파일을 다시 빌드하면 적용된다.

데이터 변경 시 프로젝트 루트에서 `.venv-training/Scripts/python.exe Tools/BuildCurriculumDataset.py`를 실행하고 Unity 플레이어를 다시 빌드한다. 출력은 `Assets/Resources/ChessCurriculumTraining.json`이며 학습용 16,385개 항목과 데이터 식별 해시를 포함한다. 기본 단계·보상은 [커리큘럼 문서](../../Codex/005.Curriculum.md)를 따른다.

현재 초기 단계의 종료 조건·보상은 단순 과제용이다. 특히 1단계는 12반수의 순포획 점수로 끝나므로 실제 전술의 최선 수를 보장하는 보상이 아니다. 실제 기보 데이터의 규모를 늘리는 것과 목표·보상을 검증하는 것은 별도 작업이다.

## 재현

`Tools/PrepareCurriculumPgn.py`는 chess 1.11.2와 zstandard 0.23.0을 사용한다. 현재 프로젝트의 별도 `output/training-validation/pgn-deps`에 준비되어 있으며 기존 ML-Agents 환경 패키지는 변경하지 않았다.

```powershell
.venv-training/Scripts/python.exe Tools/PrepareCurriculumPgn.py output/training-validation/pgn-source/lichess-2013-01.pgn.zst --out Data/Curriculum
.venv-training/Scripts/python.exe Tools/ExpandCaptureCurriculum.py output/training-validation/pgn-source/lichess-2013-01.pgn.zst
.venv-training/Scripts/python.exe Tools/ExpandRemainingCurriculum.py output/training-validation/pgn-source/lichess-2013-01.pgn.zst
.venv-training/Scripts/python.exe Tools/VerifyCurriculumPgn.py
.venv-training/Scripts/python.exe Tools/BuildCurriculumDataset.py
```

다른 PC에서는 별도 Python 환경에 위 두 패키지를 설치하고 원본을 내려받으면 된다. 재실행 시 출력 폴더의 동일한 이름의 샘플 파일을 갱신한다.

## 수집 결과와 검증

총 **17,410개 항목(train 16,385 / eval 1,025)**이다. 0단계는 기존 세 난이도를 유지한다. 1~5단계 확장은 원본 68,892경기를 스캔했다. 교환·메이트는 경기당 유형별 1개, 엔드게임은 경기당 최대 16개를 2 ply 이상 간격으로 수집했다. 같은 경기의 엔드게임 위치들은 서로 연관되어 있으며 독립 경기 수와 위치 수는 다르다. 중반은 경기당 1개를 선택한다.

중반·오프닝은 ECO 코드별 최대 5%(정수 올림), 오프닝은 처음 4 ply의 동일 수순별 최대 6.25%를 적용했다. 이는 수순 다양성을 위한 제한이며 엔진 평가상 균형이나 최선의 오프닝을 보증하지 않는다. 기존 평가 기보는 Legacy에 보존하고 새 훈련 자료에서 그 위치·원본 경기를 제외했다. 정확한 수량·원본 해시는 manifest.json의 captureExpansion과 remainingExpansion에 기록한다.

ExpandCaptureCurriculum.py는 0단계만, ExpandRemainingCurriculum.py는 1~5단계만 교체한다. 목표 수량을 확보하지 못하면 활성 데이터는 교체하지 않는다. Unity 리소스는 version 3이며 각 샘플의 첫 사용 시 Unity 규칙으로 재검증하고 캐시한다. 전체 기보 검증은 VerifyCurriculumPgn.py가 수행한다.

평가 데이터는 자동 승급의 검증 세트로 사용한다. 최종 기력 평가는 별도 미사용 자료가 필요하다. [자동 평가 안내](../../Codex/006.AutomaticEvaluation.md)를 따른다.

검증 재실행: `python Tools/VerifyCurriculumPgn.py` (프로젝트 루트).

확장 시 원본 30,352경기를 스캔했다. 이는 0단계 확장 당시 기록이며, 후속 1~5단계 확장 전의 통계다. 0-A 예시 포획의 이동 기물은 킹 561, 폰 64, 룩 174, 퀸 155, 나이트 39, 비숍 31이다. 모든 종류를 포함하지만 균등 분포는 아니며 실제 엔드게임 특성상 킹 포획 비중이 높다.

확장 후 고유 원본 경기는 전체 train 6,600경기 / eval 380경기다. 엔드게임은 train 257경기 / eval 17경기에서 각각 2,048/128개 위치를 추출했다. 학습 오프닝의 ECO 코드 수는 3수 시작 116종, 4수 시작 118종이다.
