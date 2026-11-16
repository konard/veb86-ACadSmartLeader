// Lists types, members and enum values of AutoCAD .NET reference assemblies without AutoCAD:
// reads ECMA-335 metadata only (System.Reflection.Metadata), nothing is loaded or executed.
// Usage: dotnet run -- <assembly.dll> <type-name-regex> [member-regex]
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

var path = args[0];
var typeFilter = new Regex(args[1]);
var memberFilter = args.Length > 2 ? new Regex(args[2]) : null;
using var stream = File.OpenRead(path);
using var pe = new PEReader(stream);
var md = pe.GetMetadataReader();
foreach (var th in md.TypeDefinitions)
{
    var t = md.GetTypeDefinition(th);
    var full = md.GetString(t.Namespace) + "." + md.GetString(t.Name);
    if (!typeFilter.IsMatch(full)) continue;
    if ((t.Attributes & System.Reflection.TypeAttributes.VisibilityMask) is not (System.Reflection.TypeAttributes.Public or System.Reflection.TypeAttributes.NestedPublic)) continue;
    string baseName = "";
    if (!t.BaseType.IsNil && t.BaseType.Kind == HandleKind.TypeReference) baseName = md.GetString(md.GetTypeReference((TypeReferenceHandle)t.BaseType).Name);
    else if (!t.BaseType.IsNil && t.BaseType.Kind == HandleKind.TypeDefinition) baseName = md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)t.BaseType).Name);
    Console.WriteLine($"type {full} : {baseName}");
    foreach (var fh in t.GetFields())
    {
        var f = md.GetFieldDefinition(fh);
        var name = md.GetString(f.Name);
        if (name == "value__" || (memberFilter != null && !memberFilter.IsMatch(name))) continue;
        if ((f.Attributes & System.Reflection.FieldAttributes.Public) == 0) continue;
        string value = "";
        var ch = f.GetDefaultValue();
        if (!ch.IsNil)
        {
            var c = md.GetConstant(ch);
            var br = md.GetBlobReader(c.Value);
            value = " = " + c.TypeCode switch
            {
                ConstantTypeCode.Int32 => br.ReadInt32().ToString(),
                ConstantTypeCode.Int16 => br.ReadInt16().ToString(),
                ConstantTypeCode.Byte => br.ReadByte().ToString(),
                ConstantTypeCode.UInt32 => br.ReadUInt32().ToString(),
                ConstantTypeCode.Int64 => br.ReadInt64().ToString(),
                _ => c.TypeCode.ToString(),
            };
        }
        Console.WriteLine($"  field {name}{value}");
    }
    foreach (var ph in t.GetProperties())
    {
        var name = md.GetString(md.GetPropertyDefinition(ph).Name);
        if (memberFilter == null || memberFilter.IsMatch(name)) Console.WriteLine($"  property {name}");
    }
    foreach (var eh in t.GetEvents())
    {
        var name = md.GetString(md.GetEventDefinition(eh).Name);
        if (memberFilter == null || memberFilter.IsMatch(name)) Console.WriteLine($"  event {name}");
    }
    foreach (var mh in t.GetMethods())
    {
        var m = md.GetMethodDefinition(mh);
        var name = md.GetString(m.Name);
        if ((m.Attributes & System.Reflection.MethodAttributes.Public) == 0 || (m.Attributes & System.Reflection.MethodAttributes.MemberAccessMask) != System.Reflection.MethodAttributes.Public) continue;
        if (name.StartsWith("get_") || name.StartsWith("set_") || name.StartsWith("add_") || name.StartsWith("remove_")) continue;
        if (memberFilter == null || memberFilter.IsMatch(name)) Console.WriteLine($"  method {name}({m.GetParameters().Count})");
    }
}
