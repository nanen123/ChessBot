# 실제 경기 기반 커리큘럼 PGN 샘플

출처: [Lichess Open Database](https://database.lichess.org/). 2013년 1월 Standard Rated 경기 아카이브를 사용했다. 이 묶음은 소규모 초기 자료를 빠르게 재현하기 위해 선택했으며, 최신 경기나 마스터급 경기만으로 구성된 자료는 아니다.

원본: https://database.lichess.org/standard/lichess_db_standard_rated_2013-01.pgn.zst

원본 라이선스: **CC0 1.0**. Lichess 공개 데이터베이스 안내에 따라 이용·수정·재배포할 수 있다. 다운로드한 압축 원본은 `output/training-validation/pgn-source/lichess-2013-01.pgn.zst`에 있으며 Git에서는 제외된다.

## 파일 사용법
각 단계에는 `*.train.pgn`과 `*.eval.pgn`이 있다. 목표 수량은 단계마다 학습용 16개, 평가용 4개다. 메이트는 mateIn1/mateIn2, 엔드게임은 KQK/KRK를 절반씩 수집한다. 원본 URL의 SHA256으로 경기 단위 분할을 고정하므로 같은 경기가 학습과 평가에 걸쳐 등장하지 않는다. 같은 분할 안에서는 한 경기가 여러 단계에 쓰일 수 있다. 단계 내 동일 배치는 제거하되 전체 대국의 표준 초기 배치는 예외다.

PGN에는 실제 경기 전체 수순과 선수·날짜·결과·원본 Site를 보존하고 아래 태그를 추가했다.

- `CurriculumStage`: 단계 번호 0~5
- `CurriculumStartPly`: 첫 수부터 이 수만큼 재생한 뒤 학습을 시작
- `CurriculumStartFEN`: 시작 배치 검증용 FEN
- `CurriculumCriterion`: 선택한 이유
- `DatasetSplit`: train 또는 eval

`positions.jsonl`에는 같은 시작 FEN, 해당 위치까지의 UCI 수순, 원본 경기 URL, 선택 기준과 후보 수가 있다. `solutionFirstMove`는 메이트 단계에서만 증명된 해답의 첫 수이며 포획·교환 단계에서는 조건을 충족하는 **예시 수**다. 교환 예시는 최선의 수라는 의미가 아니다. 원본 PGN의 이어지는 사람 수순도 정답 라벨로 취급하지 않는다.

## 단계별 선정 기준

| 단계 | 기준 | 검증 범위 |
| --- | --- | --- |
| 0 즉시 포획 | 보드 전체 12기물 이하, 합법적 포획이 있고 포획한 말을 즉시 합법적으로 되잡을 수 없음 | 한 수 뒤 되잡기와 정상 종료 여부 확인. 이후 전술까지 안전하다는 의미는 아님 |
| 1 포획과 방어 | 8~20기물, 합법적인 포획에 즉시 합법적인 되잡기가 가능 | 교환·방어를 경험할 후보 위치. 교환의 유불리 및 최선의 방어는 엔진으로 평가하지 않음 |
| 2 메이트 인 1·2 | 실제 경기 마지막 3반수 구간에서 강제 메이트가 존재 | 모든 합법적 방어 응수에 대해 최대 3반수 안의 메이트를 직접 탐색. mateIn2는 mateIn1이 없는 위치만 포함 |
| 3 간단한 엔드게임 | 실제 경기에서 KQK 또는 KRK, 강한 쪽 차례, 큰 기물이 공격받지 않음 | 합법성·비종료 상태 확인. 테이블베이스 승리/50수 규칙까지 증명한 데이터는 아님 |
| 4 중반 | 20~50반수 진행, 16~28기물, 기물 가치 차이 2 이하, 체크 아님 | 기물 수·가치에 따른 필터. 엔진 평가상 균형을 보장하지 않음 |
| 5 전체 대국 | 정상 종료로 기록된 표준 경기, 최소 20반수 | 시작점은 표준 초기 배치. 기보들은 향후 분석·모방학습 자료이며 Self-Play 시작 배치 다양성을 늘리지는 않음 |

변형 체스, 사용자 초기 배치, 파싱 오류, 시간패 경기 및 20반수 미만 경기는 제외한다. 시작점까지 무승부 청구가 가능한 종료 상황은 제외한다. 입력의 순서대로 조건을 충족하는 작은 표본을 선택했으므로 무작위 대표 표본은 아니다. 선수 실력 필터는 적용하지 않았다. 실전 학습 전에 충분한 자료 확대와 엔진 평가를 권장한다.

## Unity 연결 상태

**Training 환경에 연결되어 있다.** 기본 Position Source = Pgn Training은 train PGN에서 변환한 리소스를 사용한다. eval 파일은 포함하지 않는다. 원본 기보의 시작 수순을 재생해 반복 이력을 보존하고 그 위치부터 양측 Agent가 Self-Play한다. 수집 데이터가 이미 리소스로 생성되어 있으므로 Training 실행 파일을 다시 빌드하면 적용된다.

데이터 변경 시 프로젝트 루트에서 `.venv-training/Scripts/python.exe Tools/BuildCurriculumDataset.py`를 실행하고 Unity 플레이어를 다시 빌드한다. 출력은 `Assets/Resources/ChessCurriculumTraining.json`이며 학습용 96개 항목과 데이터 식별 해시를 포함한다. 기본 단계·보상은 [커리큘럼 문서](../../Codex/005.Curriculum.md)를 따른다.

현재 초기 단계의 종료 조건·보상은 단순 과제용이다. 특히 1단계는 12반수의 순포획 점수로 끝나므로 실제 전술의 최선 수를 보장하는 보상이 아니다. 실제 기보 데이터의 규모를 늘리는 것과 목표·보상을 검증하는 것은 별도 작업이다.

## 재현

`Tools/PrepareCurriculumPgn.py`는 chess 1.11.2와 zstandard 0.23.0을 사용한다. 현재 프로젝트의 별도 `output/training-validation/pgn-deps`에 준비되어 있으며 기존 ML-Agents 환경 패키지는 변경하지 않았다.

```powershell
.venv-training/Scripts/python.exe Tools/PrepareCurriculumPgn.py output/training-validation/pgn-source/lichess-2013-01.pgn.zst --out Data/Curriculum
```

다른 PC에서는 별도 Python 환경에 위 두 패키지를 설치하고 원본을 내려받으면 된다. 재실행 시 출력 폴더의 동일한 이름의 샘플 파일을 갱신한다.

## 수집 결과와 검증

원본 2,762경기를 살펴보아 단계별 20개, 총 **120개 단계별 항목**을 추출했다. 동일 경기가 여러 단계에 활용되어 고유 원본 경기는 **49개(학습 34개, 평가 15개)**다. 120개의 서로 다른 경기라는 의미는 아니다. 12개 PGN 파일의 전체 수순 합법성, 시작 FEN 일치, 강제 메이트 해답, 경기 단위 학습·평가 분리를 재검증했다.

검증 재실행: `.venv-training/Scripts/python.exe Tools/VerifyCurriculumPgn.py` (프로젝트 루트에서 실행).
