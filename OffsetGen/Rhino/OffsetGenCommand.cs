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
    // 명령어: OffsetGen
    // ---------------------------------------------------------------
    [System.Runtime.InteropServices.Guid("b3e94a17-6d2c-4c58-8f0e-91a7d5c2e604")]
    public class OffsetGenCommand : Command
    {
        const string StationLayerName = "OffsetGen_Station";
        const string WaterlineLayerName = "OffsetGen_Waterline";
        const string ButtockLayerName = "OffsetGen_Buttock";

        public override string EnglishName { get { return "OffsetGen"; } }

        // RhinoApp.WriteLine 은 인수 개수별 오버로드가 제한적이므로 항상 문자열 하나로 만들어 보냄
        static void Log(string format, params object[] args)
        {
            string msg = (args == null || args.Length == 0) ? format : string.Format(format, args);
            RhinoApp.WriteLine(msg);
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
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
                var br = go.Object(i).Brep();
                if (br == null) continue;
                breps.Add(br);
                bbox.Union(br.GetBoundingBox(true));
            }
            if (breps.Count == 0 || !bbox.IsValid)
            {
                Log("선택한 객체에서 Brep을 얻지 못했습니다.");
                return Result.Failure;
            }

            // 2) 입력 창
            string saved = "";
            var plug = OffsetGenPlugIn.Instance;
            try { if (plug != null) saved = plug.Settings.GetString("LastInput", ""); }
            catch (Exception) { saved = ""; }

            var dlg = new OffsetGenDialog(doc.ModelUnitSystem.ToString(), bbox, saved);
            if (dlg.ShowDialog() != DialogResult.OK) return Result.Cancel;

            try { if (plug != null) plug.Settings.SetString("LastInput", dlg.SavedState); }
            catch (Exception) { /* 저장 실패는 무시 */ }

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
            bool needLines = dlg.WriteDxf && dlg.IncludeLines;

            // wlTable[wl, 스테이션] = 반폭, btTable[버톡, 스테이션] = 높이 (없으면 null)
            var wlTable = new double?[nWl, nSta];
            double?[,] btTable = nBt > 0 ? new double?[nBt, nSta] : null;
            var stationSections = new List<StationSection>();

            ReportRanges(dlg, bbox);

            // 4) 스테이션별 단면 -> 워터라인 반폭, 버톡 높이 (표 값)
            ComputeTables(breps, dlg, tol, ySign, wlTable, btTable, stationSections);

            // 5) 워터라인 커브 (라이노 표시 또는 DXF 선도에서 사용)
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

            // 6) 버톡 라인 (곡면-평면 교선, 표 값으로 검증)
            var btSections = new List<ButtockSection>();
            if (nBt > 0 && (needLines || dlg.ShowButtockCurves))
                TraceButtocks(breps, dlg, bbox, tol, ySign, btTable, btSections);

            // 7) 갑판/바닥 윤곽용 조밀한 단면 (DXF 선도에서만 사용)
            List<DenseSection> dense = null;
            if (needLines)
                dense = CurveUtil.ComputeDenseSections(breps, bbox, ySign, tol, 200);

            // 8) 라이노에 커브 표시 (이전 결과는 지우고 새로 추가)
            ShowCurves(doc, dlg, stationSections, wlSections, btSections);

            string fmt = "F" + dlg.Decimals;

            // 단위 변환: 입력/계산은 현재 단위, 파일 출력만 쓰기 단위로 환산 (m -> mm 이면 x1000)
            string unitName = dlg.ToUnit;
            double sc = OffsetGenDialog.UnitMeters(dlg.FromUnit) / OffsetGenDialog.UnitMeters(dlg.ToUnit);
            Log("단위 변환: {0} -> {1} (배율 x{2})", dlg.FromUnit, dlg.ToUnit, sc);
            string docUnit = OffsetGenDialog.UnitFromRhino(doc.ModelUnitSystem.ToString());
            if (docUnit != null && docUnit != dlg.FromUnit)
                Log("주의: 선택한 현재 단위({0})가 라이노 모델 단위({1})와 다릅니다.", dlg.FromUnit, docUnit);

            var oStations = ScaleList(dlg.Stations, sc);
            var oWaterlines = ScaleList(dlg.Waterlines, sc);
            var oButtocks = ScaleList(dlg.Buttocks, sc);
            var oWlTable = ScaleTable(wlTable, sc);
            double?[,] oBtTable = btTable == null ? null : ScaleTable(btTable, sc);

            // 9) 파일 출력 (엑셀 등에서 파일을 열어두면 쓰기 실패할 수 있음)
            try
            {
                if (dlg.WriteCsv)
                {
                    string csvPath = basePath + ".csv";
                    CsvWriter.Write(csvPath, oStations, oWaterlines, oWlTable, oButtocks, oBtTable, fmt, unitName);
                    Log("CSV 저장: {0}", csvPath);
                }
                if (dlg.WriteDxf)
                {
                    var num = new StationNumbering
                    {
                        Valid = dlg.FpX > dlg.ApX && dlg.StationSpacing > 0,
                        ApX = dlg.ApX * sc,
                        FpX = dlg.FpX * sc,
                        Spacing = dlg.StationSpacing * sc
                    };

                    LinesData lines = null;
                    if (dlg.IncludeLines)
                    {
                        // 프로파일: y=0 평면 단면 (한쪽 현 모델이면 안 잡힐 수 있음 -> 바닥선으로 대체)
                        var profile = CurveUtil.SectionCurves(breps, new Plane(Point3d.Origin, Vector3d.YAxis), tol);
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
                            UnitName = unitName
                        };
                        lines = ScaleLines(lines, sc);
                    }

                    string dxfPath = basePath + ".dxf";
                    DxfWriter.Write(dxfPath, oStations, oWaterlines, oWlTable, oButtocks, oBtTable,
                                    fmt, dlg.TextHeight, num, lines, unitName);
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

        // 입력한 스테이션/워터라인/버톡이 선체 범위 안에 있는지 점검 (단위 불일치나 범위 오입력 발견용)
        static void ReportRanges(OffsetGenDialog dlg, BoundingBox bbox)
        {
            double e = 1e-9;
            int outSta = 0, outWl = 0, outBt = 0;
            foreach (double x in dlg.Stations) if (x < bbox.Min.X - e || x > bbox.Max.X + e) outSta++;
            foreach (double z in dlg.Waterlines) if (z < bbox.Min.Z - e || z > bbox.Max.Z + e) outWl++;
            double halfBeam = Math.Max(Math.Abs(bbox.Min.Y), Math.Abs(bbox.Max.Y));
            if (dlg.UseButtocks) foreach (double y in dlg.Buttocks) if (y > halfBeam + e) outBt++;

            Log("입력 점검: 선체 x {0:0.###}~{1:0.###}, z {2:0.###}~{3:0.###}, 반폭 {4:0.###} (모델 단위)",
                bbox.Min.X, bbox.Max.X, bbox.Min.Z, bbox.Max.Z, halfBeam);
            Log("  스테이션 {0}개 (범위 밖 {1}), 워터라인 {2}개 (범위 밖 {3}), 버톡 {4}개 (폭 밖 {5})",
                dlg.Stations.Count, outSta, dlg.Waterlines.Count, outWl, dlg.UseButtocks ? dlg.Buttocks.Count : 0, outBt);
            if (outSta > 0 || outWl > 0 || outBt > 0)
                Log("  주의: 범위 밖 값은 빈 칸이 됩니다. 입력 단위(탭 2의 현재 단위)와 모델 단위가 맞는지 확인하세요.");
        }

        // 스테이션 단면에서 워터라인 반폭(|y| 최댓값)과 버톡 높이(z 최솟값) 표를 채움
        static void ComputeTables(List<Brep> breps, OffsetGenDialog dlg, double tol, double ySign,
                                  double?[,] wlTable, double?[,] btTable, List<StationSection> stationSections)
        {
            int nSta = dlg.Stations.Count;
            int nWl = dlg.Waterlines.Count;
            int nBt = btTable == null ? 0 : btTable.GetLength(0);

            var btPlanes = new List<Plane>();
            for (int k = 0; k < nBt; k++)
                btPlanes.Add(new Plane(new Point3d(0, dlg.Buttocks[k] * ySign, 0), Vector3d.YAxis));

            for (int s = 0; s < nSta; s++)
            {
                double x = dlg.Stations[s];
                var sections = CurveUtil.SectionCurves(breps, new Plane(new Point3d(x, 0, 0), Vector3d.XAxis), tol);

                if (sections.Count == 0)
                {
                    Log("x = {0}: 단면 없음 (빈 열로 출력)", x);
                    continue;
                }
                stationSections.Add(new StationSection { X = x, Curves = sections });

                for (int w = 0; w < nWl; w++)
                {
                    var wl = new Plane(new Point3d(0, 0, dlg.Waterlines[w]), Vector3d.ZAxis);
                    bool found = false;
                    double maxY = 0.0;

                    foreach (var c in sections)
                    {
                        var events = Rhino.Geometry.Intersect.Intersection.CurvePlane(c, wl, tol);
                        if (events == null) continue;
                        foreach (var ev in events)
                        {
                            double ya = Math.Abs(ev.PointA.Y);
                            if (!found || ya > maxY) maxY = ya;
                            found = true;
                            if (ev.IsOverlap)
                            {
                                double yb = Math.Abs(ev.PointA2.Y);
                                if (yb > maxY) maxY = yb;
                            }
                        }
                    }
                    if (found) wlTable[w, s] = maxY;
                }

                for (int k = 0; k < nBt; k++)
                {
                    bool found = false;
                    double minZ = 0.0;

                    foreach (var c in sections)
                    {
                        var events = Rhino.Geometry.Intersect.Intersection.CurvePlane(c, btPlanes[k], tol);
                        if (events == null) continue;
                        foreach (var ev in events)
                        {
                            double za = ev.PointA.Z;
                            if (!found || za < minZ) minZ = za;
                            found = true;
                            if (ev.IsOverlap && ev.PointA2.Z < minZ) minZ = ev.PointA2.Z;
                        }
                    }
                    if (found) btTable[k, s] = minZ;
                }
            }
        }

        // 버톡마다 추적하고 결과를 명령 창에 보고
        static void TraceButtocks(List<Brep> breps, OffsetGenDialog dlg, BoundingBox bbox, double tol, double ySign,
                                  double?[,] btTable, List<ButtockSection> btSections)
        {
            double halfBeam = Math.Max(Math.Abs(bbox.Min.Y), Math.Abs(bbox.Max.Y));
            int nBt = dlg.Buttocks.Count;
            int nSta = dlg.Stations.Count;
            Log("선체 범위: x {0:0.###} ~ {1:0.###}, y {2:0.###} ~ {3:0.###}, z {4:0.###} ~ {5:0.###}  (허용오차 {6})",
                bbox.Min.X, bbox.Max.X, bbox.Min.Y, bbox.Max.Y, bbox.Min.Z, bbox.Max.Z, tol);
            Log("버톡 라인 추적: {0}개 (조밀 단면 준비 중...)", nBt);

            var ctx = TraceContext.Build(breps, bbox, tol, 240);
            if (ctx.StaX.Count == 0)
            {
                Log("  조밀 단면을 만들지 못했습니다. 선체 선택을 확인하세요.");
                return;
            }

            for (int k = 0; k < nBt; k++)
            {
                double bv = dlg.Buttocks[k];
                if (bv >= halfBeam - tol * 5.0)
                {
                    Log("  버톡 y = {0}: 최대 폭({1:0.###}) 이상이라 라인을 그리지 않음 (표 높이만 계산)", bv, halfBeam);
                    continue;
                }

                var tableZ = new List<double?>();
                for (int s = 0; s < nSta; s++) tableZ.Add(btTable[k, s]);

                var r = ButtockTracer.Trace(ctx, bv, ySign, dlg.Stations, tableZ, dlg.SmoothButtocks);

                string method = r.Method == "exact" ? "정확 교선" :
                                (r.Method == "cloud" ? "점구름 복원" : (r.Method == "sampled" ? "최저z 샘플링" : "없음"));
                string range = r.Lines.Count > 0 ? string.Format(", z {0:0.###}~{1:0.###}", r.ZMin, r.ZMax) : "";
                string check = r.ChecksTotal > 0 ? string.Format(", 검증 {0}/{1}", r.ChecksOk, r.ChecksTotal) : "";
                string note = r.Note.Length > 0 ? " [" + r.Note + "]" : "";
                Log("  버톡 y = {0}: {1}, 조각 {2}개{3}{4}{5}", bv, method, r.Lines.Count, range, check, note);

                if (r.Lines.Count > 0)
                    btSections.Add(new ButtockSection { Y = bv, Lines = r.Lines, Curves = r.Curves, Method = r.Method });
            }
        }

        // 스테이션/워터라인/버톡 커브를 전용 레이어에 추가
        static void ShowCurves(RhinoDoc doc, OffsetGenDialog dlg, List<StationSection> stationSections,
                               List<WaterlineSection> wlSections, List<ButtockSection> btSections)
        {
            if (!(dlg.ShowStationCurves || dlg.ShowWaterlineCurves || dlg.ShowButtockCurves)) return;

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
                    var attr = new ObjectAttributes { LayerIndex = btLayer };
                    if (bs.Curves != null && bs.Curves.Count > 0)
                    {
                        foreach (var c in bs.Curves)
                            if (doc.Objects.AddCurve(c, attr) != Guid.Empty) btCount++;     // 정확한 교선 그대로
                    }
                    else
                    {
                        foreach (var pts in bs.Lines)
                            if (doc.Objects.AddCurve(new PolylineCurve(pts), attr) != Guid.Empty) btCount++;
                    }
                }

            doc.Views.Redraw();
            Log("커브 추가: 스테이션 {0}개, 워터라인 {1}개, 버톡 {2}개", staCount, wlCount, btCount);
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
                r.Buttocks.Add(new ButtockSection { Y = bs.Y * sc, Lines = lines, Method = bs.Method });
            }

            r.Profile = ScaleCurves(d.Profile, xf);

            r.Outline = new List<OutlinePoint>();
            foreach (var o in d.Outline)
                r.Outline.Add(new OutlinePoint { X = o.X * sc, YTop = o.YTop * sc, ZTop = o.ZTop * sc, ZBot = o.ZBot * sc });

            r.DeckLine = d.DeckLine;
            r.ShipName = d.ShipName;
            r.ScaleText = d.ScaleText;
            r.UnitName = d.UnitName;
            return r;
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
