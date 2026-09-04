using System;
using System.Linq;
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

                Guid erpParameterGuid = ErpParameters.ResolveErpCodeGuid(doc);
                ScheduleMirrorTable table = ScheduleMirrorBuilder.Build(doc, vs, erpParameterGuid);
                if (!table.ResourceRows.Any())
                {
                    TaskDialog.Show("ERP", "В активной спецификации нет строк для выгрузки.");
                    return Result.Succeeded;
                }

                // ---- читаем сохранённую ссылку на уровне спецификации ----
                var stored = ScheduleErpLinkStorage.Read(vs);
                string existingLink = stored?.Link ?? "";
                string existingInfo = "";

                if (!string.IsNullOrWhiteSpace(existingLink))
                {
                    existingInfo = string.IsNullOrWhiteSpace(stored?.CreatedAt)
                        ? "Спецификация уже была создана ранее"
                        : $"Спецификация уже создана ({stored.CreatedAt}, {stored.CreatedBy})";
                }

                // callback сохранения ссылки после успешного экспорта
                Action<string, string> saveLink = (link, author) =>
                {
                    if (string.IsNullOrWhiteSpace(link)) return;

                    using (Transaction t = new Transaction(doc, "Bind ERP link to schedule"))
                    {
                        t.Start();
                        ScheduleErpLinkStorage.Write(vs, new ScheduleErpLinkInfo
                        {
                            Link = link.Trim(),
                            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                            CreatedBy = author
                        });
                        t.Commit();
                    }
                };

                Action clearLink = () =>
                {
                    using (Transaction t = new Transaction(doc, "Удаление недействительной ссылки ERP"))
                    {
                        t.Start();
                        ScheduleErpLinkStorage.Clear(vs);
                        t.Commit();
                    }
                };

                string projectName = GetProjectTitle(doc);
                string defaultTitle = projectName;

                string savedTitle = ScheduleErpTitleStorage.Read(vs);
                string initialTitle = string.IsNullOrWhiteSpace(savedTitle) ? defaultTitle : savedTitle;

                string ctx = "Спецификация: " + vs.Name;

                var win = new ExportWindow(
                    table,
                    ctx,
                    initialTitle,
                    existingLink,
                    existingInfo,
                    saveLink,
                    clearLink);
                var helper = new WindowInteropHelper(win);
                helper.Owner = commandData.Application.MainWindowHandle;

                win.ShowDialog();
                if (win.ExportSucceeded)
                {
                    try
                    {
                        using (Transaction t = new Transaction(doc, "Сохранение названия ресурсной ERP"))
                        {
                            t.Start();
                            ScheduleErpTitleStorage.Write(vs, win.DocTitle);
                            t.Commit();
                        }
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("ERP",
                            "Ресурсная создана, но название не удалось сохранить в Revit: " + ex.Message);
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

    internal static class ScheduleErpTitleStorage
    {
        private static readonly Guid SchemaGuid =
            new Guid("EBD27091-DB65-43D1-BDB2-1B1A203E2D45");

        private static Schema GetOrCreateSchema()
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName("ErpResourceExportTitle");
            builder.AddSimpleField("Title", typeof(string));
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            return builder.Finish();
        }

        public static string Read(ViewSchedule schedule)
        {
            try
            {
                Schema schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return "";
                Entity entity = schedule.GetEntity(schema);
                return entity.IsValid() ? entity.Get<string>("Title") ?? "" : "";
            }
            catch { return ""; }
        }

        public static void Write(ViewSchedule schedule, string title)
        {
            Schema schema = GetOrCreateSchema();
            var entity = new Entity(schema);
            entity.Set("Title", title ?? "");
            schedule.SetEntity(entity);
        }
    }
}
