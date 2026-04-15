using System.Security.Cryptography;
using System.Text;

namespace TechGloss.Infrastructure.Logging;

public static class MaskingLogger
{
    public static string HashText(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..8];
    }
}
