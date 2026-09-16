using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PropertyDriver.Generator
{
    /// <summary>
    /// <c>PropertyDriverExperiments.DrivenPropertyAttribute</c> が付いた field ごとに、それを含む partial 型へ以下を生成する。
    ///   public const string {Name}Path = "{シリアライズ上の field 名}";
    ///   public static readonly string[] DrivenPropertyPaths = { ... };
    /// 生成する文字列は Unity のシリアライズプロパティパス（field 名）で、
    /// SerializedObject.FindProperty や DrivenPropertyManager.RegisterProperty にそのまま渡せる。
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class DrivenPropertyGenerator : IIncrementalGenerator
    {
        public const string AttributeFullName = "PropertyDriverExperiments.DrivenPropertyAttribute";
        const string SerializeFieldFullName = "UnityEngine.SerializeField";
        const string NonSerializedFullName = "System.NonSerializedAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var fields = context.SyntaxProvider.ForAttributeWithMetadataName(
                AttributeFullName,
                static (node, _) => node is VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax },
                static (ctx, _) => ctx.TargetSymbol as IFieldSymbol)
                .Where(static f => f is not null)
                .Select(static (f, _) => f!);

            var grouped = fields.Collect();

            context.RegisterSourceOutput(grouped, static (spc, all) => Execute(spc, all));
        }

        static void Execute(SourceProductionContext context, ImmutableArray<IFieldSymbol> fields)
        {
            if (fields.IsDefaultOrEmpty) return;

            foreach (var group in fields.GroupBy<IFieldSymbol, INamedTypeSymbol>(f => f.ContainingType, SymbolEqualityComparer.Default))
            {
                var type = group.Key;
                var typeFields = group.OrderBy(f => f.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0).ToList();

                if (!IsPartial(type))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Diagnostics.TypeNotPartial, type.Locations.FirstOrDefault(), type.Name));
                    continue;
                }

                var entries = new List<(string constName, string fieldName)>();
                var seen = new Dictionary<string, IFieldSymbol>();
                var valid = true;

                foreach (var field in typeFields)
                {
                    var location = field.Locations.FirstOrDefault();

                    if (field.IsStatic || field.IsConst)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.FieldStatic, location, field.Name));
                        valid = false;
                        continue;
                    }

                    if (!IsUnitySerialized(field))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.FieldNotSerialized, location, field.Name));
                        valid = false;
                        continue;
                    }

                    var constName = ToConstName(field.Name);
                    if (seen.TryGetValue(constName, out var other))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Diagnostics.NameCollision, location, field.Name, other.Name, constName));
                        valid = false;
                        continue;
                    }

                    seen[constName] = field;
                    entries.Add((constName, field.Name));
                }

                if (!valid || entries.Count == 0) continue;

                context.AddSource(HintName(type), SourceText.From(Emit(type, entries), Encoding.UTF8));
            }
        }

        static bool IsPartial(INamedTypeSymbol type)
        {
            foreach (var reference in type.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is TypeDeclarationSyntax decl && !decl.Modifiers.Any(SyntaxKind.PartialKeyword))
                    return false;
            }
            // ネストした型は、外側の型も全て partial である必要がある
            return type.ContainingType == null || IsPartial(type.ContainingType);
        }

        static bool IsUnitySerialized(IFieldSymbol field)
        {
            var hasSerializeField = false;
            foreach (var attr in field.GetAttributes())
            {
                var name = attr.AttributeClass?.ToDisplayString();
                if (name == NonSerializedFullName) return false;
                if (name == SerializeFieldFullName) hasSerializeField = true;
            }
            return field.DeclaredAccessibility == Accessibility.Public || hasSerializeField;
        }

        /// <summary>定数名の元になる名前。m_Value -> Value、_value -> Value、value -> Value。</summary>
        static string ToConstName(string fieldName)
        {
            var name = fieldName;
            if (name.StartsWith("m_") && name.Length > 2) name = name.Substring(2);
            else if (name.StartsWith("_") && name.Length > 1) name = name.Substring(1);
            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        static string HintName(INamedTypeSymbol type)
        {
            var full = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty).Replace('<', '_').Replace('>', '_').Replace(',', '_').Replace(' ', '_');
            return full + ".DrivenProperty.g.cs";
        }

        static string Emit(INamedTypeSymbol type, List<(string constName, string fieldName)> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//     PropertyDriver.Generator が生成したファイル。手で編集しないこと。");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine("#nullable disable");
            sb.AppendLine();

            var ns = type.ContainingNamespace;
            var hasNamespace = ns is { IsGlobalNamespace: false };
            var indent = 0;
            if (hasNamespace)
            {
                sb.AppendLine($"namespace {ns.ToDisplayString()}");
                sb.AppendLine("{");
                indent++;
            }

            // 外側の型から順に partial 宣言を開く
            var chain = new List<INamedTypeSymbol>();
            for (var t = type; t != null; t = t.ContainingType) chain.Insert(0, t);
            foreach (var t in chain)
            {
                sb.Append(' ', indent * 4).AppendLine($"partial {TypeKeyword(t)} {t.Name}{TypeParameters(t)}");
                sb.Append(' ', indent * 4).AppendLine("{");
                indent++;
            }

            foreach (var (constName, fieldName) in entries)
            {
                sb.Append(' ', indent * 4).AppendLine($"/// <summary><see cref=\"{fieldName}\"/> のシリアライズプロパティパス。</summary>");
                sb.Append(' ', indent * 4).AppendLine($"public const string {constName}Path = \"{fieldName}\";");
            }

            sb.AppendLine();
            sb.Append(' ', indent * 4).AppendLine("/// <summary>[DrivenProperty] が付いた全 field のシリアライズプロパティパス。</summary>");
            sb.Append(' ', indent * 4).AppendLine("public static readonly string[] DrivenPropertyPaths =");
            sb.Append(' ', indent * 4).AppendLine("{");
            foreach (var (constName, _) in entries)
                sb.Append(' ', (indent + 1) * 4).AppendLine($"{constName}Path,");
            sb.Append(' ', indent * 4).AppendLine("};");

            for (var i = chain.Count - 1; i >= 0; i--)
            {
                indent--;
                sb.Append(' ', indent * 4).AppendLine("}");
            }
            if (hasNamespace) sb.AppendLine("}");

            return sb.ToString();
        }

        static string TypeKeyword(INamedTypeSymbol t) =>
            t.TypeKind == TypeKind.Struct ? (t.IsRecord ? "record struct" : "struct")
            : t.IsRecord ? "record"
            : "class";

        static string TypeParameters(INamedTypeSymbol t) =>
            t.TypeParameters.Length == 0 ? string.Empty : "<" + string.Join(", ", t.TypeParameters.Select(p => p.Name)) + ">";
    }

    static class Diagnostics
    {
        const string Category = "PropertyDriver";

        public static readonly DiagnosticDescriptor TypeNotPartial = new(
            "PD0001", "Type must be partial",
            "Type '{0}' contains [DrivenProperty] fields but is not declared partial (all containing types must be partial too)",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor FieldStatic = new(
            "PD0002", "Field must be an instance field",
            "Field '{0}' is static or const; only instance fields can be Unity-serialized and driven",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor FieldNotSerialized = new(
            "PD0003", "Field is not Unity-serialized",
            "Field '{0}' is not serialized by Unity: it must be public or marked [SerializeField], and must not be [NonSerialized]",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NameCollision = new(
            "PD0004", "Generated constant name collision",
            "Fields '{0}' and '{1}' both map to the generated constant '{2}Path'",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
    }
}
