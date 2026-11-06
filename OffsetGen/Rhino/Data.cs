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
        public List<List<Point3d>> Lines;   // x-z 폴리라인 (DXF, 대체 경로)
        public List<Curve> Curves;          // 정확한 교선 커브 (라이노 표시용, 없으면 null)
        public string Method;               // "exact" 또는 "sampled"
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
    }
}
