using LanguageExt;

namespace McpServer.Application.Abstractions
{
    public interface ICryptographyService
    {
        string Encrypt(string plainText);
        string Decrypt(string cipherText);
        string Hash(string input);
        bool VerifyHash(string input, string hash);
    }
}