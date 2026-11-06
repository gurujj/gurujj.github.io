// Made by Jejun Park, Ph. D.
// JPOffsets - 선형 오프셋 테이블 + 선도(Lines plan) 생성 명령 (Rhino .rhp 플러그인), 명령어: JPOffsets
// [1.1.0-alpha.1] 벌브 선수 대응: 버톡 라인을 "x마다 최저 z"가 아니라 버톡 평면(y=b)과 선체의 정확한 교차 곡선으로 추적
//   (이전 버전 OffsetGen 1.0.0-alpha.5 는 Archive/ 에 보관)
// 환경: Visual Studio 2015 / .NET Framework 4.8 / C# 6 이하 문법만 사용
// 참조: RhinoCommon.dll, System.Windows.Forms, System.Drawing (RhinoCommon은 Copy Local = False)
//
// 입력(탭 1): 스테이션 = AP x, FP x, 분할 수(AP~FP를 나눌 구간 수, 간격 = (FP-AP)/분할 수), 시작/끝 스테이션 번호 (AP = 0)
//             + 직접 입력 스테이션 번호(쉼표 구분)를 합쳐 작은 번호 -> 큰 번호로 정렬, 중복 제거
//             워터라인(z) / 버톡(y)은 "시작 / 끝 / 간격" + 추가 값을 합쳐 오름차순 정렬, 중복 제거
//             기본값: AP 0, FP 200, 분할 수 20, 추가 St. -0.5 0.5 1.5 18.5 19.5, WL 0~23 간격 1 (+0.4), BT 0~16 간격 1
//             현재 단위(입력값/모델 좌표가 뜻하는 단위)와 쓰기 단위(m / cm / mm)를 골라 CSV/DXF 값만 환산
//             배율 = 현재 단위 / 쓰기 단위 (m -> mm 이면 x1000). DXF 글자 높이는 쓰기 단위
// 입력(탭 2): 라이노 표시, CSV/DXF 출력, 현재/쓰기 단위, 선도 옵션(갑판선, 버톡 스무딩, 선명, 척도)
//
// 출력 (3dm과 같은 폴더, 체크한 것만):
//   OffsetTable.csv : 워터라인 반폭 표 (+ 버톡 높이 표). 선체 없으면 빈 칸
//   OffsetTable.dxf : 오프셋 표 + (선택) 선도. DXF R12 ASCII, 쓰기 단위 1:1
//       Sheer plan (X-Z)        : 프로파일, 갑판선(Sheer line), 버톡 커브, 스테이션/워터라인 격자, AP/FP, BL
//       Half-breadth plan (X-Y) : 워터라인 커브, 갑판 가장자리, 버톡 격자, 스테이션 격자
//       Body plan (Y-Z)         : 스테이션 단면 (선수 쪽은 오른쪽, 선미 쪽은 왼쪽), 워터라인/버톡 격자
//       Sheer plan 아래에 오프셋 표 -> 버톡 높이 표 -> 표제란/주요 치수
//   DXF 문자는 영문/숫자만 (한글은 ?로 바뀜)
// 버톡 라인(기본, 정밀 추적): 버톡 평면 y = b 와 선체 Brep의 교차 곡선을 그대로 사용 -> 벌브처럼 x에 대해 여러 값을 갖는 곡선도 선형을 따라감.
//   표의 버톡 높이는 스테이션 단면과 y = b 의 교점 중 최저 z (기존과 동일) + 교점이 2개 이상이면 "상부 교점" 표를 추가 출력
//   (벌브 단면은 y = b 를 아랫면/윗면에서 두 번 만남). 닫힌 교차 곡선(솔리드)이나 교차 실패 시 기존 방식(최저 z 이음)으로 자동 대체.
//   기존 방식: 조밀한 단면(200구간) 커브와 y = b 수직선의 정확한 교차점 중 최저 z를 이음 (z 급변 구간은 이분 분할)
//   - 최대 폭 근처에서는 선체 면과 평행해 오차가 증폭되므로 흔들림이 곡면 자체의 문제일 수 있음
// 라이노 표시 레이어: OffsetGen_Station / OffsetGen_Waterline / OffsetGen_Buttock
//   (다시 실행하면 이 레이어의 이전 커브는 지우고 새로 만듦)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Input.Custom;

namespace OffsetGenPlugin
{
    // ---------------------------------------------------------------
    // 플러그인 본체
    // ---------------------------------------------------------------
    public class JPOffsetsPlugIn : Rhino.PlugIns.PlugIn
    {
        public static JPOffsetsPlugIn Instance { get; private set; }
        public JPOffsetsPlugIn() { Instance = this; }
    }

    public static class PlugInInfo
    {
        public const string Author = "Jejun Park, Ph. D.";
        public const string Credit = "made by Jejun Park, Ph. D.";
        public const string Version = "1.2.0-alpha.2";
    }

    // ---------------------------------------------------------------
    // 데이터 묶음
    // ---------------------------------------------------------------
    public class StationSection
    {
        public double X;
        public List<Curve> Curves;
    }

    public class WaterlineSection
    {
        public double Z;
        public List<Curve> Curves;
    }

    // 버톡 라인: 조밀한 스테이션 단면에서 뽑은 점들 (x, y, z). 선체 밖 구간은 끊겨서 여러 조각이 됨
    public class ButtockSection
    {
        public double Y;
        public List<List<Point3d>> Lines;
        public List<Curve> Curves;   // 정밀 추적일 때 정확한 곡선 (라이노 표시용). 기존 방식이면 null
        public bool Exact;
    }

    // 조밀한 스테이션 하나의 단면 폴리라인들 (양현이면 y*ySign >= 0 쪽만)
    public class DenseSection
    {
        public double X;
        public List<Curve> Curves;              // 정확한 교차 계산용 원본 단면 커브
        public List<List<Point3d>> Polys;
    }

    // 조밀한 스테이션에서 뽑은 윤곽점: 갑판 가장자리(상단)와 바닥
    public class OutlinePoint
    {
        public double X;
        public double YTop;   // 갑판 가장자리의 반폭
        public double ZTop;   // 갑판 가장자리의 높이
        public double ZBot;   // 단면의 최저 높이
    }

    public class LinesData
    {
        public BoundingBox Bbox;
        public double Tol;
        public List<StationSection> Stations;
        public List<WaterlineSection> Waterlines;
        public List<ButtockSection> Buttocks;
        public List<Curve> Profile;
        public List<OutlinePoint> Outline;
        public bool DeckLine;
        public string ShipName;
        public string ScaleText;
        public string UnitName;   // 출력 단위 ("m" 또는 "mm")
        public double? DepthM;    // 주요 치수용 (미터): DEPTH TO MAIN DK, 비어 있으면 도면에 쓰지 않음
        public double? UpperDepthM;   // DEPTH TO UPPER DK
        public double? DraftM;
    }

    // ---------------------------------------------------------------
    // 비어 있을 때 흐린 글씨로 예제를 보여주는 텍스트 상자
    // ---------------------------------------------------------------
    public class HintTextBox : TextBox
    {
        const int WM_PAINT = 0x000F;

