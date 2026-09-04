using System;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitApi_3
{
    /// <summary>
    /// Читает значение поля с элемента только для установления связи
    /// «строка таблицы — ElementId». Для отображения и HTTP payload всегда
    /// используется фактический GetCellText из ScheduleMirrorBuilder.
    /// </summary>
    internal static class ScheduleFieldValueReader
    {
        public static string GetText(Document doc, Element instance, Element type, ScheduleField field)
        {
            if (doc == null || instance == null || field == null) return "";
            if (SafeIsCalculated(field) || SafeIsCombined(field)) return "";

            Parameter parameter = ResolveParameter(doc, instance, type, field);
            if (parameter == null) return "";

            try
            {
                string formatted = parameter.AsValueString();
                if (!string.IsNullOrWhiteSpace(formatted)) return formatted.Trim();
            }
            catch { }

            try
            {
                switch (parameter.StorageType)
                {
                    case StorageType.String:
                        return (parameter.AsString() ?? "").Trim();
                    case StorageType.Integer:
                        return parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
                    case StorageType.Double:
                        return parameter.AsDouble().ToString(CultureInfo.InvariantCulture);
                    case StorageType.ElementId:
                        ElementId id = parameter.AsElementId();
                        if (id == null || id == ElementId.InvalidElementId) return "";
                        Element referenced = doc.GetElement(id);
                        return referenced != null
                            ? (referenced.Name ?? "").Trim()
                            : id.IntegerValue.ToString(CultureInfo.InvariantCulture);
                    default:
                        return "";
                }
            }
            catch
            {
                return "";
            }
        }

        private static Parameter ResolveParameter(
            Document doc,
            Element instance,
            Element type,
            ScheduleField field)
        {
            Element source = field.FieldType == ScheduleFieldType.ElementType
                ? type ?? instance
                : instance;
            if (source == null) return null;

            try
            {
                ElementId parameterId = field.ParameterId;
                if (parameterId != null && parameterId != ElementId.InvalidElementId)
                {
                    if (parameterId.IntegerValue < 0)
                        return source.get_Parameter((BuiltInParameter)parameterId.IntegerValue);

                    ParameterElement parameterElement = doc.GetElement(parameterId) as ParameterElement;
                    Definition definition = parameterElement?.GetDefinition();
                    if (definition != null) return source.get_Parameter(definition);
                }
            }
            catch { }

            // Fallback допускается только при одном параметре с таким именем.
            // Это исключает недетерминированность LookupParameter.
            string name = "";
            try { name = field.GetName() ?? ""; } catch { }
            Parameter byName = GetUniqueByName(source, name);
            if (byName != null) return byName;

            string heading = "";
            try { heading = field.ColumnHeading ?? ""; } catch { }
            return string.Equals(name, heading, StringComparison.OrdinalIgnoreCase)
                ? null
                : GetUniqueByName(source, heading);
        }

        private static Parameter GetUniqueByName(Element element, string name)
        {
            if (element == null || string.IsNullOrWhiteSpace(name)) return null;
            try
            {
                var parameters = element.GetParameters(name);
                return parameters != null && parameters.Count == 1 ? parameters.Single() : null;
            }
            catch { return null; }
        }

        private static bool SafeIsCalculated(ScheduleField field)
        {
            try { return field.IsCalculatedField; }
            catch { return false; }
        }

        private static bool SafeIsCombined(ScheduleField field)
        {
            try { return field.IsCombinedParameterField; }
            catch { return false; }
        }
    }
}
