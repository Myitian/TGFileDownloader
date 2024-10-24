using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text;

namespace TGFileDownloader.CLI.SourceGenerator;

public readonly struct Model : IEquatable<Model>
{
    public readonly SimpleTypeInfo ContainingType;
    public readonly string FieldName;
    public readonly string PropertyAccessibility;
    public readonly string PropertyName;
    public readonly SimpleTypeInfo Type;

    public Model(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not IFieldSymbol symbol)
            throw new ArgumentException("target must be a field");
        if (symbol.Type is not INamedTypeSymbol type)
            throw new ArgumentException("target field must have a named type");
        Type = new(type);
        FieldName = symbol.Name;
        ContainingType = new(symbol.ContainingType);
        PropertyAccessibility = "";

        string? propName = null;
        AttributeData attr = context.Attributes[0];
        foreach (KeyValuePair<string, TypedConstant> kvp in attr.NamedArguments)
        {
            switch (kvp.Key)
            {
                case NonNullSetterGenerator.PropertyNameProperty:
                    propName = kvp.Value.Value as string;
                    break;
                case NonNullSetterGenerator.AccessibilityProperty:
                    FrozenSet<string> fset = ((kvp.Value.Value as string)?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? []).ToFrozenSet();
                    switch (fset.Count)
                    {
                        case 0:
                            PropertyAccessibility = "";
                            break;
                        case 1:
                            string value = fset.First();
                            PropertyAccessibility = value switch
                            {
                                "" => "",
                                "private" or "protected" or "internal" or "public" or "file" => $"{value} ",
                                _ => throw new NotSupportedException($"Accessibility \"{kvp.Value.Value}\" is not supported")
                            };
                            break;
                        case 2:
                            if (fset.Contains("protected"))
                                if (fset.Contains("private"))
                                {
                                    PropertyAccessibility = "protected private ";
                                    break;
                                }
                                else if (fset.Contains("internal"))
                                {
                                    PropertyAccessibility = "protected internal ";
                                    break;
                                }
                            goto default;
                        default:
                            throw new NotSupportedException($"Accessibility \"{kvp.Value.Value}\" is not supported");
                    }
                    break;
            }
        }
        if (propName is null)
        {
            Span<char> buffer = stackalloc char[FieldName.Length];
            FieldName.AsSpan().CopyTo(buffer);
            if (buffer.Length > 1 && buffer[0] == '_')
                buffer = buffer.Slice(1);
            if (buffer.Length > 0)
                buffer[0] = char.ToUpper(buffer[0]);
            PropertyName = buffer.ToString();
        }
        else
        {
            PropertyName = propName;
        }
    }

    public static bool operator !=(Model left, Model right)
        => !left.Equals(right);
    public static bool operator ==(Model left, Model right)
        => left.Equals(right);

    public readonly bool Equals(Model other)
        => Type == other.Type
        && FieldName == other.FieldName
        && PropertyName == other.PropertyName
        && PropertyAccessibility == other.PropertyAccessibility
        && ContainingType == other.ContainingType;
    public override readonly bool Equals(object obj)
        => obj is Model other
        && Equals(other);
    public override readonly int GetHashCode()
        => Type.GetHashCode()
        ^ FieldName.GetHashCode()
        ^ PropertyName.GetHashCode()
        ^ PropertyAccessibility.GetHashCode()
        ^ ContainingType.GetHashCode();
}

public readonly struct SimpleTypeInfo(INamedTypeSymbol symbol) : IEquatable<SimpleTypeInfo>
{
    public readonly Box<SimpleTypeInfo>? ContainingType
        = symbol.ContainingType is null ? null : new(new(symbol.ContainingType));
    public readonly ImmutableArray<string> GenericParameters
        = symbol.TypeParameters.Sort((a, b) => a.Ordinal.CompareTo(b.Ordinal)).Select(s => s.Name).ToImmutableArray();
    public readonly bool IsRecord
        = symbol.IsRecord;
    public readonly bool IsStruct
        = symbol.IsValueType;
    public readonly string Name
        = symbol.Name;
    public readonly string? Namespace
        = symbol.ContainingNamespace?.ToDisplayString();
    public readonly bool NullableMark
        = symbol.NullableAnnotation is NullableAnnotation.Annotated;

    public static bool operator !=(SimpleTypeInfo left, SimpleTypeInfo right)
        => !left.Equals(right);
    public static bool operator ==(SimpleTypeInfo left, SimpleTypeInfo right)
        => left.Equals(right);

    public bool Equals(SimpleTypeInfo other)
        => Namespace == other.Namespace
        && Name == other.Name
        && IsStruct == other.IsStruct
        && IsRecord == other.IsRecord
        && NullableMark == other.NullableMark
        && ContainingType?.Value == other.ContainingType?.Value
        && GenericParameters.SequenceEqual(other.GenericParameters);
    public override bool Equals(object obj)
        => obj is SimpleTypeInfo other
        && Equals(other);
    public override int GetHashCode()
        => (Namespace?.GetHashCode() ?? 0)
        ^ Name.GetHashCode()
        ^ (ContainingType?.Value.GetHashCode() ?? 0);
    public override string ToString()
        => new StringBuilder().Append(this, true).ToString();
    public string ToMetadataString()
        => new StringBuilder().AppendMetadata(this, true).ToString();
}

public class Box<T>(T value) where T : struct
{
    public T Value { get; set; } = value;
    public static implicit operator Box<T>(T value)
        => new(value);
    public static implicit operator T(Box<T> box)
        => box.Value;
}

