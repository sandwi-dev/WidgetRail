using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace WidgetRail.Playnite;

/// <summary>Only the new integration's own bounded files; never Bridge's files.</summary>
public static class IntegrationFiles
{
    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (stream.Length > 16384) throw new InvalidDataException("Integration metadata exceeds its limit.");
            // A second bound protects a file that grows after Length was read.
            var bytes = new byte[16385]; var count = 0;
            while (count < bytes.Length)
            {
                var next = stream.Read(bytes, count, bytes.Length - count);
                if (next == 0) break;
                count += next;
            }
            if (count > 16384) throw new InvalidDataException("Integration metadata exceeds its limit.");
            using (var input = new MemoryStream(bytes, 0, count, false))
                return (T?)new DataContractJsonSerializer(typeof(T)).ReadObject(input);
        }
    }
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void ClearOwnedDescriptor(string path, string instanceId)
    {
        // Serialize writers through the plugin instance's profile lease. A stale
        // instance must not erase the descriptor of a replacement server.
        var current = Read<ConnectionDescriptor>(path);
        if (current?.InstanceId == instanceId) File.Delete(path);
    }
}
