using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Interop;

namespace RevitApi_3
{
    [Transaction(TransactionMode.Manual)]
    public class ErpMappingActiveCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                TaskDialog.Show("ERP", "Откройте документ.");
                return Result.Failed;
            }

            Document doc = uidoc.Document;
            View activeView = uidoc.ActiveView;

            try
            {
                ViewSchedule vs = activeView as ViewSchedule;
                if (vs == null)
                {
                    TaskDialog.Show("ERP", "Активный вид не является спецификацией. Откройте нужную спецификацию и повторите.");
                    return Result.Failed;
                }

                ErpParameters.EnsureErpCodeParameter(doc);

                List<RevitItem> items = RevitCollectors.CollectFromSchedule(doc, vs);
                if (items.Count == 0)
                {
                    TaskDialog.Show("ERP", "В активной спецификации нет элементов для обработки.");
                    return Result.Succeeded;
                }

                ProjectInfo pi = doc.ProjectInformation;
                string initialEndpoint = GetStringParam(pi, ErpParameters.EndpointParamName);
                string initialTitle = GetStringParam(pi, ErpParameters.DocTitleParamName);

                var win = new MappingWindow(items: items,
                                            contextInfo: "Спецификация: " + vs.Name,
                                            initialEndpoint: initialEndpoint,
                                            initialTitle: initialTitle);

                var helper = new WindowInteropHelper(win);
                helper.Owner = commandData.Application.MainWindowHandle;

                bool? dlgResult = win.ShowDialog();
                if (dlgResult != true)
                    return Result.Succeeded;

                ApplyErpCodes(doc, win.ResultItems, win.Endpoint, win.DocTitle);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ERP", "Ошибка: " + ex.ToString());
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

        private static void ApplyErpCodes(Document doc,
                                          IList<RevitItem> items,
                                          string endpoint,
                                          string title)
        {
            if (items == null)
                return;

            int count = 0;

            using (Transaction t = new Transaction(doc, "Set ERP codes (active spec)"))
            {
                t.Start();

                var groups = items
                    .Where(r => !string.IsNullOrEmpty(r.ErpCode))
                    .GroupBy(r => r.TypeId.IntegerValue);

                foreach (var g in groups)
                {
                    ElementId typeId = new ElementId(g.Key);
                    Element type = doc.GetElement(typeId);
                    if (type == null) continue;

                    Parameter p = type.LookupParameter(ErpParameters.ErpCodeParamName);
                    if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                    {
                        p.Set(g.First().ErpCode);
                        count++;
                    }
                    else
                    {
                        foreach (RevitItem ri in g)
                        {
                            Element inst = doc.GetElement(ri.ElementId);
                            if (inst == null) continue;

                            Parameter pi = inst.LookupParameter(ErpParameters.ErpCodeParamName);
                            if (pi != null && !pi.IsReadOnly && pi.StorageType == StorageType.String)
                            {
                                pi.Set(ri.ErpCode);
                                count++;
                            }
                        }
                    }
                }

                try
                {
                    ProjectInfo piProj = doc.ProjectInformation;
                    Parameter pUrl = piProj.LookupParameter(ErpParameters.EndpointParamName);
                    if (pUrl != null && !pUrl.IsReadOnly && pUrl.StorageType == StorageType.String)
                        pUrl.Set(endpoint ?? string.Empty);

                    Parameter pTitle = piProj.LookupParameter(ErpParameters.DocTitleParamName);
                    if (pTitle != null && !pTitle.IsReadOnly && pTitle.StorageType == StorageType.String)
                        pTitle.Set(title ?? string.Empty);
                }
                catch { }

                t.Commit();
            }

            TaskDialog.Show("ERP", "Записано кодов (активная спецификация): " + count);
        }
    }
}
