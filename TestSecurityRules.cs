// ═════════════════════════════════════════════════════════════════
// LenovoAnalyzer 6条新安全规则测试用例
// 每条规则包含 ❌ 坏示例 和 ✅ 好示例
// ═════════════════════════════════════════════════════════════════

using System;
using System.Data.SqlClient;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace TestSecurityRules
{
    public class SecurityTestCases
    {
        // ═════════════════════════════════════════════════════════════════
        // SEC003 - SQL注入防护
        // ═════════════════════════════════════════════════════════════════
        public void SqlInjection_BadExamples(string userInput)
        {
            // ❌ 坏示例1: 字符串插值构造SQL
            var query1 = $"SELECT * FROM Users WHERE Id = {userInput}";
            var cmd1 = new SqlCommand(query1);

            // ❌ 坏示例2: 字符串拼接构造SQL
            var query2 = "SELECT * FROM Users WHERE Name = '" + userInput + "'";
            var cmd2 = new SqlCommand(query2);

            // ❌ 坏示例3: 直接设置CommandText
            var cmd3 = new SqlCommand();
            cmd3.CommandText = $"DELETE FROM Orders WHERE Id = {userInput}";
        }

        public void SqlInjection_GoodExamples(string userInput)
        {
            // ✅ 好示例1: 使用参数化查询
            var cmd1 = new SqlCommand("SELECT * FROM Users WHERE Id = @id");
            cmd1.Parameters.AddWithValue("@id", userInput);

            // ✅ 好示例2: 常量字符串（两侧都是字面量）
            var query = "SELECT * FROM Users";
            var cmd2 = new SqlCommand(query);
        }

        // ═════════════════════════════════════════════════════════════════
        // SEC004 - 不安全的反序列化
        // ═════════════════════════════════════════════════════════════════
        public object UnsafeDeserialization_BadExamples(byte[] data)
        {
            // ❌ 坏示例1: 使用BinaryFormatter
            var formatter = new BinaryFormatter();
            return formatter.Deserialize(new MemoryStream(data));
        }

        public void UnsafeDeserialization_TypeNameHandling(JsonSerializerOptions options)
        {
            // ❌ 坏示例2: TypeNameHandling设置为非None
            // options.TypeNameHandling = TypeNameHandling.All;  // Newtonsoft.Json示例
        }

        public T UnsafeDeserialization_Good<T>(string json)
        {
            // ✅ 好示例: 使用System.Text.Json（默认安全）
            return System.Text.Json.JsonSerializer.Deserialize<T>(json);
        }

        // ═════════════════════════════════════════════════════════════════
        // SEC005 - 不安全的随机数生成
        // ═════════════════════════════════════════════════════════════════
        public string InsecureRandom_BadExample()
        {
            // ❌ 坏示例: 使用System.Random生成安全敏感值
            var random = new Random();
            var token = random.Next(100000, 999999).ToString();
            return token;
        }

        public byte[] InsecureRandom_GoodExample()
        {
            // ✅ 好示例: 使用RandomNumberGenerator
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            var bytes = new byte[32];
            rng.GetBytes(bytes);
            return bytes;
        }

        // ═════════════════════════════════════════════════════════════════
        // SEC006 - ReDoS（正则表达式拒绝服务）
        // ═════════════════════════════════════════════════════════════════
        public bool RegexDos_BadExamples(string input)
        {
            // ❌ 坏示例1: new Regex未指定超时
            var regex1 = new Regex(@"(a+)+$");
            return regex1.IsMatch(input);
        }

        public bool RegexDos_StaticMethod_Bad(string input)
        {
            // ❌ 坏示例2: 静态方法未指定超时
            return Regex.IsMatch(input, @"(a+)+$");
        }

        public bool RegexDos_GoodExamples(string input)
        {
            // ✅ 好示例1: new Regex指定超时
            var regex1 = new Regex(@"(a+)+$", RegexOptions.None, TimeSpan.FromSeconds(1));
            return regex1.IsMatch(input);
        }

        public bool RegexDos_StaticMethod_Good(string input)
        {
            // ✅ 好示例2: 静态方法指定超时
            return Regex.IsMatch(input, @"(a+)+$", RegexOptions.None, TimeSpan.FromSeconds(1));
        }

        // ═════════════════════════════════════════════════════════════════
        // SEC007 - 资源泄漏
        // ═════════════════════════════════════════════════════════════════
        public void ResourceLeak_BadExamples(string connectionString)
        {
            // ❌ 坏示例: 创建SqlConnection未使用using
            var conn = new SqlConnection(connectionString);
            conn.Open();
            // 缺少using，连接可能泄漏
        }

        public void ResourceLeak_GoodExamples(string connectionString)
        {
            // ✅ 好示例1: 使用using语句
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
            }

            // ✅ 好示例2: 使用using声明（C# 8.0+）
            using var conn2 = new SqlConnection(connectionString);
            conn2.Open();
        }

        // ═════════════════════════════════════════════════════════════════
        // SEC008 - 不安全临时文件创建
        // ═════════════════════════════════════════════════════════════════
        public string InsecureTempFile_BadExamples()
        {
            // ❌ 坏示例1: 使用DateTime.Now构造文件名
            var path1 = Path.Combine(Path.GetTempPath(), $"temp_{DateTime.Now.Ticks}.txt");

            // ❌ 坏示例2: 使用字符串拼接
            var path2 = Path.Combine(Path.GetTempPath(), "file_" + Thread.CurrentThread.ManagedThreadId + ".log");

            // ❌ 坏示例3: 使用string.Format
            var path3 = Path.Combine(Path.GetTempPath(), string.Format("log_{0}.txt", DateTime.Now.ToString("yyyyMMdd")));

            return path1;
        }

        public string InsecureTempFile_GoodExamples()
        {
            // ✅ 好示例1: 使用Path.GetTempFileName()
            var path1 = Path.GetTempFileName();

            // ✅ 好示例2: 使用Path.GetRandomFileName()
            var path2 = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            // ✅ 好示例3: 使用Guid.NewGuid()
            var path3 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".txt");

            return path1;
        }
    }
}
