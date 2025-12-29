using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace McpUnity.Helpers
{
    /// <summary>
    /// Safe argument parsing utilities for MCP Unity Server.
    /// All methods use TryParse internally and never throw exceptions on invalid input.
    /// </summary>
    public static class ArgumentParser
    {
        #region String Methods

        /// <summary>
        /// Get a string value from the arguments dictionary.
        /// </summary>
        /// <param name="args">The arguments dictionary</param>
        /// <param name="key">The key to look up</param>
        /// <param name="defaultValue">Default value if key is missing or null</param>
        /// <returns>The string value or defaultValue</returns>
        public static string GetString(Dictionary<string, object> args, string key, string defaultValue = null)
        {
            if (args == null || string.IsNullOrEmpty(key))
                return defaultValue;

            if (!args.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            return value.ToString();
        }

        /// <summary>
        /// Require a string value from the arguments dictionary.
        /// </summary>
        /// <param name="args">The arguments dictionary</param>
        /// <param name="key">The key to look up</param>
        /// <param name="error">Error message if validation fails, null otherwise</param>
        /// <returns>The string value or null if missing</returns>
        public static string RequireString(Dictionary<string, object> args, string key, out string error)
        {
            error = null;

            if (args == null)
            {
                error = "Arguments dictionary is null";
                return null;
            }

            if (string.IsNullOrEmpty(key))
            {
                error = "Key parameter is null or empty";
                return null;
            }

            if (!args.TryGetValue(key, out var value))
            {
                error = $"Required parameter '{key}' is missing";
                return null;
            }

            if (value == null)
            {
                error = $"Required parameter '{key}' is null";
                return null;
            }

            var stringValue = value.ToString();
            if (string.IsNullOrWhiteSpace(stringValue))
            {
                error = $"Required parameter '{key}' is empty or whitespace";
                return null;
            }

            return stringValue;
        }

        #endregion

        #region Integer Methods

        /// <summary>
        /// Get an integer value from the arguments dictionary using safe TryParse.
        /// </summary>
        /// <param name="args">The arguments dictionary</param>
        /// <param name="key">The key to look up</param>
        /// <param name="defaultValue">Default value if key is missing or parsing fails</param>
        /// <returns>The parsed integer or defaultValue</returns>
        public static int GetInt(Dictionary<string, object> args, string key, int defaultValue = 0)
        {
            if (args == null || string.IsNullOrEmpty(key))
                return defaultValue;

            if (!args.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            // Handle if already an integer type
            if (value is int intVal)
                return intVal;
            if (value is long longVal)
                return (int)Math.Clamp(longVal, int.MinValue, int.MaxValue);
            if (value is double doubleVal)
                return (int)Math.Round(Math.Clamp(doubleVal, int.MinValue, int.MaxValue));
            if (value is float floatVal)
                return (int)Math.Round(Math.Clamp(floatVal, int.MinValue, int.MaxValue));

            // Parse from string representation
            var stringValue = value.ToString();
            if (string.IsNullOrWhiteSpace(stringValue))
                return defaultValue;

            // Try parsing as integer
            if (int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                return result;

            // Try parsing as double and convert (handles "1.0" style inputs)
            if (double.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleResult))
                return (int)Math.Round(Math.Clamp(doubleResult, int.MinValue, int.MaxValue));

            return defaultValue;
        }

        /// <summary>
        /// Get an integer value from the arguments dictionary, clamped to a range.
        /// </summary>
        /// <param name="args">The arguments dictionary</param>
        /// <param name="key">The key to look up</param>
        /// <param name="defaultValue">Default value if key is missing or parsing fails</param>
        /// <param name="min">Minimum allowed value (inclusive)</param>
        /// <param name="max">Maximum allowed value (inclusive)</param>
        /// <returns>The parsed integer clamped to [min, max] or defaultValue</returns>
        public static int GetIntClamped(Dictionary<string, object> args, string key, int defaultValue, int min, int max)
        {
            var value = GetInt(args, key, defaultValue);
            return Math.Clamp(value, min, max);
        }

        #endregion

        #region Float Methods

        /// <summary>
        /// Get a float value from the arguments dictionary using safe TryParse.
        /// </summary>
        /// <param name="args">The arguments dictionary</param>
        /// <param name="key">The key to look up</param>
        /// <param name="defaultValue">Default value if key is missing or parsing fails</param>
        /// <returns>The parsed float or defaultValue</returns>
        public static float GetFloat(Dictionary<string, object> args, string key, float defaultValue = 0f)
        {
            if (args == null || string.IsNullOrEmpty(key))
                return defaultValue;

            if (!args.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            // Handle if already a numeric type
            if (value is float floatVal)
                return floatVal;
            if (value is double doubleVal)
                return (float)doubleVal;
            if (value is int intVal)
                return intVal;
            if (value is long longVal)
                return longVal;

            // Parse from string representation
            var stringValue = value.ToString();
            if (string.IsNullOrWhiteSpace(stringValue))
                return defaultValue;

            if (float.TryParse(stringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                return result;

            return defaultValue;
        }

        /// <summary>
        /// Get a float value from the arguments dictionary, clamped to a range.
        /// </summary>
        /// <param name="args">The arguments dictionary</param>
        /// <param name="key">The key to look up</param>
        /// <param name="defaultValue">Default value if key is missing or parsing fails</param>
        /// <param name="min">Minimum allowed value (inclusive)</param>
        /// <param name="max">Maximum allowed value (inclusive)</param>
        /// <returns>The parsed float clamped to [min, max] or defaultValue</returns>
        public static float GetFloatClamped(Dictionary<string, object> args, string key, float defaultValue, float min, float max)
        {
            var value = GetFloat(args, key, defaultValue);
            return Math.Clamp(value, min, max);
        }

        #endregion

        #region Boolean Methods

        /// <summary>
        /// Get a boolean value from the arguments dictionary.
        /// Handles various representations: true/false, "true"/"false", 1/0, "1"/"0", "yes"/"no"
        /// </summary>
        /// <param name="args">The arguments dictionary</param>
        /// <param name="key">The key to look up</param>
        /// <param name="defaultValue">Default value if key is missing or parsing fails</param>
        /// <returns>The parsed boolean or defaultValue</returns>
        public static bool GetBool(Dictionary<string, object> args, string key, bool defaultValue = false)
        {
            if (args == null || string.IsNullOrEmpty(key))
                return defaultValue;

            if (!args.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            // Handle if already a boolean
            if (value is bool boolVal)
                return boolVal;

            // Handle numeric values (0 = false, non-zero = true)
            if (value is int intVal)
                return intVal != 0;
            if (value is long longVal)
                return longVal != 0;
            if (value is double doubleVal)
                return Math.Abs(doubleVal) > double.Epsilon;
            if (value is float floatVal)
                return Math.Abs(floatVal) > float.Epsilon;

            // Parse from string representation
            var stringValue = value.ToString();
            if (string.IsNullOrWhiteSpace(stringValue))
                return defaultValue;

            // Try standard bool parsing
            if (bool.TryParse(stringValue, out var result))
                return result;

            // Handle common string representations
            var lowerValue = stringValue.Trim().ToLowerInvariant();
            switch (lowerValue)
            {
                case "1":
                case "yes":
                case "on":
                case "enabled":
                    return true;
                case "0":
                case "no":
                case "off":
                case "disabled":
                    return false;
                default:
                    return defaultValue;
            }
        }

        #endregion

        #region Enum Methods

        /// <summary>
        /// Get an enum value from the arguments dictionary.
        /// Supports both string names and integer values.
        /// </summary>
        /// <typeparam name="T">The enum type</typeparam>
        /// <param name="args">The arguments dictionary</param>
        /// <param name="key">The key to look up</param>
        /// <param name="defaultValue">Default value if key is missing or parsing fails</param>
        /// <returns>The parsed enum value or defaultValue</returns>
        public static T GetEnum<T>(Dictionary<string, object> args, string key, T defaultValue) where T : struct, Enum
        {
            if (args == null || string.IsNullOrEmpty(key))
                return defaultValue;

            if (!args.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            // Handle if already the correct enum type
            if (value is T enumVal)
                return enumVal;

            // Handle integer values
            if (value is int intVal)
            {
                if (Enum.IsDefined(typeof(T), intVal))
                    return (T)Enum.ToObject(typeof(T), intVal);
                return defaultValue;
            }

            // Parse from string representation
            var stringValue = value.ToString();
            if (string.IsNullOrWhiteSpace(stringValue))
                return defaultValue;

            // Try parsing as enum name (case-insensitive)
            if (Enum.TryParse<T>(stringValue, ignoreCase: true, out var result))
                return result;

            // Try parsing as integer string
            if (int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intResult))
            {
                if (Enum.IsDefined(typeof(T), intResult))
                    return (T)Enum.ToObject(typeof(T), intResult);
            }

            return defaultValue;
        }

        #endregion

        #region Array Methods

        /// <summary>
        /// Get a string array from the arguments dictionary.
        /// Handles various input formats: arrays, lists, comma-separated strings.
        /// </summary>
        /// <param name="args">The arguments dictionary</param>
        /// <param name="key">The key to look up</param>
        /// <returns>The string array or empty array if not found/invalid</returns>
        public static string[] GetStringArray(Dictionary<string, object> args, string key)
        {
            if (args == null || string.IsNullOrEmpty(key))
                return Array.Empty<string>();

            if (!args.TryGetValue(key, out var value) || value == null)
                return Array.Empty<string>();

            // Handle if already a string array
            if (value is string[] stringArray)
                return stringArray;

            // Handle object array
            if (value is object[] objArray)
                return objArray.Where(o => o != null).Select(o => o.ToString()).ToArray();

            // Handle IEnumerable types (List<string>, List<object>, etc.)
            if (value is IEnumerable<string> stringEnumerable)
                return stringEnumerable.ToArray();

            if (value is System.Collections.IEnumerable enumerable && !(value is string))
            {
                var list = new List<string>();
                foreach (var item in enumerable)
                {
                    if (item != null)
                        list.Add(item.ToString());
                }
                return list.ToArray();
            }

            // Handle comma-separated string
            if (value is string stringValue && !string.IsNullOrWhiteSpace(stringValue))
            {
                return stringValue
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToArray();
            }

            return Array.Empty<string>();
        }

        /// <summary>
        /// Get an integer array from the arguments dictionary.
        /// </summary>
        /// <param name="args">The arguments dictionary</param>
        /// <param name="key">The key to look up</param>
        /// <returns>The integer array or empty array if not found/invalid</returns>
        public static int[] GetIntArray(Dictionary<string, object> args, string key)
        {
            var stringArray = GetStringArray(args, key);
            var result = new List<int>();

            foreach (var item in stringArray)
            {
                if (int.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
                    result.Add(intVal);
            }

            return result.ToArray();
        }

        /// <summary>
        /// Get a float array from the arguments dictionary.
        /// </summary>
        /// <param name="args">The arguments dictionary</param>
        /// <param name="key">The key to look up</param>
        /// <returns>The float array or empty array if not found/invalid</returns>
        public static float[] GetFloatArray(Dictionary<string, object> args, string key)
        {
            var stringArray = GetStringArray(args, key);
            var result = new List<float>();

            foreach (var item in stringArray)
            {
                if (float.TryParse(item, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatVal))
                    result.Add(floatVal);
            }

            return result.ToArray();
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Check if a key exists in the arguments dictionary and has a non-null value.
        /// </summary>
        /// <param name="args">The arguments dictionary</param>
        /// <param name="key">The key to check</param>
        /// <returns>True if the key exists and has a non-null value</returns>
        public static bool HasKey(Dictionary<string, object> args, string key)
        {
            if (args == null || string.IsNullOrEmpty(key))
                return false;

            return args.TryGetValue(key, out var value) && value != null;
        }

        /// <summary>
        /// Try to get a value of a specific type from the arguments dictionary.
        /// </summary>
        /// <typeparam name="T">The expected type</typeparam>
        /// <param name="args">The arguments dictionary</param>
        /// <param name="key">The key to look up</param>
        /// <param name="result">The result if successful</param>
        /// <returns>True if the value was found and is of the expected type</returns>
        public static bool TryGetValue<T>(Dictionary<string, object> args, string key, out T result)
        {
            result = default;

            if (args == null || string.IsNullOrEmpty(key))
                return false;

            if (!args.TryGetValue(key, out var value) || value == null)
                return false;

            if (value is T typedValue)
            {
                result = typedValue;
                return true;
            }

            return false;
        }

        #endregion
    }
}
