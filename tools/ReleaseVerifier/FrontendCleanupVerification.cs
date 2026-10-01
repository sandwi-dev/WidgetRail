using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// A targeted regression guard for dotnet/runtime#131088: ILLink constant
// propagation removed a Monitor.Exit from an async finally in the shipped IL.
// Read the actual publication, without loading it or starting any application.
// Matching call counts detect this regression; they are not a general proof of
// exception-handler correctness or balanced locking on every control-flow path.
internal static class FrontendCleanupVerification
{
    private const string Method = "WidgetPresentationSession.RefreshInvalidationsAsync.MoveNext";

    internal static void Verify(string directory)
    {
        var path = Path.Combine(directory, "WidgetPresentationSession.dll");
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var methods = new List<MethodDefinition>();
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            if (!metadata.GetString(type.Name).StartsWith("<RefreshInvalidationsAsync>d__", StringComparison.Ordinal)) continue;
            var ownerHandle = type.GetDeclaringType();
            if (ownerHandle.IsNil) continue;
            var owner = metadata.GetTypeDefinition(ownerHandle);
            if (metadata.GetString(owner.Name) != "WidgetPresentationSession" ||
                metadata.GetString(owner.Namespace) != "WidgetRail.WidgetPresentationSession") continue;
            foreach (var methodHandle in type.GetMethods())
            {
                var method = metadata.GetMethodDefinition(methodHandle);
                if (metadata.GetString(method.Name) == "MoveNext") methods.Add(method);
            }
        }
        if (methods.Count != 1 || methods[0].RelativeVirtualAddress == 0)
            throw new InvalidOperationException($"Cannot inspect {Method} in {path}: expected exactly one state-machine body. Update this publication guard if the implementation was intentionally renamed or refactored.");

        // Only reflect on framework opcode descriptors, never on the published assembly.
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opcode => unchecked((ushort)opcode.Value));
        var il = pe.GetMethodBody(methods[0].RelativeVirtualAddress).GetILReader();
        var enters = 0;
        var exits = 0;
        while (il.RemainingBytes > 0)
        {
            var offset = il.Offset;
            ushort code = il.ReadByte();
            if (code == 0xfe) code = (ushort)(0xfe00 | il.ReadByte());
            if (!opcodes.TryGetValue(code, out var opcode))
                throw new BadImageFormatException($"Unknown opcode at IL_{offset:x4} in {Method}.");
            if (opcode.OperandType == OperandType.InlineMethod)
            {
                var token = MetadataTokens.EntityHandle(il.ReadInt32());
                if (opcode == OpCodes.Call || opcode == OpCodes.Callvirt)
                {
                    var name = MonitorMethodName(metadata, token);
                    if (name == "Enter") enters++;
                    else if (name == "Exit") exits++;
                }
                continue;
            }
            var bytes = opcode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or
                    OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or
                    OperandType.InlineType or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => SwitchBytes(ref il),
                _ => throw new BadImageFormatException($"Unsupported operand at IL_{offset:x4} in {Method}.")
            };
            if (bytes > il.RemainingBytes)
                throw new BadImageFormatException($"Truncated operand at IL_{offset:x4} in {Method}.");
            il.Offset += bytes;
        }
        if (enters == 0 || enters != exits)
            throw new InvalidOperationException($"Unsafe published async cleanup in {path}: {Method} has Monitor.Enter={enters}, Monitor.Exit={exits}; expected matching nonzero counts. ILLink can remove async-finally cleanup (dotnet/runtime#131088). Keep _TrimmerIPConstProp=false until a fixed toolchain is qualified, then republish; do not distribute this frontend.");
        Console.WriteLine($"Verified published async cleanup: {Method}, Monitor.Enter={enters}, Monitor.Exit={exits}.");
    }

    private static int SwitchBytes(ref BlobReader il)
    {
        var count = il.ReadInt32();
        if (count < 0 || count > il.RemainingBytes / sizeof(int))
            throw new BadImageFormatException($"Invalid switch operand in {Method}.");
        return count * sizeof(int);
    }

    private static string? MonitorMethodName(MetadataReader metadata, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.MethodSpecification)
            handle = metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
        if (handle.Kind != HandleKind.MemberReference) return null;
        var member = metadata.GetMemberReference((MemberReferenceHandle)handle);
        if (member.Parent.Kind != HandleKind.TypeReference) return null;
        var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
        return metadata.GetString(type.Namespace) == "System.Threading" && metadata.GetString(type.Name) == "Monitor"
            ? metadata.GetString(member.Name) : null;
    }
}
