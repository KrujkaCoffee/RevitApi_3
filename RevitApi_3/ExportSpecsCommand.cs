using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;

namespace RevitErpIntegration
{
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.ReadOnly)]
    public class ExportSpecsCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show("ERP", "Откройте документ перед запуском.");
                return Result.Failed;
            }

            Document doc = uidoc.Document;

            try
            {
                ProjectInfo pi = doc.ProjectInformation;
                string url = GetStringParam(pi, ErpParameters.EndpointParamName);
                string title = GetStringParam(pi, ErpParameters.DocTitleParamName);

                if (string.IsNullOrEmpty(url))
                {
                    TaskDialog.Show("ERP", "Не заполнен параметр ERP_Endpoint в свойствах проекта.");
                    return Result.Failed;
                }

                IList<Element> specs = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSchedule))
                    .ToElements();

                int sent = 0;
                foreach (Element el in specs)
                {
                    ViewSchedule vs = el as ViewSchedule;
                    if (vs == null) continue;
                    if (!vs.Name.StartsWith("Спецификация_", StringComparison.OrdinalIgnoreCase))
                        continue;

                    List<Dictionary<string, string>> rows = ReadSchedule(vs);
                    var payload = new
                    {
                        Project = doc.Title,
                        DocumentTitle = title,
                        ScheduleName = vs.Name,
                        Rows = rows
                    };

                    string json = JsonConvert.SerializeObject(payload);
                    using (WebClient wc = new WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                        string response = wc.UploadString(url, "POST", json);
                        // можно логировать response, если нужно
                    }

                    sent++;
                }

                TaskDialog.Show("ERP", "Выгружено спецификаций: " + sent);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ERP", "Ошибка при выгрузке: " + ex.Message);
                return Result.Failed;
            }
        }

        private static string GetStringParam(ProjectInfo pi, string name)
        {
            Parameter p = pi.LookupParameter(name);
            if (p != null && p.StorageType == StorageType.String)
                return p.AsString();
            return null;
        }

        private static List<Dictionary<string, string>> ReadSchedule(ViewSchedule vs)
        {
            List<Dictionary<string, string>> result = new List<Dictionary<string, string>>();

            TableData td = vs.GetTableData();
            TableSectionData body = td.GetSectionData(SectionType.Body);

            int rows = body.NumberOfRows;
            int cols = body.NumberOfColumns;

            // заголовки
            string[] headers = new string[cols];
            for (int c = 0; c < cols; c++)
            {
                string h = vs.GetCellText(SectionType.Header, 0, c);
                if (string.IsNullOrWhiteSpace(h)) h = "Col" + c;
                headers[c] = h;
            }

            for (int r = 0; r < rows; r++)
            {
                Dictionary<string, string> dict = new Dictionary<string, string>();
                for (int c = 0; c < cols; c++)
                {
                    string text = vs.GetCellText(SectionType.Body, r, c);
                    dict[headers[c]] = text;
                }
                result.Add(dict);
            }

            return result;
        }
    }
}
