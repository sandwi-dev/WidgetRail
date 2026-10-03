using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// A targeted regression guard for dotnet/runtime#131088: ILLink constant
// propagation removed a Monitor.Exit from an async finally in the shipped IL.
// Count either direct Monitor cleanup or the session's PresentationGate scopes.
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

        var (enters, exits) = CleanupCounts(pe, metadata, methods[0]);
        // A scope call alone is insufficient if trimming damages its implementation.
        foreach (var typeHandle in metadata.TypeDefinitions.Where(handle => IsGateScope(metadata, handle)))
        {
            var scope = metadata.GetTypeDefinition(typeHandle);
            var disposals = scope.GetMethods().Select(metadata.GetMethodDefinition)
                .Where(method => metadata.GetString(method.Name) == "Dispose").ToArray();
            if (disposals.Length != 1 || CleanupCounts(pe, metadata, disposals[0]) != (0, 1))
                throw new InvalidOperationException("Published PresentationGate.Scope.Dispose must retain exactly one Monitor.Exit.");
        }
        if (enters == 0 || enters != exits)
            throw new InvalidOperationException($"Unsafe published async cleanup in {path}: {Method} has lock acquisition={enters}, lock release={exits}; expected matching nonzero counts. ILLink can remove async-finally cleanup (dotnet/runtime#131088). Keep _TrimmerIPConstProp=false until a fixed toolchain is qualified, then republish; do not distribute this frontend.");
        Console.WriteLine($"Verified published async cleanup: {Method}, lock acquisition={enters}, lock release={exits}.");
    }

    private static (int Enters, int Exits) CleanupCounts(PEReader pe, MetadataReader metadata, MethodDefinition method)
    {
        // Only reflect on framework opcode descriptors, never on the published assembly.
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opcode => unchecked((ushort)opcode.Value));
        var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
        var enters = 0;
        var exits = 0;
        EntityHandle constrained = default;
        while (il.RemainingBytes > 0)
        {
            var offset = il.Offset;
            ushort code = il.ReadByte();
            if (code == 0xfe) code = (ushort)(0xfe00 | il.ReadByte());
            if (!opcodes.TryGetValue(code, out var opcode))
                throw new BadImageFormatException($"Unknown opcode at IL_{offset:x4} in {Method}.");
            if (opcode == OpCodes.Constrained)
            {
                constrained = MetadataTokens.EntityHandle(il.ReadInt32());
                continue;
            }
            if (opcode.OperandType == OperandType.InlineMethod)
            {
                var token = MetadataTokens.EntityHandle(il.ReadInt32());
                if (opcode == OpCodes.Call || opcode == OpCodes.Callvirt)
                {
                    var name = CleanupMethodName(metadata, token, constrained);
                    if (name == "Enter") enters++;
                    else if (name == "Exit") exits++;
                }
                constrained = default;
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
        return (enters, exits);
    }

    private static int SwitchBytes(ref BlobReader il)
    {
        var count = il.ReadInt32();
        if (count < 0 || count > il.RemainingBytes / sizeof(int))
            throw new BadImageFormatException($"Invalid switch operand in {Method}.");
        return count * sizeof(int);
    }

    private static string? CleanupMethodName(MetadataReader metadata, EntityHandle handle, EntityHandle constrained)
    {
        if (handle.Kind == HandleKind.MethodSpecification)
            handle = metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            var method = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
            var type = metadata.GetTypeDefinition(method.GetDeclaringType());
            var name = metadata.GetString(method.Name);
            if (metadata.GetString(type.Namespace) == "WidgetRail.WidgetPresentationSession" &&
                metadata.GetString(type.Name) == "PresentationGate" && name == "Enter") return "Enter";
            if (metadata.GetString(type.Name) == "Scope" && name == "Dispose" && !type.GetDeclaringType().IsNil)
            {
                var parent = metadata.GetTypeDefinition(type.GetDeclaringType());
                if (metadata.GetString(parent.Namespace) == "WidgetRail.WidgetPresentationSession" &&
                    metadata.GetString(parent.Name) == "PresentationGate") return "Exit";
            }
            return null;
        }
        if (handle.Kind != HandleKind.MemberReference) return null;
        var member = metadata.GetMemberReference((MemberReferenceHandle)handle);
        if (member.Parent.Kind != HandleKind.TypeReference) return null;
        var reference = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
        if (metadata.GetString(reference.Namespace) == "System" && metadata.GetString(reference.Name) == "IDisposable" &&
            metadata.GetString(member.Name) == "Dispose" && IsGateScope(metadata, constrained)) return "Exit";
        return metadata.GetString(reference.Namespace) == "System.Threading" && metadata.GetString(reference.Name) == "Monitor"
            ? metadata.GetString(member.Name) : null;
    }
    private static bool IsGateScope(MetadataReader metadata, EntityHandle handle)
    {
        if (handle.Kind != HandleKind.TypeDefinition) return false;
        var type = metadata.GetTypeDefinition((TypeDefinitionHandle)handle);
        if (metadata.GetString(type.Name) != "Scope" || type.GetDeclaringType().IsNil) return false;
        var parent = metadata.GetTypeDefinition(type.GetDeclaringType());
        return metadata.GetString(parent.Namespace) == "WidgetRail.WidgetPresentationSession" &&
            metadata.GetString(parent.Name) == "PresentationGate";
    }

}
