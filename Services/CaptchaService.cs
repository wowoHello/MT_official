using System.Security.Cryptography;
using System.Text;

/**
 * CaptchaService
 * 使用 C# 產生純 SVG 驗證碼，完全不依賴 JavaScript / Canvas。
 * 特色：
 * 1. 產生隨機 6 位字串（密碼學安全隨機，不可預測）。
 * 2. 輸出為 Base64 SVG 字串，可直接綁定至 <img> 標籤。
 * 3. 包含隨機干擾線與噪點。
 */

namespace MT.Services;

public interface ICaptchaService
{
    (string Text, string ImageBase64) GenerateCaptcha();
}

public class CaptchaService : ICaptchaService
{
    private const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";

    public (string Text, string ImageBase64) GenerateCaptcha()
    {
        string text = GenerateRandomString(6);
        string svg = GenerateSvg(text);
        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
        return (text, $"data:image/svg+xml;base64,{base64}");
    }

    private static string GenerateRandomString(int length)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < length; i++)
        {
            sb.Append(Chars[RandomNumberGenerator.GetInt32(Chars.Length)]);
        }
        return sb.ToString();
    }

    private static string GenerateSvg(string text)
    {
        const int width = 140;
        const int height = 42;
        var sb = new StringBuilder();

        // SVG Header
        sb.Append($@"<svg width=""{width}"" height=""{height}"" xmlns=""http://www.w3.org/2000/svg"">");

        // Background
        sb.Append(@"<rect width=""100%"" height=""100%"" fill=""#f8fafc"" />");

        // Interference Lines
        for (int i = 0; i < 6; i++)
        {
            int x1 = RandomNumberGenerator.GetInt32(width);
            int y1 = RandomNumberGenerator.GetInt32(height);
            int x2 = RandomNumberGenerator.GetInt32(width);
            int y2 = RandomNumberGenerator.GetInt32(height);
            string color = $"rgb({RandomNumberGenerator.GetInt32(256)},{RandomNumberGenerator.GetInt32(256)},{RandomNumberGenerator.GetInt32(256)})";
            sb.Append($@"<line x1=""{x1}"" y1=""{y1}"" x2=""{x2}"" y2=""{y2}"" stroke=""{color}"" stroke-width=""1"" opacity=""0.5"" />");
        }

        // Noise Dots
        for (int i = 0; i < 40; i++)
        {
            int cx = RandomNumberGenerator.GetInt32(width);
            int cy = RandomNumberGenerator.GetInt32(height);
            string color = $"rgb({RandomNumberGenerator.GetInt32(256)},{RandomNumberGenerator.GetInt32(256)},{RandomNumberGenerator.GetInt32(256)})";
            sb.Append($@"<circle cx=""{cx}"" cy=""{cy}"" r=""1"" fill=""{color}"" opacity=""0.7"" />");
        }

        // Text
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            int x = 15 + (i * 20);
            int y = 28 + RandomNumberGenerator.GetInt32(-3, 4);
            int rotate = RandomNumberGenerator.GetInt32(-15, 16);
            // SVG rotate is (angle, cx, cy)
            sb.Append($@"<text x=""{x}"" y=""{y}"" font-family=""Courier New, monospace"" font-weight=""bold"" font-size=""24"" fill=""#374151"" transform=""rotate({rotate}, {x}, {y})"">{c}</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }
}
