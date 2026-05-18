using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ICryptographyProvider
    {
        string Encrypt(string plainText);
        string Decrypt(string cipherText);
        string Hash(string input);
        bool VerifyHash(string input, string hash);
    }
}