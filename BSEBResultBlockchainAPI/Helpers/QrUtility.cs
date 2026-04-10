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

            for (int i = 0; i < data.Length; i += 4)
            {
                int remaining = Math.Min(4, data.Length - i);

                byte b1 = data[i];
                byte b2 = remaining > 1 ? data[i + 1] : (byte)0;
                byte b3 = remaining > 2 ? data[i + 2] : (byte)0;
                byte b4 = remaining > 3 ? data[i + 3] : (byte)0;

                uint value = ((uint)b1 << 24) | ((uint)b2 << 16)   | ((uint)b3 << 8)  | b4;

                var chunk = new char[5];

                for (int j = 4; j >= 0; j--)
                {
                    chunk[j] = Base85Chars[(int)(value % 85)];
                    value /= 85;
                }

                // Only append required chars
                sb.Append(chunk, 0, remaining + 1);
            }

            return sb.ToString();
        }

        public static string DecodeBase85ToString(string encoded)
        {
            var bytes = DecodeBase85(encoded);
            return Encoding.UTF8.GetString(bytes);
        }

        private static byte[] DecodeBase85(string encoded)
        {
            var result = new List<byte>();

            for (int i = 0; i < encoded.Length; i += 5)
            {
                int remaining = Math.Min(5, encoded.Length - i);

                uint value = 0;

                for (int j = 0; j < 5; j++)
                {
                    int idx;

                    if (j < remaining)
                        idx = Base85Chars.IndexOf(encoded[i + j]);
                    else
                        idx = 0; // padding

                    if (idx < 0)
                        throw new FormatException($"Invalid Base85 char: {encoded[i + j]}");

                    value = value * 85 + (uint)idx;
                }

                byte b1 = (byte)(value >> 24);
                byte b2 = (byte)(value >> 16);
                byte b3 = (byte)(value >> 8);
                byte b4 = (byte)value;

                result.Add(b1);
                if (remaining > 2) result.Add(b2);
                if (remaining > 3) result.Add(b3);
                if (remaining > 4) result.Add(b4);
            }

            return result.ToArray();
        }
        public static object? DecryptStudentData(string encrypted)
        {
            try
            {
                var json = DecodeBase85ToString(encrypted);

                json = ExtractValidJson(json);   // ✅ IMPORTANT FIX
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
        private static string ExtractValidJson(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            int lastCurly = input.LastIndexOf('}');
            int lastSquare = input.LastIndexOf(']');

            int end = Math.Max(lastCurly, lastSquare);

            if (end >= 0)
                return input.Substring(0, end + 1);

            return input;
        }
        private static string FixJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return json;

            int openCurly = 0, closeCurly = 0;
            int openSquare = 0, closeSquare = 0;

            foreach (char c in json)
            {
                if (c == '{') openCurly++;
                if (c == '}') closeCurly++;
                if (c == '[') openSquare++;
                if (c == ']') closeSquare++;
            }

            // Close arrays first
            if (closeSquare < openSquare)
            {
                json += new string(']', openSquare - closeSquare);
            }

            // Then close objects
            if (closeCurly < openCurly)
            {
                json += new string('}', openCurly - closeCurly);
            }

            return json;
        }

        public static object? DecryptStringData(string encryptedWrapper)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(encryptedWrapper))
                    return null;

                // Step 1: Handle escaped JSON string
                if (encryptedWrapper.StartsWith("\""))
                {
                    encryptedWrapper = JsonSerializer.Deserialize<string>(encryptedWrapper);
                }

                // Step 2: Deserialize to correct structure
                var list = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(encryptedWrapper);

                if (list == null || list.Count == 0)
                    return null;

                // Step 3: Take LAST version (latest)
                var lastEntry = list.Last();
                var encoded = lastEntry.Values.First();

                // Step 4: Decode Base85
                var json = DecodeBase85ToString(encoded);

                // Step 5: Clean JSON
                json = json.Trim().TrimEnd('|');
                json = FixJson(json);

                // Step 6: Deserialize
                return JsonSerializer.Deserialize<object>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
            }
            catch (Exception ex)
            {
                throw new Exception("DecryptStringData failed", ex);
            }
        }
        //public static object? DecryptStringData(string encryptedWrapper)
        //{
        //    try
        //    {
        //        if (string.IsNullOrWhiteSpace(encryptedWrapper))
        //            return null;

        //        // ✅ STEP 1: Detect double-encoded JSON
        //        if (encryptedWrapper.StartsWith("\""))
        //        {
        //            encryptedWrapper = JsonSerializer.Deserialize<string>(encryptedWrapper);
        //        }

        //        // ✅ STEP 2: Now deserialize to List<string>
        //        List<string>? list;

        //        try
        //        {
        //            list = JsonSerializer.Deserialize<List<string>>(encryptedWrapper);
        //        }
        //        catch
        //        {
        //            list = new List<string> { encryptedWrapper };
        //        }

        //        if (list == null || list.Count == 0)
        //            return null;

        //        // ✅ STEP 3: Take last value
        //        var encoded = list.Last();

        //        // ✅ STEP 4: Decode Base85
        //        var json = DecodeBase85ToString(encoded);

        //        // ✅ STEP 5: Clean JSON
        //        json = json.Trim().TrimEnd('|');
        //        json = FixJson(json);

        //        // ✅ STEP 6: Deserialize final object
        //        return JsonSerializer.Deserialize<object>(
        //            json,
        //            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        //        );
        //    }
        //    catch (Exception ex)
        //    {
        //        throw new Exception("DecryptStringData failed", ex);
        //    }
        //}
    }
}