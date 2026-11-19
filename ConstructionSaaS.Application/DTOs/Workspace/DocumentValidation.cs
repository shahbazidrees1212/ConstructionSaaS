using System.Text;
namespace ConstructionSaaS.Application.DTOs.Workspace;
public static class DocumentValidation
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    public static string? ContentType(string name, byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaximumBytes) return null;
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension == ".pdf" && bytes.AsSpan().StartsWith("%PDF-"u8)) return "application/pdf";
        if (extension == ".png" && bytes.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10})) return "image/png";
        if ((extension == ".jpg" || extension == ".jpeg") && bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return "image/jpeg";
        if (extension is ".txt" or ".csv")
        {
            try
            {
                var text = new UTF8Encoding(false, true).GetString(bytes);
                if (text.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')) return null;
                return extension == ".csv" ? "text/csv" : "text/plain";
            }
            catch (DecoderFallbackException) { return null; }
        }
        return null;
    }
}
