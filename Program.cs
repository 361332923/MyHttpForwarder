using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fiddler;

class Program
{
    // 日志文件路径：位于 EXE 相同目录下
    static string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "capture_log.txt");

    static async Task Main(string[] args)
    {
        // 1. 参数校验
        if (args.Length < 2)
        {
            Console.WriteLine("用法：HttpForwarder.exe <URL正则> <转发地址> [端口(默认8888)]");
            return;
        }

        string urlPattern = args[0];
        string forwardToUrl = args[1];
        int listenPort = (args.Length >= 3) ? int.Parse(args[2]) : 8888;

        WriteLog($"=== 程序启动 ===");
        WriteLog($"[配置] 正则规则: {urlPattern}");
        WriteLog($"[配置] 转发目标: {forwardToUrl}");
        WriteLog($"[配置] 监听端口: {listenPort}");

        // 2. 核心监听逻辑
        FiddlerApplication.AfterSessionComplete += (session) =>
        {
            try
            {
                // 正则匹配 URL
                if (Regex.IsMatch(session.fullUrl, urlPattern, RegexOptions.IgnoreCase))
                {
                    // 确保有响应且是 JSON
                    if (session.oResponse != null && session.oResponse.MIMEType.Contains("json"))
                    {
                        string jsonBody = session.GetResponseBodyAsString();
                        WriteLog($"[命中] 捕获到目标 URL: {session.fullUrl}");

                        // 异步 POST 转发
                        Task.Run(async () => {
                            try {
                                using (var client = new HttpClient())
                                {
                                    var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                                    var response = await client.PostAsync(forwardToUrl, content);
                                    WriteLog($" >> [转发成功] 状态码: {response.StatusCode} | URL: {session.fullUrl}");
                                }
                            } catch (Exception ex) {
                                WriteLog($" >> [转发失败] 错误: {ex.Message}");
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"[异常] 处理 Session 时出错: {ex.Message}");
            }
            finally
            {
                // 内存优化：清空大对象，防止 30 天运行内存溢出
                session.RequestBody = new byte[0];
                session.ResponseBody = new byte[0];
            }
        };

        // 3. 启动 FiddlerCore
        FiddlerCoreStartupFlags flags = FiddlerCoreStartupFlags.Default 
                                        | FiddlerCoreStartupFlags.RegisterAsSystemProxy 
                                        | FiddlerCoreStartupFlags.DecryptSSL;

        FiddlerApplication.Startup(listenPort, flags);
        WriteLog($"[*] 服务已进入监听状态...");

        // 4. 保持运行（适合影刀后台隐藏运行）
        while (true)
        {
            await Task.Delay(60000); // 每分钟休眠一次，极低消耗
            
            // 可选：如果日志文件超过 10MB，自动清理
            FileInfo fileInfo = new FileInfo(logFilePath);
            if (fileInfo.Exists && fileInfo.Length > 10 * 1024 * 1024)
            {
                File.WriteAllText(logFilePath, $"[{DateTime.Now}] 日志超过10MB，已自动重置。{Environment.NewLine}");
            }
        }
    }

    // 通用的写日志方法
    static void WriteLog(string message)
    {
        try
        {
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            // 控制台输出（调试用）
            Console.Write(entry);
            // 写入本地文件
            File.AppendAllText(logFilePath, entry);
        }
        catch { /* 忽略日志写入本身的错误 */ }
    }
}
