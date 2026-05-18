using System.Text;

namespace McpServer.Application.Files.Utils
{
    public static class EncodingHelper
    {
        public static Encoding GetEncoding(string? encodingName)
        {
            if (string.IsNullOrEmpty(encodingName))
            {
                return Encoding.UTF8;
            }

            try
            {
                return Encoding.GetEncoding(encodingName);
            }
            catch
            {
                // Log warning but continue with UTF-8
                return Encoding.UTF8;
            }
        }
    }
}