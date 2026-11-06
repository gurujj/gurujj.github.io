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
    // 입력 창 (탭 2개)
    // ---------------------------------------------------------------
    public class OffsetGenDialog : Form
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        TextBox txtAp, txtFp, txtStaSpacing, txtStaStartNo, txtStaEndNo;
        HintTextBox txtStaCustom;
        TextBox txtWlStart, txtWlEnd, txtWlStep;
        HintTextBox txtWlExtra;
        TextBox txtBtStart, txtBtEnd, txtBtStep;
        HintTextBox txtBtExtra;
        TextBox txtTextH, txtShip, txtScale;
        NumericUpDown numDecimals;
        ComboBox cmbFrom, cmbTo;
        Label lblFactor;
        string lastDefaultTextH = "2500";
        string modelUnitLabel = "";
        CheckBox chkCsv, chkDxf, chkLines, chkDeck, chkButtock;
        CheckBox chkShowSta, chkShowWl, chkShowBt, chkSmooth;

        public List<double> Stations = new List<double>();
        public List<double> Waterlines = new List<double>();
        public List<double> Buttocks = new List<double>();
        public bool UseButtocks = true;
        public int Decimals = 3;
        public bool WriteCsv = true;
        public bool WriteDxf = true;
        public bool IncludeLines = true;
        public bool DeckLine = true;
        public bool SmoothButtocks = false;
        public bool ShowStationCurves = true;
        public bool ShowWaterlineCurves = true;
        public bool ShowButtockCurves = true;
        public string FromUnit = "m";          // 현재 단위 (입력값/모델 좌표가 뜻하는 단위): m, cm, mm
        public string ToUnit = "mm";           // 쓰기 단위 (CSV/DXF 출력 단위)
        public double TextHeight = 2.5;        // DXF 글자 높이 (출력 단위)
        public double ApX = 0.0;               // 입력값은 모델 단위
        public double FpX = 200.0;
        public double StationSpacing = 10.0;   // 스테이션 1개의 x 길이
        public string ShipName = "";
        public string ScaleText = "1:100";

        public string SavedState = "";         // 다음 실행에서 복원할 마지막 입력 (같은 선체일 때만 사용)
        string hullSig = "";

        public OffsetGenDialog(string modelUnitName, BoundingBox bbox, string savedState)
        {
            Text = "OffsetGen - 오프셋 테이블 / 선도 생성";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(430, 520);
            modelUnitLabel = modelUnitName;

            var tabs = new TabControl { Left = 8, Top = 8, Width = 414, Height = 460 };
            var tabA = new TabPage("단면 정의");
            var tabB = new TabPage("출력 / 선도");
            tabs.TabPages.Add(tabA);
            tabs.TabPages.Add(tabB);
            Controls.Add(tabs);

            // ===== 탭 1: 단면 정의 (입력값은 모두 모델 단위) =====
            int y = 6;

            var gbSta = new GroupBox { Text = "스테이션 (AP 기준 번호, AP = 0)", Left = 6, Top = y, Width = 392, Height = 148 };
            tabA.Controls.Add(gbSta);
            AddLabel("AP x", 12, 24, gbSta);
            txtAp = AddText("0", 44, 21, 70, gbSta);
            AddLabel("FP x", 124, 24, gbSta);
            txtFp = AddText("200", 156, 21, 70, gbSta);
            AddLabel("간격", 236, 24, gbSta);
            txtStaSpacing = AddText("10", 268, 21, 70, gbSta);
            AddLabel("시작 St.", 12, 54, gbSta);
            txtStaStartNo = AddText("0", 62, 51, 60, gbSta);
            AddLabel("끝 St.", 140, 54, gbSta);
            txtStaEndNo = AddText("20", 180, 51, 60, gbSta);
            AddLabel("간격 = 스테이션 1개의 x 길이", 12, 82, gbSta);
            AddLabel("추가 스테이션 번호 (쉼표 구분, 위 스테이션에 추가)", 12, 100, gbSta);
            txtStaCustom = new HintTextBox { Hint = "예: -0.5, 0.5, 19.5, 20.5", Text = "-0.5, 0.5, 1.5, 18.5, 19.5", Left = 12, Top = 120, Width = 366 };
            gbSta.Controls.Add(txtStaCustom);
            y += 154;

            var gbWl = new GroupBox { Text = "워터라인 (z, 높이 방향)", Left = 6, Top = y, Width = 392, Height = 110 };
            tabA.Controls.Add(gbWl);
            AddLabel("시작", 12, 24, gbWl);
            txtWlStart = AddText("0", 44, 21, 70, gbWl);
            AddLabel("끝", 124, 24, gbWl);
            txtWlEnd = AddText("23", 144, 21, 70, gbWl);
            AddLabel("간격", 226, 24, gbWl);
            txtWlStep = AddText("1", 260, 21, 70, gbWl);
            AddLabel("추가 값 (쉼표 구분, 위 범위에 추가)", 12, 54, gbWl);
            txtWlExtra = new HintTextBox { Hint = "예: 0.4, 2.5", Text = "0.4", Left = 12, Top = 74, Width = 366 };
            gbWl.Controls.Add(txtWlExtra);
            y += 116;

            var gbBt = new GroupBox { Text = "버톡 (y, 폭 방향, 중심선에서의 거리)", Left = 6, Top = y, Width = 392, Height = 110 };
            tabA.Controls.Add(gbBt);
            AddLabel("시작", 12, 24, gbBt);
            txtBtStart = AddText("0", 44, 21, 70, gbBt);
            AddLabel("끝", 124, 24, gbBt);
            txtBtEnd = AddText("16", 144, 21, 70, gbBt);
            AddLabel("간격", 226, 24, gbBt);
            txtBtStep = AddText("1", 260, 21, 60, gbBt);
            chkButtock = new CheckBox { Text = "사용", Left = 330, Top = 21, Width = 56, Checked = true };
            gbBt.Controls.Add(chkButtock);
            AddLabel("추가 값 (쉼표 구분, 위 범위에 추가)", 12, 54, gbBt);
            txtBtExtra = new HintTextBox { Hint = "예: 0.5, 1.5", Left = 12, Top = 74, Width = 366 };
            gbBt.Controls.Add(txtBtExtra);
            y += 116;

            AddLabel("소수점 자리수", 12, y + 3, tabA);
            numDecimals = AddNum(3, 0, 8, 110, y, 60, tabA);
            AddLabel("입력값 단위 = 현재 단위 (탭 2에서 선택)", 190, y + 3, tabA);

            // ===== 탭 2: 출력 / 선도 =====
            y = 6;

            chkShowSta = new CheckBox { Text = "스테이션 커브", Left = 6, Top = y, Width = 125, Checked = true };
            chkShowWl = new CheckBox { Text = "워터라인 커브", Left = 136, Top = y, Width = 125, Checked = true };
            chkShowBt = new CheckBox { Text = "버톡 커브", Left = 266, Top = y, Width = 125, Checked = true };
            tabB.Controls.Add(chkShowSta);
            tabB.Controls.Add(chkShowWl);
            tabB.Controls.Add(chkShowBt);
            AddLabel("위 항목: 라이노에 단면 커브를 레이어로 추가", 6, y + 26, tabB);
            y += 52;

            var gbOut = new GroupBox { Text = "파일 출력", Left = 6, Top = y, Width = 392, Height = 136 };
            tabB.Controls.Add(gbOut);
            chkCsv = new CheckBox { Text = "CSV", Left = 12, Top = 22, Width = 60, Checked = true };
            chkDxf = new CheckBox { Text = "DXF", Left = 80, Top = 22, Width = 60, Checked = true };
            gbOut.Controls.Add(chkCsv);
            gbOut.Controls.Add(chkDxf);
            AddLabel("DXF 글자 높이", 190, 24, gbOut);
            txtTextH = AddText("2.5", 280, 21, 60, gbOut);
            chkLines = new CheckBox { Text = "DXF에 선도 포함 (Body / Half-breadth / Sheer plan)", Left = 12, Top = 50, Width = 370, Checked = true };
            gbOut.Controls.Add(chkLines);
            AddLabel("현재 단위", 12, 84, gbOut);
            cmbFrom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 74, Top = 80, Width = 56 };
            AddLabel("→ 쓰기 단위", 140, 84, gbOut);
            cmbTo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 226, Top = 80, Width = 56 };
            foreach (string u in new[] { "m", "cm", "mm" })
            {
                cmbFrom.Items.Add(u);
                cmbTo.Items.Add(u);
            }
            string detected = UnitFromRhino(modelUnitName);
            cmbFrom.SelectedIndex = detected == "cm" ? 1 : (detected == "mm" ? 2 : 0);   // 라이노 모델 단위로 초기화
            cmbTo.SelectedIndex = 2;                                                       // mm
            lastDefaultTextH = N(2.5 / UnitMeters("mm"));
            txtTextH.Text = lastDefaultTextH;
            cmbFrom.SelectedIndexChanged += OnUnitChanged;
            cmbTo.SelectedIndexChanged += OnUnitChanged;
            gbOut.Controls.Add(cmbFrom);
            gbOut.Controls.Add(cmbTo);
            lblFactor = new Label { Left = 12, Top = 110, AutoSize = true };
            gbOut.Controls.Add(lblFactor);
            UpdateFactorLabel();
            y += 144;

            var gbLn = new GroupBox { Text = "선도 옵션", Left = 6, Top = y, Width = 392, Height = 128 };
            tabB.Controls.Add(gbLn);
            chkDeck = new CheckBox { Text = "갑판선(Sheer line) / 갑판 가장자리 그리기", Left = 12, Top = 22, Width = 370, Checked = true };
            gbLn.Controls.Add(chkDeck);
            chkSmooth = new CheckBox { Text = "버톡 스무딩 (정확 교선이 실패해 대체 방식을 쓸 때만 적용)", Left = 12, Top = 48, Width = 370, Checked = false };
            gbLn.Controls.Add(chkSmooth);
            AddLabel("선명", 12, 92, gbLn);
            txtShip = AddText("", 48, 89, 150, gbLn);
            AddLabel("척도", 212, 92, gbLn);
            txtScale = AddText("1:100", 248, 89, 70, gbLn);

            ApplyInitial(bbox, savedState);

            // ===== 버튼 =====
            var ok = new Button { Text = "생성", Left = 230, Top = 476, Width = 90 };
            var cancel = new Button { Text = "취소", Left = 328, Top = 476, Width = 90, DialogResult = DialogResult.Cancel };
            ok.Click += OnOk;
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }


        // 선체 범위에서 기본값을 채우고, 같은 선체의 저장된 입력이 있으면 그것으로 덮어씀
        void ApplyInitial(BoundingBox bb, string saved)
        {
            if (!bb.IsValid) return;
            double halfBeam = Math.Max(Math.Abs(bb.Min.Y), Math.Abs(bb.Max.Y));
            hullSig = KeyValueCodec.Signature(bb.Min.X, bb.Max.X, bb.Min.Z, bb.Max.Z, halfBeam);

            var d = AutoDefaults.Compute(bb.Min.X, bb.Max.X, bb.Min.Z, bb.Max.Z, halfBeam);
            txtAp.Text = d.Ap; txtFp.Text = d.Fp; txtStaSpacing.Text = d.Spacing;
            txtStaStartNo.Text = d.StartNo; txtStaEndNo.Text = d.EndNo; txtStaCustom.Text = "";
            txtWlStart.Text = d.WlStart; txtWlEnd.Text = d.WlEnd; txtWlStep.Text = d.WlStep; txtWlExtra.Text = "";
            txtBtStart.Text = d.BtStart; txtBtEnd.Text = d.BtEnd; txtBtStep.Text = d.BtStep; txtBtExtra.Text = "";

            var kv = KeyValueCodec.Parse(saved);
            string sig;
            if (!kv.TryGetValue("sig", out sig) || sig != hullSig) return;   // 다른 선체면 복원하지 않음
            Restore(kv, "ap", txtAp); Restore(kv, "fp", txtFp); Restore(kv, "spacing", txtStaSpacing);
            Restore(kv, "st0", txtStaStartNo); Restore(kv, "st1", txtStaEndNo); Restore(kv, "stx", txtStaCustom);
            Restore(kv, "wl0", txtWlStart); Restore(kv, "wl1", txtWlEnd); Restore(kv, "wls", txtWlStep); Restore(kv, "wlx", txtWlExtra);
            Restore(kv, "bt0", txtBtStart); Restore(kv, "bt1", txtBtEnd); Restore(kv, "bts", txtBtStep); Restore(kv, "btx", txtBtExtra);
            string dec;
            int dv;
            if (kv.TryGetValue("dec", out dec) && int.TryParse(dec, out dv) && dv >= numDecimals.Minimum && dv <= numDecimals.Maximum)
                numDecimals.Value = dv;
        }

        static void Restore(Dictionary<string, string> kv, string key, TextBox tb)
        {
            string v;
            if (kv.TryGetValue(key, out v)) tb.Text = v;
        }

        string BuildSavedState()
        {
            var kv = new Dictionary<string, string>();
            kv["sig"] = hullSig;
            kv["ap"] = txtAp.Text; kv["fp"] = txtFp.Text; kv["spacing"] = txtStaSpacing.Text;
            kv["st0"] = txtStaStartNo.Text; kv["st1"] = txtStaEndNo.Text; kv["stx"] = txtStaCustom.Text;
            kv["wl0"] = txtWlStart.Text; kv["wl1"] = txtWlEnd.Text; kv["wls"] = txtWlStep.Text; kv["wlx"] = txtWlExtra.Text;
            kv["bt0"] = txtBtStart.Text; kv["bt1"] = txtBtEnd.Text; kv["bts"] = txtBtStep.Text; kv["btx"] = txtBtExtra.Text;
            kv["dec"] = ((int)numDecimals.Value).ToString(CultureInfo.InvariantCulture);
            return KeyValueCodec.Serialize(kv);
        }

        static string N(double v)
        {
            return v.ToString("0.####", Inv);
        }

        // 글자 높이가 기본값 그대로면 쓰기 단위에 맞는 기본값(2.5 m에 해당하는 값)으로 바꿈
        void OnUnitChanged(object sender, EventArgs e)
        {
            string newDefault = N(2.5 / UnitMeters(cmbTo.SelectedItem.ToString()));
            if (txtTextH.Text.Trim() == lastDefaultTextH) txtTextH.Text = newDefault;
            lastDefaultTextH = newDefault;
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

        void OnOk(object sender, EventArgs e)
        {
            double ap, fp, spacing, stStart, stEnd;
            if (!double.TryParse(txtAp.Text, NumberStyles.Float, Inv, out ap) ||
                !double.TryParse(txtFp.Text, NumberStyles.Float, Inv, out fp) || fp <= ap)
            {
                MessageBox.Show("AP, FP 값을 확인하세요. (FP > AP)");
                return;
            }
            if (!double.TryParse(txtStaSpacing.Text, NumberStyles.Float, Inv, out spacing) || spacing <= 0)
            {
                MessageBox.Show("스테이션 간격을 확인하세요. (> 0)");
                return;
            }
            if (!double.TryParse(txtStaStartNo.Text, NumberStyles.Float, Inv, out stStart) ||
                !double.TryParse(txtStaEndNo.Text, NumberStyles.Float, Inv, out stEnd) || stEnd < stStart)
            {
                MessageBox.Show("시작/끝 스테이션 번호를 확인하세요. (시작 <= 끝)");
                return;
            }
            ApX = ap;
            FpX = fp;
            StationSpacing = spacing;

            // 간격 스테이션 번호 + 추가 번호 -> 정렬/중복 제거 -> x 위치
            string stErr;
            Stations = ListBuilder.BuildStations(ap, spacing, stStart, stEnd, txtStaCustom.Text, out stErr);
            if (Stations == null)
            {
                MessageBox.Show(stErr);
                return;
            }

            // 워터라인: 범위 + 추가 값 (오름차순, 중복 제거)
            Waterlines = ListBuilder.ParseRange(txtWlExtra.Text, txtWlStart.Text, txtWlEnd.Text, txtWlStep.Text);
            if (Waterlines == null || Waterlines.Count == 0)
            {
                MessageBox.Show("워터라인 값을 확인하세요. (시작 <= 끝, 간격 > 0, 추가 값은 숫자)");
                return;
            }

            // 버톡: 범위 + 추가 값 (오름차순, 중복 제거)
            UseButtocks = chkButtock.Checked;
            if (UseButtocks)
            {
                Buttocks = ListBuilder.ParseRange(txtBtExtra.Text, txtBtStart.Text, txtBtEnd.Text, txtBtStep.Text);
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
            if (!double.TryParse(txtTextH.Text, NumberStyles.Float, Inv, out h) || h <= 0)
            {
                MessageBox.Show("DXF 글자 높이를 확인하세요.");
                return;
            }
            TextHeight = h;

            FromUnit = cmbFrom.SelectedItem.ToString();
            ToUnit = cmbTo.SelectedItem.ToString();
            IncludeLines = chkLines.Checked;
            DeckLine = chkDeck.Checked;
            SmoothButtocks = chkSmooth.Checked;
            ShipName = txtShip.Text;
            ScaleText = txtScale.Text;
            ShowStationCurves = chkShowSta.Checked;
            ShowWaterlineCurves = chkShowWl.Checked;
            ShowButtockCurves = chkShowBt.Checked;
            Decimals = (int)numDecimals.Value;
            SavedState = BuildSavedState();
            DialogResult = DialogResult.OK;
            Close();
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
}
