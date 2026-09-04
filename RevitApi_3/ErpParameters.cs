using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace RevitApi_3
{
    /// <summary>
    /// Единая точка работы с параметром ERP. ADSK_Код изделия намеренно не
    /// переиспользуется: это отдельный параметр заказчика с другой семантикой.
    /// </summary>
    public static class ErpParameters
    {
        public const string ErpCodeParamName = "Код 1C-ERP";
        public const string GroupName = "ERP";

        // GUID уже присутствует в пилотном файле заказчика. Для новых документов
        // создаём определение с тем же GUID, чтобы не плодить одноимённые параметры.
        public static readonly Guid ErpCodeParamGuid =
            new Guid("74688997-A66E-4BF8-A86A-8475E239419A");

        /// <summary>
        /// Гарантирует shared-параметр и экземплярную привязку для категорий
        /// переданных элементов. Если старый документ содержит одно одноимённое
        /// shared-определение с другим GUID, оно безопасно переиспользуется.
        /// </summary>
        public static Guid EnsureErpCodeParameterForItems(Document doc, IList<RevitItem> items)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (items == null || items.Count == 0) return ErpCodeParamGuid;

            Application app = doc.Application;
            SharedParameterElement shared = ResolveSharedParameter(doc, throwOnAmbiguousName: true);
            Definition definition;
            Guid actualGuid;

            if (shared != null)
            {
                definition = shared.GetDefinition();
                actualGuid = shared.GuidValue;
            }
            else
            {
                EnsureNoNonSharedNameConflict(doc);
                definition = GetOrCreateCanonicalDefinition(app);
                actualGuid = ErpCodeParamGuid;
            }

            CategorySet requiredCategories = app.Create.NewCategorySet();
            var categoryIds = new HashSet<int>();

            foreach (RevitItem item in items)
            {
                Element instance = doc.GetElement(item.ElementId);
                Category category = instance?.Category;
                if (category == null || !category.AllowsBoundParameters) continue;
                if (!categoryIds.Add(category.Id.IntegerValue)) continue;
                requiredCategories.Insert(category);
            }

            if (requiredCategories.Size == 0)
                throw new InvalidOperationException(
                    "Для элементов активной спецификации не найдено категорий, поддерживающих параметры.");

            BindingMap map = doc.ParameterBindings;
            ElementBinding existing = map.get_Item(definition) as ElementBinding;
            bool migrateFromType = existing is TypeBinding;

            if (existing is InstanceBinding)
            {
                bool allCategoriesBound = true;
                foreach (Category category in requiredCategories)
                {
                    if (existing.Categories.Contains(category)) continue;
                    allCategoriesBound = false;
                    break;
                }
                if (allCategoriesBound) return actualGuid;
            }

            CategorySet combinedCategories = app.Create.NewCategorySet();
            if (existing != null)
            {
                foreach (Category category in existing.Categories)
                {
                    if (category != null && category.AllowsBoundParameters)
                    {
                        categoryIds.Add(category.Id.IntegerValue);
                        combinedCategories.Insert(category);
                    }
                }
            }
            foreach (Category category in requiredCategories)
            {
                if (!combinedCategories.Contains(category))
                    combinedCategories.Insert(category);
            }

            Dictionary<ElementId, string> valuesToMigrate = migrateFromType
                ? SnapshotTypeValues(doc, actualGuid, categoryIds)
                : new Dictionary<ElementId, string>();

            using (Transaction transaction = new Transaction(doc, "Настройка параметра Код 1C-ERP"))
            {
                transaction.Start();

                InstanceBinding instanceBinding = app.Create.NewInstanceBinding(combinedCategories);
                bool bound = existing == null
                    ? map.Insert(definition, instanceBinding, BuiltInParameterGroup.PG_DATA)
                    : map.ReInsert(definition, instanceBinding, BuiltInParameterGroup.PG_DATA);

                if (!bound)
                {
                    transaction.RollBack();
                    throw new InvalidOperationException(
                        "Revit не разрешил экземплярную привязку параметра «Код 1C-ERP».");
                }

                doc.Regenerate();

                foreach (KeyValuePair<ElementId, string> pair in valuesToMigrate)
                {
                    if (string.IsNullOrWhiteSpace(pair.Value)) continue;
                    Element element = doc.GetElement(pair.Key);
                    Parameter parameter = element?.get_Parameter(actualGuid);
                    if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
                        parameter.Set(pair.Value);
                }

                transaction.Commit();
            }

            return actualGuid;
        }

        public static Guid ResolveErpCodeGuid(Document doc)
        {
            SharedParameterElement parameter = ResolveSharedParameter(doc, throwOnAmbiguousName: true);
            return parameter?.GuidValue ?? ErpCodeParamGuid;
        }

        public static Parameter GetCodeParameter(Element element, Guid parameterGuid)
        {
            if (element == null) return null;
            Parameter parameter = element.get_Parameter(parameterGuid);
            if (parameter != null) return parameter;

            // Новый документ до Ensure: разрешаем только однозначный shared-параметр.
            SharedParameterElement shared = ResolveSharedParameter(element.Document, throwOnAmbiguousName: false);
            return shared == null ? null : element.get_Parameter(shared.GuidValue);
        }

        public static string ReadCode(Element element, Guid parameterGuid)
        {
            Parameter parameter = GetCodeParameter(element, parameterGuid);
            if (parameter == null || parameter.StorageType != StorageType.String) return "";
            return (parameter.AsString() ?? "").Trim();
        }

        private static SharedParameterElement ResolveSharedParameter(Document doc, bool throwOnAmbiguousName)
        {
            if (doc == null) return null;

            List<SharedParameterElement> all = new FilteredElementCollector(doc)
                .OfClass(typeof(SharedParameterElement))
                .Cast<SharedParameterElement>()
                .ToList();

            SharedParameterElement canonical = all.FirstOrDefault(x => x.GuidValue == ErpCodeParamGuid);
            if (canonical != null)
            {
                if (!IsErpParameterName(canonical.Name))
                    throw new InvalidOperationException(
                        $"GUID ERP-параметра уже занят параметром «{canonical.Name}». Ожидалось «{ErpCodeParamName}».");
                if (all.Any(x => x.Id.IntegerValue != canonical.Id.IntegerValue &&
                                 IsErpParameterName(x.Name)))
                    throw new InvalidOperationException(
                        "Кроме канонического параметра найден одноимённый ERP-параметр с другим GUID. " +
                        "Автоматическая запись остановлена до устранения дубликата.");
                return canonical;
            }

            List<SharedParameterElement> sameName = all
                .Where(x => IsErpParameterName(x.Name))
                .ToList();

            if (sameName.Count == 1) return sameName[0];
            if (sameName.Count > 1 && throwOnAmbiguousName)
                throw new InvalidOperationException(
                    "В документе найдено несколько shared-параметров «Код 1C-ERP» с разными GUID. " +
                    "Автоматическая запись остановлена, чтобы не повредить данные.");
            return null;
        }

        private static void EnsureNoNonSharedNameConflict(Document doc)
        {
            bool conflict = new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterElement))
                .Cast<ParameterElement>()
                .Any(x => !(x is SharedParameterElement) &&
                          IsErpParameterName(x.Name));

            if (conflict)
                throw new InvalidOperationException(
                    "В документе уже есть не-shared параметр «Код 1C-ERP». " +
                    "Создание одноимённого shared-параметра остановлено во избежание неоднозначности.");
        }

        private static Dictionary<ElementId, string> SnapshotTypeValues(
            Document doc,
            Guid parameterGuid,
            HashSet<int> categoryIds)
        {
            var result = new Dictionary<ElementId, string>();
            foreach (Element instance in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (instance?.Category == null || !categoryIds.Contains(instance.Category.Id.IntegerValue))
                    continue;

                string value = ReadString(instance?.get_Parameter(parameterGuid));
                if (string.IsNullOrWhiteSpace(value))
                {
                    Element type = doc.GetElement(instance.GetTypeId());
                    value = ReadString(type?.get_Parameter(parameterGuid));
                }

                if (!string.IsNullOrWhiteSpace(value))
                    result[instance.Id] = value;
            }
            return result;
        }

        private static string ReadString(Parameter parameter)
        {
            return parameter != null && parameter.StorageType == StorageType.String
                ? (parameter.AsString() ?? "").Trim()
                : "";
        }

        private static bool IsErpParameterName(string name)
        {
            return string.Equals(name, ErpCodeParamName, StringComparison.Ordinal) ||
                   string.Equals(name, "Код 1С-ERP", StringComparison.Ordinal);
        }

        private static Definition GetOrCreateCanonicalDefinition(Application app)
        {
            string originalPath = app.SharedParametersFilename;
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RevitErpIntegration");
            string file = Path.Combine(folder, "ERPSharedParameters_74688997.txt");

            Directory.CreateDirectory(folder);
            if (!File.Exists(file) || new FileInfo(file).Length == 0)
            {
                File.WriteAllText(file,
                    "# This is a Revit shared parameter file.\r\n" +
                    "# Do not edit manually.\r\n" +
                    "*META\tVERSION\tMINVERSION\r\n" +
                    "META\t2\t1\r\n" +
                    "*GROUP\tID\tNAME\r\n" +
                    "*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\r\n");
            }

            try
            {
                app.SharedParametersFilename = file;
                DefinitionFile definitionFile = app.OpenSharedParameterFile();
                if (definitionFile == null)
                    throw new InvalidOperationException("Не удалось открыть служебный файл общих параметров ERP.");

                DefinitionGroup group = definitionFile.Groups.get_Item(GroupName) ??
                                        definitionFile.Groups.Create(GroupName);
                ExternalDefinition existing = group.Definitions.get_Item(ErpCodeParamName) as ExternalDefinition;
                if (existing != null)
                {
                    if (existing.GUID != ErpCodeParamGuid)
                        throw new InvalidOperationException(
                            "Служебный файл содержит ERP-параметр с неожиданным GUID.");
                    return existing;
                }

                var options = new ExternalDefinitionCreationOptions(ErpCodeParamName, ParameterType.Text)
                {
                    GUID = ErpCodeParamGuid,
                    Description = "Код номенклатуры 1C ERP",
                    UserModifiable = true
                };
                return group.Definitions.Create(options);
            }
            finally
            {
                app.SharedParametersFilename = originalPath;
            }
        }
    }
}
