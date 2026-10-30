using System;
using System.Collections.Generic;
using System.Globalization;

namespace DSPQualitySystem.Save
{
    /// <summary>
    /// Document-level validation for the quality save file. Kept free of BepInEx/Unity/game types
    /// so the offline test suite can link this file and exercise the exact checks used by
    /// QualitySave.Load. A non-null return value means "ignore the whole file": quality points are
    /// silently harmless to lose, but applying another save's points would corrupt the game.
    /// </summary>
    public static class SaveGuard
    {
        public const int SchemaVersion = 1;

        /// <summary>Validate a parsed document against the save slot that is being loaded.</summary>
        /// <returns>null when compatible, otherwise a short reason (stable log wording).</returns>
        public static string ValidateDocument(Dictionary<string, object> root, string expectedGameName, int expectedSeed)
        {
            if (root == null)
            {
                return "root is not an object";
            }

            string versionError = ValidateVersion(ReadInt(root, "version"));
            if (versionError != null)
            {
                return versionError;
            }

            string nameError = ValidateGameName(ReadString(root, "gameName"), expectedGameName);
            if (nameError != null)
            {
                return nameError;
            }

            return ValidateSeed(ReadInt(root, "seed"), expectedSeed);
        }

        public static string ValidateVersion(int fileVersion)
        {
            if (fileVersion == SchemaVersion)
            {
                return null;
            }
            return "unsupported schema version " + fileVersion;
        }

        public static string ValidateGameName(string fileGameName, string expectedGameName)
        {
            // Legacy/unknown values are tolerated: only a positive mismatch is fatal.
            if (string.IsNullOrEmpty(fileGameName) || string.IsNullOrEmpty(expectedGameName))
            {
                return null;
            }
            if (string.Equals(fileGameName, expectedGameName, StringComparison.Ordinal))
            {
                return null;
            }
            return "gameName mismatch: file='" + fileGameName + "' expected='" + expectedGameName + "'";
        }

        public static string ValidateSeed(int fileSeed, int expectedSeed)
        {
            // Seed 0 means "not recorded / not available" (legacy file or no game data yet).
            if (fileSeed == 0 || expectedSeed == 0)
            {
                return null;
            }
            if (fileSeed == expectedSeed)
            {
                return null;
            }
            return "seed mismatch: file=" + fileSeed + " expected=" + expectedSeed;
        }

        public static string ReadString(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }
            return value as string;
        }

        public static int ReadInt(Dictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return 0;
            }
            if (value is long)
            {
                long l = (long)value;
                if (l > int.MaxValue)
                {
                    return int.MaxValue;
                }
                if (l < int.MinValue)
                {
                    return int.MinValue;
                }
                return (int)l;
            }
            if (value is double)
            {
                return (int)Math.Round((double)value);
            }
            if (value is string)
            {
                int parsed;
                if (int.TryParse((string)value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    return parsed;
                }
            }
            return 0;
        }
    }
}
