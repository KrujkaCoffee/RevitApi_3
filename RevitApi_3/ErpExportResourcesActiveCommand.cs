using System;
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

                // 1. Берём все элементы из этой спецификации
                var items = RevitCollectors.CollectFromSchedule(doc, vs);

                if (items.Count == 0)
                {
                    TaskDialog.Show("ERP", "В активной спецификации нет элементов для выгрузки.");
                    return Result.Succeeded;
                }

                ProjectInfo pi = doc.ProjectInformation;
                string paramTitle = GetStringParam(pi, ErpParameters.DocTitleParamName);

                string defaultTitle = "Спецификация_" + GetProjectTitle(doc);
                string initialTitle = string.IsNullOrEmpty(paramTitle) ? defaultTitle : paramTitle;


                string ctx = "Спецификация: " + vs.Name;
                var win = new ExportWindow(items, ctx, initialTitle);
                var helper = new WindowInteropHelper(win);
                helper.Owner = commandData.Application.MainWindowHandle;

                bool? dlg = win.ShowDialog();
                if (dlg == true)
                {
                    // Сохраняем Title в ProjectInfo (если параметр есть)
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
