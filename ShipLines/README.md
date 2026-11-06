# ShipLines - 오프셋 테이블 / 선도 생성기

라이노 없이 실행되는 독립 스크립트 (Python 3, 표준 라이브러리만 사용).

    python3 ship_lines.py [출력폴더]    # 기본: ShipLines/output

| 파일 | 내용 |
|---|---|
| output/halfbreadths.csv | 스테이션 x 수선(WL) 반폭 표 [m] |
| output/heights.csv | 키일 / 버톡 높이 / 갑판 높이 / 갑판 반폭 [m] |
| output/lines_plan.svg | 선도 (Sheer / Half-breadth / Body plan) + 오프셋 표 + 표제란 |
| output/lines_plan.dxf | 같은 도면, DXF R12, 1 m = 1000 mm |

예제선: Lpp 100 m, B 16 m, T 6 m, D 9 m, 21개 스테이션(+선수 20.5), Cb ≈ 0.68.
다른 선형은 `ship_lines.py` 위쪽의 PARTICULARS 와 `half_breadth() / z_keel() / z_deck() / x_end()` 를 수정한다.
실제 Rhino 모델에서 뽑으려면 기존 `OffsetGen` 플러그인을 사용한다.
