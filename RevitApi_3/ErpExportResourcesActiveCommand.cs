using System;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
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
                    TaskDialog.Show("ERP", "Активный вид не является спецификацией.");
                    return Result.Failed;
                }

                var exportRows = ScheduleSemanticExport.BuildExportRows(doc, vs);
                if (exportRows == null || exportRows.Count == 0)
                {
                    TaskDialog.Show("ERP", "В активной спецификации нет строк для выгрузки.");
                    return Result.Succeeded;
                }

                // REST справочники
                List<ErpTreeNode> treeRoots;
                List<RefNamedItem> types;
                List<RefNamedItem> units;
                List<RefNamedItem> stages;

                try
                {
                    treeRoots = ErpClient.LoadErpTree();
                    types = ErpClient.LoadNomenclatureTypes();
                    units = ErpClient.LoadUnits();
                    stages = ErpClient.LoadStages();
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("ERP", "Сервис недоступен.\n" + ex.Message);
                    return Result.Succeeded;
                }

                // ---- читаем сохранённую ссылку на уровне спецификации ----
                var stored = ScheduleErpLinkStorage.Read(vs);
                string existingLink = stored?.Link ?? "";
                string existingInfo = "";

                // ---- валидируем ссылку через REST: удаляем ТОЛЬКО при явном False ----
                if (!string.IsNullOrWhiteSpace(existingLink))
                {
                    bool? alive = ErpClient.CheckResourceLinkAlive(existingLink);
                    existingInfo = string.IsNullOrWhiteSpace(stored?.CreatedAt)
    ? "Спецификация уже была создана ранее ✅"
    : $"Спецификация уже создана ✅ ( {stored.CreatedAt} , {stored.CreatedBy} )";
                    if (alive == false)
                    {
                        using (Transaction t = new Transaction(doc, "Unbind invalid ERP link"))
                        {
                            t.Start();
                            ScheduleErpLinkStorage.Clear(vs);
                            t.Commit();
                        }
                        existingLink = "";
                    }
                }

                // callback сохранения ссылки после успешного экспорта
                Action<string, string> saveLink = (link, info) =>
                {
                    if (string.IsNullOrWhiteSpace(link)) return;

                    using (Transaction t = new Transaction(doc, "Bind ERP link to schedule"))
                    {
                        t.Start();
                        ScheduleErpLinkStorage.Write(vs, new ScheduleErpLinkInfo
                        {
                            Link = link.Trim(),
                            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                            CreatedBy = info
                        });
                        t.Commit();
                    }
                };

                string projectName = GetProjectTitle(doc);
                string defaultTitle = projectName;

                ProjectInfo pi = doc.ProjectInformation;
                string paramTitle = GetStringParam(pi, ErpParameters.DocTitleParamName);
                string initialTitle = string.IsNullOrEmpty(paramTitle) ? defaultTitle : paramTitle;

                string ctx = "Спецификация: " + vs.Name;

                var win = new ExportWindow(exportRows, ctx, initialTitle, treeRoots, types, units, existingLink, existingInfo, saveLink);
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

    internal class ScheduleErpLinkInfo
    {
        public string Link { get; set; }
        public string CreatedAt { get; set; }
        public string CreatedBy { get; set; }
    }

    internal static class ScheduleErpLinkStorage
    {
        private static readonly Guid SchemaGuid = new Guid("D5D0F2D1-3EF4-4CB8-9E3E-8A74B7D8D7F1");

        private static Schema GetOrCreateSchema()
        {
            var s = Schema.Lookup(SchemaGuid);
            if (s != null) return s;

            var sb = new SchemaBuilder(SchemaGuid);
            sb.SetSchemaName("ErpResourceExportInfo");
            sb.AddSimpleField("Link", typeof(string));
            sb.AddSimpleField("CreatedAt", typeof(string));
            sb.AddSimpleField("CreatedBy", typeof(string));
            sb.SetReadAccessLevel(AccessLevel.Public);
            sb.SetWriteAccessLevel(AccessLevel.Public);
            return sb.Finish();
        }

        public static ScheduleErpLinkInfo Read(ViewSchedule vs)
        {
            try
            {
                var schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;

                var ent = vs.GetEntity(schema);
                if (!ent.IsValid()) return null;

                return new ScheduleErpLinkInfo
                {
                    Link = ent.Get<string>("Link") ?? "",
                    CreatedAt = ent.Get<string>("CreatedAt") ?? "",
                    CreatedBy = ent.Get<string>("CreatedBy") ?? ""
                };
            }
            catch { return null; }
        }

        public static void Write(ViewSchedule vs, ScheduleErpLinkInfo info)
        {
            var schema = GetOrCreateSchema();
            var ent = new Entity(schema);
            ent.Set("Link", info?.Link ?? "");
            ent.Set("CreatedAt", info?.CreatedAt ?? "");
            ent.Set("CreatedBy", info?.CreatedBy ?? "");
            vs.SetEntity(ent);
        }

        public static void Clear(ViewSchedule vs)
        {
            Write(vs, new ScheduleErpLinkInfo { Link = "", CreatedAt = "", CreatedBy = "" });
        }
    }
}
