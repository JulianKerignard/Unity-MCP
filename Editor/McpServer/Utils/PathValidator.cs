using System;
using UnityEngine;

namespace McpUnity.Utils
{
    /// <summary>
    /// Utility class for validating and sanitizing file paths
    /// Prevents path traversal attacks and ensures paths stay within project bounds
    /// </summary>
    public static class PathValidator
    {
        /// <summary>
        /// Sanitize and validate a file path for security
        /// </summary>
        /// <param name="path">The path to validate</param>
        /// <param name="requiredPrefix">Required prefix (default: "Assets/")</param>
        /// <returns>Sanitized path</returns>
        /// <exception cref="ArgumentException">If path is invalid or potentially malicious</exception>
        public static string SanitizePath(string path, string requiredPrefix = "Assets/")
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Path cannot be empty");

            // Normalize path separators
            path = path.Replace("\\", "/");

            // Block path traversal attempts
            if (path.Contains(".."))
                throw new ArgumentException("Path traversal (..) is not allowed for security reasons");

            // Verify path starts with required prefix
            if (!path.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Path must be within '{requiredPrefix}' folder");

            // Block absolute paths outside project
            if (path.StartsWith("/") && !path.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Absolute paths outside project are not allowed");

            return path;
        }

        /// <summary>
        /// Validate a path without throwing (returns success/failure)
        /// </summary>
        /// <param name="path">Path to validate</param>
        /// <param name="sanitizedPath">Output sanitized path if valid</param>
        /// <param name="errorMessage">Output error message if invalid</param>
        /// <param name="requiredPrefix">Required prefix</param>
        /// <returns>True if valid, false otherwise</returns>
        public static bool TryValidatePath(string path, out string sanitizedPath, out string errorMessage, string requiredPrefix = "Assets/")
        {
            sanitizedPath = null;
            errorMessage = null;

            try
            {
                sanitizedPath = SanitizePath(path, requiredPrefix);
                return true;
            }
            catch (ArgumentException ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Validate that a path has a specific file extension
        /// </summary>
        /// <param name="path">Path to check</param>
        /// <param name="extension">Required extension (e.g., ".unity", ".prefab")</param>
        /// <returns>True if path has the extension</returns>
        public static bool HasExtension(string path, string extension)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Ensure a directory path ends with a separator
        /// </summary>
        public static string EnsureDirectorySeparator(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return path.EndsWith("/") ? path : path + "/";
        }

        /// <summary>
        /// Get the directory portion of a path
        /// </summary>
        public static string GetDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            var lastSep = path.LastIndexOf('/');
            return lastSep > 0 ? path.Substring(0, lastSep) : string.Empty;
        }

        /// <summary>
        /// Combine two path segments safely
        /// </summary>
        public static string CombinePaths(string basePath, string relativePath)
        {
            basePath = EnsureDirectorySeparator(basePath);
            if (relativePath.StartsWith("/"))
                relativePath = relativePath.Substring(1);
            return basePath + relativePath;
        }
    }
}
