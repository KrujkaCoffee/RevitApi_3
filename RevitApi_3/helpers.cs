using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitApi_3
{
    internal static class SchedulePreviewBuilder
    {
        public static List<ExportRow> Build(ViewSchedule vs)
        {
            var result = new List<ExportRow>();

            TableData td = vs.GetTableData();
            TableSectionData header = td.GetSectionData(SectionType.Header);
            TableSectionData body = td.GetSectionData(SectionType.Body);

            int cols = body.NumberOfColumns;

            // Считываем заголовки колонок
            string[] heads = new string[cols];
            for (int c = 0; c < cols; c++)
            {
                string last = "";
                for (int r = 0; r < header.NumberOfRows; r++)
                {
                    string t = header.GetCellText(r, c);
                    if (!string.IsNullOrWhiteSpace(t)) last = t.Trim();
                }
                heads[c] = last ?? "";
            }

            int cStage = FindCol(heads, "Этап");
            int cName = FindCol(heads, "Наименование", "ADSK_Наименование");
            int cErp = FindCol(heads, "Код 1C-ERP", "Код ERP");
            int cUnit = FindCol(heads, "Ед. изм.", "Единица измерения", "ADSK_Единица измерения");
            int cQty = FindCol(heads, "Количество", "Кол-во", "Count");
            int cM1 = FindCol(heads, "Масса / шт", "Масса/шт");
            int cMT = FindCol(heads, "Масса всего", "Суммарная масса");

            // (опционально) семейство/тип, если есть в спецификации
            int cFam = FindCol(heads, "Семейство");
            int cTyp = FindCol(heads, "Тип");

            for (int r = 0; r < body.NumberOfRows; r++)
            {
                string stage = GetCell(body, r, cStage);
                string name = GetCell(body, r, cName);
                string erp = GetCell(body, r, cErp);
                string unit = GetCell(body, r, cUnit);
                string qty = GetCell(body, r, cQty);
                string m1 = GetCell(body, r, cM1);
                string mt = GetCell(body, r, cMT);

                // Отсечём "пустые" строки (часто это заголовки групп или пустые разделители)
                bool looksLikeData =
                    !string.IsNullOrWhiteSpace(erp) ||
                    !string.IsNullOrWhiteSpace(qty) ||
                    !string.IsNullOrWhiteSpace(unit);

                if (!looksLikeData)
                    continue;

                result.Add(new ExportRow
                {
                    Stage = stage,
                    ScheduleName = vs.Name,
                    FamilyName = GetCell(body, r, cFam),
                    TypeName = GetCell(body, r, cTyp),
                    DisplayName = name,
                    ErpCode = erp,
                    Unit = unit,
                    // Если у тебя Quantity/Mass поля числовые — можно оставить строками в UI,
                    // либо попытаться парсить. Для "1:1 как в спецификации" лучше хранить текст.
                    QuantityText = qty,
                    MassPerItemText = m1,
                    TotalMassText = mt
                });
            }

            return result;
        }

        private static int FindCol(string[] heads, params string[] candidates)
        {
            for (int c = 0; c < heads.Length; c++)
            {
                string h = heads[c] ?? "";
                foreach (var cand in candidates)
                {
                    if (string.IsNullOrEmpty(cand)) continue;
                    if (h.IndexOf(cand, StringComparison.OrdinalIgnoreCase) >= 0)
                        return c;
                }
            }
            return -1;
        }

        private static string GetCell(TableSectionData sec, int r, int c)
        {
            if (c < 0) return "";
            try
            {
                return sec.GetCellText(r, c) ?? "";
            }
            catch { return ""; }
        }
    }
}
