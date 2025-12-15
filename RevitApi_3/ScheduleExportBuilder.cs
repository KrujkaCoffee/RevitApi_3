using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitApi_3
{
    internal static class ScheduleExportBuilder
    {
        public static ScheduleExportTable Build(ViewSchedule vs)
        {
            var table = new ScheduleExportTable();

            TableData td = vs.GetTableData();
            TableSectionData header = td.GetSectionData(SectionType.Header);
            TableSectionData body = td.GetSectionData(SectionType.Body);

            int cols = body.NumberOfColumns;

            // Заголовки колонок (берём "нижний" непустой текст в Header)
            var headers = new List<string>();
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int c = 0; c < cols; c++)
            {
                string h = "";
                for (int r = 0; r < header.NumberOfRows; r++)
                {
                    string t = header.GetCellText(r, c);
                    if (!string.IsNullOrWhiteSpace(t))
                        h = t.Trim();
                }

                if (string.IsNullOrWhiteSpace(h))
                    h = "Column_" + c;

                // делаем уникальными, чтобы не ломать JSON/таблицу
                if (used.TryGetValue(h, out int n))
                {
                    n++;
                    used[h] = n;
                    h = h + " (" + n + ")";
                }
                else
                {
                    used[h] = 1;
                }

                headers.Add(h);
            }

            table.Headers = headers;

            table.ErpCodeCol = FindColumn(headers, "Код 1C-ERP", "ERP", "Код");
            table.UnitCol = FindColumn(headers, "Ед", "Единица", "Unit");

            for (int r = 0; r < body.NumberOfRows; r++)
            {
                var row = new ScheduleExportRow();

                for (int c = 0; c < cols; c++)
                {
                    string v = "";
                    try { v = body.GetCellText(r, c) ?? ""; } catch { v = ""; }
                    row.Values.Add(v);
                }

                if (table.ErpCodeCol >= 0 && table.ErpCodeCol < row.Values.Count)
                    row.ErpCode = row.Values[table.ErpCodeCol];

                if (table.UnitCol >= 0 && table.UnitCol < row.Values.Count)
                    row.Unit = row.Values[table.UnitCol];

                // Важно: НЕ фильтруем строки. Тогда предпросмотр 1:1 как в Revit.
                table.Rows.Add(row);
            }

            return table;
        }

        private static int FindColumn(List<string> headers, params string[] tokens)
        {
            for (int i = 0; i < headers.Count; i++)
            {
                string h = headers[i] ?? "";
                foreach (var t in tokens)
                {
                    if (string.IsNullOrEmpty(t)) continue;
                    if (h.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                        return i;
                }
            }
            return -1;
        }
    }
}
