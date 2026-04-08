using System.Text;
using System.Text.Json;

namespace BSEBResultBlockchainAPI.Helpers
{
    public static class QrUtility
    {
        // Base85 character set (RFC 1924 / Ascii85 variant)
        private static readonly string Base85Chars ="0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz!#$%&()*+-;<=>?@^_`{|}~";

        public static string GenerateEncrypteForstudentdata(object studentData)
        {
            // Serialize to JSON
            var json = JsonSerializer.Serialize(studentData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Convert to bytes
            var bytes = Encoding.UTF8.GetBytes(json);

            // Encode using Base85
            return EncodeBase85(bytes);
        }

        private static string EncodeBase85(byte[] data)
        {
            var sb = new StringBuilder();
            int padding = (4 - data.Length % 4) % 4;
            var paddedData = new byte[data.Length + padding];
            Buffer.BlockCopy(data, 0, paddedData, 0, data.Length);

            for (int i = 0; i < paddedData.Length; i += 4)
            {
                uint value = ((uint)paddedData[i] << 24)
                           | ((uint)paddedData[i + 1] << 16)
                           | ((uint)paddedData[i + 2] << 8)
                           | paddedData[i + 3];

                var chunk = new char[5];
                for (int j = 4; j >= 0; j--)
                {
                    chunk[j] = Base85Chars[(int)(value % 85)];
                    value /= 85;
                }
                sb.Append(chunk);
            }

            // Remove padding chars from end
            return sb.ToString().Substring(0, sb.Length - padding);
        }

        public static string DecodeBase85ToString(string encoded)
        {
            var bytes = DecodeBase85(encoded);
            return Encoding.UTF8.GetString(bytes);
        }

        private static byte[] DecodeBase85(string encoded)
        {
            int padding = (5 - encoded.Length % 5) % 5;
            encoded = encoded.PadRight(encoded.Length + padding, Base85Chars[0]);

            var result = new List<byte>();
            for (int i = 0; i < encoded.Length; i += 5)
            {
                uint value = 0;
                for (int j = 0; j < 5; j++)
                {
                    int idx = Base85Chars.IndexOf(encoded[i + j]);
                    if (idx < 0) throw new FormatException($"Invalid Base85 char: {encoded[i + j]}");
                    value = value * 85 + (uint)idx;
                }
                result.Add((byte)(value >> 24));
                result.Add((byte)(value >> 16));
                result.Add((byte)(value >> 8));
                result.Add((byte)value);
            }

            // Remove padding bytes
            return result.Take(result.Count - padding).ToArray();
        }

        public static object? DecryptStudentData(string encrypted)
        {
            try
            {
                var json = DecodeBase85ToString(encrypted);

                // Step 1: Trim unwanted chars
                json = json.Trim();

                // Step 2: Remove trailing garbage like '|'
                json = json.TrimEnd('|');

                // Step 3: FIX broken JSON structure
                json = FixJson(json);

                return JsonSerializer.Deserialize<object>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                throw new Exception("Decryption failed", ex);
            }
        }

        private static string FixJson(string json)
        {
            int openCurly = json.Count(c => c == '{');
            int closeCurly = json.Count(c => c == '}');

            int openSquare = json.Count(c => c == '[');
            int closeSquare = json.Count(c => c == ']');

            // Add missing closing brackets
            if (closeSquare < openSquare)
            {
                json += new string(']', openSquare - closeSquare);
            }

            if (closeCurly < openCurly)
            {
                json += new string('}', openCurly - closeCurly);
            }

            return json;
        }
    }
}