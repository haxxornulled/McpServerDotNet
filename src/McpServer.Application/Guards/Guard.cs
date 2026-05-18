using System.Runtime.CompilerServices;

namespace McpServer.Application.Guards
{
    public static class Guard
    {
        public static T NotNull<T>(T value, [CallerArgumentExpression("value")] string name = "")
        {
            if (value is null)
                throw new ArgumentNullException(name);
            return value;
        }

        public static string NotNullOrWhiteSpace(string value, [CallerArgumentExpression("value")] string name = "")
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Parameter {name} cannot be null or whitespace", name);
            return value;
        }

        public static T NotOutOfRange<T>(T value, T min, T max, [CallerArgumentExpression("value")] string name = "") 
            where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
                throw new ArgumentOutOfRangeException(name, $"Parameter {name} must be between {min} and {max}");
            return value;
        }

        public static T NotEmpty<T>(T value, [CallerArgumentExpression("value")] string name = "")
        {
            if (value is System.Collections.ICollection collection && collection.Count == 0)
                throw new ArgumentException($"Parameter {name} cannot be empty", name);
            
            if (value is string str && string.IsNullOrEmpty(str))
                throw new ArgumentException($"Parameter {name} cannot be null or empty", name);
                
            return value;
        }
    }
}
