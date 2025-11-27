using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.ApplicationServices;

namespace RevitApi_3
{
    public static class ErpParameters
    {
        public const string ErpCodeParamName = "Код 1C-ERP";
        public const string GroupName = "ERP";

        public const string EndpointParamName = "ERP_Endpoint";
        public const string DocTitleParamName = "ERP_DocumentTitle";

        /// <summary>
        /// Гарантирует существование shared-определения "Код 1C-ERP"
        /// и привязку ко ВСЕМ категориям, в которых встречаются переданные RevitItem.
        /// </summary>
        public static void EnsureErpCodeParameterForItems(Document doc, IList<RevitItem> items)
        {
            if (items == null || items.Count == 0)
                return;

            Application app = doc.Application;

            // 1. Берём (или создаём) определение параметра
            Definition def = GetOrCreateErpDefinition(app);

            // 2. Собираем набор категорий, с которыми реально работаем
            CategorySet catSet = app.Create.NewCategorySet();
            var usedIds = new HashSet<int>();

            foreach (var ri in items)
            {
                Element type = doc.GetElement(ri.TypeId);
                if (type == null || type.Category == null)
                    continue;

                Category cat = type.Category;
                int catId = cat.Id.IntegerValue;

                if (usedIds.Contains(catId))
                    continue;

                usedIds.Add(catId);
                catSet.Insert(cat);
            }

            if (catSet.Size == 0)
                return;

            // 3. Привязываем параметр к этим категориям
            BindingMap map = doc.ParameterBindings;
            ElementBinding existing = map.get_Item(def) as ElementBinding;

            using (Transaction t = new Transaction(doc, "Bind ERP code parameter"))
            {
                t.Start();

                if (existing == null)
                {
                    TypeBinding binding = app.Create.NewTypeBinding(catSet);
                    map.Insert(def, binding, BuiltInParameterGroup.PG_DATA);
                }
                else
                {
                    foreach (Category c in catSet)
                    {
                        if (!existing.Categories.Contains(c))
                            existing.Categories.Insert(c);
                    }

                    map.ReInsert(def, existing, BuiltInParameterGroup.PG_DATA);
                }

                t.Commit();
            }
        }

        private static Definition GetOrCreateErpDefinition(Application app)
        {
            string originalPath = app.SharedParametersFilename;

            bool useTempFile = string.IsNullOrEmpty(originalPath) || !File.Exists(originalPath);
            string tempFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "RevitErpIntegration");
            string tempFile = Path.Combine(tempFolder, "ERPSharedParameters.txt");

            if (useTempFile)
            {
                Directory.CreateDirectory(tempFolder);
                if (!File.Exists(tempFile))
                    File.WriteAllText(tempFile, string.Empty);

                app.SharedParametersFilename = tempFile;
            }

            DefinitionFile defFile = app.OpenSharedParameterFile();
            if (defFile == null)
                throw new InvalidOperationException("Не удалось открыть файл общих параметров ERP.");

            DefinitionGroup group = defFile.Groups.get_Item(GroupName) ?? defFile.Groups.Create(GroupName);

            Definition def = group.Definitions.get_Item(ErpCodeParamName);
            if (def == null)
            {
                ExternalDefinitionCreationOptions opt =
                    new ExternalDefinitionCreationOptions(ErpCodeParamName, ParameterType.Text);
                def = group.Definitions.Create(opt);
            }

            if (useTempFile)
            {
                app.SharedParametersFilename = originalPath;
            }

            return def;
        }
    }
}