        public string Hint { get; set; }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_PAINT && Text.Length == 0 && !string.IsNullOrEmpty(Hint))
            {
                using (var g = Graphics.FromHwnd(Handle))
                {
                    var rect = new Rectangle(1, 1, Math.Max(1, ClientSize.Width - 1), Math.Max(1, ClientSize.Height - 1));
                    TextRenderer.DrawText(g, Hint, Font, rect, SystemColors.GrayText,
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                }
            }
        }
    }

    // ---------------------------------------------------------------
    // 입력 창 (탭 2개)
    // ---------------------------------------------------------------
    public class OffsetGenDialog : Form
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        TextBox txtAp, txtFp, txtStaDiv, txtStaStartNo, txtStaEndNo;
        Label lblSpacing;
        string lastDivText = "20";
        HintTextBox txtStaCustom;
        TextBox txtWlStart, txtWlEnd, txtWlStep;
        HintTextBox txtWlExtra;
        TextBox txtBtStart, txtBtEnd, txtBtStep;
        HintTextBox txtBtExtra;
        TextBox txtTextH, txtShip, txtScale;
        NumericUpDown numDecimals;
        ComboBox cmbFrom, cmbTo;
        Label lblFactor;
        string lastDefaultTextH = "0";
        string modelUnitLabel = "";
        CheckBox chkCsv, chkDxf, chkLines, chkDeck, chkButtock;
        CheckBox chkShowSta, chkShowWl, chkShowBt, chkSmooth, chkExact;
        RadioButton rbFpOrigin, rbApOrigin;
        TextBox txtDepth, txtUpperDepth, txtDraft;

        public List<double> Stations = new List<double>();
        public List<double> Waterlines = new List<double>();
        public List<double> Buttocks = new List<double>();
        public bool UseButtocks = true;
        public int Decimals = 0;
        public bool WriteCsv = true;
        public bool WriteDxf = true;
        public bool IncludeLines = true;
        public bool DeckLine = true;
        public bool SmoothButtocks = true;
        public bool ExactButtocks = true;
        public bool FpOrigin = true;           // 스테이션 번호: FP 원점(FP = 0, 선미 쪽으로 증가) / AP 원점(AP = 0, 선수 쪽으로 증가)
        public double? DepthM;                 // 제원 (미터)
        public double? UpperDepthM;
        public double? DraftM;
        public bool ShowStationCurves = true;
        public bool ShowWaterlineCurves = true;
        public bool ShowButtockCurves = true;
        public string FromUnit = "m";          // 현재 단위 (입력값/모델 좌표가 뜻하는 단위): m, cm, mm
        public string ToUnit = "mm";           // 쓰기 단위 (CSV/DXF 출력 단위)
        public double TextHeight = 0.0;        // 글자 높이 (출력 단위), 0 = 자동
        public double ApX = 0.0;               // 입력값은 모델 단위
        public double FpX = 200.0;
        public double StationSpacing = 10.0;   // 스테이션 1개의 x 길이
        public string ShipName = "";
        public string ScaleText = "1:100";

        // bbox: 선택한 선체의 바운딩 박스. 유효하면 입력 창 초기값을 선체 치수로 채움
        public OffsetGenDialog(string modelUnitName, BoundingBox bbox)
        {
            Text = "JPOffsets - 오프셋 테이블 / 선도 생성";
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Malgun Gothic", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(600, 780);
            modelUnitLabel = modelUnitName;

            var tabs = new TabControl { Left = 8, Top = 8, Width = 584, Height = 716 };
            var tabA = new TabPage("단면 정의");
            var tabB = new TabPage("출력 / 선도");
            tabs.TabPages.Add(tabA);
            tabs.TabPages.Add(tabB);
            Controls.Add(tabs);

            // ===== 탭 1: 단면 정의 (입력값은 모두 모델 단위) =====
            int y = 8;
            const int GW = 560;   // 그룹 상자 폭

            var gbSta = new GroupBox { Text = "스테이션 (A.P.~F.P. 를 분할 수로 등분)", Left = 6, Top = y, Width = GW, Height = 258 };
            tabA.Controls.Add(gbSta);
            AddLabel("A.P. 위치", 14, 30, gbSta);
            txtAp = AddText("0", 92, 27, 90, gbSta);
            AddLabel("F.P. 위치", 200, 30, gbSta);
            txtFp = AddText("200", 278, 27, 90, gbSta);
            AddLabel("분할 수", 390, 30, gbSta);
            txtStaDiv = AddText("20", 450, 27, 80, gbSta);
            var lblHint = new Label { Text = "A.P., F.P. 위치 = 길이 방향(x) 좌표 (모델 단위)", Left = 14, Top = 58, AutoSize = true, ForeColor = Color.Gray };
            gbSta.Controls.Add(lblHint);
            lblSpacing = new Label { Left = 14, Top = 82, AutoSize = true };
            gbSta.Controls.Add(lblSpacing);

            AddLabel("스테이션 번호 원점", 14, 112, gbSta);
            rbFpOrigin = new RadioButton { Text = "FP 원점  (FP = 0, FP → AP 로 번호 증가)", Left = 30, Top = 134, Width = 500, Checked = true };
            rbApOrigin = new RadioButton { Text = "AP 원점  (AP = 0, AP → FP 로 번호 증가)", Left = 30, Top = 158, Width = 500 };
            gbSta.Controls.Add(rbFpOrigin);
            gbSta.Controls.Add(rbApOrigin);

            AddLabel("시작 St.", 14, 192, gbSta);
            txtStaStartNo = AddText("0", 80, 189, 70, gbSta);
            AddLabel("끝 St.", 170, 192, gbSta);
            txtStaEndNo = AddText("20", 218, 189, 70, gbSta);
            AddLabel("추가 스테이션 번호 (쉼표 구분)", 14, 220, gbSta);
            txtStaCustom = new HintTextBox { Hint = "예: -0.5, 0.5, 19.5, 20.5", Text = "-0.5, 0.5, 1.5, 18.5, 19.5", Left = 250, Top = 217, Width = 290 };
            gbSta.Controls.Add(txtStaCustom);
            txtAp.TextChanged += delegate { UpdateSpacingLabel(); };
            txtFp.TextChanged += delegate { UpdateSpacingLabel(); };
            txtStaDiv.TextChanged += delegate
            {
                // 끝 St.가 직전 분할 수와 같았다면(=끝 스테이션) 새 분할 수를 따라감
                if (txtStaEndNo != null && txtStaEndNo.Text.Trim() == lastDivText) txtStaEndNo.Text = txtStaDiv.Text.Trim();
                lastDivText = txtStaDiv.Text.Trim();
                UpdateSpacingLabel();
            };
            UpdateSpacingLabel();
            y += 266;

            var gbWl = new GroupBox { Text = "워터라인 W.L. (z, 높이 방향)", Left = 6, Top = y, Width = GW, Height = 124 };
            tabA.Controls.Add(gbWl);
            AddLabel("시작", 14, 30, gbWl);
            txtWlStart = AddText("1", 54, 27, 90, gbWl);
            AddLabel("끝", 168, 30, gbWl);
            txtWlEnd = AddText("23", 194, 27, 90, gbWl);
            AddLabel("간격", 308, 30, gbWl);
            txtWlStep = AddText("1", 348, 27, 90, gbWl);
            AddLabel("추가 값 (쉼표 구분, 위 범위에 추가)", 14, 64, gbWl);
            txtWlExtra = new HintTextBox { Hint = "예: 0.5, 2.5", Text = "0.5", Left = 14, Top = 86, Width = 530 };
            gbWl.Controls.Add(txtWlExtra);
            y += 132;

            var gbBt = new GroupBox { Text = "버톡 B.T. (y, 폭 방향, 중심선에서의 거리)", Left = 6, Top = y, Width = GW, Height = 124 };
            tabA.Controls.Add(gbBt);
            AddLabel("시작", 14, 30, gbBt);
            txtBtStart = AddText("2", 54, 27, 90, gbBt);
            AddLabel("끝", 168, 30, gbBt);
            txtBtEnd = AddText("16", 194, 27, 90, gbBt);
            AddLabel("간격", 308, 30, gbBt);
            txtBtStep = AddText("1", 348, 27, 90, gbBt);
            chkButtock = new CheckBox { Text = "사용", Left = 462, Top = 27, Width = 80, Checked = true };
            gbBt.Controls.Add(chkButtock);
            AddLabel("추가 값 (쉼표 구분, 위 범위에 추가)", 14, 64, gbBt);
            txtBtExtra = new HintTextBox { Hint = "예: 0.5, 1, 1.5", Text = "0.5, 1, 1.5", Left = 14, Top = 86, Width = 530 };
            gbBt.Controls.Add(txtBtExtra);
            y += 132;

            AddLabel("소수점 자리수", 14, y + 4, tabA);
            numDecimals = AddNum(0, 0, 8, 118, y, 70, tabA);   // 쓰기 단위 mm 기본이므로 정수 (m로 바꾸면 3으로 자동 변경)
            AddLabel("입력값 단위 = 현재 단위 (탭 2에서 선택)", 210, y + 4, tabA);

            // ===== 탭 2: 출력 / 선도 =====
            y = 8;

            chkShowSta = new CheckBox { Text = "스테이션 커브", Left = 8, Top = y, Width = 170, Checked = true };
            chkShowWl = new CheckBox { Text = "워터라인 커브", Left = 190, Top = y, Width = 170, Checked = true };
            chkShowBt = new CheckBox { Text = "버톡 커브", Left = 372, Top = y, Width = 170, Checked = true };
            tabB.Controls.Add(chkShowSta);
            tabB.Controls.Add(chkShowWl);
            tabB.Controls.Add(chkShowBt);
            AddLabel("위 항목: 라이노에 단면 커브를 레이어로 추가", 8, y + 30, tabB);
            y += 62;

            var gbOut = new GroupBox { Text = "파일 출력", Left = 6, Top = y, Width = GW, Height = 170 };
            tabB.Controls.Add(gbOut);
            chkCsv = new CheckBox { Text = "CSV", Left = 14, Top = 28, Width = 70, Checked = true };
            chkDxf = new CheckBox { Text = "DXF", Left = 92, Top = 28, Width = 70, Checked = true };
            gbOut.Controls.Add(chkCsv);
            gbOut.Controls.Add(chkDxf);
            AddLabel("글자 높이 (0 = 자동)", 250, 31, gbOut);
            txtTextH = AddText("0", 400, 27, 80, gbOut);
            chkLines = new CheckBox { Text = "DXF에 선도 포함 (Body / Half-breadth / Sheer plan)", Left = 14, Top = 60, Width = 520, Checked = true };
            gbOut.Controls.Add(chkLines);
            AddLabel("현재 단위", 14, 100, gbOut);
            cmbFrom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 90, Top = 96, Width = 70 };
            AddLabel("→ 쓰기 단위", 180, 100, gbOut);
            cmbTo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 270, Top = 96, Width = 70 };
            foreach (string u in new[] { "m", "cm", "mm" })
            {
                cmbFrom.Items.Add(u);
                cmbTo.Items.Add(u);
            }
            string detected = UnitFromRhino(modelUnitName);
            cmbFrom.SelectedIndex = detected == "cm" ? 1 : (detected == "mm" ? 2 : 0);   // 라이노 모델 단위로 초기화
            cmbTo.SelectedIndex = 2;                                                       // mm
            txtTextH.Text = lastDefaultTextH;
            cmbFrom.SelectedIndexChanged += OnUnitChanged;
            cmbTo.SelectedIndexChanged += OnUnitChanged;
            gbOut.Controls.Add(cmbFrom);
            gbOut.Controls.Add(cmbTo);
            lblFactor = new Label { Left = 14, Top = 134, AutoSize = true };
            gbOut.Controls.Add(lblFactor);
            UpdateFactorLabel();
            y += 178;

            var gbLn = new GroupBox { Text = "선도 옵션 / 제원", Left = 6, Top = y, Width = GW, Height = 250 };
            tabB.Controls.Add(gbLn);
            chkDeck = new CheckBox { Text = "갑판선(Sheer line) / 갑판 가장자리 그리기", Left = 14, Top = 28, Width = 520, Checked = true };
            gbLn.Controls.Add(chkDeck);
            chkSmooth = new CheckBox { Text = "버톡 라인 스무딩 (기존 방식으로 대체된 버톡에만 적용)", Left = 14, Top = 54, Width = 520, Checked = true };
            gbLn.Controls.Add(chkSmooth);
            chkExact = new CheckBox { Text = "버톡 정밀 추적 (벌브 선수 대응, 권장)", Left = 14, Top = 80, Width = 520, Checked = true };
            gbLn.Controls.Add(chkExact);
            AddLabel("선명", 14, 118, gbLn);
            txtShip = AddText("", 60, 115, 200, gbLn);
            AddLabel("척도", 290, 118, gbLn);
            txtScale = AddText("1:100", 336, 115, 90, gbLn);
            AddLabel("제원 (m, 비우면 도면에 쓰지 않음)", 14, 152, gbLn);
            AddLabel("DEPTH TO MAIN DK", 14, 182, gbLn);
            txtDepth = AddText("", 160, 179, 90, gbLn);
            AddLabel("DEPTH TO UPPER DK", 280, 182, gbLn);
            txtUpperDepth = AddText("", 430, 179, 100, gbLn);
            AddLabel("DRAFT DESIGN", 14, 214, gbLn);
            txtDraft = AddText("", 160, 211, 90, gbLn);

            // ===== 버튼 =====
            var ok = new Button { Text = "생성", Left = 400, Top = 738, Width = 90, Height = 30 };
            var cancel = new Button { Text = "취소", Left = 500, Top = 738, Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            ok.Click += OnOk;
            Controls.Add(ok);
            Controls.Add(cancel);
            Controls.Add(new Label { Text = PlugInInfo.Credit, Left = 12, Top = 746, AutoSize = true, ForeColor = Color.Gray });
            AcceptButton = ok;
            CancelButton = cancel;

            ApplyHullDefaults(bbox);
        }

        // 선체 바운딩 박스로 초기값 채움: A.P./F.P. = x 최소/최대, 수선 = 1 m 간격으로 최고 높이까지,
        // 버톡 = 2 m 부터 1 m 간격으로 최대 반폭까지 (추가 값 0.5, 1, 1.5 m). 값은 모두 현재 단위(모델 단위)로 환산
        void ApplyHullDefaults(BoundingBox bb)
        {
            if (!bb.IsValid) return;
            double um = UnitMeters(cmbFrom.SelectedItem.ToString());
            if (um <= 0) return;
            double perM = 1.0 / um;                                   // 1 m 가 현재 단위로 얼마인가
            double length = bb.Max.X - bb.Min.X;
            if (length <= 0) return;

            txtAp.Text = N(Math.Round(bb.Min.X, 3));
            txtFp.Text = N(Math.Round(bb.Max.X, 3));

            double topM = Math.Max(bb.Max.Z, 0.0) * um;
            int wlEnd = (int)Math.Floor(topM + 1e-9);
            if (wlEnd >= 1)
            {
                txtWlStart.Text = N(perM);
                txtWlEnd.Text = N(wlEnd * perM);
                txtWlStep.Text = N(perM);
                txtWlExtra.Text = N(0.5 * perM);
            }

            double halfBeamM = Math.Max(Math.Abs(bb.Min.Y), Math.Abs(bb.Max.Y)) * um;
            int btEnd = (int)Math.Floor(halfBeamM + 1e-9);
            if (btEnd >= 2)
            {
                txtBtStart.Text = N(2 * perM);
                txtBtEnd.Text = N(btEnd * perM);
                txtBtStep.Text = N(perM);
                txtBtExtra.Text = N(0.5 * perM) + ", " + N(perM) + ", " + N(1.5 * perM);
            }
            UpdateSpacingLabel();
        }

        static string N(double v)
        {
            return v.ToString("0.####", Inv);
        }

        // 글자 높이가 기본값 그대로면 쓰기 단위에 맞는 기본값(2.5 m에 해당하는 값)으로 바꿈
        void OnUnitChanged(object sender, EventArgs e)
        {

            // 소수 자리수: 기본값(mm 0자리 / 그 외 3자리)일 때만 쓰기 단위에 맞춰 바꿈
            string to = cmbTo.SelectedItem.ToString();
            if (numDecimals != null && (numDecimals.Value == 0 || numDecimals.Value == 3))
                numDecimals.Value = to == "mm" ? 0 : 3;
            UpdateFactorLabel();
        }

        void UpdateFactorLabel()
        {
            double f = UnitMeters(cmbFrom.SelectedItem.ToString()) / UnitMeters(cmbTo.SelectedItem.ToString());
            lblFactor.Text = "곱하는 배율: x" + N(f) + "   (라이노 모델 단위: " + modelUnitLabel + ")";
        }

        // 단위 -> 미터 환산값 (m=1, cm=0.01, mm=0.001)
        public static double UnitMeters(string u)
        {
            switch (u)
            {
                case "mm": return 0.001;
                case "cm": return 0.01;
                default: return 1.0;
            }
        }

        // 라이노 UnitSystem 이름 -> m/cm/mm (지원하지 않는 단위면 null)
        public static string UnitFromRhino(string rhinoName)
        {
            switch (rhinoName)
            {
                case "Meters": return "m";
                case "Centimeters": return "cm";
                case "Millimeters": return "mm";
                default: return null;
            }
        }

        // AP, FP, 분할 수로 계산되는 스테이션 간격을 보여줌
        void UpdateSpacingLabel()
        {
            double ap, fp, div;
            if (double.TryParse(txtAp.Text, NumberStyles.Float, Inv, out ap) &&
                double.TryParse(txtFp.Text, NumberStyles.Float, Inv, out fp) &&
                double.TryParse(txtStaDiv.Text, NumberStyles.Float, Inv, out div) && fp > ap && div > 0)
                lblSpacing.Text = "스테이션 간격 = (F.P. - A.P.) / 분할 수 = " + N((fp - ap) / div) + "   (Lpp = " + N(fp - ap) + ")";
            else
                lblSpacing.Text = "스테이션 간격 = (F.P. - A.P.) / 분할 수";
        }

        void OnOk(object sender, EventArgs e)
        {
            double ap, fp, spacing, stStart, stEnd;
            if (!double.TryParse(txtAp.Text, NumberStyles.Float, Inv, out ap) ||
                !double.TryParse(txtFp.Text, NumberStyles.Float, Inv, out fp) || fp <= ap)
            {
                MessageBox.Show("A.P., F.P. 위치를 확인하세요. (F.P. > A.P.)");
                return;
            }
            double div;
            if (!double.TryParse(txtStaDiv.Text, NumberStyles.Float, Inv, out div) || div <= 0)
            {
                MessageBox.Show("분할 수를 확인하세요. (> 0, AP~FP를 나누는 구간 수)");
                return;
            }
            spacing = (fp - ap) / div;   // 스테이션 1개의 x 길이 = (FP - AP) / 분할 수
            if (!double.TryParse(txtStaStartNo.Text, NumberStyles.Float, Inv, out stStart) ||
                !double.TryParse(txtStaEndNo.Text, NumberStyles.Float, Inv, out stEnd) || stEnd < stStart)
            {
                MessageBox.Show("시작/끝 스테이션 번호를 확인하세요. (시작 <= 끝)");
                return;
            }
            ApX = ap;
            FpX = fp;
            StationSpacing = spacing;

            // 간격 스테이션 번호: 시작, 시작+1, ... (끝 이하)
            var numbers = new List<double>();
            int staCount = (int)Math.Floor(stEnd - stStart + 1e-9) + 1;
            if (staCount > 2000)
            {
                MessageBox.Show("스테이션이 너무 많습니다. 시작/끝 번호를 확인하세요.");
                return;
            }
            for (int i = 0; i < staCount; i++) numbers.Add(stStart + i);

            // 추가 스테이션 번호
            if (!string.IsNullOrWhiteSpace(txtStaCustom.Text))
            {
                var parts = txtStaCustom.Text.Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    double v;
                    if (!double.TryParse(part, NumberStyles.Float, Inv, out v))
                    {
                        MessageBox.Show("추가 스테이션 번호를 확인하세요: " + part);
                        return;
                    }
                    numbers.Add(v);
                }
            }

            // 작은 번호 -> 큰 번호로 정렬하고 중복 제거 후 x 위치로 변환
            numbers.Sort();
            Stations = new List<double>();
            double lastNo = double.NaN;
            foreach (double no in numbers)
            {
                if (!double.IsNaN(lastNo) && Math.Abs(no - lastNo) < 1e-6) continue;
                Stations.Add(rbFpOrigin.Checked ? fp - no * spacing : ap + no * spacing);
                lastNo = no;
            }

            // 워터라인: 범위 + 추가 값 (오름차순, 중복 제거)
            Waterlines = ParseRange(txtWlExtra.Text, txtWlStart.Text, txtWlEnd.Text, txtWlStep.Text);
            if (Waterlines == null || Waterlines.Count == 0)
            {
                MessageBox.Show("워터라인 값을 확인하세요. (시작 <= 끝, 간격 > 0, 추가 값은 숫자)");
                return;
            }

            // 버톡: 범위 + 추가 값 (오름차순, 중복 제거)
            UseButtocks = chkButtock.Checked;
            if (UseButtocks)
            {
                Buttocks = ParseRange(txtBtExtra.Text, txtBtStart.Text, txtBtEnd.Text, txtBtStep.Text);
                if (Buttocks == null || Buttocks.Count == 0)
                {
                    MessageBox.Show("버톡 값을 확인하세요. (시작 <= 끝, 간격 > 0, 추가 값은 숫자) 버톡을 쓰지 않으려면 '사용'을 해제하세요.");
                    return;
                }
            }
            else
            {
                Buttocks = new List<double>();
            }

            WriteCsv = chkCsv.Checked;
            WriteDxf = chkDxf.Checked;
            if (!WriteCsv && !WriteDxf)
            {
                MessageBox.Show("CSV 또는 DXF 중 하나 이상 선택하세요.");
                return;
            }

            double h;
            if (!double.TryParse(txtTextH.Text, NumberStyles.Float, Inv, out h) || h < 0)
            {
                MessageBox.Show("글자 높이를 확인하세요. (0 = 자동, 또는 쓰기 단위의 길이)");
                return;
            }
            TextHeight = h;

            FromUnit = cmbFrom.SelectedItem.ToString();
            ToUnit = cmbTo.SelectedItem.ToString();
            IncludeLines = chkLines.Checked;
            DeckLine = chkDeck.Checked;
            SmoothButtocks = chkSmooth.Checked;
            ExactButtocks = chkExact.Checked;
            FpOrigin = rbFpOrigin.Checked;
            double dv;
            DepthM = double.TryParse(txtDepth.Text, NumberStyles.Float, Inv, out dv) && dv > 0 ? (double?)dv : null;
            UpperDepthM = double.TryParse(txtUpperDepth.Text, NumberStyles.Float, Inv, out dv) && dv > 0 ? (double?)dv : null;
            DraftM = double.TryParse(txtDraft.Text, NumberStyles.Float, Inv, out dv) && dv > 0 ? (double?)dv : null;
            ShipName = txtShip.Text;
            ScaleText = txtScale.Text;
            ShowStationCurves = chkShowSta.Checked;
            ShowWaterlineCurves = chkShowWl.Checked;
            ShowButtockCurves = chkShowBt.Checked;
            Decimals = (int)numDecimals.Value;
            DialogResult = DialogResult.OK;
            Close();
        }

        // 범위(시작/끝/간격)와 추가 값을 합쳐 오름차순 정렬, 중복 제거.
        // 범위 칸이 모두 비어 있으면 추가 값만 사용. 형식 오류면 null.
        static List<double> ParseRange(string extra, string start, string end, string step)
        {
            var result = new List<double>();

            bool anyRange = !(string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(end) &&
                              string.IsNullOrWhiteSpace(step));
            if (anyRange)
            {
                double a, b, d;
                if (!double.TryParse(start, NumberStyles.Float, Inv, out a)) return null;
                if (!double.TryParse(end, NumberStyles.Float, Inv, out b)) return null;
                if (!double.TryParse(step, NumberStyles.Float, Inv, out d)) return null;
                if (d <= 0 || b < a) return null;

                // 끝값이 간격의 정확한 배수가 아니면 끝값은 포함되지 않음
                int n = (int)Math.Floor((b - a) / d + 1e-9);
                if (n > 5000) return null;
                for (int i = 0; i <= n; i++) result.Add(a + d * i);
            }

            if (!string.IsNullOrWhiteSpace(extra))
            {
                var parts = extra.Split(new[] { ' ', ',', ';', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    double v;
                    if (!double.TryParse(part, NumberStyles.Float, Inv, out v)) return null;
                    result.Add(v);
                }
            }

            result.Sort();
            for (int k = result.Count - 1; k > 0; k--)
                if (Math.Abs(result[k] - result[k - 1]) < 1e-9) result.RemoveAt(k);
            return result;
        }

        void AddLabel(string text, int x, int y, Control parent)
        {
            parent.Controls.Add(new Label { Text = text, Left = x, Top = y, AutoSize = true });
        }

        TextBox AddText(string text, int x, int y, int w, Control parent)
        {
            var tb = new TextBox { Text = text, Left = x, Top = y, Width = w };
            parent.Controls.Add(tb);
            return tb;
        }

        NumericUpDown AddNum(int val, int min, int max, int x, int y, int w, Control parent)
        {
            var n = new NumericUpDown { Minimum = min, Maximum = max, Value = val, Left = x, Top = y, Width = w };
            parent.Controls.Add(n);
            return n;
        }
    }

    // ---------------------------------------------------------------
    // 커브 유틸
    // ---------------------------------------------------------------
    public static class CurveUtil
    {
        // 한쪽 현 기준: 더 큰 쪽의 y 부호를 양(+)으로 사용
        public static double YSign(BoundingBox bb)
        {
            return Math.Abs(bb.Min.Y) > Math.Abs(bb.Max.Y) ? -1.0 : 1.0;
        }

        // 모든 Brep과 평면의 교차 커브
        public static List<Curve> SectionCurves(List<Brep> breps, Plane plane, double tol)
        {
            var result = new List<Curve>();
            foreach (var brep in breps)
            {
                Curve[] curves;
                Point3d[] pts;
                if (Rhino.Geometry.Intersect.Intersection.BrepPlane(brep, plane, tol, out curves, out pts)
                    && curves != null)
                    result.AddRange(curves);
            }
            return result;
        }

        // 커브들을 점 목록으로 바꿈. clip이 true면 양현 모델의 반대쪽(y<0 쪽)을 잘라냄.
        public static List<List<Point3d>> Prepare(IEnumerable<Curve> curves, double ySign, double tol, bool clip)
        {
            var result = new List<List<Point3d>>();
            if (curves == null) return result;

            foreach (var c in curves)
            {
                var pts = CurveToPoints(c, tol);
                if (pts.Count < 2) continue;
                if (clip) ClipPositiveSide(result, pts, ySign, tol);
                else result.Add(pts);
            }
            return result;
        }

        public static List<Point3d> CurveToPoints(Curve c, double tol)
        {
            var pts = new List<Point3d>();

            // 각도 3도 기준으로 폴리라인화
            var pc = c.ToPolyline(0, 0, 0.0523599, 0.0, 0.0, tol, 0.0, 0.0, true);
            if (pc != null)
            {
                Polyline pl;
                if (pc.TryGetPolyline(out pl))
                    foreach (var p in pl) pts.Add(p);
            }

            // 실패하면 등분 점으로 대체
            if (pts.Count < 2)
            {
                pts.Clear();
                double[] t = c.DivideByCount(80, true);
                if (t != null)
                    foreach (double ti in t) pts.Add(c.PointAt(ti));
            }
            return pts;
        }

        // 폴리라인에서 y*ySign >= 0 쪽만 남김 (중심선을 가로지르는 구간은 y=0에서 자름)
        static void ClipPositiveSide(List<List<Point3d>> output, List<Point3d> pts, double ySign, double tol)
        {
            var cur = new List<Point3d>();
            for (int i = 0; i < pts.Count; i++)
            {
                Point3d p = pts[i];
                double sp = p.Y * ySign;
                bool pin = sp >= -tol;

                if (i == 0)
                {
                    if (pin) cur.Add(p);
                    continue;
                }

                Point3d q = pts[i - 1];
                double sq = q.Y * ySign;
                bool qin = sq >= -tol;

                if (pin && qin)
                {
                    cur.Add(p);
                }
                else if (pin && !qin)
                {
                    cur.Add(CrossAtZero(q, p, sq, sp));
                    cur.Add(p);
                }
                else if (!pin && qin)
                {
                    cur.Add(CrossAtZero(q, p, sq, sp));
                    if (cur.Count >= 2) output.Add(cur);
                    cur = new List<Point3d>();
                }
            }
            if (cur.Count >= 2) output.Add(cur);
        }

        static Point3d CrossAtZero(Point3d q, Point3d p, double sq, double sp)
        {
            double d = sq - sp;
            double t = Math.Abs(d) < 1e-12 ? 0.0 : sq / d;
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;
            return new Point3d(q.X + t * (p.X - q.X), q.Y + t * (p.Y - q.Y), q.Z + t * (p.Z - q.Z));
        }

        // 조밀한 스테이션(n+1개)의 단면을 폴리라인으로 미리 만들어 둠 (윤곽/버톡 계산에 공통 사용)
        public static List<DenseSection> ComputeDenseSections(List<Brep> breps, BoundingBox bb,
                                                              double ySign, double tol, int n)
        {
            var res = new List<DenseSection>();
            double len = bb.Max.X - bb.Min.X;
            if (len <= 0 || n < 1) return res;

            for (int i = 0; i <= n; i++)
            {
                double x = bb.Min.X + len * i / n;
                var secs = SectionCurves(breps, new Plane(new Point3d(x, 0, 0), Vector3d.XAxis), tol);
                if (secs.Count == 0) continue;

                var polys = Prepare(secs, ySign, tol, true);
                if (polys.Count == 0) continue;
                res.Add(new DenseSection { X = x, Curves = secs, Polys = polys });
            }
            return res;
        }

        // 단면의 최상단(갑판 가장자리)과 최저점을 뽑음.
        // 갑판 가장자리 = 단면에서 가장 높은 점들 중 중심선에서 가장 먼 점 (추정)
        public static List<OutlinePoint> ComputeOutline(List<DenseSection> dense, BoundingBox bb,
                                                        double ySign, double tol)
        {
            var res = new List<OutlinePoint>();
            double dz = Math.Max(tol * 5.0, (bb.Max.Z - bb.Min.Z) * 0.002);

            foreach (var ds in dense)
            {
                bool any = false;
                double zTop = double.MinValue, zBot = double.MaxValue;
                foreach (var pl in ds.Polys)
                    foreach (var p in pl)
                    {
                        any = true;
                        if (p.Z > zTop) zTop = p.Z;
                        if (p.Z < zBot) zBot = p.Z;
                    }
                if (!any) continue;

                double yTop = 0.0;
                foreach (var pl in ds.Polys)
                    foreach (var p in pl)
                        if (p.Z >= zTop - dz)
                        {
                            double yy = p.Y * ySign;
                            if (yy > yTop) yTop = yy;
                        }

                res.Add(new OutlinePoint { X = ds.X, YTop = yTop, ZTop = zTop, ZBot = zBot });
            }
            return res;
        }

        // 단면 커브들과 수직선(y = yPlane, x 고정)의 정확한 교차점 중 가장 낮은 z. 교차가 없으면 null.
        // (폴리라인 근사 오차가 섞이지 않게 원본 커브로 교차시킴)
        static double? BottomZ(List<Curve> curves, double x, double yPlane, double zLo, double zHi, double tol)
        {
            bool found = false;
            double zMin = 0.0;
            var vline = new Line(new Point3d(x, yPlane, zLo), new Point3d(x, yPlane, zHi));

            foreach (var c in curves)
            {
                var events = Rhino.Geometry.Intersect.Intersection.CurveLine(c, vline, tol, tol);
                if (events == null) continue;
                foreach (var ev in events)
                {
                    double za = ev.PointA.Z;
                    if (!found || za < zMin) { zMin = za; found = true; }
                    if (ev.IsOverlap && ev.PointA2.Z < zMin) zMin = ev.PointA2.Z;
                }
            }
            return found ? (double?)zMin : null;
        }

        // 버톡 라인: 조밀한 단면(200구간)에서 y = b 수직선과의 정확한 교차점 중 가장 낮은 z를 이어서 만듦.
        // [내삽 개선] 200구간 격자만 직선으로 잇지 않고, 이웃 표본의 z 차이가 큰 구간(벌브 선수 끝 등)과
        //  선체 끝 가장자리는 단면을 새로 만들어 이분 분할로 촘촘히 채움 (격자 한 칸을 직선으로 가로지르지 않게).
        // 단면이 b까지 못 미치는 구간은 끊김. secCache: x -> 단면 커브 (버톡들이 공유해 중복 계산을 피함)
        public static List<List<Point3d>> ComputeButtockLines(List<Brep> breps, List<DenseSection> dense, BoundingBox bb,
                                                              double b, double ySign, double tol, double zLo, double zHi,
                                                              Dictionary<double, List<Curve>> secCache)
        {
            var result = new List<List<Point3d>>();
            double yPlane = b * ySign;
            double len = bb.Max.X - bb.Min.X;
            double hullH = bb.Max.Z - bb.Min.Z;
            if (len <= 0 || hullH <= 0) return result;

            // 초기 표본: ComputeDenseSections 와 같은 격자 (200구간). 단면이 없던 x는 값 없음(null)
            const int n = 200;
            var xs = new List<double>();
            var zs = new List<double?>();
            double eps = len * 1e-9;
            int di = 0;
            for (int i = 0; i <= n; i++)
            {
                double x = bb.Min.X + len * i / n;
                while (di < dense.Count && dense[di].X < x - eps) di++;
                double? z = null;
                if (di < dense.Count && Math.Abs(dense[di].X - x) <= eps)
                    z = BottomZ(dense[di].Curves, x, yPlane, zLo, zHi, tol);
                xs.Add(x); zs.Add(z);
            }

            AdaptiveSampler.Eval f = delegate (double x)
            {
                List<Curve> secs;
                if (!secCache.TryGetValue(x, out secs))
                {
                    secs = SectionCurves(breps, new Plane(new Point3d(x, 0, 0), Vector3d.XAxis), tol);
                    secCache[x] = secs;
                }
                if (secs.Count == 0) return null;
                return BottomZ(secs, x, yPlane, zLo, zHi, tol);
            };

            // z 차이가 선체 높이의 0.4%를 넘으면 분할, 최소 간격은 허용오차 또는 선체 길이의 0.002%
            var pieces = AdaptiveSampler.Refine(f, xs, zs, hullH * 0.004, Math.Max(tol, len * 2e-5), 600);
            foreach (var pc in pieces)
            {
                var line = new List<Point3d>();
                foreach (var p in pc) line.Add(new Point3d(p.X, yPlane, p.Y));
                result.Add(line);
            }
            return result;
        }

        // z만 가볍게 평활화 (선도/표시용 - 표의 값은 그대로 둠). 양 끝점은 고정.
        // [개선] 점 간격이 불균등해도 맞도록 이웃 두 점을 x 비율로 보간한 값과 평균하고(균등 간격이면 [1 2 1]/4와 같음),
        //  가파른 구간(기울기 > 0.5)의 점은 고정해서 벌브처럼 급변하는 곡률이 뭉개지지 않게 함.
        public static void SmoothZ(List<Point3d> pts, int passes)
        {
            if (pts == null || pts.Count < 3) return;
            int n = pts.Count;

            var fix = new bool[n];
            for (int i = 1; i < n - 1; i++)
            {
                double dxa = Math.Abs(pts[i].X - pts[i - 1].X), dza = Math.Abs(pts[i].Z - pts[i - 1].Z);
                double dxb = Math.Abs(pts[i + 1].X - pts[i].X), dzb = Math.Abs(pts[i + 1].Z - pts[i].Z);
                fix[i] = dza > 0.5 * dxa || dzb > 0.5 * dxb;
            }

            var z = new double[n];
            for (int k = 0; k < passes; k++)
            {
                for (int i = 0; i < n; i++) z[i] = pts[i].Z;
                for (int i = 1; i < n - 1; i++)
                {
                    if (fix[i]) continue;
                    double span = pts[i + 1].X - pts[i - 1].X;
                    if (Math.Abs(span) < 1e-12) continue;
                    double t = (pts[i].X - pts[i - 1].X) / span;
                    double target = z[i - 1] + (z[i + 1] - z[i - 1]) * t;
                    Point3d q = pts[i];
                    pts[i] = new Point3d(q.X, q.Y, 0.5 * z[i] + 0.5 * target);
                }
            }
        }
    }

    // ---------------------------------------------------------------
    // DXF 도면 작성 (선도 + 오프셋 표 + 제원 / 표제란 + 이중 테두리/구역 표시)
    //   참고 도면(사진)과 같은 구성: 위 = [반폭 표] [Body plan] [높이 표], 중간 = Sheer plan, 아래 = Half-breadth plan,
    //   맨 아래 가운데 = 제원, 오른쪽 위 = 개정란, 오른쪽 아래 = 표제란 (그 위는 다른 정보용 빈 공간)
    //   색: 격자 초록, 곡선 노랑, 프로파일/갑판선 자홍, 라벨 시안, 중심선/기선/숫자 흰색, 탄젠트 점 빨강
    //   모든 크기는 선체 길이 u 에 대한 비율, 글자 높이 0 = 자동 (u x 0.005)
    // ---------------------------------------------------------------
    public static class DxfWriter
    {
        const string LayerGrid = "OFFSET_GRID";
        const string LayerText = "OFFSET_TEXT";
        const string LayerFrame = "OFFSET_FRAME";
        const string LayerPart = "OFFSET_PART";
        const string LayerTitle = "OFFSET_TITLE";
        const string LinesGrid = "LINES_GRID";
        const string LinesStation = "LINES_STATION";
        const string LinesWaterline = "LINES_WATERLINE";
        const string LinesButtock = "LINES_BUTTOCK";
        const string LinesProfile = "LINES_PROFILE";
        const string LinesDeck = "LINES_DECK";
        const string LinesRef = "LINES_REF";
        const string LinesLabel = "LINES_LABEL";
        const string CalloutText = "LINES_CALLOUT_TEXT";        // 빨간 라벨 글자 (이 레이어만 끄거나 지우면 됨)
        const string CalloutLeader = "LINES_CALLOUT_LEADER";    // 라벨 지시선 (주황)
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // lines가 null이면 표만 그림. textH <= 0 이면 자동 크기
        public static void Write(string path, OffsetData data, string fmt, double textH,
                                 StationNumbering num, LinesData lines, string unitName)
        {
            var b = new DxfBuilder();
            b.AddLayer(LayerGrid, 3);
            b.AddLayer(LayerText, 7);
            b.AddLayer(LayerFrame, 7);
            b.AddLayer(LayerPart, 3);
            b.AddLayer(LayerTitle, 6);
            b.AddLayer(LinesGrid, 3);
            b.AddLayer(LinesStation, 2);
            b.AddLayer(LinesWaterline, 2);
            b.AddLayer(LinesButtock, 2);
            b.AddLayer(LinesProfile, 6);
            b.AddLayer(LinesDeck, 6);
            b.AddLayer(LinesRef, 7);
            b.AddLayer(LinesLabel, 4);
            b.AddLayer(CalloutText, 1);
            b.AddLayer(CalloutLeader, 30);

            var rowLabels = new List<string>();
            foreach (double x in data.StationX)
                rowLabels.Add(num != null && num.Valid ? num.Label(x) : x.ToString(fmt, Inv));

            var t1 = OffsetTableFactory.CenterLineTable(data, rowLabels, fmt, unitName);
            var t2 = OffsetTableFactory.BaseLineTable(data, rowLabels, fmt, unitName);

            bool hasLines = lines != null && lines.Bbox.IsValid;
            if (!hasLines)
            {
                // 표만: 위아래로 쌓음
                double span = 1.0;
                if (data.StationX.Count > 1) span = Math.Abs(data.StationX[0] - data.StationX[data.StationX.Count - 1]);
                double th = textH > 0 ? textH : span / 400.0;
                double oy = 0.0;
                t1.Draw(b, 0.0, oy, th, LayerGrid, LayerText);
                oy -= t1.UnitsHeight() * th + 12.0 * th;
                t2.Draw(b, 0.0, oy, th, LayerGrid, LayerText);
                b.Save(path);
                return;
            }

            // ---------- 크기 (선체 길이 u 에 대한 비율, 참고 도면 실측) ----------
            double xMin = lines.Bbox.Min.X, xMax = lines.Bbox.Max.X;
            double zLow = Math.Min(0.0, lines.Bbox.Min.Z);          // 기선 z = 0 (그 아래로 내려간 선체가 있으면 포함)
            double zMax = lines.Bbox.Max.Z;
            double halfBeam = Math.Max(Math.Abs(lines.Bbox.Min.Y), Math.Abs(lines.Bbox.Max.Y));
            double u = xMax - xMin;
            if (halfBeam < 1e-9) halfBeam = u * 0.1;
            double bodyH = zMax - zLow;
            double um = OffsetGenDialog.UnitMeters(unitName);
            if (um <= 0) um = 1.0;

            double tH = textH > 0 ? textH : u * 0.005;               // 기본 글자 높이 (스테이션 번호 등)

            // 시안 라벨 글자 높이: 가장 촘촘한 수선 간격에 맞춰 (사진처럼 모든 선에 작은 라벨)
            double minGap = double.MaxValue;
            var wlSorted = new List<double>(data.Waterlines); wlSorted.Sort();
            for (int i = 1; i < wlSorted.Count; i++)
                if (wlSorted[i] - wlSorted[i - 1] > 1e-9) minGap = Math.Min(minGap, wlSorted[i] - wlSorted[i - 1]);
            double cyH = minGap < double.MaxValue ? 0.85 * minGap : 0.7 * tH;
            cyH = Math.Max(0.32 * tH, Math.Min(0.7 * tH, cyH));
            double labW = 10.0 * cyH + 0.5 * tH;                      // "23.0 W.L." 폭 (여유 포함)

            // 위쪽 띠: 왼쪽 표 | Body plan (라벨 포함) | 오른쪽 표
            double cxBody = xMin + 0.547 * u;
            double bodyL = cxBody - halfBeam, bodyR = cxBody + halfBeam;
            double outL = bodyL - labW - 0.6 * tH, outR = bodyR + labW + 0.6 * tH;
            double gap = 0.030 * u;                                   // 왼쪽 표 | Body plan | 오른쪽 표 사이 간격
            double tLeft = xMin - 0.012 * u;
            double availL = Math.Max(outL - gap - tLeft, u * 0.2);
            double rx = outR + gap;
            double availR = Math.Max(xMax - rx, u * 0.2);

            double thT = Math.Min(tH * 0.9, Math.Min(availL / t1.UnitsWidth(), availR / t2.UnitsWidth()));
            double bodyNeed = bodyH + 0.045 * u;                      // Body plan + 아래 제목
            double hBand = Math.Max(0.148 * u, Math.Max(bodyNeed, Math.Max(t1.UnitsHeight(), t2.UnitsHeight()) * thT));
            double bandBottom = zMax + 0.040 * u;
            double bandTop = bandBottom + hBand;
            double bodyBase = bandBottom + 0.030 * u + (hBand - bodyNeed) / 2.0;

            double planGap = 0.085 * u;                               // Sheer plan 아래 ~ Half-breadth plan 위 (스테이션 번호 + 빨간 라벨 자리)
            double yHB = zLow - planGap - halfBeam;                   // Half-breadth plan 중심선 (아래쪽)

            // ---------- 선도 ----------
            DrawLines(b, lines, data, num, fmt, u, tH, cyH, labW, zLow, bodyBase, cxBody, yHB, um);

            // ---------- 표 (두 표의 위/아래 선을 맞춤) ----------
            t1.Draw(b, tLeft, bandTop, thT, LayerGrid, LayerText, availL, hBand);
            t2.Draw(b, rx, bandTop, thT, LayerGrid, LayerText, availR, hBand);

            // ---------- 테두리 (이중) + 구역 표시 ----------
            double lineEnd = yHB - 0.012 * u;                         // 스테이션선 아래 끝
            double regionTop = lineEnd - 0.060 * u;                  // 도면과 제원 사이 마진
            double regionBottom = regionTop - 0.176 * u;
            double iL = xMin - labW - 0.025 * u, iR = xMax + labW + 0.025 * u;
            double iB = regionBottom, iT = bandTop + 0.075 * u;
            double fb = 0.011 * u;
            DrawFrame(b, iL, iB, iR, iT, fb, tH);

            // ---------- 제원 (하부 중앙) ----------
            DrawParticulars(b, lines, num, (iL + iR) / 2.0, regionTop, u, um);

            // ---------- 개정란 (오른쪽 위 모서리), 표제란 (오른쪽 아래 모서리) ----------
            DrawRevisionBox(b, iR, iT, 0.150 * u, 0.0097 * u);
            DrawTitleBlock(b, iR, iB, 0.17 * u, 0.09 * u, "Lines & Offset Plan", u * 0.0085);

            b.Save(path);
        }

        // -------------------- 이중 테두리 + 구역 표시 --------------------
        // 바깥/안쪽 사각형, 그 사이 띠에 가로 6칸(오른쪽이 1) / 세로 3칸(위가 C) 구분선과 문자
        static void DrawFrame(DxfBuilder b, double iL, double iB, double iR, double iT, double fb, double tH)
        {
            double oL = iL - fb, oR = iR + fb, oB = iB - fb, oT = iT + fb;
            Rect(b, LayerFrame, oL, oB, oR, oT);
            Rect(b, LayerFrame, iL, iB, iR, iT);

            const int nCol = 6, nRow = 3;
            double cw = (iR - iL) / nCol, rh = (iT - iB) / nRow;
            double th = fb * 0.55;
            for (int i = 0; i <= nCol; i++)
            {
                double x = iL + i * cw;
                if (i > 0 && i < nCol)
                {
                    b.Line(LayerFrame, x, iT, x, oT);
                    b.Line(LayerFrame, x, iB, x, oB);
                }
                if (i < nCol)
                {
                    string label = (nCol - i).ToString(Inv);          // 왼쪽 6 ... 오른쪽 1
                    b.Text(LayerText, x + cw / 2.0, (iT + oT) / 2.0, th, label, 1);
                    b.Text(LayerText, x + cw / 2.0, (iB + oB) / 2.0, th, label, 1);
                }
            }
            string letters = "CBA";
            for (int j = 0; j < nRow; j++)
            {
                double yTop = iT - j * rh, yBot = yTop - rh;
                if (j > 0)
                {
                    b.Line(LayerFrame, iL, yTop, oL, yTop);
                    b.Line(LayerFrame, iR, yTop, oR, yTop);
                }
                string label = letters[j].ToString();                 // 위 C, 가운데 B, 아래 A
                b.Text(LayerText, (iL + oL) / 2.0, (yTop + yBot) / 2.0, th, label, 1);
                b.Text(LayerText, (iR + oR) / 2.0, (yTop + yBot) / 2.0, th, label, 1);
            }
        }

        static void Rect(DxfBuilder b, string layer, double l, double bt, double r, double t)
        {
            b.Line(layer, l, bt, r, bt);
            b.Line(layer, r, bt, r, t);
            b.Line(layer, r, t, l, t);
            b.Line(layer, l, t, l, bt);
        }

        // -------------------- 제원 (하부 중앙, 초록 큰 글씨) --------------------
        // cx = 가운데 x, top = 제목 기준선 y. 사진: 제목(밑줄) + 항목 6줄, 값은 오른쪽 정렬, 단위 m
        static void DrawParticulars(DxfBuilder b, LinesData d, StationNumbering num, double cx, double top, double u, double um)
        {
            var rows = new List<string[]>();
            double loa = (d.Bbox.Max.X - d.Bbox.Min.X) * um;
            rows.Add(new[] { "LENGTH O.A", "approx. " + loa.ToString("0.00", Inv) });
            if (num != null && num.Valid)
                rows.Add(new[] { "LENGTH B.P", "approx. " + ((num.FpX - num.ApX) * um).ToString("0.00", Inv) });
            double beam = 2.0 * Math.Max(Math.Abs(d.Bbox.Min.Y), Math.Abs(d.Bbox.Max.Y)) * um;
            rows.Add(new[] { "BREADTH", beam.ToString("0.00", Inv) });
            if (d.DepthM.HasValue) rows.Add(new[] { "DEPTH TO MAIN DK", d.DepthM.Value.ToString("0.00", Inv) });
            if (d.UpperDepthM.HasValue) rows.Add(new[] { "DEPTH TO UPPER DK", d.UpperDepthM.Value.ToString("0.00", Inv) });
            if (d.DraftM.HasValue) rows.Add(new[] { "DRAFT DESIGN", d.DraftM.Value.ToString("0.00", Inv) });

            double hHead = 0.0135 * u, h = 0.0078 * u, pitch = 0.0125 * u;
            double width = 0.205 * u;                                // 제목 폭 (사진 비율)
            double valRight = 0.172 * u;
            double ox = cx - width / 2.0;

            b.Text(LayerPart, ox, top, hHead, "PRINCIPAL PARTICULARS", 0);
            b.Line(LayerPart, ox, top - 0.0035 * u, ox + width, top - 0.0035 * u);
            for (int i = 0; i < rows.Count; i++)
            {
                double ty = top - 0.0155 * u - i * pitch;
                b.Text(LayerPart, ox, ty, h, rows[i][0], 3);
                b.Text(LayerPart, ox + valRight, ty, h, rows[i][1], 2);
                b.Text(LayerPart, ox + valRight + 0.007 * u, ty, h, "m", 3);
            }
        }

        // -------------------- 개정란 (오른쪽 위 모서리에 붙임) --------------------
        // 사진: REVISIONS 제목 행 / ZONE REV DESCRIPTION DATE APPROVED / ALL - INITIAL ISSUE (날짜) (빈칸)
        static void DrawRevisionBox(DxfBuilder b, double right, double top, double width, double rowH)
        {
            double ox = right - width;
            double[] cols = { 0.12, 0.08, 0.34, 0.24, 0.22 };
            string[] heads = { "ZONE", "REV", "DESCRIPTION", "DATE", "APPROVED" };
            string[] vals = { "ALL", "-", "INITIAL ISSUE", DateTime.Now.ToString("yyyy/MM/dd", Inv), "" };
            double th = rowH * 0.38;
            Rect(b, LayerFrame, ox, top - 3 * rowH, right, top);
            b.Line(LayerFrame, ox, top - rowH, right, top - rowH);
            b.Line(LayerFrame, ox, top - 2 * rowH, right, top - 2 * rowH);
            b.Text(LayerText, ox + width / 2.0, top - rowH / 2.0, th, "REVISIONS", 1);
            double x = ox;
            for (int c = 0; c < cols.Length; c++)
            {
                double w = width * cols[c];
                if (c > 0) b.Line(LayerFrame, x, top - rowH, x, top - 3 * rowH);
                b.Text(LayerText, x + w / 2.0, top - 1.5 * rowH, th, heads[c], 1);
                if (vals[c].Length > 0)
                    b.Text(c == 3 ? LayerTitle : LayerText, x + w / 2.0, top - 2.5 * rowH, th, vals[c], 1);
                x += w;
            }
        }

        // -------------------- 표제란 (오른쪽 아래 모서리, 제목만) --------------------
        static void DrawTitleBlock(DxfBuilder b, double right, double bottom, double width, double height, string title, double th)
        {
            Rect(b, LayerFrame, right - width, bottom, right, bottom + height);
            b.Text(LayerTitle, right - width / 2.0, bottom + height / 2.0, th, title, 1);
        }

        // -------------------- 라벨/제목 도우미 --------------------
        // 오름차순 위치 중 앞 라벨과 minGap 이상 떨어진 것만 남김
        static List<int> ThinIndexes(List<double> pos, double minGap)
        {
            var keep = new List<int>();
            double last = double.NegativeInfinity;
            for (int i = 0; i < pos.Count; i++)
                if (pos[i] - last >= minGap) { keep.Add(i); last = pos[i]; }
            return keep;
        }

        // 가운데 정렬 시안 제목 + 밑줄 (사진의 Body plan 아래 제목)
        static void UnderlinedTitle(DxfBuilder b, double cx, double y, double h, string text)
        {
            b.Text(LinesLabel, cx, y, h, text, 1);
            double w = text.Length * 0.9 * h;
            b.Line(LinesLabel, cx - w / 2.0, y - h * 0.8, cx + w / 2.0, y - h * 0.8);
        }

        // -------------------- 빨간 라벨 도우미 --------------------
        class Callout
        {
            public double Ax, Ay;   // 곡선 끝점 (지시선이 닿는 곳)
            public double Snapped; // 라벨 기준 x (가장 가까운 스테이션선)
            public string Name;
        }

        // 조각들 중 점이 가장 많은 것 (같은 곡선의 끊어진 조각 중 대표)
        static List<Point3d> LongestPiece(List<List<Point3d>> pieces)
        {
            List<Point3d> best = null;
            if (pieces == null) return null;
            foreach (var p in pieces)
                if (p != null && p.Count >= 2 && (best == null || p.Count > best.Count)) best = p;
            return best;
        }

        static P2 PickEnd(List<Point3d> pts, double xMin, double xMax, double u)
        {
            var xy = new List<P2>();
            foreach (var p in pts) xy.Add(new P2(p.X, p.Z));
            return PickEnd(xy, xMin, xMax, u);
        }

        // 곡선 두 끝 중 선체 끝(xMin/xMax)에 걸리지 않은 쪽, 둘 다 안쪽이거나 둘 다 끝이면 선미 쪽
        static P2 PickEnd(List<P2> xy, double xMin, double xMax, double u)
        {
            P2 e0 = xy[0], e1 = xy[xy.Count - 1];
            Func<double, bool> inside = x => x > xMin + 0.01 * u && x < xMax - 0.01 * u;
            if (!inside(e0.X) && inside(e1.X)) return e1;
            return e0;
        }

        // 빨간 글자를 지정한 줄(rowYs)들에 번갈아 한 줄로 늘어놓음. 같은 줄의 라벨은 서로 겹치지 않게 오른쪽으로 밀고,
        // 곡선 끝점에서 라벨까지 주황 지시선을 그림. 라벨의 기준 x = 곡선 끝에 가장 가까운 스테이션선
        static void PlaceCallouts(DxfBuilder b, List<Callout> items, List<double> stationX, double h, double[] rowYs,
                                  double limitLeft, double limitRight)
        {
            if (items.Count == 0 || stationX.Count == 0) return;
            foreach (var it in items)
            {
                double best = stationX[0];
                foreach (double sx in stationX) if (Math.Abs(sx - it.Ax) < Math.Abs(best - it.Ax)) best = sx;
                it.Snapped = best;
            }
            items.Sort((p, q) => p.Snapped.CompareTo(q.Snapped));

            // 줄마다 왼쪽에서 오른쪽으로 겹치지 않게 배치
            int nRows = rowYs.Length;
            var rowItems = new List<Callout>[nRows];
            var rowW = new List<double>[nRows];
            var rowX = new List<double>[nRows];
            for (int r = 0; r < nRows; r++) { rowItems[r] = new List<Callout>(); rowW[r] = new List<double>(); rowX[r] = new List<double>(); }
            var right = new double[nRows];
            for (int r = 0; r < nRows; r++) right[r] = double.NegativeInfinity;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                int r = i % nRows;
                double w = (it.Name.Length * 0.9 + 0.6) * h;
                double cx = Math.Max(it.Snapped, right[r] + w / 2.0 + 0.3 * h);
                right[r] = cx + w / 2.0;
                rowItems[r].Add(it); rowW[r].Add(w); rowX[r].Add(cx);
            }

            // 줄이 도면 오른쪽 한계를 넘으면 줄 전체를 왼쪽으로 당김 (왼쪽 한계는 넘지 않게)
            for (int r = 0; r < nRows; r++)
            {
                int n = rowItems[r].Count;
                if (n == 0) continue;
                double over = rowX[r][n - 1] + rowW[r][n - 1] / 2.0 - limitRight;
                if (over > 0)
                {
                    double room = rowX[r][0] - rowW[r][0] / 2.0 - limitLeft;
                    double shift = Math.Min(over, Math.Max(room, 0.0));
                    for (int k = 0; k < n; k++) rowX[r][k] -= shift;
                }
            }

            for (int r = 0; r < nRows; r++)
                for (int k = 0; k < rowItems[r].Count; k++)
                {
                    var it = rowItems[r][k];
                    double cx = rowX[r][k], ry = rowYs[r];
                    b.Text(CalloutText, cx, ry, h, it.Name, 1);
                    double ty = (it.Ay >= ry) ? ry + 0.7 * h : ry - 0.7 * h;   // 지시선이 닿는 라벨 가장자리
                    b.Line(CalloutLeader, it.Ax, it.Ay, cx, ty);
                }
        }

        // Body plan: 곡선 위쪽 끝 바로 위에 번호를 적되, 앞 라벨과 겹치면 생략
        static void PlaceTopLabels(DxfBuilder b, List<P2> tops, List<string> names, double h)
        {
            var idx = new List<int>();
            for (int i = 0; i < tops.Count; i++) idx.Add(i);
            idx.Sort((p, q) => tops[p].X.CompareTo(tops[q].X));
            double lastRight = double.NegativeInfinity;
            foreach (int i in idx)
            {
                double w = (names[i].Length * 0.9 + 0.6) * h;
                double x = tops[i].X;
                if (x - w / 2.0 < lastRight) continue;
                lastRight = x + w / 2.0;
                b.Text(CalloutText, x, tops[i].Y + 0.9 * h, h, names[i], 1);
            }
        }

        // 시안 라벨 열 아래 끝의 "L" 꺾쇠: 기준선 위로 짧은 세로 획과 라벨 쪽으로 뻗는 발
        // dir = -1 (왼쪽 열, 발이 왼쪽으로), +1 (오른쪽 열, 발이 오른쪽으로)
        static void LBracket(DxfBuilder b, double x0, double y0, int dir, double width, double h)
        {
            b.Line(LinesLabel, x0, y0, x0, y0 + 4.0 * h);
            b.Line(LinesLabel, x0, y0, x0 + dir * width, y0);
        }

        // -------------------- 선도 --------------------
        static void DrawLines(DxfBuilder b, LinesData d, OffsetData od, StationNumbering num, string fmt,
                              double u, double tH, double cyH, double labW, double zLow,
                              double bodyBase, double cxBody, double yHB, double um)
        {
            double xMin = d.Bbox.Min.X, xMax = d.Bbox.Max.X;
            double zMax = d.Bbox.Max.Z;
            double ySign = CurveUtil.YSign(d.Bbox);
            double halfBeam = Math.Max(Math.Abs(d.Bbox.Min.Y), Math.Abs(d.Bbox.Max.Y));
            if (halfBeam < 1e-9) halfBeam = u * 0.1;

            double dyBody = bodyBase - zLow;            // Body plan 의 z -> 도면 y 이동량
            double bodyTop = zMax + dyBody;
            double bodyL = cxBody - halfBeam, bodyR = cxBody + halfBeam;
            bool numbered = num != null && num.Valid;
            double midX = numbered ? (num.ApX + num.FpX) / 2.0 : (xMin + xMax) / 2.0;   // midship (스테이션 10)
            double eps = u * 1e-6;
            double lineBottom = yHB - 0.012 * u;

            // 숫자 라벨 (미터)
            var wlM = od.WaterlinesM;
            var btM = od.ButtocksM;

            // ---- 기준선 / 중심선 (흰색) ----
            b.Line(LinesRef, xMin, 0.0, xMax, 0.0);                          // BL (Sheer plan)
            b.Line(LinesRef, xMin, yHB, xMax, yHB);                          // CL (Half-breadth plan)
            b.Line(LinesRef, cxBody, bodyBase, cxBody, bodyTop);             // CL (Body plan)
            b.Line(LinesRef, bodyL, bodyBase, bodyR, bodyBase);              // BL (Body plan)

            // ---- 바깥 테두리 격자선 (초록) ----
            b.Line(LinesGrid, xMin, zMax, xMax, zMax);                       // Sheer plan 위
            b.Line(LinesGrid, xMin, yHB + halfBeam, xMax, yHB + halfBeam);   // Half-breadth plan 위
            b.Line(LinesGrid, bodyL, bodyBase, bodyL, bodyTop);              // Body plan 좌/우/위
            b.Line(LinesGrid, bodyR, bodyBase, bodyR, bodyTop);
            b.Line(LinesGrid, bodyL, bodyTop, bodyR, bodyTop);

            // ---- 워터라인: 격자 (Sheer, Body plan) + 시안 라벨 (좌/우) ----
            var order = new List<int>();
            for (int i = 0; i < od.Waterlines.Count; i++) order.Add(i);
            order.Sort((p, q) => od.Waterlines[p].CompareTo(od.Waterlines[q]));
            var wlPos = new List<double>();
            foreach (int i in order) wlPos.Add(od.Waterlines[i]);
            foreach (double z in wlPos)
            {
                b.Line(LinesGrid, xMin, z, xMax, z);
                b.Line(LinesGrid, bodyL, z + dyBody, bodyR, z + dyBody);
            }
            foreach (int k in ThinIndexes(wlPos, cyH * 1.05))
            {
                double z = wlPos[k];
                string t = OffsetTableFactory.Head(wlM[order[k]]) + " W.L.";
                b.Text(LinesLabel, xMin - 0.4 * tH, z, cyH, t, 2);              // 왼쪽
                b.Text(LinesLabel, xMax + 0.4 * tH, z, cyH, t, 3);              // 오른쪽
                b.Text(LinesLabel, bodyL - 0.4 * tH, z + dyBody, cyH, t, 2);    // Body plan 왼쪽
                b.Text(LinesLabel, bodyR + 0.4 * tH, z + dyBody, cyH, t, 3);    // Body plan 오른쪽
            }

            // ---- 버톡: 격자 (Half-breadth, Body plan) + 시안 라벨 (좌/우) ----
            var bo = new List<int>();
            for (int i = 0; i < od.Buttocks.Count; i++) if (od.Buttocks[i] > 1e-9) bo.Add(i);
            bo.Sort((p, q) => od.Buttocks[p].CompareTo(od.Buttocks[q]));
            var btPos = new List<double>();
            foreach (int i in bo) btPos.Add(od.Buttocks[i]);
            foreach (double yb in btPos)
            {
                b.Line(LinesGrid, xMin, yHB + yb, xMax, yHB + yb);
                b.Line(LinesGrid, cxBody + yb, bodyBase, cxBody + yb, bodyTop);
                b.Line(LinesGrid, cxBody - yb, bodyBase, cxBody - yb, bodyTop);
            }
            foreach (int k in ThinIndexes(btPos, cyH * 1.05))
            {
                double yb = btPos[k];
                string t = OffsetTableFactory.Head(btM[bo[k]]) + " B.T.";
                b.Text(LinesLabel, xMin - 0.4 * tH, yHB + yb, cyH, t, 2);
                b.Text(LinesLabel, xMax + 0.4 * tH, yHB + yb, cyH, t, 3);
            }
            b.Text(LinesLabel, xMin - 0.4 * tH, yHB, cyH, "C.L.", 2);
            b.Text(LinesLabel, xMax + 0.4 * tH, yHB, cyH, "C.L.", 3);

            // ---- 스테이션선 + 번호 (두 평면 사이, 번호 자리에서 선을 끊음) ----
            var sorted = new List<double>(od.StationX);
            sorted.Sort();
            var rowEnd = new double[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
            double labelY0 = zLow - 0.020 * u;
            foreach (double x in sorted)
            {
                string label = numbered ? num.Label(x) : x.ToString(fmt, Inv);
                double wLabel = (label.Length * 0.9 + 0.6) * tH;
                int rowIdx = -1;
                for (int r = 0; r < rowEnd.Length; r++)
                    if (x - wLabel / 2.0 >= rowEnd[r]) { rowIdx = r; break; }
                if (rowIdx < 0)
                {
                    b.Line(LinesGrid, x, zMax, x, lineBottom);                   // 번호를 못 쓰면 선은 이어서
                }
                else
                {
                    rowEnd[rowIdx] = x + wLabel / 2.0;
                    double ly = labelY0 - rowIdx * 1.6 * tH;
                    b.Text(LayerText, x, ly, tH, label, 1);
                    b.Line(LinesGrid, x, zMax, x, ly + 0.85 * tH);
                    b.Line(LinesGrid, x, ly - 0.85 * tH, x, lineBottom);
                }
            }

            // ---- Half-breadth plan 아래: 스테이션 번호 (작은 흰 글자, 겹치면 줄을 바꿈) ----
            double hS = 0.7 * tH;
            var rowEnd2 = new double[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
            foreach (double x in sorted)
            {
                string label = numbered ? num.Label(x) : x.ToString(fmt, Inv);
                double wLabel = (label.Length * 0.9 + 0.6) * hS;
                for (int r = 0; r < rowEnd2.Length; r++)
                    if (x - wLabel / 2.0 >= rowEnd2[r])
                    {
                        rowEnd2[r] = x + wLabel / 2.0;
                        b.Text(LayerText, x, lineBottom - hS * (0.9 + 1.5 * r), hS, label, 1);
                        break;
                    }
            }

            // ---- Sheer plan: 프로파일 (y=0 단면), 없으면 단면 최저점으로 바닥선 대체 ----
            bool profileDrawn = false;
            foreach (var pts in CurveUtil.Prepare(d.Profile, ySign, d.Tol, false))
            {
                var pl = new List<P2>();
                foreach (var p in pts) pl.Add(new P2(p.X, p.Z));
                b.AddPolyline(LinesProfile, pl);
                profileDrawn = true;
            }
            if (!profileDrawn && d.Outline != null && d.Outline.Count >= 2)
            {
                var keel = new List<P2>();
                foreach (var o in d.Outline) keel.Add(new P2(o.X, o.ZBot));
                b.AddPolyline(LinesProfile, keel);
            }

            // ---- 갑판선(Sheer line): Sheer plan + Half-breadth plan ----
            if (d.DeckLine && d.Outline != null && d.Outline.Count >= 2)
            {
                var sheer = new List<P2>();
                var deckEdge = new List<P2>();
                foreach (var o in d.Outline)
                {
                    sheer.Add(new P2(o.X, o.ZTop));
                    deckEdge.Add(new P2(o.X, yHB + o.YTop));
                }
                b.AddPolyline(LinesDeck, sheer);
                b.AddPolyline(LinesDeck, deckEdge);
            }

            // ---- Sheer plan: 버톡 커브 ----
            if (d.Buttocks != null)
                foreach (var bs in d.Buttocks)
                    foreach (var pts in bs.Lines)
                    {
                        var pl = new List<P2>();
                        foreach (var p in pts) pl.Add(new P2(p.X, p.Z));
                        b.AddPolyline(LinesButtock, pl);
                    }

            // ---- Half-breadth plan: 워터라인 커브 ----
            foreach (var ws in d.Waterlines)
                foreach (var pts in CurveUtil.Prepare(ws.Curves, ySign, d.Tol, true))
                {
                    var pl = new List<P2>();
                    foreach (var p in pts) pl.Add(new P2(p.X, yHB + p.Y * ySign));
                    b.AddPolyline(LinesWaterline, pl);
                }

            // ---- Body plan: 스테이션 단면. midship 기준으로 선미(왼쪽)/선수(오른쪽), midship 은 양쪽 모두 ----
            foreach (var ss in d.Stations)
            {
                bool aft = ss.X <= midX + eps, fwd = ss.X >= midX - eps;
                foreach (var pts in CurveUtil.Prepare(ss.Curves, ySign, d.Tol, true))
                {
                    if (aft)
                    {
                        var pl = new List<P2>();
                        foreach (var p in pts) pl.Add(new P2(cxBody - p.Y * ySign, p.Z + dyBody));
                        b.AddPolyline(LinesStation, pl);
                    }
                    if (fwd)
                    {
                        var pl = new List<P2>();
                        foreach (var p in pts) pl.Add(new P2(cxBody + p.Y * ySign, p.Z + dyBody));
                        b.AddPolyline(LinesStation, pl);
                    }
                }
            }

            // ---- 빨간 라벨 (별도 레이어 LINES_CALLOUT_TEXT / LINES_CALLOUT_LEADER) ----
            double hC = 0.8 * tH;
            var stX = od.StationX;

            // Sheer plan: 버톡 곡선마다 이름 (곡선이 끝나는 쪽의 스테이션선 위치, 지시선으로 연결)
            var sheerItems = new List<Callout>();
            if (d.Buttocks != null)
                foreach (var bs in d.Buttocks)
                {
                    var pts = LongestPiece(bs.Lines);
                    if (pts == null) continue;
                    P2 a = PickEnd(pts, xMin, xMax, u);
                    sheerItems.Add(new Callout { Ax = a.X, Ay = a.Y, Name = OffsetTableFactory.Head(bs.Y * um) + " B.T." });
                }
            PlaceCallouts(b, sheerItems, stX, hC, new[] { zMax + 0.014 * u, zLow - 0.052 * u }, xMin - 0.5 * labW, xMax + 0.9 * labW);

            // Half-breadth plan: 수선 곡선마다 이름
            var planItems = new List<Callout>();
            foreach (var ws in d.Waterlines)
            {
                var pieces = new List<List<Point3d>>(CurveUtil.Prepare(ws.Curves, ySign, d.Tol, true));
                var pts = LongestPiece(pieces);
                if (pts == null) continue;
                var xy = new List<P2>();
                foreach (var p in pts) xy.Add(new P2(p.X, yHB + p.Y * ySign));
                P2 a = PickEnd(xy, xMin, xMax, u);
                planItems.Add(new Callout { Ax = a.X, Ay = a.Y, Name = OffsetTableFactory.Head(ws.Z * um) + " W.L." });
            }
            double planTop = yHB + halfBeam;
            PlaceCallouts(b, planItems, stX, hC, new[] { planTop + 0.010 * u, planTop + 0.020 * u }, xMin - 0.5 * labW, xMax + 0.9 * labW);

            // Body plan: 스테이션 곡선마다 스테이션 번호 (곡선 위쪽 끝, 겹치면 생략)
            double hB = 0.6 * tH;
            var leftTops = new List<P2>(); var rightTops = new List<P2>();
            var leftNames = new List<string>(); var rightNames = new List<string>();
            foreach (var ss in d.Stations)
            {
                bool aft = ss.X <= midX + eps, fwd = ss.X >= midX - eps;
                P2 top = new P2(0, double.NegativeInfinity);
                foreach (var pts in CurveUtil.Prepare(ss.Curves, ySign, d.Tol, true))
                    foreach (var p in pts)
                        if (p.Z > top.Y) top = new P2(p.Y * ySign, p.Z);
                if (double.IsNegativeInfinity(top.Y)) continue;
                string nm = numbered ? num.Label(ss.X) : ss.X.ToString(fmt, Inv);
                if (aft) { leftTops.Add(new P2(cxBody - top.X, top.Y + dyBody)); leftNames.Add(nm); }
                if (fwd) { rightTops.Add(new P2(cxBody + top.X, top.Y + dyBody)); rightNames.Add(nm); }
            }
            PlaceTopLabels(b, leftTops, leftNames, hB);
            PlaceTopLabels(b, rightTops, rightNames, hB);

            // ---- 시안 라벨 열 아래 끝의 "L" 꺾쇠 (기선 / 중심선 표시) ----
            LBracket(b, xMin - 0.4 * tH, 0.0, -1, labW, cyH);
            LBracket(b, xMax + 0.4 * tH, 0.0, +1, labW, cyH);
            LBracket(b, xMin - 0.4 * tH, yHB, -1, labW, cyH);
            LBracket(b, xMax + 0.4 * tH, yHB, +1, labW, cyH);
            LBracket(b, bodyL - 0.4 * tH, bodyBase, -1, labW, cyH);
            LBracket(b, bodyR + 0.4 * tH, bodyBase, +1, labW, cyH);

            // ---- Body plan 제목 (아래 가운데, 시안 밑줄) ----
            UnderlinedTitle(b, cxBody, bodyBase - 0.016 * u, tH * 1.1, "BODY PLAN");
        }
    }

    // ---------------------------------------------------------------
    // 명령어: OffsetGen
    // ---------------------------------------------------------------
    [System.Runtime.InteropServices.Guid("c7a21d5e-48b3-4f96-a0d2-3e5b9c8f1a47")]
    public class JPOffsetsCommand : Command
    {
        const string StationLayerName = "OffsetGen_Station";
        const string WaterlineLayerName = "OffsetGen_Waterline";
        const string ButtockLayerName = "OffsetGen_Buttock";

        public override string EnglishName { get { return "JPOffsets"; } }

        // RhinoApp.WriteLine 은 인자 개수별 오버로드가 제한적이므로 항상 하나의 문자열로 만들어서 출력
        static void Log(string format, params object[] args)
        {
            string msg = (args == null || args.Length == 0) ? format : string.Format(format, args);
            RhinoApp.WriteLine(msg);
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            Log("JPOffsets {0} - made by {1}", PlugInInfo.Version, PlugInInfo.Author);

            // 1) 선체 선택 (서피스 / 폴리서피스, 여러 개 가능)
            var go = new GetObject();
            go.SetCommandPrompt("선체 서피스 또는 폴리서피스 선택");
            go.GeometryFilter = ObjectType.Surface | ObjectType.PolysrfFilter;
            go.GetMultiple(1, 0);
            if (go.CommandResult() != Result.Success) return go.CommandResult();

            var breps = new List<Brep>();
            var bbox = BoundingBox.Empty;
            for (int i = 0; i < go.ObjectCount; i++)
            {
                var b = go.Object(i).Brep();
                if (b == null) continue;
                breps.Add(b);
                bbox.Union(b.GetBoundingBox(true));
            }
            if (breps.Count == 0)
            {
                Log("선택한 객체에서 Brep을 얻지 못했습니다.");
                return Result.Failure;
            }

            // 2) 입력 창 (바운딩 박스로 기본값 채움)
            var dlg = new OffsetGenDialog(doc.ModelUnitSystem.ToString(), bbox);
            if (dlg.ShowDialog() != DialogResult.OK) return Result.Cancel;

            // 3) 저장 경로(확장자 제외): 3dm과 같은 폴더
            string basePath;
            if (!string.IsNullOrEmpty(doc.Path))
            {
                basePath = Path.Combine(Path.GetDirectoryName(doc.Path), "OffsetTable");
            }
            else
            {
                using (var sfd = new SaveFileDialog())
                {
                    sfd.FileName = "OffsetTable";
                    sfd.Filter = "Offset table (확장자는 자동 지정)|*.*";
                    if (sfd.ShowDialog() != DialogResult.OK) return Result.Cancel;
                    basePath = Path.Combine(Path.GetDirectoryName(sfd.FileName),
                                            Path.GetFileNameWithoutExtension(sfd.FileName));
                }
            }

            double tol = doc.ModelAbsoluteTolerance;
            double ySign = CurveUtil.YSign(bbox);
            int nSta = dlg.Stations.Count;
            int nWl = dlg.Waterlines.Count;
            int nBt = dlg.UseButtocks ? dlg.Buttocks.Count : 0;

            // 표 데이터: 스테이션(행) x 수선/버톡(열). 한 칸의 값은 교점 전부 (오름차순, 벌브 등은 여러 개)
            double mFrom = OffsetGenDialog.UnitMeters(dlg.FromUnit);   // 현재 단위 -> m (열 머리글용)
            var od = new OffsetData(nSta, nWl, nBt);
            foreach (double xs in dlg.Stations) od.StationX.Add(xs);
            foreach (double zw in dlg.Waterlines) { od.Waterlines.Add(zw); od.WaterlinesM.Add(zw * mFrom); }
            for (int k = 0; k < nBt; k++) { od.Buttocks.Add(dlg.Buttocks[k]); od.ButtocksM.Add(dlg.Buttocks[k] * mFrom); }
            var stationSections = new List<StationSection>();

            // 4) 스테이션별 단면 -> 표의 각 열
            for (int s = 0; s < nSta; s++)
            {
                double x = dlg.Stations[s];
                var plane = new Plane(new Point3d(x, 0, 0), Vector3d.XAxis);
                var sections = CurveUtil.SectionCurves(breps, plane, tol);

                if (sections.Count == 0)
                {
                    Log("x = {0}: 단면 없음 (빈 행으로 출력)", x);
                    continue;
                }
                stationSections.Add(new StationSection { X = x, Curves = sections });

                // 수선: 단면과 z = 일정 평면의 교점들의 중심선에서의 거리 (y)
                for (int w = 0; w < nWl; w++)
                    od.Wl[s, w] = CrossY(sections, dlg.Waterlines[w], ySign, tol);

                // 버톡: 단면과 y = 일정 평면의 교점들의 높이 (z). CENTER CONTOUR = y = 0
                for (int k = 0; k < nBt; k++)
                    od.Bt[s, k] = CrossZ(sections, dlg.Buttocks[k] * ySign, tol);
                od.Center[s] = CrossZ(sections, 0.0, tol);

                // BOTTOM TANGENT LINE: 단면이 기선(z = 0 고정)과 만나는 평탄부 끝의 반폭
                var flat = CrossY(sections, 0.0, ySign, tol);
                if (flat.Count > 0 && flat[flat.Count - 1] > tol * 10.0) od.BottomTangent[s] = flat[flat.Count - 1];

                // DECK SIDE LINE: 갑판 가장자리(단면 최상단)의 반폭, SIDE TANGENT LINE: 현측이 이 단면의 최대 반폭에 닿는 높이
                var polys = CurveUtil.Prepare(sections, ySign, tol, true);
                if (polys.Count > 0)
                {
                    var one = new DenseSection { X = x, Curves = sections, Polys = polys };
                    var ol = CurveUtil.ComputeOutline(new List<DenseSection> { one }, bbox, ySign, tol);
                    if (ol.Count > 0 && ol[0].YTop > tol * 10.0) od.DeckSide[s] = ol[0].YTop;

                    double yMax = 0.0;
                    foreach (var pl in polys)
                        foreach (var p in pl)
                            if (p.Y * ySign > yMax) yMax = p.Y * ySign;
                    if (yMax > tol * 10.0)
                    {
                        var zz = CrossZ(sections, (yMax - tol) * ySign, tol);
                        if (zz.Count > 0) { od.SideTangent[s] = zz[0]; od.SideY[s] = yMax; }
                    }
                }
            }

            // 워터라인/버톡 커브 (라이노 표시 또는 DXF 선도에서 사용)
            bool needLines = dlg.WriteDxf && dlg.IncludeLines;

            var wlSections = new List<WaterlineSection>();
            if (dlg.ShowWaterlineCurves || needLines)
            {
                foreach (double z in dlg.Waterlines)
                {
                    var wlPlane = new Plane(new Point3d(0, 0, z), Vector3d.ZAxis);
                    var curves = CurveUtil.SectionCurves(breps, wlPlane, tol);
                    if (curves.Count > 0)
                        wlSections.Add(new WaterlineSection { Z = z, Curves = curves });
                }
            }

            // 조밀한 스테이션 단면 (버톡 라인, 갑판선/바닥선 계산용)
            List<DenseSection> dense = null;
            if (needLines || (nBt > 0 && dlg.ShowButtockCurves))
                dense = CurveUtil.ComputeDenseSections(breps, bbox, ySign, tol, 200);

            // 버톡 라인: 단면 커브와 y = b 수직선의 정확한 교차점 중 최저 z를 이어서 생성
            var btSections = new List<ButtockSection>();
            if (nBt > 0 && dense != null)
            {
                double halfBeam = Math.Max(Math.Abs(bbox.Min.Y), Math.Abs(bbox.Max.Y));
                double hullH = bbox.Max.Z - bbox.Min.Z;
                double zLo = bbox.Min.Z - hullH - 1.0;
                double zHi = bbox.Max.Z + hullH + 1.0;

                var secCache = new Dictionary<double, List<Curve>>();   // 분할로 새로 만든 단면을 버톡들이 공유
                int nExact = 0, nFallback = 0;
                for (int k = 0; k < nBt; k++)
                {
                    double bv = dlg.Buttocks[k];
                    if (bv >= halfBeam - tol * 5.0)
                    {
                        Log("버톡 y = {0}: 최대 폭({1}) 이상이라 라인을 그리지 않음 (표 높이만 계산)", bv, halfBeam);
                        continue;
                    }
                    if (bv > halfBeam * 0.95)
                        Log("버톡 y = {0}: 최대 폭 근처라 선체 면과 거의 평행해 흔들릴 수 있음", bv);

                    // 1) 정밀 추적: 버톡 평면과 선체의 교차 곡선 (벌브 선수의 다가 곡선도 그대로)
                    List<Curve> exactCurves;
                    List<List<Point3d>> exactLines;
                    string note = "";
                    if (dlg.ExactButtocks &&
                        ButtockTracer.TryTrace(breps, bv * ySign, tol, bbox.Max.X - bbox.Min.X, out exactCurves, out exactLines, out note))
                    {
                        btSections.Add(new ButtockSection { Y = bv, Lines = exactLines, Curves = exactCurves, Exact = true });
                        nExact++;
                        ValidateButtock(k, bv, exactLines, dlg.Stations, od, tol);
                        continue;
                    }
                    if (dlg.ExactButtocks)
                        Log("버톡 y = {0}: 정밀 추적 실패({1}) -> 기존 방식(최저 z 이음)으로 대체", bv, note);

                    // 2) 기존 방식: 단면마다 최저 z를 이음 (닫힌 솔리드 등 정밀 추적이 안 될 때)
                    var btLines = CurveUtil.ComputeButtockLines(breps, dense, bbox, bv, ySign, tol, zLo, zHi, secCache);
                    if (dlg.SmoothButtocks)
                        foreach (var bl in btLines) CurveUtil.SmoothZ(bl, 5);
                    if (btLines.Count > 0)
                    {
                        btSections.Add(new ButtockSection { Y = bv, Lines = btLines });
                        nFallback++;
                    }
                }
                Log("버톡 라인: 정밀 추적 {0}개, 기존 방식 {1}개", nExact, nFallback);
            }

            // 5) 라이노에 커브 표시 (이전 결과는 지우고 새로 추가)
            if (dlg.ShowStationCurves || dlg.ShowWaterlineCurves || dlg.ShowButtockCurves)
            {
                int staLayer = GetOrCreateLayer(doc, StationLayerName, Color.DodgerBlue);
                int wlLayer = GetOrCreateLayer(doc, WaterlineLayerName, Color.OrangeRed);
                int btLayer = GetOrCreateLayer(doc, ButtockLayerName, Color.MediumSeaGreen);
                ClearLayer(doc, staLayer);
                ClearLayer(doc, wlLayer);
                ClearLayer(doc, btLayer);

                int staCount = 0, wlCount = 0, btCount = 0;

                if (dlg.ShowStationCurves)
                    foreach (var ss in stationSections)
                        foreach (var c in ss.Curves)
                        {
                            var attr = new ObjectAttributes { LayerIndex = staLayer };
                            if (doc.Objects.AddCurve(c, attr) != Guid.Empty) staCount++;
                        }

                if (dlg.ShowWaterlineCurves)
                    foreach (var ws in wlSections)
                        foreach (var c in ws.Curves)
                        {
                            var attr = new ObjectAttributes { LayerIndex = wlLayer };
                            if (doc.Objects.AddCurve(c, attr) != Guid.Empty) wlCount++;
                        }

                if (dlg.ShowButtockCurves)
                    foreach (var bs in btSections)
                    {
                        // 정밀 추적이면 정확한 곡선, 아니면 폴리라인
                        var shown = new List<Curve>();
                        if (bs.Curves != null) shown.AddRange(bs.Curves);
                        else foreach (var pts in bs.Lines) shown.Add(new PolylineCurve(pts));
                        foreach (var c in shown)
                        {
                            var attr = new ObjectAttributes { LayerIndex = btLayer };
                            if (doc.Objects.AddCurve(c, attr) != Guid.Empty) btCount++;
                        }
                    }

                doc.Views.Redraw();
                Log("커브 추가: 스테이션 {0}개, 워터라인 {1}개, 버톡 {2}개", staCount, wlCount, btCount);
            }

            string fmt = "F" + dlg.Decimals;

            // 단위 변환: 입력/계산은 현재 단위, 파일 출력만 쓰기 단위로 환산 (m -> mm 이면 x1000)
            string unitName = dlg.ToUnit;
            double sc = OffsetGenDialog.UnitMeters(dlg.FromUnit) / OffsetGenDialog.UnitMeters(dlg.ToUnit);
            Log("단위 변환: {0} -> {1} (배율 x{2})", dlg.FromUnit, dlg.ToUnit, sc);
            string docUnit = OffsetGenDialog.UnitFromRhino(doc.ModelUnitSystem.ToString());
            if (docUnit != null && docUnit != dlg.FromUnit)
                Log("주의: 선택한 현재 단위({0})가 라이노 모델 단위({1})와 다릅니다.", dlg.FromUnit, docUnit);

            // 출력 단위로 환산한 표 데이터 (열 머리글의 수선/버톡 값은 미터 그대로)
            var oData = od.Scaled(sc);

            var num = new StationNumbering
            {
                Valid = dlg.FpX > dlg.ApX && dlg.StationSpacing > 0,
                ApX = dlg.ApX * sc,
                FpX = dlg.FpX * sc,
                Spacing = dlg.StationSpacing * sc,
                FpOrigin = dlg.FpOrigin
            };
            var rowLabels = new List<string>();
            foreach (double xs in oData.StationX)
                rowLabels.Add(num.Valid ? num.Label(xs) : xs.ToString(fmt, CultureInfo.InvariantCulture));

            // 값이 여러 개인 칸 수 (벌브 등)
            int nMulti = 0;
            for (int s = 0; s < nSta; s++)
            {
                for (int k = 0; k < nBt; k++) if (od.Bt[s, k] != null && od.Bt[s, k].Count > 1) nMulti++;
                if (od.Center[s] != null && od.Center[s].Count > 1) nMulti++;
            }
            if (nMulti > 0) Log("높이 표에서 교점이 2개 이상인 칸: {0}곳 (벌브 등) -> 한 칸에 오름차순으로 쌓아 출력", nMulti);

            // 6) 파일 출력 (엑셀 등에서 파일을 열어두면 쓰기 실패할 수 있음)
            try
            {
                if (dlg.WriteCsv)
                {
                    string csvPath = basePath + ".csv";
                    var c1 = OffsetTableFactory.CenterLineTable(oData, rowLabels, fmt, unitName);
                    var c2 = OffsetTableFactory.BaseLineTable(oData, rowLabels, fmt, unitName);
                    File.WriteAllText(csvPath, OffsetTableFactory.ToCsv(c1, c2), Encoding.ASCII);
                    Log("CSV 저장: {0}", csvPath);
                }
                if (dlg.WriteDxf)
                {
                    LinesData lines = null;
                    if (dlg.IncludeLines)
                    {
                        // 프로파일: y=0 평면 단면 (한쪽 현 모델이면 안 잡힐 수 있음 -> 바닥선으로 대체)
                        var profile = CurveUtil.SectionCurves(breps, new Plane(Point3d.Origin, Vector3d.YAxis), tol);
                        // 갑판 가장자리/바닥: 조밀한 단면에서 추출
                        var outline = CurveUtil.ComputeOutline(dense, bbox, ySign, tol);

                        lines = new LinesData
                        {
                            Bbox = bbox,
                            Tol = tol,
                            Stations = stationSections,
                            Waterlines = wlSections,
                            Buttocks = btSections,
                            Profile = profile,
                            Outline = outline,
                            DeckLine = dlg.DeckLine,
                            ShipName = dlg.ShipName,
                            ScaleText = dlg.ScaleText,
                            UnitName = unitName,
                            DepthM = dlg.DepthM,
                            UpperDepthM = dlg.UpperDepthM,
                            DraftM = dlg.DraftM
                        };
                        lines = ScaleLines(lines, sc);
                    }

                    string dxfPath = basePath + ".dxf";
                    DxfWriter.Write(dxfPath, oData, fmt, dlg.TextHeight, num, lines, unitName);
                    Log("DXF 저장{0}: {1}", dlg.IncludeLines ? " (선도 포함)" : "", dxfPath);
                }
            }
            catch (IOException ex)
            {
                Log("파일 저장 실패 (다른 프로그램에서 열려 있는지 확인): " + ex.Message);
                return Result.Failure;
            }

            return Result.Success;
        }

        static List<double> ScaleList(List<double> src, double sc)
        {
            var r = new List<double>();
            if (src == null) return r;
            foreach (double v in src) r.Add(v * sc);
            return r;
        }

        static double?[,] ScaleTable(double?[,] src, double sc)
        {
            int n0 = src.GetLength(0), n1 = src.GetLength(1);
            var r = new double?[n0, n1];
            for (int i = 0; i < n0; i++)
                for (int j = 0; j < n1; j++)
                    if (src[i, j].HasValue) r[i, j] = src[i, j].Value * sc;
            return r;
        }

        static List<Curve> ScaleCurves(List<Curve> src, Transform xf)
        {
            var r = new List<Curve>();
            if (src == null) return r;
            foreach (var c in src)
            {
                var d = c.DuplicateCurve();
                d.Transform(xf);
                r.Add(d);
            }
            return r;
        }

        // 선도 데이터를 출력 단위로 변환한 복사본 (배율 1이면 그대로 반환)
        static LinesData ScaleLines(LinesData d, double sc)
        {
            if (Math.Abs(sc - 1.0) < 1e-12) return d;

            var xf = Transform.Scale(Point3d.Origin, sc);
            var r = new LinesData();
            r.Bbox = new BoundingBox(
                new Point3d(d.Bbox.Min.X * sc, d.Bbox.Min.Y * sc, d.Bbox.Min.Z * sc),
                new Point3d(d.Bbox.Max.X * sc, d.Bbox.Max.Y * sc, d.Bbox.Max.Z * sc));
            r.Tol = d.Tol * sc;

            r.Stations = new List<StationSection>();
            foreach (var ss in d.Stations)
                r.Stations.Add(new StationSection { X = ss.X * sc, Curves = ScaleCurves(ss.Curves, xf) });

            r.Waterlines = new List<WaterlineSection>();
            foreach (var ws in d.Waterlines)
                r.Waterlines.Add(new WaterlineSection { Z = ws.Z * sc, Curves = ScaleCurves(ws.Curves, xf) });

            r.Buttocks = new List<ButtockSection>();
            foreach (var bs in d.Buttocks)
            {
                var lines = new List<List<Point3d>>();
                foreach (var pts in bs.Lines)
                {
                    var np = new List<Point3d>();
                    foreach (var p in pts) np.Add(new Point3d(p.X * sc, p.Y * sc, p.Z * sc));
                    lines.Add(np);
                }
                r.Buttocks.Add(new ButtockSection { Y = bs.Y * sc, Lines = lines });
            }

            r.Profile = ScaleCurves(d.Profile, xf);

            r.Outline = new List<OutlinePoint>();
            foreach (var o in d.Outline)
                r.Outline.Add(new OutlinePoint { X = o.X * sc, YTop = o.YTop * sc, ZTop = o.ZTop * sc, ZBot = o.ZBot * sc });

            r.DeckLine = d.DeckLine;
            r.ShipName = d.ShipName;
            r.ScaleText = d.ScaleText;
            r.UnitName = d.UnitName;
            r.DepthM = d.DepthM;
            r.UpperDepthM = d.UpperDepthM;
            r.DraftM = d.DraftM;
            return r;
        }

        // 정밀 추적한 버톡 곡선과 표(스테이션 단면 교차)의 최저 z가 서로 맞는지 확인해 명령줄에 보고
        static void ValidateButtock(int k, double bv, List<List<Point3d>> lines, List<double> stations,
                                    OffsetData od, double tol)
        {
            var polys = new List<List<P2>>();
            foreach (var l in lines) polys.Add(ButtockTracer.ToXZ(l));

            double maxDiff = 0.0, maxAt = 0.0;
            int checkedN = 0, missing = 0;
            for (int s = 0; s < stations.Count; s++)
            {
                var tab = od.Bt[s, k];
                if (tab == null || tab.Count == 0) continue;
                double x = stations[s];
                if (!PolyTools.InXRange(polys, x, 0.0)) continue;
                var zs = PolyTools.CrossingsAtX(polys, x, tol * 0.1);
                if (zs.Count == 0) { missing++; continue; }
                double d = Math.Abs(zs[0] - tab[0]);
                checkedN++;
                if (d > maxDiff) { maxDiff = d; maxAt = x; }
            }
            double limit = Math.Max(tol * 5.0, 1e-9);
            if (maxDiff > limit || missing > 0)
                Log("  버톡 y = {0} 검증: 표 {1}곳 비교, 최대 차이 {2:0.#####} (x = {3}), 교점 없음 {4}곳 -> 확인 필요",
                    bv, checkedN, maxDiff, maxAt, missing);
        }

        // 단면 커브들과 수평면 z = 일정 의 교점들의 중심선에서의 거리 (한쪽 현만, 오름차순, 가까운 값은 하나로)
        static List<double> CrossY(List<Curve> secs, double z, double ySign, double tol)
        {
            var ys = new List<double>();
            var plane = new Plane(new Point3d(0, 0, z), Vector3d.ZAxis);
            foreach (var c in secs)
            {
                var events = Rhino.Geometry.Intersect.Intersection.CurvePlane(c, plane, tol);
                if (events == null) continue;
                foreach (var ev in events)
                {
                    double y = ev.PointA.Y * ySign;
                    if (y >= -tol) ys.Add(Math.Max(y, 0.0));
                    if (ev.IsOverlap)
                    {
                        double y2 = ev.PointA2.Y * ySign;
                        if (y2 >= -tol) ys.Add(Math.Max(y2, 0.0));
                    }
                }
            }
            return PolyTools.Distinct(ys, Math.Max(tol * 10.0, 1e-9));
        }

        // 단면 커브들과 수직면 y = 일정 의 교점들의 높이 (오름차순, 가까운 값은 하나로)
        static List<double> CrossZ(List<Curve> secs, double yPlane, double tol)
        {
            var zs = new List<double>();
            var plane = new Plane(new Point3d(0, yPlane, 0), Vector3d.YAxis);
            foreach (var c in secs)
            {
                var events = Rhino.Geometry.Intersect.Intersection.CurvePlane(c, plane, tol);
                if (events == null) continue;
                foreach (var ev in events)
                {
                    zs.Add(ev.PointA.Z);
                    if (ev.IsOverlap) zs.Add(ev.PointA2.Z);
                }
            }
            return PolyTools.Distinct(zs, Math.Max(tol * 10.0, 1e-9));
        }

        static int GetOrCreateLayer(RhinoDoc doc, string name, Color color)
        {
            var existing = doc.Layers.FindName(name);
            if (existing != null) return existing.Index;
            return doc.Layers.Add(name, color);
        }

        // 레이어에 있는 기존 객체 삭제 (OffsetGen 전용 레이어에만 사용)
        static void ClearLayer(RhinoDoc doc, int layerIndex)
        {
            var objs = doc.Objects.FindByLayer(doc.Layers[layerIndex]);
            if (objs == null) return;
            foreach (var o in objs) doc.Objects.Delete(o, true);
        }
    }
}
