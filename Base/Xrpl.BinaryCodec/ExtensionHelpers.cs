using System;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Xrpl.BinaryCodec
{
    public static class ExtensionHelpers
    {
        public class NumFunc
        {
            public int Precision { get; set; }
            public int Exponent { get; set; }
            public ulong Mantissa { get; set; }
        }
        public static NumFunc NumFuncAll(this string input)
        {
            string regex = Types.IouValue.ValueRegex;
            var match = Regex.Match(input, regex);
            if (!match.Success)
            {
                throw new BinaryCodecException($"Value {input} must match {regex}");
            }
            var numberGroup = match.Groups[2];
            var fractionGroup = match.Groups[4];
            var exponentGroup = match.Groups[6];
            var exponent = 0;
            var mantissaString = numberGroup.Success ? numberGroup.Value : "";
            if (fractionGroup.Success)
            {
                var fraction = fractionGroup.Value;
                mantissaString += fraction;
                exponent -= fraction.Length;
            }
            if (exponentGroup.Success)
            {
                exponent += int.Parse(exponentGroup.Value);
            }

            mantissaString = mantissaString.TrimStart('0');
            var trimmed = mantissaString.TrimEnd('0');
            if (trimmed.Length == 0)
            {
                trimmed = "0";
                mantissaString = "0";
            }
            return new NumFunc()
            {
                Precision = trimmed.Length,
                Exponent = mantissaString == "0" ? 0 : exponent + mantissaString.Length - trimmed.Length,
                Mantissa = ulong.Parse(trimmed)
            };
        }
        public static int Exponent(this string input)
        {
            string regex = Types.IouValue.ValueRegex;
            var match = Regex.Match(input, regex);
            if (!match.Success)
            {
                throw new BinaryCodecException($"Value {input} must match {regex}");
            }
            var numberGroup = match.Groups[2];
            var fractionGroup = match.Groups[4];
            var exponentGroup = match.Groups[6];
            var exponent = 0;
            var mantissaString = numberGroup.Success ? numberGroup.Value : "";
            if (fractionGroup.Success)
            {
                var fraction = fractionGroup.Value;
                mantissaString += fraction;
                exponent -= fraction.Length;
            }
            if (exponentGroup.Success)
            {
                exponent += int.Parse(exponentGroup.Value);
            }
            mantissaString = mantissaString.TrimStart('0');
            var trimmed = mantissaString.TrimEnd('0');
            if (trimmed.Length == 0)
            {
                return 0;
            }
            // The value is trimmed * 10^(exponent + trailing zeros): the written exponent and the fraction shift it too.
            return exponent + mantissaString.Length - trimmed.Length;
        }

        public static int Precision(this string input)
        {
            string regex = Types.IouValue.ValueRegex;
            var match = Regex.Match(input, regex);
            if (!match.Success)
            {
                throw new BinaryCodecException($"Value {input} must match {regex}");
            }
            var numberGroup = match.Groups[2];
            var fractionGroup = match.Groups[4];
            var mantissaString = numberGroup.Success ? numberGroup.Value : "";
            if (fractionGroup.Success)
            {
                var fraction = fractionGroup.Value;
                mantissaString += fraction;
            }
            mantissaString = mantissaString.TrimStart('0');
            var trimmed = mantissaString.TrimEnd('0');
            if (trimmed.Length == 0)
            {
                trimmed = "0";
            }
            return trimmed.Length;
        }
    }
}

