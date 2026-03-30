using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CrestCreates.Infrastructure.Caching.Interceptors
{
    public interface ICacheKeyExpressionParser
    {
        string Parse(string expression, object[] args, ParameterInfo[] parameters);
        bool EvaluateCondition(string? condition, object[] args, ParameterInfo[] parameters);
    }

    public class CacheKeyExpressionParser : ICacheKeyExpressionParser
    {
        private static readonly Regex ParameterRegex = new Regex(@"#([a-zA-Z_][a-zA-Z0-9_]*)(?:\.([a-zA-Z_][a-zA-Z0-9_]*))?", RegexOptions.Compiled);

        public string Parse(string expression, object[] args, ParameterInfo[] parameters)
        {
            if (string.IsNullOrEmpty(expression))
                return string.Empty;

            var result = expression;
            var matches = ParameterRegex.Matches(expression);

            foreach (Match match in matches)
            {
                var paramName = match.Groups[1].Value;
                var propertyName = match.Groups[2].Success ? match.Groups[2].Value : null;

                var paramIndex = Array.FindIndex(parameters, p => p.Name == paramName);
                if (paramIndex < 0 || paramIndex >= args.Length)
                    continue;

                var value = args[paramIndex];
                string replacement;

                if (propertyName != null && value != null)
                {
                    replacement = GetPropertyValue(value, propertyName)?.ToString() ?? "null";
                }
                else
                {
                    replacement = value?.ToString() ?? "null";
                }

                result = result.Replace(match.Value, replacement);
            }

            return result;
        }

        private object? GetPropertyValue(object obj, string propertyName)
        {
            var property = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            return property?.GetValue(obj);
        }

        public bool EvaluateCondition(string? condition, object[] args, ParameterInfo[] parameters)
        {
            if (string.IsNullOrEmpty(condition))
                return true;

            var parsedCondition = Parse(condition, args, parameters);
            return !parsedCondition.Equals("false", StringComparison.OrdinalIgnoreCase) &&
                   !parsedCondition.Equals("0", StringComparison.OrdinalIgnoreCase) &&
                   !parsedCondition.Equals("null", StringComparison.OrdinalIgnoreCase);
        }
    }
}
