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

            // 1) Пытаемся снять заголовки из SectionType.Header
            var headers = new List<string>();
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            bool anyHeaderText = false;
            if (header != null && header.NumberOfRows > 0)
            {
                for (int c = 0; c < cols; c++)
                {
                    string h = "";
                    for (int r = 0; r < header.NumberOfRows; r++)
                    {
                        string t = SafeGetCellText(vs, SectionType.Header, r, c);
                        if (!string.IsNullOrWhiteSpace(t))
                        {
                            anyHeaderText = true;
                            h = t.Trim(); // берём "нижний" непустой
                        }
                    }

                    headers.Add(h);
                }
            }

            int bodyStartRow = 0;

            // 2) Если заголовков в Header реально нет (кейс "показывать шапку" выключено),
            //    берём заголовки из первой строки Body и сдвигаем старт данных.
            if (!anyHeaderText && body.NumberOfRows > 0)
            {
                headers.Clear();
                for (int c = 0; c < cols; c++)
                {
                    string h = SafeGetCellText(vs, SectionType.Body, 0, c);
                    headers.Add((h ?? "").Trim());
                }
                bodyStartRow = 1;
            }

            // 3) Добиваем пустые/уникальность
            for (int c = 0; c < headers.Count; c++)
            {
                string h = headers[c];
                if (string.IsNullOrWhiteSpace(h))
                    h = "Column_" + c;

                if (used.TryGetValue(h, out int n))
                {
                    n++;
                    used[h] = n;
                    h = h + " (" + n + ")";
                }
                else used[h] = 1;

                headers[c] = h;
            }

            table.Headers = headers;

            table.ErpCodeCol = FindColumn(headers, "Код 1C-ERP", "Код 1С-ERP", "ERP", "Код");
            table.UnitCol = FindColumn(headers, "Ед", "Единица", "Unit");

            // 4) Строки Body 1:1
            for (int r = bodyStartRow; r < body.NumberOfRows; r++)
            {
                var row = new ScheduleExportRow();

                for (int c = 0; c < cols; c++)
                    row.Values.Add(SafeGetCellText(vs, SectionType.Body, r, c) ?? "");

                if (table.ErpCodeCol >= 0 && table.ErpCodeCol < row.Values.Count)
                    row.ErpCode = row.Values[table.ErpCodeCol];

                if (table.UnitCol >= 0 && table.UnitCol < row.Values.Count)
                    row.Unit = row.Values[table.UnitCol];

                table.Rows.Add(row);
            }

            return table;
        }

        private static string SafeGetCellText(TableView tv, SectionType sec, int r, int c)
        {
            try { return tv.GetCellText(sec, r, c); }
            catch { return ""; }
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
