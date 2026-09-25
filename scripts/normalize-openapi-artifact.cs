// normalize-openapi-artifact.cs — publish-time normalizer for the captured
// v1.public OpenAPI document (PF-1).
//
// Usage:
//   dotnet run scripts/normalize-openapi-artifact.cs <input.json> <output.json>
//
// What it does (deliberately minimal and deterministic):
//   1. Parses the document captured from a running host's
//      GET /openapi/v1.public.json.
//   2. Removes the top-level `servers` object — it echoes the base URL of
//      whichever process exported it (127.0.0.1:5108 here, a real domain in
//      staging), so keeping it would make the committed artifact depend on
//      the exporting machine. Consumers pair this contract with their own
//      configured platform base URL.
//   3. Validates the document is an OpenAPI 3.x document with at least one
//      path (fails loudly rather than publishing an empty/broken artifact).
//   4. Writes it with System.Text.Json (insertion order preserved, 2-space
//      indent, trailing newline) so the output is byte-identical no matter
//      which OS or shell ran the export.
//
// A .NET 10 file-based app on purpose: every contributor to this repo has
// the .NET SDK, and both export scripts (sh + ps1) call this same tool, so
// there is exactly one serializer behind the published artifact.

#:property Nullable=enable

using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: dotnet run scripts/normalize-openapi-artifact.cs <input.json> <output.json>");
    return 2;
}

var (inputPath, outputPath) = (args[0], args[1]);

JsonNode document;
try
{
    document = JsonNode.Parse(await File.ReadAllTextAsync(inputPath))
        ?? throw new InvalidOperationException("document parsed to null");
}
catch (Exception ex) when (ex is JsonException or InvalidOperationException)
{
    Console.Error.WriteLine($"error: {inputPath} is not valid JSON: {ex.Message}");
    return 1;
}

if (document is not JsonObject root)
{
    Console.Error.WriteLine($"error: {inputPath} is not a JSON object");
    return 1;
}

if (root["openapi"]?.GetValue<string>() is not { } version || !version.StartsWith("3.", StringComparison.Ordinal))
{
    Console.Error.WriteLine($"error: expected an OpenAPI 3.x document, found '{root["openapi"]}'");
    return 1;
}

if (root["paths"] is not JsonObject paths || paths.Count == 0)
{
    Console.Error.WriteLine("error: document has no paths — refusing to publish an empty contract");
    return 1;
}

root.Remove("servers");

var options = new JsonSerializerOptions { WriteIndented = true };
// LF explicitly: JsonSerializer's indented writer emits \r\n on Windows and
// \n elsewhere, which would make the artifact blob differ by exporter OS —
// and CI diffs this file byte-for-byte. Normalize every newline to LF.
var json = document.ToJsonString(options).Replace("\r\n", "\n", StringComparison.Ordinal);
await File.WriteAllTextAsync(outputPath, json + "\n");

Console.WriteLine($"valid OpenAPI {version} — {paths.Count} paths — servers stripped");
return 0;
