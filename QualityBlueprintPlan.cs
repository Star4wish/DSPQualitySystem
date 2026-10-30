using System;

namespace DSPQualitySystem
{
    /// <summary>
    /// v0.11.2 (task-13 B / P5 stage A): pure helpers that carry a building's quality points through
    /// blueprints by appending TWO ints to the vanilla <c>BlueprintBuilding.parameters</c> array:
    ///
    ///     ... vanilla parameters (0..k-1) ... | Magic | inc
    ///
    /// <c>parameters</c> is serialized by <c>BlueprintBuilding.Export/Import</c> as an int16 length
    /// followed by that many int32 values, so appending is length-prefixed and safe: vanilla
    /// Import/SetParameters/FromParamsArray all read length-first and ignore trailing extras.
    /// <c>Magic</c> is a fixed non-zero marker so an unreadable tail is simply ignored.
    /// </summary>
    public static class QualityBlueprintPlan
    {
        /// <summary>Marker value written before the inc ("QSQ" + 1). Never produced by vanilla.</summary>
        public const int Magic = 0x51535101;

        /// <summary>Largest int16 length the vanilla blueprint Import can read back.</summary>
        public const int MaxParamCount = 32765;

        /// <summary>
        /// Returns a copy of <paramref name="parameters"/> with Magic+inc appended, or the same array
        /// when inc &lt;= 0 or the marker is already present with the same value (idempotent). Returns
        /// null when appending would overflow the int16 length, so the caller leaves vanilla untouched.
        /// </summary>
        public static int[] Append(int[] parameters, int inc)
        {
            if (inc < 0)
            {
                inc = 0;
            }
            if (inc == 0)
            {
                return parameters;
            }
            if (parameters == null)
            {
                parameters = new int[0];
            }
            int n = parameters.Length;
            if (n >= MaxParamCount)
            {
                return null;
            }
            if (n >= 2 && parameters[n - 2] == Magic)
            {
                if (parameters[n - 1] == inc)
                {
                    return parameters;
                }
                // Re-copying the same building must refresh the stored value, not stack a second pair.
                int[] replaced = (int[])parameters.Clone();
                replaced[n - 1] = inc;
                return replaced;
            }

            int[] result = new int[n + 2];
            Array.Copy(parameters, result, n);
            result[n] = Magic;
            result[n + 1] = inc;
            return result;
        }

        /// <summary>
        /// Reads Magic+inc from the tail of a parameter array. <paramref name="count"/> is the logical
        /// length (0 or a stale value falls back to the array length). Never throws; any mismatch or
        /// non-finite value returns false and inc = 0 so the caller behaves exactly like vanilla.
        /// </summary>
        public static bool TryRead(int[] parameters, int count, out int inc)
        {
            inc = 0;
            try
            {
                if (parameters == null)
                {
                    return false;
                }
                int n = count;
                if (n <= 0 || n > parameters.Length)
                {
                    n = parameters.Length;
                }
                if (n < 2 || parameters[n - 2] != Magic)
                {
                    return false;
                }
                int value = parameters[n - 1];
                if (value < 0)
                {
                    return false;
                }
                inc = value;
                return true;
            }
            catch (Exception)
            {
                inc = 0;
                return false;
            }
        }
    }
}