[Generator]
public class NonNullSetterGenerator : IIncrementalGenerator
{
    public const string AccessibilityProperty = "Accessibility";
    public const string GeneratedNamespace = "TGFileDownloader.CLI.Generated";
    public const string NonNullSetterAttribute = "NonNullSetterAttribute";
    public const string PropertyNameProperty = "PropertyName";
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
#if DEBUG
        // Debugger.Launch();
#endif
        context.RegisterPostInitializationOutput(static postInitializationContext =>
            postInitializationContext.AddSource("NonNullSetterAttribute.g.cs", SourceText.From($$"""
            // <auto-generated />
            using System;
            namespace {{GeneratedNamespace}}
            {
                [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
                public sealed class {{NonNullSetterAttribute}} : Attribute
                {
                    public string {{PropertyNameProperty}} { get; set; }
                    public string {{AccessibilityProperty}} { get; set; }
                }
            }
            """, Encoding.UTF8)));

        const string attrName = $"{GeneratedNamespace}.{NonNullSetterAttribute}";
        IncrementalValuesProvider<Model> pipeline = context.SyntaxProvider.ForAttributeWithMetadataName(
            fullyQualifiedMetadataName: attrName,
            predicate: static (syntaxNode, _) => syntaxNode is VariableDeclaratorSyntax,
            transform: static (context, _) => new Model(context));

        context.RegisterSourceOutput(pipeline, static (context, model) =>
        {
            string code = CreateCSharpCode(model, true);
            context.AddSource(new StringBuilder()
                .AppendMetadata(model.ContainingType)
                .Append('_')
                .Append(model.PropertyName)
                .Append(".g.cs")
                .ToString(),
                code);
        });
    }
    static string CreateCSharpCode(Model model, bool nullable)
    {
        StringBuilder sb = new();
        int depth = 0;
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        if (!string.IsNullOrWhiteSpace(model.ContainingType.Namespace))
        {
            sb.Append("namespace ")
                .AppendLine(model.ContainingType.Namespace)
                .AppendLine("{");
            depth++;
        }
        CreateTypeDefCSharpCode(model.ContainingType, sb, ref depth);
        sb.Append(' ', depth * 4)
            .Append(model.PropertyAccessibility)
            .Append("global::")
            .Append(model.Type, true, nullable)
            .Append(' ')
            .AppendLine(model.PropertyName);
        sb.Append(' ', depth * 4)
            .AppendLine("{");
        depth++;
        sb.Append(' ', depth * 4)
            .Append("get => ")
            .Append(model.FieldName)
            .AppendLine(";");
        sb.Append(' ', depth * 4)
            .AppendLine("set");
        sb.Append(' ', depth * 4)
            .AppendLine("{");
        depth++;
        sb.Append(' ', depth * 4)
            .AppendLine("if (value is not null)");
        sb.Append(' ', (depth + 1) * 4)
            .Append(model.FieldName)
            .AppendLine(" = value;");
        depth--;
        sb.Append(' ', depth * 4)
            .AppendLine("}");
        depth--;
        sb.Append(' ', depth * 4)
            .AppendLine("}");
        sb.AppendLine();
        while (depth > 0)
        {
            depth--;
            sb.Append(' ', depth * 4)
                .AppendLine("}");
        }
        return sb.ToString();
    }
    static void CreateTypeDefCSharpCode(SimpleTypeInfo type, StringBuilder sb, ref int depth)
    {
        if (type.ContainingType is not null)
        {
            CreateTypeDefCSharpCode(type.ContainingType.Value, sb, ref depth);
        }
        sb.Append(' ', depth * 4)
            .Append("partial ")
            .Append(type.IsRecord ? "record " : "")
            .Append(type.IsStruct ? "struct " : "class ")
            .Append(type, false)
            .AppendLine();
        sb.Append(' ', depth * 4)
            .AppendLine("{");
        depth++;
    }
}
public static class Extension
{
    public static StringBuilder Append(this StringBuilder sb, SimpleTypeInfo typeInfo, bool recursive = true, bool nullable = false)
    {
        if (recursive)
        {
            if (typeInfo.ContainingType is not null)
                sb.Append(typeInfo.ContainingType.Value, true).Append('.');
            else if (typeInfo.Namespace is not null)
                sb.Append(typeInfo.Namespace).Append('.');
        }
        sb.Append(typeInfo.Name);
        if (typeInfo.GenericParameters.Length > 0)
        {
            sb.Append('<');
            bool first = true;
            foreach (string param in typeInfo.GenericParameters)
            {
                if (first)
                    first = false;
                else
                    sb.Append(", ");
                sb.Append(param);
            }
            sb.Append('>');
        }
        if (nullable && typeInfo.NullableMark)
            sb.Append('?');
        return sb;
    }
    public static StringBuilder AppendMetadata(this StringBuilder sb, SimpleTypeInfo typeInfo, bool recursive = true)
    {
        if (recursive)
        {
            if (typeInfo.ContainingType is not null)
                sb.AppendMetadata(typeInfo.ContainingType.Value, true).Append('.');
            else if (typeInfo.Namespace is not null)
                sb.Append(typeInfo.Namespace).Append('.');
        }
        sb.Append(typeInfo.Name);
        if (typeInfo.GenericParameters.Length > 0)
            sb.Append('`').Append(typeInfo.GenericParameters.Length);
        return sb;
    }
}