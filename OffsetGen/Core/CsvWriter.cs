using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace OffsetGenPlugin
{
    public static class CsvWriter
    {
        // 표 1: 워터라인 반폭(행 = 워터라인, 열 = 스테이션), 표 2: 버톡 높이 (빈 줄로 구분). 값이 없으면 빈 칸.
        public static string Build(List<double> stations, List<double> waterlines, double?[,] wlTable,
                                   List<double> buttocks, double?[,] btTable, string fmt, string unitName)
        {
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;

            sb.Append("Waterline Z \\ Station X (" + unitName + ")");
            for (int s = 0; s < stations.Count; s++)
                sb.Append(',').Append(stations[s].ToString(fmt, inv));
            sb.AppendLine();

            for (int w = 0; w < waterlines.Count; w++)
            {
                sb.Append(waterlines[w].ToString(fmt, inv));
                for (int s = 0; s < stations.Count; s++)
                {
                    sb.Append(',');
                    if (wlTable[w, s].HasValue) sb.Append(wlTable[w, s].Value.ToString(fmt, inv));
                }
                sb.AppendLine();
            }

            if (btTable != null && buttocks != null && buttocks.Count > 0)
            {
                sb.AppendLine();
                sb.Append("Buttock Y \\ Station X (" + unitName + ")");
                for (int s = 0; s < stations.Count; s++)
                    sb.Append(',').Append(stations[s].ToString(fmt, inv));
                sb.AppendLine();

                for (int k = 0; k < buttocks.Count; k++)
                {
                    sb.Append(buttocks[k].ToString(fmt, inv));
                    for (int s = 0; s < stations.Count; s++)
                    {
                        sb.Append(',');
                        if (btTable[k, s].HasValue) sb.Append(btTable[k, s].Value.ToString(fmt, inv));
                    }
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        public static void Write(string path, List<double> stations, List<double> waterlines, double?[,] wlTable,
                                 List<double> buttocks, double?[,] btTable, string fmt, string unitName)
        {
            File.WriteAllText(path, Build(stations, waterlines, wlTable, buttocks, btTable, fmt, unitName), Encoding.ASCII);
        }
    }
}
