using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;

namespace WidgetRail.Playnite;

internal static class WireJson
{
    private static JavaScriptSerializer Serializer() => new JavaScriptSerializer
        { MaxJsonLength = IntegrationIdentity.MaximumResponseBytes, RecursionLimit = 24 };
    internal static LocalApiResponse Response(object value, int status = 200)
    {
        string json;
        try { json = Serializer().Serialize(value); }
        catch (InvalidOperationException) { throw new ApiException(413, "response_limit"); }
        if (Encoding.UTF8.GetByteCount(json) > IntegrationIdentity.MaximumResponseBytes) throw new ApiException(413, "response_limit");
        return new LocalApiResponse(status, Encoding.UTF8.GetBytes(json));
    }
    internal static Dictionary<string, object> Body(LocalApiRequest request)
    {
        if (request.Body.Length == 0) return new Dictionary<string, object>();
        try
        {
            var text = new UTF8Encoding(false, true).GetString(request.Body);
            return Serializer().DeserializeObject(text) as Dictionary<string, object> ?? throw new ApiException(400, "object_required");
        }
        catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is FormatException)
        { throw new ApiException(400, "invalid_json"); }
    }
    internal static string Text(object? value, int maximum = 256)
    {
        var text = value as string ?? "";
        return text.Length <= maximum ? text : text.Substring(0, maximum);
    }
    internal static int Integer(string? text, int fallback, int maximum)
    {
        if (text == null) return fallback;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0 || value > maximum)
            throw new ApiException(400, "invalid_range");
        return value;
    }
}

internal sealed class ApiException : Exception
{
    internal int Status { get; }
    internal string Code { get; }
    internal ApiException(int status, string code) : base(code) { Status = status; Code = code; }
}
