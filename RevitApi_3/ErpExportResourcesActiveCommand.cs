using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Interop;

namespace RevitApi_3
{
    [Transaction(TransactionMode.Manual)]
    public class ErpExportResourcesActiveCommand : IExternalCommand
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
                var vs = activeView as ViewSchedule;
                if (vs == null)
                {
                    TaskDialog.Show("ERP", "Активный вид не является спецификацией. Откройте нужную спецификацию и повторите.");
                    return Result.Failed;
                }

                // NEW: семантическая выгрузка (FEC + sort/group из Definition)
                var exportRows = ScheduleSemanticExport.BuildExportRows(doc, vs);
                foreach (var vd in exportRows) {
                    if (vd.ErpCode == "00-qwe") {
                        Console.WriteLine(" ");
                    }
                
                }

                if (exportRows == null || exportRows.Count == 0)
                {
                    TaskDialog.Show("ERP",
                        "В активной спецификации нет строк данных для выгрузки (проверь фильтры/группировку).");
                    return Result.Succeeded;
                }

                // 2) Данные для выбора выходного изделия
                List<ErpTreeNode> treeRoots;
                List<RefNamedItem> types;
                List<RefNamedItem> units;

                try
                {
                    treeRoots = ErpClient.LoadErpTree();
                    types = ErpClient.LoadNomenclatureTypes();
                    units = ErpClient.LoadUnits();
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("ERP", "Сервис недоступен (подбор выходного изделия).\n" + ex.Message);
                    return Result.Succeeded;
                }

                string projectName = GetProjectTitle(doc);
                string defaultTitle = "Спецификация_" + projectName;

                ProjectInfo pi = doc.ProjectInformation;
                string paramTitle = GetStringParam(pi, ErpParameters.DocTitleParamName);
                string initialTitle = string.IsNullOrEmpty(paramTitle) ? defaultTitle : paramTitle;

                string ctx = "Спецификация: " + vs.Name;

                var win = new ExportWindow(exportRows, ctx, initialTitle, treeRoots, types, units);
                var helper = new WindowInteropHelper(win);
                helper.Owner = commandData.Application.MainWindowHandle;

                bool? dlg = win.ShowDialog();
                if (dlg == true)
                {
                    using (Transaction t = new Transaction(doc, "Update ERP export title (active spec)"))
                    {
                        t.Start();
                        try
                        {
                            Parameter pTitle = pi.LookupParameter(ErpParameters.DocTitleParamName);
                            if (pTitle != null && !pTitle.IsReadOnly && pTitle.StorageType == StorageType.String)
                                pTitle.Set(win.DocTitle ?? string.Empty);
                        }
                        catch { }
                        t.Commit();
                    }
                }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ERP", "Ошибка: " + ex);
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

        private static string GetProjectTitle(Document doc)
        {
            try
            {
                if (!string.IsNullOrEmpty(doc.Title))
                    return doc.Title;

                if (!string.IsNullOrEmpty(doc.PathName))
                    return System.IO.Path.GetFileNameWithoutExtension(doc.PathName);
            }
            catch { }
            return "Проект";
        }
    }
}
