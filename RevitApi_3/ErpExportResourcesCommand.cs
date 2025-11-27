using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Interop;

namespace RevitApi_3
{
    [Transaction(TransactionMode.Manual)]
    public class ErpExportResourcesCommand : IExternalCommand
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

            try
            {
                // Берём элементы из всех спецификаций (с уже сохранёнными кодами)
                List<RevitItem> items = RevitCollectors.CollectFromAllSpecs(doc);
                if (items.Count == 0)
                {
                    TaskDialog.Show("ERP", "Не найдено элементов в спецификациях с префиксом 'Спецификация_'.");
                    return Result.Succeeded;
                }

                ProjectInfo pi = doc.ProjectInformation;
                string initialEndpoint = GetStringParam(pi, ErpParameters.EndpointParamName);
                string initialTitle = GetStringParam(pi, ErpParameters.DocTitleParamName);

                var win = new ExportWindow(items,
                                           "Все спецификации проекта",
                                           initialEndpoint,
                                           initialTitle);

                var helper = new WindowInteropHelper(win);
                helper.Owner = commandData.Application.MainWindowHandle;

                bool? dlg = win.ShowDialog();
                if (dlg != true)
                    return Result.Succeeded;

                // Обновим endpoint/title в ProjectInfo
                using (Transaction t = new Transaction(doc, "Update ERP settings (export)"))
                {
                    t.Start();
                    try
                    {
                        Parameter pUrl = pi.LookupParameter(ErpParameters.EndpointParamName);
                        if (pUrl != null && !pUrl.IsReadOnly && pUrl.StorageType == StorageType.String)
                            pUrl.Set(win.Endpoint ?? string.Empty);

                        Parameter pTitle = pi.LookupParameter(ErpParameters.DocTitleParamName);
                        if (pTitle != null && !pTitle.IsReadOnly && pTitle.StorageType == StorageType.String)
                            pTitle.Set(win.DocTitle ?? string.Empty);
                    }
                    catch { }
                    t.Commit();
                }

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
    }
}
